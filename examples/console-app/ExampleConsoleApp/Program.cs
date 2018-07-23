using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Rebalanser.Core;
using Rebalanser.SqlServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ExampleConsoleApp
{
    class Program
    {
        private static List<ClientTask> clientTasks;

        static void Main(string[] args)
        {
            Providers.Register(new SqlServerProvider("Server=(local);Database=RabbitMqScaling;Trusted_Connection=true;"));
            RunAsync().Wait();
        }

        private static async Task RunAsync()
        {
            clientTasks = new List<ClientTask>();

            using (var context = new RebalanserContext())
            {
                context.OnAssignment += (sender, args) =>
                {
                    var queues = context.GetAssignedResources();
                    foreach (var queue in queues)
                        StartConsumingQueue(queue);
                };

                context.OnCancelAssignment += (sender, args) =>
                {
                    LogInfo("Consumer subscription cancelled");
                    StopAllConsumption();
                };

                context.OnError += (sender, args) =>
                    LogInfo($"Error: {args.Message}, automatic recovery set to: {args.AutoRecoveryEnabled}, Exception: {args.Exception.Message}");

                await context.StartAsync("NotificationsGroup", new ContextOptions() { AutoRecoveryOnError = true, RestartDelay = TimeSpan.FromSeconds(30) });

                Console.WriteLine("Press enter to shutdown");
                while (!Console.KeyAvailable)
                    await Task.Delay(100);

                StopAllConsumption();
                Task.WaitAll(clientTasks.Select(x => x.Client).ToArray());
            }
        }

        private static void StartConsumingQueue(string queueName)
        {
            LogInfo("Subscription started for queue: " + queueName);
            var cts = new CancellationTokenSource();

            var task = Task.Factory.StartNew(() =>
            {
                try
                {
                    var factory = new ConnectionFactory() { HostName = "localhost" };
                    using (var connection = factory.CreateConnection())
                    using (var channel = connection.CreateModel())
                    {
                        var consumer = new EventingBasicConsumer(channel);
                        consumer.Received += (model, ea) =>
                        {
                            var body = ea.Body;
                            var message = Encoding.UTF8.GetString(body);
                            LogInfo($"{queueName} Received {message}");
                        };
                        channel.BasicConsume(queue: queueName,
                                             autoAck: true,
                                             consumer: consumer);

                        while (!cts.Token.IsCancellationRequested)
                            Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex.ToString());
                }

                if (cts.Token.IsCancellationRequested)
                    LogInfo("Cancellation signal received for " + queueName);
                else
                    LogInfo("Consumer stopped for " + queueName);
            }, TaskCreationOptions.LongRunning);

            clientTasks.Add(new ClientTask() { Cts = cts, Client = task });
        }

        private static void StopAllConsumption()
        {
            foreach (var ct in clientTasks)
                ct.Cts.Cancel();
        }

        private static void LogInfo(string text)
        {
            Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: INFO  : {text}");
        }

        private static void LogError(string text)
        {
            Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR  : {text}");
        }
    }
}
