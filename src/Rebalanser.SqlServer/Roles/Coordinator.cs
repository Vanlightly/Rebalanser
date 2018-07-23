using Rebalanser.Core;
using Rebalanser.Core.Logging;
using Rebalanser.SqlServer.Clients;
using Rebalanser.SqlServer.Resources;
using Rebalanser.SqlServer.Store;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalanser.SqlServer.Roles
{
    class Coordinator
    {
        private ILogger logger;
        private IResourceService resourceService;
        private IClientService clientService;
        private ResourceGroupStore store;

        private List<string> resources;
        private List<Guid> clients;
        private int currentFencingToken;

        public Coordinator(ILogger logger,
            IResourceService resourceService,
            IClientService clientService,
            ResourceGroupStore store)
        {
            this.logger = logger;
            this.resourceService = resourceService;
            this.clientService = clientService;
            this.store = store;
            this.resources = new List<string>();
            this.clients = new List<Guid>();
        }

        public int GetFencingToken()
        {
            return currentFencingToken;
        }

        public async Task ExecuteCoordinatorRoleAsync(Guid coordinatorClientId,
            ClientEvent clientEvent,
            OnChangeActions onChangeActions,
            CancellationToken token)
        {
            currentFencingToken = clientEvent.FencingToken;
            var self = await clientService.KeepAliveAsync(coordinatorClientId);
            var resourcesNow = (await resourceService.GetResourcesAsync(clientEvent.ResourceGroup)).OrderBy(x => x).ToList();
            var clientsNow = await GetLiveClientsAsync(clientEvent, coordinatorClientId);
            var clientIds = clientsNow.Select(x => x.ClientId).ToList();
            clientIds.Add(coordinatorClientId);

            if (clientsNow.Any(x => x.FencingToken > clientEvent.FencingToken))
            {
                clientEvent.CoordinatorToken.FencingTokenViolation = true;
                return;
            }

            if (!resources.OrderBy(x => x).SequenceEqual(resourcesNow.OrderBy(x => x)))
            {
                logger.Debug($"Resource change: Old: {string.Join(",", resources.OrderBy(x => x))} New: {string.Join(",", resourcesNow.OrderBy(x => x))}");
                await TriggerRebalancingAsync(coordinatorClientId, clientEvent, clientsNow, resourcesNow, onChangeActions, token);
            }
            else if (!clients.OrderBy(x => x).SequenceEqual(clientIds.OrderBy(x => x)))
            {
                logger.Debug($"Client change: Old: {string.Join(",", clients.OrderBy(x => x))} New: {string.Join(",", clientIds.OrderBy(x => x))}");
                await TriggerRebalancingAsync(coordinatorClientId, clientEvent, clientsNow, resourcesNow, onChangeActions, token);
            }
            else
            {
                // no change, do nothing
            }
        }

        public int GetCurrentFencingToken()
        {
            return this.currentFencingToken;
        }

        private async Task<List<Client>> GetLiveClientsAsync(ClientEvent clientEvent, Guid coordinatorClientId)
        {
            var allClientsNow = (await clientService.GetActiveClientsAsync(clientEvent.ResourceGroup))
                                    .Where(x => x.ClientId != coordinatorClientId)
                                    .ToList();

            var liveClientsNow = allClientsNow.Where(x => (x.TimeNow - x.LastKeepAlive) < clientEvent.KeepAliveExpiryPeriod).ToList();

            return liveClientsNow;
        }

        private async Task TriggerRebalancingAsync(Guid coordinatorClientId,
            ClientEvent clientEvent,
            List<Client> clients,
            List<string> resources,
            OnChangeActions onChangeActions,
            CancellationToken token)
        {
            this.logger.Info($"---------- Rebalancing triggered -----------");

            // request stop of all clients
            this.logger.Info("COORDINATOR: Requested stop");
            if (clients.Any())
            {
                var result = await clientService.StopActivityAsync(clientEvent.FencingToken, clients);
                if (result == ModifyClientResult.FencingTokenViolation)
                {
                    clientEvent.CoordinatorToken.FencingTokenViolation = true;
                    return;
                }
                else if (result == ModifyClientResult.Error)
                {
                    this.logger.Error("COORDINATOR: Rebalancing error");
                    return;
                }
            }

            // stop all resource activity in local coordinator client
            foreach (var onStopAction in onChangeActions.OnStopActions)
                onStopAction.Invoke();

            // wait for all live clients to confirm stopped
            bool allClientsWaiting = false;
            List<Client> clientsNow = null;
            while (!allClientsWaiting && !token.IsCancellationRequested)
            {
                WaitFor(TimeSpan.FromSeconds(5), token);
                clientsNow = await GetLiveClientsAsync(clientEvent, coordinatorClientId);

                if (!clientsNow.Any())
                    allClientsWaiting = true;
                else
                    allClientsWaiting = clientsNow.All(x => x.ClientStatus == ClientStatus.Waiting);
            }
            this.logger.Info("COORDINATOR: Stop confirmed");

            // assign resources first to coordinator then to other live clients 
            if (token.IsCancellationRequested)
                return;
            else if (allClientsWaiting)
            {
                var resourcesToAssign = new Queue<string>(resources);
                var clientStartRequests = new List<ClientStartRequest>();
                int remainingClients = clientsNow.Count + 1;
                var resourcesPerClient = Math.Max(1, resourcesToAssign.Count / remainingClients);

                var coordinatorRequest = new ClientStartRequest();
                coordinatorRequest.ClientId = coordinatorClientId;
                while (coordinatorRequest.AssignedResources.Count < resourcesPerClient && resourcesToAssign.Any())
                    coordinatorRequest.AssignedResources.Add(resourcesToAssign.Dequeue());

                clientStartRequests.Add(coordinatorRequest);
                remainingClients--;

                foreach (var client in clientsNow)
                {
                    resourcesPerClient = Math.Max(1, resourcesToAssign.Count / remainingClients);

                    var request = new ClientStartRequest();
                    request.ClientId = client.ClientId;

                    while (request.AssignedResources.Count < resourcesPerClient && resourcesToAssign.Any())
                        request.AssignedResources.Add(resourcesToAssign.Dequeue());

                    clientStartRequests.Add(request);
                    remainingClients--;
                }

                this.logger.Info("COORDINATOR: Resources assigned");
                var startResult = await clientService.StartActivityAsync(clientEvent.FencingToken, clientStartRequests);
                if (startResult == ModifyClientResult.FencingTokenViolation)
                {
                    clientEvent.CoordinatorToken.FencingTokenViolation = true;
                    return;
                }
                else if (startResult == ModifyClientResult.Error)
                {
                    this.logger.Error("COORDINATOR: Rebalancing error");
                    return;
                }

                this.store.SetResources(new SetResourcesRequest() { AssignmentStatus = AssignmentStatus.ResourcesAssigned, Resources = coordinatorRequest.AssignedResources });
                foreach (var onStartAction in onChangeActions.OnStartActions)
                    onStartAction.Invoke();

                logger.Debug("COORDINATOR: Local client started");

                var clientIds = clientsNow.Select(x => x.ClientId).ToList();
                clientIds.Add(coordinatorClientId);
                this.clients = clientIds;
                this.resources = resources;
                logger.Info($"---------- Activity Started -----------");
            }
            else
            {
                // log it
                logger.Info("!!!");
                return;
            }
        }

        private void WaitFor(TimeSpan delayPeriod, CancellationToken token)
        {
            try
            {
                Task.Delay(delayPeriod, token);
            }
            catch (TaskCanceledException)
            { }
        }
    }
}
