﻿using Consul;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.Consul.Clients
{
    class ClientService : IClientService
    {
        public async Task CreateClientAsync(ConsulClient consulClient, string resourceGroup, Guid clientId)
        {
            var client = new Client()
            {
                AssignedResources = new List<string>(),
                ClientId = clientId,
                ClientStatus = ClientStatus.Waiting,
                CoordinatorStatus = CoordinatorStatus.StopActivity,
                
            }
                command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
                command.Parameters.Add("@ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
                command.Parameters.Add("@ClientStatus", SqlDbType.TinyInt).Value = ClientStatus.Waiting;
                command.Parameters.Add("@CoordinatorStatus", SqlDbType.TinyInt).Value = CoordinatorStatus.StopActivity;
            
        }

        public async Task<List<Client>> GetClientsAsync(string resourceGroup)
        {
            var clients = new List<Client>();
            using (var conn = new SqlConnection(this.connectionString))
            {
                await conn.OpenAsync();
                var command = conn.CreateCommand();
                command.CommandText = @"SELECT [ClientId]
      ,[LastKeepAlive]
      ,[ClientStatus]
      ,[CoordinatorStatus]
      ,GETUTCDATE() AS [TimeNow]
FROM [RBR].[Clients] 
WHERE ResourceGroup = @ResourceGroup";
                command.Parameters.Add("@ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var client = new Client();
                        client.ClientStatus = (ClientStatus)(byte)reader["ClientStatus"];
                        client.CoordinatorStatus = (CoordinatorStatus)(byte)reader["CoordinatorStatus"];
                        client.ClientId = (Guid)reader["ClientId"];
                        client.LastKeepAlive = (DateTime)reader["LastKeepAlive"];
                        client.TimeNow = (DateTime)reader["TimeNow"];
                        clients.Add(client);
                    }
                }
            }

            return clients;
        }

        public async Task<Client> KeepAliveAsync(Guid clientId)
        {
            using (var conn = new SqlConnection(this.connectionString))
            {
                await conn.OpenAsync();
                var command = conn.CreateCommand();
                command.CommandText = @"UPDATE [RBR].[Clients]
   SET [LastKeepAlive] = GETUTCDATE()
   OUTPUT inserted.*
WHERE ClientId = @ClientId";
                command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var client = new Client();
                        client.ClientId = clientId;
                        client.ClientStatus = (ClientStatus)(byte)reader["ClientStatus"];
                        client.CoordinatorStatus = (CoordinatorStatus)(byte)reader["CoordinatorStatus"];
                        client.LastKeepAlive = (DateTime)reader["LastKeepAlive"];
                        client.AssignedResources = reader["Resources"].ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                        return client;
                    }
                }
            }

            throw new Exception("No client with this id!");
        }

        public async Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus)
        {
            using (var conn = new SqlConnection(this.connectionString))
            {
                await conn.OpenAsync();
                var command = conn.CreateCommand();
                command.CommandText = @"UPDATE [RBR].[Clients]
   SET [ClientStatus] = @ClientStatus
WHERE ClientId = @ClientId";

                command.Parameters.Add("@ClientStatus", SqlDbType.TinyInt).Value = clientStatus;
                command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> clientStartRequests)
        {
            using (var conn = new SqlConnection(this.connectionString))
            {
                await conn.OpenAsync();
                var command = conn.CreateCommand();
                command.CommandText = @"UPDATE [RBR].[Clients]
   SET [CoordinatorStatus] = @CoordinatorStatus
      ,[Resources] = @Resources
      ,[FencingToken] = @FencingToken
WHERE ClientId = @ClientId
AND FencingToken <= @FencingToken
SELECT @@ROWCOUNT";

                foreach (var request in clientStartRequests)
                {
                    command.Parameters.Clear();
                    command.Parameters.Add("@CoordinatorStatus", SqlDbType.TinyInt).Value = CoordinatorStatus.ResourcesGranted;
                    command.Parameters.Add("@FencingToken", SqlDbType.Int).Value = fencingToken;
                    command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = request.ClientId;
                    command.Parameters.Add("@Resources", SqlDbType.VarChar, -1).Value = string.Join(",", request.AssignedResources);
                    var result = (int) await command.ExecuteScalarAsync();
                    if (result == 0)
                        return ModifyClientResult.FencingTokenViolation;
                }

                return ModifyClientResult.Ok;
            }
        }

        public async Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients)
        {
            using (var conn = new SqlConnection(this.connectionString))
            {
                await conn.OpenAsync();
                var command = conn.CreateCommand();
                command.CommandText = GetSetStatusQuery(clients);
                command.Parameters.Add("@CoordinatorStatus", SqlDbType.TinyInt).Value = CoordinatorStatus.StopActivity;
                command.Parameters.Add("@FencingToken", SqlDbType.Int).Value = fencingToken;

                for (int i = 0; i < clients.Count; i++)
                    command.Parameters.Add($"@Client{i}", SqlDbType.UniqueIdentifier).Value = clients[i].ClientId;

                var rowsUpdated = (int)await command.ExecuteScalarAsync();

                if (rowsUpdated != clients.Count)
                    return ModifyClientResult.FencingTokenViolation;

                return ModifyClientResult.Ok;
            }
        }

        private string GetSetStatusQuery(List<Client> clients)
        {
            var sb = new StringBuilder();
            sb.Append(@"UPDATE [RBR].[Clients]
   SET [CoordinatorStatus] = @CoordinatorStatus
      ,[Resources] = ''
      ,[FencingToken] = @FencingToken
WHERE ClientId IN (");

            for (int i = 0; i < clients.Count; i++)
            {
                if (i > 0)
                    sb.Append(",");

                sb.Append($"@Client{i}");
            }

            sb.Append(@")
AND FencingToken <= @FencingToken
SELECT @@ROWCOUNT");

            return sb.ToString();
        }
    }
}
