using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Core.Logging
{
    public enum LogLevel
    {
        DEBUG=0,
        INFO=1,
        WARN=2,
        ERROR=3
    }

    public interface ILogger
    {
        void SetMinimumLevel(LogLevel logLevel);
        void Debug(string text);
        void Info(string text);
        void Error(string text);
        void Error(Exception ex);
        void Error(string text, Exception ex);
    }
}
