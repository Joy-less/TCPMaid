using System.Net.Sockets;
using System.Net.Security;

namespace TCPMaid {
    /// <summary>
    /// A maid that helps setup a channel to the server.
    /// </summary>
    public sealed class ClientMaid : Maid, IDisposable {
        /// <summary>
        /// The preferences for this maid.
        /// </summary>
        public new ClientOptions Options => (ClientOptions)base.Options;
        /// <summary>
        /// Whether the client has a connected channel to the server.
        /// </summary>
        public bool Connected => Channel is not null && Channel.Connected;
        /// <summary>
        /// The connected channel to the server.
        /// </summary>
        public Channel? Channel { get; private set; }

        /// <summary>
        /// Triggers when the channel is connected.
        /// </summary>
        public event Action<Channel>? OnConnect;
        /// <summary>
        /// Triggers when the channel is abandoned. (Reason, ByRemote)
        /// </summary>
        public event Action<string, bool>? OnDisconnect;
        /// <summary>
        /// Triggers when the channel receives a message.
        /// </summary>
        public event Action<Message>? OnReceive;

        /// <summary>
        /// Creates a new client maid with the given options.
        /// </summary>
        public ClientMaid(ClientOptions? options = null) : base(options ?? new ClientOptions()) {
        }
        /// <summary>
        /// Attempts to connect to the server.
        /// </summary>
        /// <param name="ServerAddress">The URL or IP address of the server.</param>
        /// <param name="ServerPort">The port number the server is listening on.</param>
        /// <returns><see langword="true"/> if connected successfully; <see langword="false"/> otherwise.</returns>
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
    /// <summary>
    /// The preferences for a client maid.
    /// </summary>
    public sealed class ClientOptions : Options {
        /// <summary>
        /// Whether to use the server certificate to encrypt the connection.<br/>
        /// Default: <see langword="false"/>
        /// </summary>
        public bool SSL = false;
    }
}
