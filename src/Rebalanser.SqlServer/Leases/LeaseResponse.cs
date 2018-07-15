using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.SqlServer.Leases
{
    public class LeaseResponse
    {
        public LeaseResult Result { get; set; }
        public Lease Lease { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }

        public bool IsErrorResponse()
        {
            return Result == LeaseResult.TransientError
                || Result == LeaseResult.Error;
        }
    }
}
