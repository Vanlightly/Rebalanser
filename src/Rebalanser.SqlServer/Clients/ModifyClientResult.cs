using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.SqlServer.Clients
{
    public enum ModifyClientResult
    {
        Ok,
        FencingTokenViolation,
        Error
    }
}
