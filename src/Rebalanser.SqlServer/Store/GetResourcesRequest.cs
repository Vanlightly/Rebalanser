using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.SqlServer.Store
{
    class GetResourcesRequest
    {
        public AssignmentStatus ResourceAssignmentStatus { get; set; }
        public List<string> Resources { get; set; }
    }
}
