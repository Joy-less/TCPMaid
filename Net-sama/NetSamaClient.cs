using System;
using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;
using System.Net.Security;

#nullable enable

namespace NetSama {
    public class NetCodeClient : NetSamaBase {
        public readonly bool UseSsl;

        public ClientOptions Options => (ClientOptions)BaseOptions;
        public bool Connected => Connection is not null && Connection.Connected;
        public Connection? Connection { get; private set; }

        public event Action<Connection>? OnConnect;
        public event Action<Connection, bool, string>? OnDisconnect;

        public NetCodeClient(bool use_ssl = false, ClientOptions? options = null) : base(options ?? new ClientOptions()) {
            UseSsl = use_ssl;
        }
        public async Task<bool> ConnectAsync(string ServerIpAddress, int ServerPort) {
            // Return success if already connected
            if (Connected) return true;

            // Create TcpClient
            TcpClient TcpClient = new() { NoDelay = true };

            // Try connect to server
            try {
                await TcpClient.ConnectAsync(ServerIpAddress, ServerPort);
            }
            // Failed to connect
            catch (Exception) {
                return false;
            }

            // Create connection (SSL or not)
            try {
                NetworkStream NetworkStream = TcpClient.GetStream();
                // SSL (encrypted)
                if (UseSsl) {
                    // Create SSL stream
                    SslStream SslStream = new(NetworkStream, false);
                    // Authenticate stream
                    await SslStream.AuthenticateAsClientAsync(ServerIpAddress);
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
            Connection.OnDisconnect += (ByRemote, Reason) => OnDisconnect?.Invoke(Connection, ByRemote, Reason);
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
