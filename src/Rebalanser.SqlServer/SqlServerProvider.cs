using Rebalanser.Core;
using Rebalanser.Core.Logging;
using Rebalanser.SqlServer.Clients;
using Rebalanser.SqlServer.Leases;
using Rebalanser.SqlServer.Resources;
using Rebalanser.SqlServer.Roles;
using Rebalanser.SqlServer.Store;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalanser.SqlServer
{
    public class SqlServerProvider : IRebalanserProvider
    {
        private string connectionString;
        private ILogger logger;
        private ILeaseService leaseService;
        private IResourceService resourceService;
        private IClientService clientService;
        private Guid clientId;
        private Coordinator coordinator;
        private Follower follower;
        private string resourceGroup;
        private static object startLockObj = new object();
        private bool started;
        private bool isCoordinator;
        private ResourceGroupStore store;

        public SqlServerProvider(string connectionString,
            ILogger logger = null,
            ILeaseService leaseService = null,
            IResourceService resourceService = null,
            IClientService clientService = null)
        {
            this.connectionString = connectionString;
            this.store = new ResourceGroupStore();

            if (logger == null)
                this.logger = new NullLogger();
            else
                this.logger = logger;

            if (leaseService == null)
                this.leaseService = new LeaseService(this.connectionString, this.logger);
            else
                this.leaseService = leaseService;

            if (resourceService == null)
                this.resourceService = new ResourceService(this.connectionString);
            else
                this.resourceService = resourceService;

            if (clientService == null)
                this.clientService = new ClientService(this.connectionString);
            else
                this.clientService = clientService;

            this.clientId = Guid.NewGuid();
            this.coordinator = new Coordinator(this.logger, this.resourceService, this.clientService, this.store);
            this.follower = new Follower(this.logger, this.clientService, this.store);
        }

        public async Task StartAsync(string resourceGroup,
            OnChangeActions onChangeActions,
            CancellationToken token)
        {
            // just in case someone does something "clever"
            lock (startLockObj)
            {
                if (this.started)
                    throw new RebalanserException("Context already started");
            }

            this.resourceGroup = resourceGroup;
            await this.clientService.CreateClientAsync(this.resourceGroup, this.clientId);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
            {
                try
                {
                    var clientEvents = new BlockingCollection<ClientEvent>();

                    var leaderElectionTask = StartLeadershipTask(token, clientEvents);
                    var roleTask = StartRoleTask(token, onChangeActions, clientEvents);

                    while (!token.IsCancellationRequested
                        && !leaderElectionTask.IsCompleted
                        && !clientEvents.IsCompleted)
                    {
                        await Task.Delay(100);
                    }

                    if (token.IsCancellationRequested)
                    {
                        logger.Error("Context shutting down due to cancellation");
                    }
                    else
                    {
                        if (leaderElectionTask.IsFaulted)
                        {
                            logger.Error("Shutdown due to leader election task fault");
                        }
                        else if (roleTask.IsFaulted)
                        {
                            logger.Error("Shutdown due to role task fault");
                        }
                        else
                        {
                            logger.Error("Unknown shutdown reason");
                        }

                        await leaderElectionTask;
                        await roleTask;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex.ToString());
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        private Task StartLeadershipTask(CancellationToken token,
            BlockingCollection<ClientEvent> clientEvents)
        {
            return Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var request = new AcquireLeaseRequest()
                    {
                        ClientId = this.clientId,
                        ResourceGroup = this.resourceGroup
                    };
                    var response = await TryAcquireLeaseAsync(request);
                    if (response.Result == LeaseResult.Granted)
                    {
                        var lease = response.Lease;
                        var coordinatorToken = new CoordinatorToken();
                        PostLeaderEvent(lease.FencingToken, lease.ExpiryPeriod, coordinatorToken, clientEvents);
                        await WaitFor(GetInterval(lease.ExpiryPeriod), token, coordinatorToken);

                        // lease renewal loop
                        while (!token.IsCancellationRequested && !coordinatorToken.FencingTokenViolation)
                        {
                            response = await TryRenewLeaseAsync(new RenewLeaseRequest() { ClientId = this.clientId, ResourceGroup = this.resourceGroup, FencingToken = lease.FencingToken });
                            if (response.Result == LeaseResult.Granted)
                            {
                                PostLeaderEvent(lease.FencingToken, lease.ExpiryPeriod, coordinatorToken, clientEvents);
                                await WaitFor(GetInterval(lease.ExpiryPeriod), token, coordinatorToken);
                            }
                            else if (response.Result == LeaseResult.Denied)
                            {
                                PostFollowerEvent(lease.ExpiryPeriod, clientEvents);
                                await WaitFor(GetInterval(lease.ExpiryPeriod), token);
                                break;
                            }
                            else
                            {
                                // log it, failure scenario
                                PostFollowerEvent(lease.ExpiryPeriod, clientEvents);
                                await WaitFor(GetInterval(lease.ExpiryPeriod), token);
                                break;
                            }
                        }
                    }
                    else if (response.Result == LeaseResult.Denied)
                    {
                        PostFollowerEvent(response.Lease.ExpiryPeriod, clientEvents);
                        await WaitFor(GetInterval(response.Lease.ExpiryPeriod), token);
                    }
                    else
                    {
                        // log it, this is a failure scenario
                        PostFollowerEvent(response.Lease.ExpiryPeriod, clientEvents);
                        await WaitFor(GetInterval(response.Lease.ExpiryPeriod), token);
                    }
                }

                clientEvents.CompleteAdding();
            });
        }

        private async Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest request)
        {
            int triesLeft = 3;
            while (triesLeft > 0)
            {
                triesLeft--;
                var response = await this.leaseService.TryAcquireLeaseAsync(request);
                if (response.Result != LeaseResult.TransientError)
                    return response;
                else if (triesLeft > 0)
                    await Task.Delay(2000);
                else
                    return response;
            }

            // this should never happen
            return new LeaseResponse()
            {
                Result = LeaseResult.Error
            };
        }

        private async Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest request)
        {
            int triesLeft = 3;
            while (triesLeft > 0)
            {
                triesLeft--;
                var response = await this.leaseService.TryRenewLeaseAsync(request);
                if (response.Result != LeaseResult.TransientError)
                    return response;
                else if (triesLeft > 0)
                    await Task.Delay(2000);
                else
                    return response;
            }

            // this should never happen
            return new LeaseResponse()
            {
                Result = LeaseResult.Error
            };
        }

        private TimeSpan GetInterval(TimeSpan leaseExpiry)
        {
            return TimeSpan.FromMilliseconds(leaseExpiry.TotalMilliseconds / 2.5);
        }

        private void PostLeaderEvent(int fencingToken,
            TimeSpan keepAliveExpiryPeriod,
            CoordinatorToken coordinatorToken,
            BlockingCollection<ClientEvent> clientEvents)
        {
            this.logger.Debug($"{clientId} is leader");
            this.isCoordinator = true;
            var clientEvent = new ClientEvent()
            {
                ResourceGroup = this.resourceGroup,
                EventType = EventType.Coordinator,
                FencingToken = fencingToken,
                CoordinatorToken = coordinatorToken,
                KeepAliveExpiryPeriod = keepAliveExpiryPeriod
            };
            clientEvents.Add(clientEvent);
        }

        private void PostFollowerEvent(TimeSpan keepAliveExpiryPeriod,
            BlockingCollection<ClientEvent> clientEvents)
        {
            this.logger.Debug($"{clientId} is follower");
            this.isCoordinator = false;
            var clientEvent = new ClientEvent()
            {
                EventType = EventType.Follower,
                ResourceGroup = resourceGroup,
                KeepAliveExpiryPeriod = keepAliveExpiryPeriod
            };
            clientEvents.Add(clientEvent);
        }

        private Task StartRoleTask(CancellationToken token,
            OnChangeActions onChangeActions,
            BlockingCollection<ClientEvent> clientEvents)
        {
            return Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !clientEvents.IsAddingCompleted)
                {
                    try
                    {
                        // take the most recent event, if multiple are queued up then we only need the latest
                        ClientEvent clientEvent = null;
                        while (clientEvents.Any())
                        {
                            try
                            {
                                clientEvent = clientEvents.Take(token);
                            }
                            catch (OperationCanceledException) { }
                        }

                        // if there was an event then call the appropriate role beahvaiour
                        if (clientEvent != null)
                        {
                            if (clientEvent.EventType == EventType.Coordinator)
                            {
                                await this.coordinator.ExecuteCoordinatorRoleAsync(this.clientId,
                                        clientEvent,
                                        onChangeActions,
                                        token);
                            }
                            else
                            {
                                await this.follower.ExecuteFollowerRoleAsync(this.clientId,
                                        clientEvent,
                                        onChangeActions,
                                        token);
                            }
                        }
                        else
                        {
                            await WaitFor(TimeSpan.FromSeconds(1), token);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex);
                        await WaitFor(TimeSpan.FromSeconds(1), token);
                    }
                }
            });
        }

        private async Task WaitFor(TimeSpan delayPeriod, CancellationToken token, CoordinatorToken coordinatorToken)
        {
            var sw = new Stopwatch();
            sw.Start();
            while (!token.IsCancellationRequested && !coordinatorToken.FencingTokenViolation)
            {
                if (sw.Elapsed < delayPeriod)
                    await Task.Delay(100);
                else
                    break;
            }
        }

        private async Task WaitFor(TimeSpan delayPeriod, CancellationToken token)
        {
            try
            {
                await Task.Delay(delayPeriod, token);
            }
            catch (TaskCanceledException)
            { }
        }

        public IList<string> GetAssignedResources()
        {
            while (true)
            {
                var response = this.store.GetResources();
                if (response.AssignmentStatus == AssignmentStatus.ResourcesAssigned || response.AssignmentStatus == AssignmentStatus.NoResourcesAssigned)
                    return response.Resources;
                else
                    Thread.Sleep(100);
            }
        }

        public IList<string> GetAssignedResources(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var response = this.store.GetResources();
                if (response.AssignmentStatus == AssignmentStatus.ResourcesAssigned || response.AssignmentStatus == AssignmentStatus.NoResourcesAssigned)
                    return response.Resources;

                Thread.Sleep(100);
            }

            return new List<string>();
        }


    }
}
