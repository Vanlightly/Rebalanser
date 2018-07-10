using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.SqlServer.Leases
{
    public class RenewLeaseRequest
    {
        public Guid ClientId { get; set; }
        public string ResourceGroup { get; set; }
        public int FencingToken { get; set; }
    }
}
