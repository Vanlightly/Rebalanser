using Consul;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.RabbitMQTools
{
    public class QueueManager
    {
        private static string ConnStr;
        private static HttpClient Client;

        public static void Initialize(string connectionString, RabbitConnection rabbitConnection)
        {
            ConnStr = connectionString;

            Client = new HttpClient();
            Client.BaseAddress = new Uri($"http://{rabbitConnection.Host}:{rabbitConnection.ManagementPort}/api/");
            var byteArray = Encoding.ASCII.GetBytes($"{rabbitConnection.Username}:{rabbitConnection.Password}");
            Client.DefaultRequestHeaders.Authorization
                = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        public static void EnsureResourceGroup(string resourceGroup, int leaseExpirySeconds)
        {
            bool rgExists = false;

            using (var conn = new SqlConnection(ConnStr))
            {
                conn.Open();
                var command = conn.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM RBR.ResourceGroups WHERE ResourceGroup = @ResourceGroup";
                command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
                int count = (int)command.ExecuteScalar();
                rgExists = count == 1;

                if (!rgExists)
                {
                    command.Parameters.Clear();
                    command.CommandText = $"INSERT INTO RBR.ResourceGroups(ResourceGroup, FencingToken, LeaseExpirySeconds) VALUES(@ResourceGroup, 1, @LeaseExpirySeconds)";
                    command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
                    command.Parameters.Add("LeaseExpirySeconds", SqlDbType.Int).Value = leaseExpirySeconds;
                    command.ExecuteNonQuery();
                    Console.WriteLine("Created consumer group");
                }
                else
                {
                    command.Parameters.Clear();
                    command.CommandText = $"UPDATE RBR.ResourceGroups SET LeaseExpirySeconds = @LeaseExpirySeconds WHERE ResourceGroup = @ResourceGroup";
                    command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
                    command.Parameters.Add("LeaseExpirySeconds", SqlDbType.Int).Value = leaseExpirySeconds;
                    command.ExecuteNonQuery();
                    Console.WriteLine("Consumer group exists");
                }
            }
        }

        public static async Task ReconcileQueuesSqlAsync(string resourceGroup, string queuePrefix)
        {
            var rabbitQueues = await GetQueuesFromRabbitMQAsync(queuePrefix);
            Console.WriteLine($"{rabbitQueues.Count} matching queues in RabbitMQ");
            var sqlQueues = GetQueuesFromSqlServer(resourceGroup);
            Console.WriteLine($"{sqlQueues.Count} matching registered queues in SQL Server backend");

            foreach (var queue in rabbitQueues)
            {
                if (!sqlQueues.Any(x => x == queue))
                {
                    InsertQueueSql(resourceGroup, queue);
                    Console.WriteLine("Added queue to backend");
                }
            }

            foreach (var queue in sqlQueues)
            {
                if (!rabbitQueues.Any(x => x == queue))
                {
                    DeleteQueueSql(resourceGroup, queue);
                    Console.WriteLine("Removed queue from backend");
                }
            }
        }

        //public static async Task ReconcileQueuesConsulAsync(string resourceGroup)
        //{
        //    var rabbitQueues = await GetQueuesFromRabbitMQAsync(resourceGroup);
        //    var consulQueues = await GetQueuesFromConsulAsync(resourceGroup);

        //    foreach (var queue in rabbitQueues)
        //    {
        //        if (!consulQueues.Any(x => x == queue))
        //        {
        //            await PutQueueConsulAsync(resourceGroup, queue);
        //        }
        //    }

        //    foreach (var queue in consulQueues)
        //    {
        //        if (!rabbitQueues.Any(x => x == queue))
        //        {
        //            await DeleteQueueConsulAsync(resourceGroup, queue);
        //        }
        //    }

        //    Console.WriteLine(string.Join(',', rabbitQueues));
        //}

        public static async Task AddQueueSqlAsync(string resourceGroup, string exchangeName, string queuePrefix)
        {
            var lastQueue = await GetMaxQueueAsync(queuePrefix);
            if (lastQueue == null)
            {
                var queueName = queuePrefix + "_0001";
                await PutQueueRabbitMQAsync(exchangeName, queueName);
                InsertQueueSql(resourceGroup, queueName);
                Console.WriteLine($"Queue {queueName} added to consumer group {resourceGroup}");
            }
            else
            {
                var qNumber = lastQueue.Substring(lastQueue.IndexOf("_") + 1);
                int nextNumber = (int.Parse(qNumber)) + 1;
                var queueName = queuePrefix + "_" + nextNumber.ToString().PadLeft(4, '0');
                await PutQueueRabbitMQAsync(exchangeName, queueName);
                InsertQueueSql(resourceGroup, queueName);
                Console.WriteLine($"Queue {queueName} added to consumer group {resourceGroup}");
            }
        }

        //public static async Task AddQueueConsulAsync(string resourceGroup)
        //{
        //    var lastQueue = await GetMaxQueueAsync(resourceGroup);
        //    if (lastQueue == null)
        //    {
        //        await PutQueueRabbitMQAsync(resourceGroup + "Ex", resourceGroup + "Q_0001");
        //        await PutQueueConsulAsync(resourceGroup, resourceGroup + "Q_0001");
        //    }
        //    else
        //    {
        //        var qNumber = lastQueue.Substring(lastQueue.IndexOf("_") + 1);
        //        int nextNumber = (int.Parse(qNumber)) + 1;
        //        var queueName = resourceGroup + "Q_" + nextNumber.ToString().PadLeft(4, '0');
        //        await PutQueueRabbitMQAsync(resourceGroup + "Ex", queueName);
        //        await PutQueueConsulAsync(resourceGroup, queueName);
        //    }
        //}

        public static async Task RemoveQueueSqlAsync(string resourceGroup, string queuePrefix)
        {
            var lastQueue = await GetMaxQueueAsync(queuePrefix);
            if (lastQueue != null)
            {
                await DeleteQueueRabbitMQAsync(lastQueue);
                DeleteQueueSql(resourceGroup, lastQueue);
                Console.WriteLine($"Queue {lastQueue} removed from consumer group {resourceGroup}");
            }
        }

        //public static async Task RemoveQueueConsulAsync(string resourceGroup)
        //{
        //    var lastQueue = await GetMaxQueueAsync(resourceGroup);
        //    await DeleteQueueRabbitMQAsync(lastQueue);
        //    await DeleteQueueConsulAsync(resourceGroup, lastQueue);
        //}

        public static async Task<List<string>> GetQueuesAsync(string queuePrefix)
        {
            return await GetQueuesFromRabbitMQAsync(queuePrefix);
        }

        private static async Task<string> GetMaxQueueAsync(string queuePrefix)
        {
            var queuesRabbit = await GetQueuesFromRabbitMQAsync(queuePrefix);

            return queuesRabbit.OrderBy(x => x).LastOrDefault();
        }

        private static async Task<List<string>> GetQueuesFromRabbitMQAsync(string queuePrefix)
        {
            var response = await Client.GetAsync("queues");
            var json = await response.Content.ReadAsStringAsync();
            var queues = JArray.Parse(json);
            var queueNames = queues.Select(x => x["name"].Value<string>()).Where(x => x.StartsWith(queuePrefix)).ToList();

            return queueNames;
        }

        private static List<string> GetQueuesFromSqlServer(string resourceGroup)
        {
            var queues = new List<string>();

            using (var conn = new SqlConnection(ConnStr))
            {
                conn.Open();
                var command = conn.CreateCommand();
                command.CommandText = "SELECT ResourceName FROM RBR.Resources WHERE ResourceGroup = @ResourceGroup";
                command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        queues.Add(reader.GetString(0));
                    }
                }
            }

            return queues;
        }

        //private static async Task<List<string>> GetQueuesFromConsulAsync(string resourceGroup)
        //{
        //    var queues = new List<string>();

        //    using (var client = new ConsulClient())
        //    {
        //        var response = await client.KV.List("queues/" + resourceGroup);
        //        foreach (var q in response.Response)
        //            queues.Add(Encoding.UTF8.GetString(q.Value, 0, q.Value.Length));
        //    }

        //    return queues;
        //}

        private static async Task PutQueueRabbitMQAsync(string exchange, string queueName, string vhost = "%2f")
        {
            if (vhost == "/")
                vhost = "%2f";

            var createExchangeContent = new StringContent("{\"type\":\"x-consistent-hash\",\"auto_delete\":false,\"durable\":true,\"internal\":false,\"arguments\":{}}", Encoding.UTF8, "application/json");
            var createExchangeResponse = await Client.PutAsync($"exchanges/{vhost}/{exchange}", createExchangeContent);
            if (!createExchangeResponse.IsSuccessStatusCode)
                throw new Exception("Failed to create exchange");

            var createQueueContent = new StringContent("{ \"durable\":true}", Encoding.UTF8, "application/json");
            var createQueueResponse = await Client.PutAsync($"queues/{vhost}/{queueName}", createQueueContent);
            if (!createQueueResponse.IsSuccessStatusCode)
                throw new Exception("Failed to create queue");

            var createBindingsContent = new StringContent("{\"routing_key\":\"10\",\"arguments\":{}}", Encoding.UTF8, "application/json");
            var createBindingsResponse = await Client.PostAsync($"bindings/{vhost}/e/{exchange}/q/{queueName}", createBindingsContent);
            if (!createBindingsResponse.IsSuccessStatusCode)
                throw new Exception("Failed to create exchange to queue bindings");
        }

        private static void InsertQueueSql(string resourceGroup, string queueName)
        {
            using (var conn = new SqlConnection(ConnStr))
            {
                conn.Open();
                var command = conn.CreateCommand();
                command.CommandText = $"INSERT INTO RBR.Resources(ResourceGroup, ResourceName) VALUES(@ResourceGroup, @ResourceName)";
                command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
                command.Parameters.Add("ResourceName", SqlDbType.VarChar, 1000).Value = queueName;
                command.ExecuteNonQuery();
            }
        }

        //public static async Task PutQueueConsulAsync(string resourceGroup, string queueName)
        //{
        //    using (var client = new ConsulClient())
        //    {
        //        var putPair = new KVPair($"queues/{resourceGroup}/{queueName}")
        //        {
        //            Value = Encoding.UTF8.GetBytes(queueName)
        //        };

        //        var putAttempt = await client.KV.Put(putPair);
        //    }
        //}

        private static async Task DeleteQueueRabbitMQAsync(string queueName, string vhost = "%2f")
        {
            if (vhost == "/")
                vhost = "%2f";

            var response = await Client.DeleteAsync($"queues/{vhost}/{queueName}");
        }

        private static void DeleteQueueSql(string resourceGroup, string queueName)
        {
            using (var conn = new SqlConnection(ConnStr))
            {
                conn.Open();
                var command = conn.CreateCommand();
                command.CommandText = $"DELETE FROM RBR.Resources WHERE ResourceGroup = @ResourceGroup AND ResourceName = @ResourceName";
                command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
                command.Parameters.Add("ResourceName", SqlDbType.VarChar, 1000).Value = queueName;
                command.ExecuteNonQuery();
            }
        }

        //public static async Task DeleteQueueConsulAsync(string resourceGroup, string queueName)
        //{
        //    using (var client = new ConsulClient())
        //    {
        //        var response = await client.KV.Delete($"queues/{resourceGroup}/{queueName}");
        //    }
        //}
    }
}
