using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text;
using System.Threading.Tasks;

namespace Rebalanser.SqlServer.Connections
{
    public class ConnectionHelper
    {
        public static async Task<SqlConnection> GetOpenConnectionAsync(string connectionString)
        {
            int tries = 0;
            while(tries <= 3)
            {
                tries++;
                try
                {
                    var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();
                    return connection;
                }
                catch(SqlException ex)
                {
                    if (ex.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        if (tries == 3)
                            throw;

                        // wait 1, 2, 4 seconds -- would be nice to not to delay cancellation here
                        await Task.Delay(TimeSpan.FromSeconds(tries * 2));
                    }
                    else
                        throw;
                }
            }

            return null;
        }
    }
}
