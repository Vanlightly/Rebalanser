using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalanser.Core
{
    /// <summary>
    /// Creates a Rebalanser node that participates in a resource group
    /// </summary>
    public class RebalanserContext : IDisposable
    {
        private IRebalanserProvider rebalanserProvider;
        private CancellationTokenSource cts;

        public RebalanserContext()
        {
            this.rebalanserProvider = Providers.GetProvider();
        }

        /// <summary>
        /// Called when a rebalancing is triggered
        /// </summary>
        public event EventHandler OnCancelAssignment;

        /// <summary>
        /// Called once the node has been assigned new resources
        /// </summary>
        public event EventHandler OnAssignment;

        /// <summary>
        /// Starts the node
        /// </summary>
        /// <param name="resourceGroup">The id of the resource group</param>
        /// <returns></returns>
        public async Task StartAsync(string resourceGroup)
        {
            this.cts = new CancellationTokenSource();
            var onChangeActions = new OnChangeActions();
            onChangeActions.AddOnStartAction(StartActivity);
            onChangeActions.AddOnStopAction(CancelActivity);
            await this.rebalanserProvider.StartAsync(resourceGroup, onChangeActions, this.cts.Token);
        }

        /// <summary>
        /// Returns the list of assigned resources. This is a blocking call that blocks until
        /// resources have been assigned. Note that if there are more nodes participating in 
        /// the resource group than there are resources, then the node may be assigned zero resources. Once
        /// rebalancing is complete, this method will return with an empty collection of resources. 
        /// </summary>
        /// <returns>A list of resources assigned to the node</returns>
        public IList<string> GetAssignedResources()
        {
            return this.rebalanserProvider.GetAssignedResources();
        }

        /// <summary>
        /// See GetAssignedResources(). To prevent unbounded blocking, this method receives a 
        /// CancellationToken which can be used to unblock the method
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public IList<string> GetAssignedResources(CancellationToken token)
        {
            return this.rebalanserProvider.GetAssignedResources(token);
        }

        /// <summary>
        /// Initiates a shutdown of the node. Note that this is asynchronous and the node may still be shutting down when this method completes.
        /// </summary>
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
