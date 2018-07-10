using Consul;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.Consul.Resources
{
    class ResourceService : IResourceService
    {
        private ConsulClientConfiguration configuration;

        public ResourceService(string consulServerUrl)
        {
            this.configuration = new ConsulClientConfiguration()
            {
                Address = new Uri(consulServerUrl)
            };
        }

        public async Task<List<string>> GetResourcesAsync(string resourceGroup)
        {
            var resources = new List<string>();

            using (var client = new ConsulClient())
            {
                var response = await client.KV.List($"rebalanser/resources/{resourceGroup}");
                foreach (var item in response.Response)
                {
                    resources.Add(Encoding.UTF8.GetString(item.Value, 0, item.Value.Length));
                }
            }

            return resources;
        }
    }
}
