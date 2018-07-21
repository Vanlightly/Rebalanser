using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ExampleConsoleApp
{
    public class ClientTask
    {
        public CancellationTokenSource Cts { get; set; }
        public Task Client { get; set; }
    }
}
