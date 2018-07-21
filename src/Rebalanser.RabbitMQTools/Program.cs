using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace Rebalanser.RabbitMQTools
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Invalid command");
                Environment.ExitCode = 1;
            }

            var builder = new ConfigurationBuilder().AddCommandLine(args);
            IConfigurationRoot configuration = builder.Build();

            var command = GetMandatoryArg(configuration, "Command");
            var backend = GetMandatoryArg(configuration, "Backend");
            var connection = GetMandatoryArg(configuration, "ConnString");

            var rabbitConn = new RabbitConnection()
            {
                Host = GetOptionalArg(configuration, "RabbitHost", "localhost"),
                VirtualHost = GetOptionalArg(configuration, "RabbitVHost", "/"),
                Username = GetOptionalArg(configuration, "RabbitUser", "guest"),
                Password = GetOptionalArg(configuration, "RabbitPassword", "guest"),
                Port = int.Parse(GetOptionalArg(configuration, "RabbitPort", "5672")),
                ManagementPort = int.Parse(GetOptionalArg(configuration, "RabbitMgmtPort", "15672"))
            };

            var queueInventory = new QueueInventory()
            {
                ConsumerGroup = GetMandatoryArg(configuration, "ConsumerGroup"),
                ExchangeName = GetMandatoryArg(configuration, "ExchangeName"),
                QueueCount = int.Parse(GetMandatoryArg(configuration, "QueueCount")),
                QueuePrefix = GetMandatoryArg(configuration, "QueuePrefix"),
                LeaseExpirySeconds = int.Parse(GetMandatoryArg(configuration, "LeaseExpirySeconds"))
            };

            if (command.Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                if (backend.Equals("mssql", StringComparison.OrdinalIgnoreCase))
                    DeployQueuesWithSqlBackend(connection, rabbitConn, queueInventory);
                else
                    Console.WriteLine("Only mssql backend is supported");
            }
            else
                Console.WriteLine("Only create command is supported");
        }

        public static void DeployQueuesWithSqlBackend(string connection, RabbitConnection rabbitConn, QueueInventory queueInventory)
        {
            try
            {
                QueueManager.Initialize(connection, rabbitConn);
                QueueManager.EnsureResourceGroup(queueInventory.ConsumerGroup, queueInventory.LeaseExpirySeconds);

                Console.WriteLine("Phase 1 - Reconcile Backend with existing RabbitMQ queues ---------");
                QueueManager.ReconcileQueuesSqlAsync(queueInventory.ConsumerGroup, queueInventory.QueuePrefix).Wait();

                Console.WriteLine("Phase 2 - Ensure supplied queue count is deployed ---------");
                var existingQueues = QueueManager.GetQueuesAsync(queueInventory.QueuePrefix).Result;
                if (existingQueues.Count > queueInventory.QueueCount)
                {
                    var queuesToRemove = existingQueues.Count - queueInventory.QueueCount;
                    for (int i = 0; i < queuesToRemove; i++)
                        QueueManager.RemoveQueueSqlAsync(queueInventory.ConsumerGroup, queueInventory.QueuePrefix).Wait();
                }
                else if (existingQueues.Count < queueInventory.QueueCount)
                {
                    var queuesToAdd = queueInventory.QueueCount - existingQueues.Count;
                    for (int i = 0; i < queuesToAdd; i++)
                        QueueManager.AddQueueSqlAsync(queueInventory.ConsumerGroup, queueInventory.ExchangeName, queueInventory.QueuePrefix).Wait();
                }

                Console.WriteLine("Complete");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }

        public static string GetMandatoryArg(IConfiguration configuration, string argName)
        {
            var value = configuration[argName];
            if (string.IsNullOrEmpty(value))
                throw new Exception($"No argument {argName}");

            return value;
        }

        public static string GetOptionalArg(IConfiguration configuration, string argName, string defaultValue)
        {
            var value = configuration[argName];
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            return value;
        }
    }
}
