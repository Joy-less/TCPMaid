using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace TCPMaid {
    /// <summary>
    /// A maid that helps setup channels to multiple clients.
    /// </summary>
    public sealed class ServerMaid : Maid, IDisposable {
        /// <summary>
        /// The preferences for this maid.
        /// </summary>
        public new ServerOptions Options => (ServerOptions)base.Options;
        /// <summary>
        /// Whether the server is started.
        /// </summary>
        public bool Running { get; private set; }

        /// <summary>
        /// Triggers when the server is started.
        /// </summary>
        public event Action? OnStart;
        /// <summary>
        /// Triggers when the server is stopped.
        /// </summary>
        public event Action? OnStop;
        /// <summary>
        /// Triggers when a new channel is connected.
        /// </summary>
        public event Action<Channel>? OnConnect;
        /// <summary>
        /// Triggers when a channel is abandoned.
        /// </summary>
        public event Action<Channel, string, bool>? OnDisconnect;
        /// <summary>
        /// Triggers when a channel receives a message.
        /// </summary>
        public event Action<Channel, Message>? OnReceive;

        private TcpListener? Listener;
        private readonly ConcurrentDictionary<Channel, byte> Channels = new();

        /// <summary>
        /// Creates a new server maid with the given options.
        /// </summary>
        public ServerMaid(ServerOptions? options = null) : base(options ?? new ServerOptions()) {
        }
        /// <summary>
        /// Starts listening for clients on the given port, unless already running.
        /// </summary>
        public void Start(int Port) {
            // Ensure server is not running
            if (Running) return;
            // Start listener
            Listener = TcpListener.Create(Port);
            Listener.Server.NoDelay = true;
            Listener.Start();
            // Accept clients
            _ = AcceptAsync();
            // Mark server as running
            Running = true;
            // Invoke start event
            OnStart?.Invoke();
        }
        /// <summary>
        /// Stops listening for clients and disconnects existing clients.
        /// </summary>
        public void Stop() {
            // Mark server as not running
            if (!Running) return;
            Running = false;
            // Disconnect from all clients
            _ = DisconnectAllAsync(DisconnectReason.ServerShutdown);
            // Stop listener
            Listener?.Stop();
            Listener = null;
            // Invoke stop event
            OnStop?.Invoke();
        }
        /// <summary>
        /// Sends a message to every connected client.
        /// </summary>
        public async Task BroadcastAsync(Message Message, Channel? Exclude = null, Predicate<Channel>? ExcludeWhere = null) {
            // Send message to each client
            await EachClientAsync(async Client => await Client.SendAsync(Message), Exclude, ExcludeWhere);
        }
        /// <summary>
        /// Disconnects every connected client.
        /// </summary>
        public async Task DisconnectAllAsync(string Reason = DisconnectReason.None, Channel? Exclude = null, Predicate<Channel>? ExcludeWhere = null) {
            // Disconnect each client
            await EachClientAsync(async Client => await Client.DisconnectAsync(Reason), Exclude, ExcludeWhere);
        }
        /// <summary>
        /// Runs an asynchronous action for every connected client.
        /// </summary>
        public async Task EachClientAsync(Func<Channel, Task> Action, Channel? Exclude, Predicate<Channel>? ExcludeWhere) {
            // Start action for each client
            List<Task> Tasks = new();
            foreach (Channel Client in Clients) {
                if (Client != Exclude && (ExcludeWhere is null || !ExcludeWhere(Client))) {
                    Tasks.Add(Action(Client));
                }
            }
            // Wait until all actions are complete
            await Task.WhenAll(Tasks);
        }
        /// <summary>
        /// Gets a collection of every connected client.
        /// </summary>
        public ICollection<Channel> Clients => Channels.Keys;
        
        private async Task AcceptAsync() {
            // Accept TCP client
            TcpClient TCPClient = await Listener!.AcceptTcpClientAsync();
            // Accept another TCP client
            _ = AcceptAsync();

            // Create channel
            NetworkStream? NetworkStream = null;
            SslStream? SSLStream = null;
            Channel? Channel = null;
            try {
                // Get the network stream
                NetworkStream = TCPClient.GetStream();

                // SSL (encrypted)
                if (Options.Certificate is X509Certificate2 Certificate) {
                    // Create SSL stream
                    SSLStream = new SslStream(NetworkStream, false);
                    // Authenticate stream
                    await SSLStream.AuthenticateAsServerAsync(Certificate, clientCertificateRequired: false, checkCertificateRevocation: true);
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
                // Return failure
                return;
            }

            // Disconnect if there are too many clients
            if (Options.MaxClients is not null && Clients.Count >= Options.MaxClients) {
                await Channel.DisconnectAsync(DisconnectReason.TooManyClients);
                return;
            }

            // Listen to disconnect event
            Channel.OnDisconnect += (Reason, ByRemote) => {
                // Remove client from channels
                Channels.TryRemove(Channel, out _);
                // Invoke disconnect event
                OnDisconnect?.Invoke(Channel, Reason, ByRemote);
            };
            // Listen to receive event
            Channel.OnReceive += (Message) => {
                // Invoke receive event
                OnReceive?.Invoke(Channel, Message);
            };
            // Add client to channels
            Channels.TryAdd(Channel, 0);
            // Invoke connect event
            OnConnect?.Invoke(Channel);
        }
        void IDisposable.Dispose() {
            Stop();
        }
    }
    /// <summary>
    /// The preferences for a server maid.
    /// </summary>
    public sealed class ServerOptions : Options {
        /// <summary>
        /// The certificate used for encryption. Ensure the client has SSL enabled.<br/>
        /// Default: <see langword="null"/>
        /// </summary>
        public X509Certificate2? Certificate = null;
        /// <summary>
        /// The maximum number of clients that can connect to the server at once.<br/>
        /// Default: <see langword="null"/>
        /// </summary>
        public int? MaxClients = null;
        /// <summary>
        /// The maximum number of pending bytes from a client before it is disconnected.<br/>
        /// Default: 3MB
        /// </summary>
        public int MaxPendingSize = 3_000_000;
    }
}
