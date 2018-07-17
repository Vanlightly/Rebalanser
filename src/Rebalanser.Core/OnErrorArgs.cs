using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Core
{
    public class OnErrorArgs : EventArgs
    {
        public OnErrorArgs(string message, bool autoRecoveryEnabled, Exception exception) : base()
        {
            Message = message;
            AutoRecoveryEnabled = autoRecoveryEnabled;
            Exception = exception;
        }

        public string Message { get; set; }
        public bool AutoRecoveryEnabled { get; set; }
        public Exception Exception { get; set; }
    }
}
