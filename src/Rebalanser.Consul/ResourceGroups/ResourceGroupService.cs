using Consul;
using Newtonsoft.Json;
using Rebalanser.Consul.Resources;
using Rebalanser.Consul.Roles;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalanser.Consul.ResourceGroups
{
    class ResourceGroupService : IResourceGroupService
    {
        public async Task<ResourceGroup> GetResourceGroupAsync(ConsulClient client, string resourceGroup)
        {
            var key = $"rebalanser/resourcegroups/{resourceGroup}";
            var rgDetailsResponse = await client.KV.Get(key);
            if (rgDetailsResponse.StatusCode != HttpStatusCode.OK)
            {
                // TODO
            }
            var responseText = Encoding.UTF8.GetString(rgDetailsResponse.Response.Value, 0, rgDetailsResponse.Response.Value.Length);
            var result = JsonConvert.DeserializeObject<ResourceGroup>(responseText);

            return result;
        }

        public async Task PutResourceGroupAsync(ConsulClient client, string key, ResourceGroup rgDetails)
        {
            var putPair = new KVPair(key)
            {
                Value = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(rgDetails))
            };

            var putAttempt = await client.KV.Put(putPair);
            // TODO 
        }
    }
}
