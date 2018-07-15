using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Core
{
    public class OnChangeActions
    {
        public OnChangeActions()
        {
            OnStartActions = new List<Action>();
            OnStopActions = new List<Action>();
            OnErrorActions = new List<Action<string, Exception>>();
        }

        public List<Action> OnStartActions { get; set; }
        public List<Action> OnStopActions { get; set; }
        public List<Action<string, Exception>> OnErrorActions { get; set; }

        public void AddOnStartAction(Action action)
        {
            OnStartActions.Add(action);
        }

        public void AddOnStopAction(Action action)
        {
            OnStopActions.Add(action);
        }

        public void AddOnErrorAction(Action<string, Exception> action)
        {
            OnErrorActions.Add(action);
        }
    }
}
