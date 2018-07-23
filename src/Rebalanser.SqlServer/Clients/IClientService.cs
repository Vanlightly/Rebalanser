using Rebalanser.SqlServer.Clients;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.SqlServer.Clients
{
    public interface IClientService
    {
        Task CreateClientAsync(string resourceGroup, Guid clientId);
        Task<List<Client>> GetActiveClientsAsync(string resourceGroup);
        Task<Client> KeepAliveAsync(Guid clientId);
        Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus);
        Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients);
        Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> clientStartRequests);
    }
}
