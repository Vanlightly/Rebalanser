using Consul;
using Rebalanser.Consul.ResourceGroups;
using Rebalanser.Consul.Roles;
using Rebalanser.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalanser.Consul
{
    public class ConsulProvider : IRebalanserProvider
    {
        private string consulServerUrl;
        private IResourceGroupService resourceGroupService;

        public ConsulProvider(string consulServerUrl,
            IResourceGroupService resourceGroupService)
        {
            this.consulServerUrl = consulServerUrl;

            if (resourceGroupService == null)
                this.resourceGroupService = new ResourceGroupService();
            else
                this.resourceGroupService = resourceGroupService;
        }

        public async Task StartAsync(string group, OnChangeActions onChangeActions, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public IList<string> GetAssignedResources()
        {
            throw new NotImplementedException();
        }

        public IList<string> GetAssignedResources(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        private Task StartLeadershipTask(string resourceGroup,
            Guid clientId,
            CancellationToken token,
            BlockingCollection<ClientEvent> clientEvents)
        {
            return Task.Run(async () =>
            {
                using (var client = new ConsulClient(SetConfig))
                {
                    var key = $"rebalanser/resourcegroups/{resourceGroup}";
                    var session = client.CreateLock(key);

                    while (!token.IsCancellationRequested)
                    {
                        var rgDetails = await GetResourceGroupAsync(client, resourceGroup);
                        await session.Acquire();
                        if (session.IsHeld)
                        {
                            PostLeaderEvent(resourceGroup, session, clientEvents);

                            while (!token.IsCancellationRequested)
                            {
                                await WaitFor(TimeSpan.FromSeconds(1), token);
                                if (!session.IsHeld)
                                {
                                    PostFollowerEvent(resourceGroup, clientEvents);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            PostFollowerEvent(resourceGroup, clientEvents);
                            await WaitFor(TimeSpan.FromSeconds(rgDetails.LeaderPollingInterval), token);
                        }
                    }
                }
            });
        }

        private void PostLeaderEvent(string resourceGroup,
            IDistributedLock coordinatorLock,
            BlockingCollection<ClientEvent> clientEvents)
        {
            //this.logger.Debug($"{clientId} is leader");
            //this.isCoordinator = true;
            var clientEvent = new ClientEvent()
            {
                ResourceGroup = resourceGroup,
                EventType = EventType.Coordinator,
                CoordinatorLock = coordinatorLock
            };
            clientEvents.Add(clientEvent);
        }

        private void PostFollowerEvent(string resourceGroup,
            BlockingCollection<ClientEvent> clientEvents)
        {
            //this.logger.Debug($"{clientId} is leader");
            //this.isCoordinator = true;
            var clientEvent = new ClientEvent()
            {
                ResourceGroup = resourceGroup,
                EventType = EventType.Follower
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

        private async Task WaitFor(TimeSpan delayPeriod, CancellationToken token)
        {
            try
            {
                await Task.Delay(delayPeriod, token);
            }
            catch (TaskCanceledException)
            { }
        }

        private void SetConfig(ConsulClientConfiguration config)
        {
            config.Address = new Uri(consulServerUrl);
        }
    }
}
