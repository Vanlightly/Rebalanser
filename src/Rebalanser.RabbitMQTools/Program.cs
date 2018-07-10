using System;

namespace Rebalanser.RabbitMQTools
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Choose your backend [sql/consul]:");
            string backend = Console.ReadLine();

            Console.WriteLine("Enter your consumer group:");
            string consumerGroup = Console.ReadLine();
            QueueManager.EnsureResourceGroup(consumerGroup);

            while (true)
            {
                if (backend.Equals("sql"))
                {
                    QueueManager.ReconcileQueuesSqlAsync(consumerGroup).Wait();

                    Console.WriteLine("+ to add a queue, - to remove, s rk 10 to send messages, l to list queues in each store, q to quit");
                    var input = Console.ReadLine();

                    if (input == "+")
                        QueueManager.AddQueueSqlAsync(consumerGroup).Wait();
                    else if (input == "-")
                        QueueManager.RemoveQueueSqlAsync(consumerGroup).Wait();
                    else if (input == "q")
                        break;
                    else if (input.StartsWith("s"))
                    {
                        var rk = input.Split()[1];
                        var count = int.Parse(input.Split()[2]);
                        MessageManager.SendMessagesViaClient(consumerGroup + "Ex", rk, count);
                    }

                    var queues = QueueManager.GetQueuesAsync(consumerGroup).Result;
                    Console.WriteLine(string.Join(',', queues));
                }
                else if (backend.Equals("consul"))
                {
                    QueueManager.ReconcileQueuesConsulAsync(consumerGroup).Wait();

                    Console.WriteLine("+ to add a queue, - to remove, s rk 10 to send messages, l to list queues in each store, q to quit");
                    var input = Console.ReadLine();

                    if (input == "+")
                        QueueManager.AddQueueConsulAsync(consumerGroup).Wait();
                    else if (input == "-")
                        QueueManager.RemoveQueueConsulAsync(consumerGroup).Wait();
                    else if (input == "q")
                        break;
                    else if (input.StartsWith("s"))
                    {
                        var rk = input.Split()[1];
                        var count = int.Parse(input.Split()[2]);
                        MessageManager.SendMessagesViaClient(consumerGroup + "Ex", rk, count);
                    }

                    var queues = QueueManager.GetQueuesAsync(consumerGroup).Result;
                    Console.WriteLine(string.Join(',', queues));
                }
            }
        }
    }
}
