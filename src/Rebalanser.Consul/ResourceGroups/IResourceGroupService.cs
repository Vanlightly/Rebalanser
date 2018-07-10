using Consul;
using Rebalanser.Consul.Resources;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.Consul.ResourceGroups
{
    public interface IResourceGroupService
    {
        Task<ResourceGroup> GetResourceGroupAsync(ConsulClient client, string resourceGroup);
        Task PutResourceGroupAsync(ConsulClient client, string key, ResourceGroup rgDetails);
    }
}
