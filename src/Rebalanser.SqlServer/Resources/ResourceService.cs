using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.SqlServer.Resources
{
    class ResourceService : IResourceService
    {
        private string connectionString;

        public ResourceService(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public async Task<List<string>> GetResourcesAsync(string resourceGroup)
        {
            var resources = new List<string>();
            using (var conn = new SqlConnection(this.connectionString))
            {
                await conn.OpenAsync();
                var command = conn.CreateCommand();
                command.CommandText = "SELECT ResourceName FROM [RBR].[Resources] WHERE ResourceGroup = @ResourceGroup";
                command.Parameters.Add("@ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        resources.Add(reader.GetString(0));
                }
            }

            return resources;
        }
    }
}
