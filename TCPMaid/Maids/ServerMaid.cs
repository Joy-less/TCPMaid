using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace TCPMaid {
    public sealed class ServerMaid : Maid, IDisposable {
        public new ServerOptions Options => (ServerOptions)base.Options;
        public bool Running { get; private set; }

        public event Action? OnStart;
        public event Action? OnStop;
        public event Action<Connection>? OnConnect;
        public event Action<Connection, string, bool>? OnDisconnect;
        public event Action<Connection, Message>? OnReceive;

        private TcpListener? Listener;
        private readonly ConcurrentDictionary<Connection, byte> Connections = new();

        public ServerMaid(ServerOptions? options = null) : base(options ?? new ServerOptions()) {
        }
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
        public async Task BroadcastAsync(Message Message, Connection? Exclude = null, Predicate<Connection>? ExcludeWhere = null) {
            // Send message to each client
            await EachClientAsync(async Client => await Client.SendAsync(Message), Exclude, ExcludeWhere);
        }
        public async Task DisconnectAllAsync(string Reason = DisconnectReason.None, Connection? Exclude = null, Predicate<Connection>? ExcludeWhere = null) {
            // Disconnect each client
            await EachClientAsync(async Client => await Client.DisconnectAsync(Reason), Exclude, ExcludeWhere);
        }
        private async Task EachClientAsync(Func<Connection, Task> Action, Connection? Exclude, Predicate<Connection>? ExcludeWhere) {
            // Start action for each client
            List<Task> Tasks = new();
            foreach (Connection Client in Clients) {
                if (Client != Exclude && (ExcludeWhere is null || !ExcludeWhere(Client))) {
                    Tasks.Add(Action(Client));
                }
            }
            // Wait until all actions are complete
            await Task.WhenAll(Tasks);
        }
        public ICollection<Connection> Clients => Connections.Keys;
        
        private async Task AcceptAsync() {
            // Accept TCP client
            TcpClient TCPClient = await Listener!.AcceptTcpClientAsync();
            // Accept another TCP client
            _ = AcceptAsync();

            // Create connection
            NetworkStream? NetworkStream = null;
            SslStream? SSLStream = null;
            Connection? Connection = null;
            try {
                // Get the network stream
                NetworkStream = TCPClient.GetStream();

                // SSL (encrypted)
                if (Options.Certificate is X509Certificate2 Certificate) {
                    // Create SSL stream
                    SSLStream = new SslStream(NetworkStream, false);
                    // Authenticate stream
                    await SSLStream.AuthenticateAsServerAsync(Certificate, clientCertificateRequired: false, checkCertificateRevocation: true);
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
                return;
            }

            // Disconnect if there are too many clients
            if (Options.MaxClients is not null && Clients.Count >= Options.MaxClients) {
                await Connection.DisconnectAsync(DisconnectReason.TooManyClients);
                return;
            }

            // Listen to disconnect event
            Connection.OnDisconnect += (Reason, ByRemote) => {
                // Remove client from connections
                Connections.TryRemove(Connection, out _);
                // Invoke disconnect event
                OnDisconnect?.Invoke(Connection, Reason, ByRemote);
            };
            // Listen to receive event
            Connection.OnReceive += (Message) => {
                // Invoke receive event
                OnReceive?.Invoke(Connection, Message);
            };
            // Add client to connections
            Connections.TryAdd(Connection, 0);
            // Invoke connect event
            OnConnect?.Invoke(Connection);
        }
        void IDisposable.Dispose() {
            Stop();
        }
    }
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
