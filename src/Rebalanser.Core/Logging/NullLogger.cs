using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Core.Logging
{
    public class NullLogger : ILogger
    {
        public void Debug(string text)
        {
            
        }

        public void Error(string text)
        {
            
        }

        public void Error(Exception ex)
        {
            
        }

        public void Error(string text, Exception ex)
        {
            
        }

        public void Info(string text)
        {
        }

        public void SetMinimumLevel(LogLevel logLevel)
        {
        }
    }
}
