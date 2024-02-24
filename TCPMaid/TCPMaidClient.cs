using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;

namespace TCPMaid {
    public sealed class TCPMaidClient : TCPMaid, IDisposable {
        public new ClientOptions Options => (ClientOptions)base.Options;
        public bool Connected => Server is not null && Server.Connected;
        public Connection? Server { get; private set; }

        public event Action<Connection>? OnConnect;
        public event Action<bool, string>? OnDisconnect;
        public event Action<Message>? OnReceive;

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
            try {
                // Get the network stream
                NetworkStream NetworkStream = TcpClient.GetStream();

                // SSL (encrypted)
                if (Ssl) {
                    // Create SSL stream
                    SslStream SslStream = new(NetworkStream, false);
                    // Authenticate stream
                    await SslStream.AuthenticateAsClientAsync(ServerHost);
                    // Create encrypted connection
                    Server = new Connection(this, TcpClient, SslStream);
                }
                // Plain
                else {
                    // Create plain connection
                    Server = new Connection(this, TcpClient, NetworkStream);
                }
            }
            // Failed to create connection
            catch (Exception) {
                return false;
            }

            // Listen to disconnect event
            Server.OnDisconnect += (ByRemote, Reason) => {
                // Remove connection
                Server = null;
                // Invoke disconnect event
                OnDisconnect?.Invoke(ByRemote, Reason);
            };
            // Listen to receive event
            Server.OnReceive += (Message) => {
                // Invoke receive event
                OnReceive?.Invoke(Message);
            };
            // Listen to server
            _ = ListenForTCPMessages(Server);
            _ = ListenForUDPMessages(Server);
            // Start measuring ping
            _ = StartPingPong(Server);
            // Invoke connect event
            OnConnect?.Invoke(Server);
            // Return success
            return true;
        }

        void IDisposable.Dispose() {
            Server?.DisconnectAsync().Wait();
        }
    }
    public sealed class ClientOptions : BaseOptions {
            
    }
}
