using Rebalanser.Core;
using Rebalanser.Core.Logging;
using Rebalanser.SqlServer.Clients;
using Rebalanser.SqlServer.Store;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalanser.SqlServer.Roles
{
    class Follower
    {
        private ILogger logger;
        private IClientService clientService;
        private ResourceGroupStore store;

        public Follower(ILogger logger,
            IClientService clientService,
            ResourceGroupStore store)
        {
            this.logger = logger;
            this.clientService = clientService;
            this.store = store;
        }

        public async Task ExecuteFollowerRoleAsync(Guid followerClientId,
            ClientEvent clientEvent,
            OnChangeActions onChangeActions,
            CancellationToken token)
        {
            var self = await clientService.KeepAliveAsync(followerClientId);
            this.logger.Debug($"FOLLOWER : Keep Alive sent. Coordinator: {self.CoordinatorStatus} Client: {self.ClientStatus}");
            if (self.CoordinatorStatus == CoordinatorStatus.StopActivity)
            {
                if (self.ClientStatus == ClientStatus.Active)
                {
                    logger.Info("-------------- Stopping activity ---------------");
                    logger.Debug("FOLLOWER : Invoking on stop actions");
                    foreach (var stopAction in onChangeActions.OnStopActions)
                        stopAction.Invoke();

                    this.store.SetResources(new SetResourcesRequest() { AssignmentStatus = AssignmentStatus.AssignmentInProgress, Resources = new List<string>() });
                    await clientService.SetClientStatusAsync(followerClientId, ClientStatus.Waiting);
                    logger.Info($"FOLLOWER : State= {self.ClientStatus} -> WAITING");
                }
                else
                {
                    logger.Debug($"FOLLOWER : State= {self.ClientStatus}");
                }
            }
            else if (self.CoordinatorStatus == CoordinatorStatus.ResourcesGranted)
            {
                if (self.ClientStatus == ClientStatus.Waiting)
                {
                    if (self.AssignedResources.Any())
                        this.store.SetResources(new SetResourcesRequest() { AssignmentStatus = AssignmentStatus.ResourcesAssigned, Resources = self.AssignedResources });
                    else
                        this.store.SetResources(new SetResourcesRequest() { AssignmentStatus = AssignmentStatus.NoResourcesAssigned, Resources = new List<string>() });

                    await clientService.SetClientStatusAsync(followerClientId, ClientStatus.Active);

                    if (self.AssignedResources.Any())
                        logger.Info($"FOLLOWER : Granted resources={string.Join(",", self.AssignedResources)}");
                    else
                        logger.Info("FOLLOWER : No resources available to be assigned.");
                    foreach (var startAction in onChangeActions.OnStartActions)
                        startAction.Invoke();

                    logger.Info($"FOLLOWER : State={self.ClientStatus} -> ACTIVE");
                    logger.Info("-------------- Activity started ---------------");
                }
                else
                {
                    logger.Debug($"FOLLOWER : State= {self.ClientStatus}");
                }
            }
        }
    }
}
