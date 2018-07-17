using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Core
{
    public class ContextOptions
    {
        public bool AutoRecoveryOnError { get; set; }
        public TimeSpan RestartDelay { get; set; }
    }
}
