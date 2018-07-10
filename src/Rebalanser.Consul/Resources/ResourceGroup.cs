using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Consul.Resources
{
    public class ResourceGroup
    {
        public string Name { get; set; }
        public Guid ClientId { get; set; }
        public int FencingToken { get; set; }
        public int LeaderPollingInterval { get; set; }
    }
}
