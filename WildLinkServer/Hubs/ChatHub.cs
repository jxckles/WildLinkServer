using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace WildLinkServer.Hubs
{
    public class ChatHub : Hub
    {
        private string connectionString = "Data Source=ChatApp.db";
        private static ConcurrentDictionary<string, string> ConnectedUsers = new ConcurrentDictionary<string, string>();

        public override async Task OnConnectedAsync()
        {
            var userName = Context.User?.Identity?.Name ?? "Unknown user";
            // Store the connection ID with the associated username
            ConnectedUsers[Context.ConnectionId] = userName;
            await base.OnConnectedAsync();
        }

        public async Task JoinChat(UserConnection userConnection)
        {
            // Update the connected users dictionary
            ConnectedUsers[Context.ConnectionId] = userConnection.UserName;

            await SaveConnectionID(userConnection);

            if (string.IsNullOrEmpty(userConnection.Interest))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "GeneralChat");
                await Clients.Group("GeneralChat").SendAsync("ReceiveMessage", userConnection.UserName, "has joined the general chat.");
            }
            else
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, userConnection.Interest);
                await Clients.Group(userConnection.Interest).SendAsync("ReceiveMessage", userConnection.UserName, $"has joined the {userConnection.Interest} chat.");
            }
        }

        public async Task SendMessage(string txtSender, string message, string interest)
        {
            if (string.IsNullOrEmpty(interest))
            {
                await Clients.Group("GeneralChat").SendAsync("ReceiveMessage", txtSender, message);
            }
            else
            {
                await Clients.Group(interest).SendAsync("ReceiveMessage", txtSender, message);
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var userConnection = await GetUserConnection(Context.ConnectionId);
            if (userConnection != null)
            {
                var userName = userConnection.UserName;
                var interest = userConnection.Interest;

                if (string.IsNullOrEmpty(interest))
                {
                    await Clients.Group("GeneralChat").SendAsync("ReceiveMessage", userName, "has disconnected.");
                }
                else
                {
                    await Clients.Group(interest).SendAsync("ReceiveMessage", userName, "has disconnected.");
                }

                await RemoveUserConnection(Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        private async Task<UserConnection> GetUserConnection(string connectionId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT UserName, Interest FROM Users WHERE ConnectionID = @ConnectionID";
                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ConnectionID", connectionId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            return new UserConnection
                            {
                                UserName = reader["UserName"].ToString(),
                                Interest = reader["Interest"].ToString()
                            };
                        }
                    }
                }
            }
            return null;
        }

        private async Task RemoveUserConnection(string connectionId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = "DELETE FROM Users WHERE ConnectionID = @ConnectionID";
                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ConnectionID", connectionId);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        private async Task<string> GetUserInterest(string connectionId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT Interest FROM Users WHERE ConnectionID = @ConnectionID";
                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ConnectionID", connectionId);
                    return (string)await command.ExecuteScalarAsync();
                }
            }
        }

        private async Task SaveConnectionID(UserConnection userConnection)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = "SELECT COUNT(*) FROM Users WHERE UserName = @UserName";
                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserName", userConnection.UserName);
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());

                        if (count == 0)
                        {
                            query = "INSERT INTO Users (UserName, ConnectionID, Interest) VALUES (@UserName, @ConnectionID, @Interest)";
                            command.CommandText = query;
                            command.Parameters.AddWithValue("@ConnectionID", userConnection.ConnectionID);
                            command.Parameters.AddWithValue("@Interest", userConnection.Interest);
                            await command.ExecuteNonQueryAsync();
                        }
                        else
                        {
                            query = "UPDATE Users SET ConnectionID = @ConnectionID, Interest = @Interest WHERE UserName = @UserName";
                            command.CommandText = query;
                            command.Parameters.AddWithValue("@ConnectionID", userConnection.ConnectionID);
                            command.Parameters.AddWithValue("@Interest", userConnection.Interest);
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving user connection ID: " + ex.Message);
            }
        }
    }

    public class UserConnection
    {
        public string UserName { get; set; }
        public string ConnectionID { get; set; }
        public string Interest { get; set; }
    }
}
