using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;

namespace TCPMaid {
    public sealed class TCPMaidClient : TCPMaidBase {
        public ClientOptions Options => (ClientOptions)BaseOptions;
        public bool Connected => Connection is not null && Connection.Connected;
        public Connection? Connection { get; private set; }

        public event Action<Connection>? OnConnect;
        public event Action<Connection, bool, string>? OnDisconnect;

        public TCPMaidClient(ClientOptions? options = null) : base(options ?? new ClientOptions()) {
        }
        public async Task<bool> ConnectAsync(string ServerHost, int ServerPort, bool Ssl = false) {
            // Return failure if already connected
            if (Connected) return false;

            // Create TcpClient
            TcpClient TcpClient = new() { NoDelay = true };

            // Try connect to server
            try {
                await TcpClient.ConnectAsync(ServerHost, ServerPort);
            }
            // Failed to connect
            catch (Exception) {
                return false;
            }

            // Create connection (SSL or not)
            Connection Client;
            try {
                NetworkStream NetworkStream = TcpClient.GetStream();
                // SSL (encrypted)
                if (Ssl) {
                    // Create SSL stream
                    SslStream SslStream = new(NetworkStream, false);
                    // Authenticate stream
                    await SslStream.AuthenticateAsClientAsync(ServerHost);
                    // Create encrypted connection
                    Connection = Client = new Connection(this, TcpClient, (IPEndPoint)TcpClient.Client.RemoteEndPoint!, SslStream, NetworkStream);
                }
                // Plain
                else {
                    // Create plain connection
                    Connection = Client = new Connection(this, TcpClient, (IPEndPoint)TcpClient.Client.RemoteEndPoint!, NetworkStream, NetworkStream);
                }
            }
            // Failed to create connection
            catch (Exception) {
                return false;
            }

            // Listen to disconnect event
            Client.OnDisconnect += (ByRemote, Reason) => {
                // Remove connection
                Connection = null;
                // Invoke disconnect event
                OnDisconnect?.Invoke(Client, ByRemote, Reason);
            };
            // Listen to server
            _ = ListenForMessages(Client);
            // Start measuring ping
            _ = StartPingPong(Client);
            // Invoke connect event
            OnConnect?.Invoke(Client);
            // Return success
            return true;
        }
    }
    public sealed class ClientOptions : BaseOptions {
            
    }
}
