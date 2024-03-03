using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace TCPMaid {
    public sealed class ServerMaid : Maid, IDisposable {
        public readonly int Port;
        public new ServerOptions Options => (ServerOptions)base.Options;
        public bool Active { get; private set; }

        public event Action? OnStart;
        public event Action? OnStop;
        public event Action<Connection>? OnConnect;
        public event Action<Connection, bool, string>? OnDisconnect;
        public event Action<Connection, Message>? OnReceive;

        private readonly TcpListener Listener;
        private readonly ConcurrentDictionary<Connection, byte> Clients = new();

        public ServerMaid(int port, ServerOptions? options = null) : base(options ?? new ServerOptions()) {
            // Initialise port field
            Port = port;
            // Create TCP Listener
            Listener = TcpListener.Create(Port);
            Listener.Server.NoDelay = true;
        }
        public void Start(X509Certificate2? Certificate = null) {
            // Start listener
            Listener.Start();
            // Accept clients
            _ = AcceptClientAsync(Certificate);
            // Invoke start event
            OnStart?.Invoke();
            // Mark the server as activated
            Active = true;
        }
        public void Stop() {
            // Mark the server as deactivated
            if (!Active) return;
            Active = false;
            // Disconnect from all clients
            _ = DisconnectAllAsync(DisconnectReason.ServerShutdown);
            // Stop listener
            Listener.Stop();
            // Invoke stop event
            OnStop?.Invoke();
        }
        public async Task BroadcastAsync(Message Message, Connection? Exclude = null, Predicate<Connection>? ExcludeWhere = null) {
            // Send message to each client
            await ForEachClientAsync(async Client => await Client.SendAsync(Message), Exclude, ExcludeWhere);
        }
        public async Task DisconnectAllAsync(string Reason = DisconnectReason.NoReasonGiven, Connection? Exclude = null, Predicate<Connection>? ExcludeWhere = null) {
            // Disconnect each client
            await ForEachClientAsync(async Client => await Client.DisconnectAsync(Reason), Exclude, ExcludeWhere);
        }
        public Connection[] GetClients() {
            return Clients.Keys.ToArray();
        }
        public int ClientCount => Clients.Count;
        
        private async Task ForEachClientAsync(Func<Connection, Task> Action, Connection? Exclude, Predicate<Connection>? ExcludeWhere) {
            // Start action for each client
            List<Task> Tasks = new();
            foreach (KeyValuePair<Connection, byte> Client in Clients) {
                if (Client.Key != Exclude && (ExcludeWhere is null || !ExcludeWhere(Client.Key))) {
                    Tasks.Add(Action(Client.Key));
                }
            }
            // Wait until all actions are complete
            await Task.WhenAll(Tasks);
        }
        private async Task AcceptClientAsync(X509Certificate2? Certificate) {
            // Wait for a client to connect
            TcpClient TcpClient;
            try {
                TcpClient = await Listener.AcceptTcpClientAsync();
            }
            finally {
                _ = AcceptClientAsync(Certificate);
            }

            // Create connection (SSL or not)
            Connection Client;
            try {
                // Get the network stream
                NetworkStream NetworkStream = TcpClient.GetStream();

                // SSL (encrypted)
                if (Certificate is not null) {
                    // Create SSL stream
                    SslStream SslStream = new(NetworkStream, false);
                    // Authenticate stream
                    await SslStream.AuthenticateAsServerAsync(Certificate, clientCertificateRequired: false, checkCertificateRevocation: true);
                    // Create encrypted connection
                    Client = new Connection(this, TcpClient, SslStream);
                }
                // Plain
                else {
                    // Create plain connection
                    Client = new Connection(this, TcpClient, NetworkStream);
                }
            }
            // Failed to create connection
            catch (Exception) {
                return;
            }

            // Disconnect if there are too many clients
            if (Options.MaxClients is not null && Clients.Count >= Options.MaxClients) {
                await Client.DisconnectAsync(DisconnectReason.TooManyClients);
                return;
            }

            // Listen to disconnect event
            Client.OnDisconnect += (ByRemote, Reason) => {
                // Remove client from connections
                Clients.TryRemove(Client, out _);
                // Invoke disconnect event
                OnDisconnect?.Invoke(Client, ByRemote, Reason);
            };
            // Listen to receive event
            Client.OnReceive += (Message) => {
                // Invoke receive event
                OnReceive?.Invoke(Client, Message);
            };
            // Add client to connections
            Clients.TryAdd(Client, 0);
            // Listen to client
            _ = ListenAsync(Client);
            // Start measuring ping
            _ = PingPongAsync(Client);
            // Invoke connect event
            OnConnect?.Invoke(Client);
        }
        void IDisposable.Dispose() {
            Stop();
        }
    }
    public sealed class ServerOptions : Options {
        /// <summary>The maximum number of clients that can connect to the server at once.<br/>
        /// Default: <see langword="null"/></summary>
        public int? MaxClients = null;
        /// <summary>The maximum number of pending bytes from a client before it is disconnected.<br/>
        /// Default: 4MB</summary>
        public int MaxPendingSize = 4_000_000;
    }
}
