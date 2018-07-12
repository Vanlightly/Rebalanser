using System;
using System.Collections.Generic;
using System.Text;

namespace Rebalanser.Core.Logging
{
    /// <summary>
    /// Temporary hack
    /// </summary>
    public class ConsoleLogger : ILogger
    {
        private LogLevel logLevel;

        public ConsoleLogger()
        {
            this.logLevel = LogLevel.DEBUG;
        }

        public ConsoleLogger(LogLevel logLevel)
        {
            this.logLevel = logLevel;
        }

        public void Error(string text)
        {
            if ((int)this.logLevel <= 3)
                Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR : {text}");
        }

        public void Error(Exception ex)
        {
            if ((int)this.logLevel <= 3)
                Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR : {ex.ToString()}");
        }

        public void Error(string text, Exception ex)
        {
            if ((int)this.logLevel <= 3)
                Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR : {text} : {ex.ToString()}");
        }

        public void Info(string text)
        {
            if ((int)this.logLevel <= 1)
                Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: INFO  : {text}");
        }

        public void Debug(string text)
        {
            if((int)this.logLevel == 0)
                Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: DEBUG  : {text}");
        }

        public void SetMinimumLevel(LogLevel logLevel)
        {
            this.logLevel = logLevel;
        }
    }
}
