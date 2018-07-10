using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.RabbitMQTools
{
    public class MessageManager
    {
        private static HttpClient Client;

        static MessageManager()
        {
            Client = new HttpClient();
            var byteArray = Encoding.ASCII.GetBytes($"guest:guest");
            Client.DefaultRequestHeaders.Authorization
                = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        public static async Task SendMessagesViaHttpAsync(string exchange, string routingKeyPrefix, int count, string vhost = "%2f")
        {
            for (int i = 0; i < count; i++)
            {
                string routingKey = routingKeyPrefix + i;
                var content = new StringContent("{\"properties\":{},\"routing_key\":\"" + routingKey + "\",\"payload\":\"" + routingKey + "\",\"payload_encoding\":\"string\"}", Encoding.UTF8, "application/json");
                var response = await Client.PostAsync($"http://localhost:15672/api/exchanges/{vhost}/{exchange}/publish", content);
            }
        }

        public static void SendMessagesViaClient(string exchange, string routingKeyPrefix, int count)
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    for (int i = 0; i < count; i++)
                    {
                        string routingKey = routingKeyPrefix + i;
                        string message = routingKeyPrefix + i;
                        var body = Encoding.UTF8.GetBytes(message);

                        channel.BasicPublish(exchange: exchange,
                                             routingKey: routingKey,
                                             basicProperties: null,
                                             body: body);
                    }
                }
            }
        }


    }
}
