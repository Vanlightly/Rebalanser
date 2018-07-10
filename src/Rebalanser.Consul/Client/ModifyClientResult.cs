using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Consul.Clients
{
    public enum ModifyClientResult
    {
        Ok,
        FencingTokenViolation,
        Error
    }
}
