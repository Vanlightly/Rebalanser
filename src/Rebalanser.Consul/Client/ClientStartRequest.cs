using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Consul.Clients
{
    public class ClientStartRequest
    {
        public ClientStartRequest()
        {
            AssignedResources = new List<string>();
        }

        public Guid ClientId { get; set; }
        public List<string> AssignedResources { get; set; }
    }
}
