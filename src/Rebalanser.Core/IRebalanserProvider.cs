using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalanser.Core
{
    public interface IRebalanserProvider
    {
        Task StartAsync(string group, OnChangeActions onChangeActions, CancellationToken token, ContextOptions contextOptions);
        Task WaitForCompletionAsync();
        IList<string> GetAssignedResources();
        IList<string> GetAssignedResources(CancellationToken token);
    }
}
