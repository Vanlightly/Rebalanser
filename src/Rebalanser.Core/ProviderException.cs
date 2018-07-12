using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Core
{
    public class ProviderException : Exception
    {
        public ProviderException(string message)
            : base(message)
        {

        }
    }
}
