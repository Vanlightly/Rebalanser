using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalanser.Core
{
    public class RebalanserContext : IDisposable
    {
        private IRebalanserProvider rebalanserProvider;
        private CancellationTokenSource cts;

        public RebalanserContext()
        {
            this.rebalanserProvider = Providers.GetProvider();
        }

        public event EventHandler OnCancelAssignment;
        public event EventHandler OnAssignment;

        public async Task StartAsync(string resourceGroup)
        {
            this.cts = new CancellationTokenSource();
            var onChangeActions = new OnChangeActions();
            onChangeActions.AddOnStartAction(StartActivity);
            onChangeActions.AddOnStopAction(CancelActivity);
            await this.rebalanserProvider.StartAsync(resourceGroup, onChangeActions, this.cts.Token);
        }

        public IList<string> GetAssignedResources()
        {
            return this.rebalanserProvider.GetAssignedResources();
        }

        public IList<string> GetAssignedResources(CancellationToken token)
        {
            return this.rebalanserProvider.GetAssignedResources(token);
        }

        public void Dispose()
        {
            this.cts.Cancel();
        }

        private void CancelActivity()
        {
            RaiseOnCancelAssignment(EventArgs.Empty);
        }

        protected virtual void RaiseOnCancelAssignment(EventArgs e)
        {
            OnCancelAssignment?.Invoke(this, e);
        }

        private void StartActivity()
        {
            RaiseOnAssignments(EventArgs.Empty);
        }

        protected virtual void RaiseOnAssignments(EventArgs e)
        {
            OnAssignment?.Invoke(this, e);
        }
    }
}
