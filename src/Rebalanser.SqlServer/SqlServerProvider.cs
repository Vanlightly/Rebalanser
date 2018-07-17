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
            CancellationToken parentToken,
            ContextOptions contextOptions)
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
                while (!parentToken.IsCancellationRequested)
                {
                    var childTaskCts = new CancellationTokenSource();
                    try
                    {
                        var clientEvents = new BlockingCollection<ClientEvent>();

                        var leaderElectionTask = StartLeadershipTask(childTaskCts.Token, clientEvents);
                        var roleTask = StartRoleTask(childTaskCts.Token, onChangeActions, clientEvents);

                        while (!parentToken.IsCancellationRequested
                            && !leaderElectionTask.IsCompleted
                            && !clientEvents.IsCompleted)
                        {
                            await Task.Delay(100);
                        }

                        // cancel child tasks
                        childTaskCts.Cancel();

                        if (parentToken.IsCancellationRequested)
                        {
                            logger.Info("Context shutting down due to cancellation");
                        }
                        else
                        {
                            if (leaderElectionTask.IsFaulted)
                                await NotifyOfErrorAsync(leaderElectionTask, "Shutdown due to leader election task fault", contextOptions.AutoRecoveryOnError, onChangeActions);
                            else if (roleTask.IsFaulted)
                                await NotifyOfErrorAsync(roleTask, "Shutdown due to coordinator/follower task fault", contextOptions.AutoRecoveryOnError, onChangeActions);
                            else
                                NotifyOfError(onChangeActions, "Unknown shutdown reason", contextOptions.AutoRecoveryOnError, null);

                            if (contextOptions.AutoRecoveryOnError)
                                await WaitFor(contextOptions.RestartDelay, parentToken);
                            else
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        NotifyOfError(onChangeActions, $"An unexpected error has caused shutdown. Automatic restart is set to {contextOptions.AutoRecoveryOnError}", contextOptions.AutoRecoveryOnError, ex);

                        if (contextOptions.AutoRecoveryOnError)
                            await WaitFor(contextOptions.RestartDelay, parentToken);
                        else
                            break;
                    }
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        private async Task NotifyOfErrorAsync(Task faultedTask, string message, bool autoRecoveryEnabled, OnChangeActions onChangeActions)
        {
            await InvokeOnErrorAsync(faultedTask, message, autoRecoveryEnabled, onChangeActions);
            InvokeOnStop(onChangeActions);
        }

        private void NotifyOfError(OnChangeActions onChangeActions, string message, bool autoRecoveryEnabled, Exception exception)
        {
            InvokeOnError(onChangeActions, message, autoRecoveryEnabled, exception);
            InvokeOnStop(onChangeActions);
        }

        private async Task InvokeOnErrorAsync(Task faultedTask, string message, bool autoRecoveryEnabled, OnChangeActions onChangeActions)
        {
            try
            {
                await faultedTask;
            }
            catch (Exception ex)
            {
                InvokeOnError(onChangeActions, message, autoRecoveryEnabled, ex);
            }
        }

        private void InvokeOnError(OnChangeActions onChangeActions, string message, bool autoRecoveryEnabled, Exception exception)
        {
            try
            {
                foreach (var onErrorAction in onChangeActions.OnErrorActions)
                    onErrorAction.Invoke(message, autoRecoveryEnabled, exception);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex.ToString());
            }
        }

        private void InvokeOnStop(OnChangeActions onChangeActions)
        {
            try
            {
                foreach (var onErrorAction in onChangeActions.OnStopActions)
                    onErrorAction.Invoke();
            }
            catch (Exception ex)
            {
                this.logger.Error(ex.ToString());
            }
        }

        private Task StartLeadershipTask(CancellationToken token,
            BlockingCollection<ClientEvent> clientEvents)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var request = new AcquireLeaseRequest()
                        {
                            ClientId = this.clientId,
                            ResourceGroup = this.resourceGroup
                        };
                        var response = await TryAcquireLeaseAsync(request, token);
                        if (response.Result == LeaseResult.Granted) // is now the Coordinator
                        {
                            await ExecuteLeaseRenewals(token, clientEvents, response.Lease);
                        }
                        else if (response.Result == LeaseResult.Denied) // is a Follower
                        {
                            PostFollowerEvent(response.Lease.ExpiryPeriod, clientEvents);
                            await WaitFor(GetInterval(response.Lease.ExpiryPeriod), token);
                        }
                        else if (response.Result == LeaseResult.NoLease)
                        {
                            throw new RebalanserException($"The resource group {this.resourceGroup} does not exist.");
                        }
                        else if (response.IsErrorResponse())
                        {
                            throw new RebalanserException("An non-recoverable error occurred.", response.Exception);
                        }
                        else
                        {
                            throw new RebalanserException("A non-supported lease result was received"); // should never happen, just in case I screw up in the future
                        }
                    }
                }
                finally
                {
                    clientEvents.CompleteAdding();
                }
            });
        }

        private async Task ExecuteLeaseRenewals(CancellationToken token,
            BlockingCollection<ClientEvent> clientEvents,
            Lease lease)
        {
            var coordinatorToken = new CoordinatorToken();
            PostLeaderEvent(lease.FencingToken, lease.ExpiryPeriod, coordinatorToken, clientEvents);
            await WaitFor(GetInterval(lease.ExpiryPeriod), token, coordinatorToken);

            // lease renewal loop
            while (!token.IsCancellationRequested && !coordinatorToken.FencingTokenViolation)
            {
                var response = await TryRenewLeaseAsync(new RenewLeaseRequest() { ClientId = this.clientId, ResourceGroup = this.resourceGroup, FencingToken = lease.FencingToken }, token);
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
                else if (response.Result == LeaseResult.NoLease)
                {
                    throw new RebalanserException($"The resource group {this.resourceGroup} does not exist.");
                }
                else if (response.IsErrorResponse())
                {
                    throw new RebalanserException("An non-recoverable error occurred.", response.Exception);
                }
                else
                {
                    throw new RebalanserException("A non-supported lease result was received"); // should never happen, just in case I screw up in the future
                }
            }
        }

        private async Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest request, CancellationToken token)
        {
            int delaySeconds = 2;
            int triesLeft = 3;
            while (triesLeft > 0)
            {
                triesLeft--;
                var response = await this.leaseService.TryAcquireLeaseAsync(request);
                if (response.Result != LeaseResult.TransientError)
                    return response;
                else if (triesLeft > 0)
                    await WaitFor(TimeSpan.FromSeconds(delaySeconds), token);
                else
                    return response;

                delaySeconds = delaySeconds * 2;
            }

            // this should never happen
            return new LeaseResponse() { Result = LeaseResult.Error };
        }

        private async Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest request, CancellationToken token)
        {
            int delaySeconds = 2;
            int triesLeft = 3;
            while (triesLeft > 0)
            {
                triesLeft--;
                var response = await this.leaseService.TryRenewLeaseAsync(request);
                if (response.Result != LeaseResult.TransientError)
                    return response;
                else if (triesLeft > 0)
                    await WaitFor(TimeSpan.FromSeconds(delaySeconds), token);
                else
                    return response;

                delaySeconds = delaySeconds * 2;
            }

            // this should never happen
            return new LeaseResponse() { Result = LeaseResult.Error };
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
