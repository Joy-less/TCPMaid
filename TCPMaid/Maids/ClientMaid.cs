using System;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net.Security;

namespace TCPMaid {
    public sealed class ClientMaid : Maid, IDisposable {
        public new ClientOptions Options => (ClientOptions)base.Options;
        public bool Connected => Connection is not null && Connection.Connected;
        public Connection? Connection { get; private set; }

        public event Action<Connection>? OnConnect;
        public event Action<string, bool>? OnDisconnect;
        public event Action<Message>? OnReceive;

        public ClientMaid(ClientOptions? options = null) : base(options ?? new ClientOptions()) {
        }
        public async Task<bool> ConnectAsync(string ServerAddress, int ServerPort) {
            // Fail if already connected
            if (Connected) return false;
            
            // Create connection
            TcpClient? TCPClient = null;
            NetworkStream? NetworkStream = null;
            SslStream? SSLStream = null;
            Connection? Connection = null;
            try {
                // Create TCPClient
                TCPClient = new TcpClient() { NoDelay = true };
                // Connect TCPClient
                await TCPClient.ConnectAsync(ServerAddress, ServerPort);
                // Get the network stream
                NetworkStream = TCPClient.GetStream();

                // SSL (encrypted)
                if (Options.SSL) {
                    // Create SSL stream
                    SSLStream = new SslStream(NetworkStream, false);
                    // Authenticate stream
                    await SSLStream.AuthenticateAsClientAsync(ServerAddress);
                    // Create encrypted connection
                    Connection = new Connection(this, TCPClient, SSLStream);
                }
                // Plain
                else {
                    // Create plain connection
                    Connection = new Connection(this, TCPClient, NetworkStream);
                }
            }
            // Failed to create connection
            catch (Exception) {
                // Dispose objects
                TCPClient?.Dispose();
                NetworkStream?.Dispose();
                SSLStream?.Dispose();
                Connection?.Dispose();
                // Return failure
                return false;
            }

            // Listen to disconnect event
            Connection.OnDisconnect += (Reason, ByRemote) => {
                // Remove connection
                this.Connection = null;
                // Invoke disconnect event
                OnDisconnect?.Invoke(Reason, ByRemote);
            };
            // Listen to receive event
            Connection.OnReceive += (Message) => {
                // Invoke receive event
                OnReceive?.Invoke(Message);
            };
            // Set connection field
            this.Connection = Connection;
            // Invoke connect event
            OnConnect?.Invoke(Connection);
            // Return success
            return true;
        }

        void IDisposable.Dispose() {
            _ = Connection?.DisconnectAsync();
        }
    }
    public sealed class ClientOptions : Options {
        /// <summary>
        /// If <see langword="true"/>, use the server certificate to encrypt the connection.<br/>
        /// Default: <see langword="false"/>
        /// </summary>
        public bool SSL = false;
    }
}
