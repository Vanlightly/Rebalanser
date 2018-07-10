using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.Consul.Resources
{
    public interface IResourceService
    {
        Task<List<string>> GetResourcesAsync(string resourceGroup);
    }
}
