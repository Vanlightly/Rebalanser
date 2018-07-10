using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.SqlServer.Store
{
    class SetResourcesRequest
    {
        public AssignmentStatus AssignmentStatus { get; set; }
        public List<string> Resources { get; set; }
    }
}
