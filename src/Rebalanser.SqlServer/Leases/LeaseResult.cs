using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.SqlServer.Leases
{
    public enum LeaseResult
    {
        NoLease,
        Granted,
        Denied,
        TransientError,
        Error
    }
}
