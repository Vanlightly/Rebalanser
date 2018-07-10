using Consul;
using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Consul.Roles
{
    public class ClientEvent
    {
        public EventType EventType { get; set; }
        public IDistributedLock CoordinatorLock { get; set; }
        public int FencingToken { get; set; }
        public string ResourceGroup { get; set; }
        public CoordinatorToken CoordinatorToken { get; set; }
    }
}
