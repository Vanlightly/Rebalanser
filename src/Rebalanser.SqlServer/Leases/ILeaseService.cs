using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.SqlServer.Leases
{
    public interface ILeaseService
    {
        Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest acquireLeaseRequest);
        Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest renewLeaseRequest);
        Task RelinquishLeaseAsync(RelinquishLeaseRequest relinquishLeaseRequest);
    }
}
