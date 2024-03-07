using System;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net.Security;

namespace TCPMaid {
    public sealed class ClientMaid : Maid, IDisposable {
        public new ClientOptions Options => (ClientOptions)base.Options;
        public bool Connected => Channel is not null && Channel.Connected;
        public Channel? Channel { get; private set; }

        public event Action<Channel>? OnConnect;
        public event Action<string, bool>? OnDisconnect;
        public event Action<Message>? OnReceive;

        public ClientMaid(ClientOptions? options = null) : base(options ?? new ClientOptions()) {
        }
        public async Task<bool> ConnectAsync(string ServerAddress, int ServerPort) {
            // Fail if already connected
            if (Connected) return false;
            
            // Create channel
            TcpClient? TCPClient = null;
            NetworkStream? NetworkStream = null;
            SslStream? SSLStream = null;
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
                    // Create encrypted channel
                    Channel = new Channel(this, TCPClient, SSLStream);
                }
                // Plain
                else {
                    // Create plain channel
                    Channel = new Channel(this, TCPClient, NetworkStream);
                }
            }
            // Failed to create channel
            catch (Exception) {
                // Dispose objects
                TCPClient?.Dispose();
                NetworkStream?.Dispose();
                SSLStream?.Dispose();
                Channel?.Dispose();
                Channel = null;
                // Return failure
                return false;
            }

            // Listen to disconnect event
            Channel.OnDisconnect += (Reason, ByRemote) => {
                // Remove channel
                Channel = null;
                // Invoke disconnect event
                OnDisconnect?.Invoke(Reason, ByRemote);
            };
            // Listen to receive event
            Channel.OnReceive += (Message) => {
                // Invoke receive event
                OnReceive?.Invoke(Message);
            };
            // Invoke connect event
            OnConnect?.Invoke(Channel);
            // Return success
            return true;
        }

        void IDisposable.Dispose() {
            _ = Channel?.DisconnectAsync();
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
