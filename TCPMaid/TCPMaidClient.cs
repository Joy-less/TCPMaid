using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;

namespace TCPMaid {
    public class TCPMaidClient : TCPMaidBase {
        public ClientOptions Options => (ClientOptions)BaseOptions;
        public bool Connected => Connection is not null && Connection.Connected;
        public Connection? Connection { get; private set; }

        public event Action<Connection>? OnConnect;
        public event Action<Connection, bool, string>? OnDisconnect;

        public TCPMaidClient(ClientOptions? options = null) : base(options ?? new ClientOptions()) {
        }
        public async Task<bool> ConnectAsync(string ServerHost, int ServerPort, bool Ssl = false) {
            // Return success if already connected
            if (Connected) return true;

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
            try {
                NetworkStream NetworkStream = TcpClient.GetStream();
                // SSL (encrypted)
                if (Ssl) {
                    // Create SSL stream
                    SslStream SslStream = new(NetworkStream, false);
                    // Authenticate stream
                    await SslStream.AuthenticateAsClientAsync(ServerHost);
                    // Create encrypted connection
                    Connection = new Connection(this, TcpClient, (IPEndPoint)TcpClient.Client.RemoteEndPoint!, SslStream, NetworkStream);
                }
                // Plain
                else {
                    // Create plain connection
                    Connection = new Connection(this, TcpClient, (IPEndPoint)TcpClient.Client.RemoteEndPoint!, NetworkStream, NetworkStream);
                }
            }
            // Failed to create connection
            catch (Exception) {
                return false;
            }

            // Listen to disconnect event
            Connection.OnDisconnect += (ByRemote, Reason) => {
                // Invoke disconnect event
                OnDisconnect?.Invoke(Connection, ByRemote, Reason);
            };
            // Listen to server
            _ = ListenForMessages(Connection);
            // Start measuring ping
            _ = StartPingPong(Connection);
            // Invoke connect event
            OnConnect?.Invoke(Connection);
            // Return success
            return true;
        }
    }
    public sealed class ClientOptions : BaseOptions {
            
    }
}
