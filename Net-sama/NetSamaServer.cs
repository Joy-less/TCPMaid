using System;
using System.Threading.Tasks;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;

#nullable enable

namespace NetSama {
    public sealed class NetCodeServer : NetSamaBase {
        public readonly int Port;
        public bool Active { get; private set; } = true;
        public ServerOptions Options => (ServerOptions)BaseOptions;

        public event Action? OnStop;
        public event Action<Connection>? OnConnect;
        public event Action<Connection, bool, string>? OnDisconnect;

        private readonly TcpListener Listener;
        private readonly ConcurrentDictionary<Connection, byte> Clients = new();

        private X509Certificate2? Certificate;

        public NetCodeServer(int port, ServerOptions? options = null) : base(options ?? new ServerOptions()) {
            // Initialise port field
            Port = port;
            // Create TcpListener
            Listener = TcpListener.Create(Port);
            Listener.Server.NoDelay = true;
            // Remove clients upon disconnect
            OnDisconnect += (Client, ByRemote, Reason) => {
                Clients.TryRemove(Client, out _);
            };
        }
        public void Start(X509Certificate2? certificate = null) {
            // Initialise certificate field
            Certificate = certificate;
            // Start listener
            Listener.Start();
            // Accept clients
            _ = AcceptClientAsync();
        }
        public async Task StopAsync() {
            // Mark the server as deactivated
            if (!Active) return;
            Active = false;
            // Disconnect from all clients
            while (Clients.Keys.FirstOrDefault() is Connection Client) {
                await Client.DisconnectAsync(DisconnectReason.ServerShutdown);
            }
            // Stop listener
            Listener.Stop();
            // Invoke stop event
            OnStop?.Invoke();
        }
        public async Task BroadcastAsync(Message Message, Connection? Exclude = null, Predicate<Connection>? ExcludeWhere = null) {
            foreach (Connection Client in GetClients()) {
                if (Client != Exclude && (ExcludeWhere is null || !ExcludeWhere(Client))) {
                    await Client.SendAsync(Message);
                }
            }
        }
        public Connection[] GetClients() {
            return Clients.Keys.ToArray();
        }

        private async Task AcceptClientAsync() {
            // Wait for a client to connect
            TcpClient TcpClient;
            try {
                TcpClient = await Listener.AcceptTcpClientAsync();
            }
            catch (Exception) {
                return;
            }
            finally {
                _ = AcceptClientAsync();
            }

            // Get the client's network stream
            NetworkStream NetworkStream = TcpClient.GetStream();

            // Create connection (SSL or not)
            Connection Client;
            try {
                // SSL (encrypted)
                if (Certificate is not null) {
                    // Create SSL stream
                    SslStream SslStream = new(NetworkStream, false);
                    // Authenticate stream
                    await SslStream.AuthenticateAsServerAsync(Certificate, clientCertificateRequired: false, checkCertificateRevocation: true);
                    // Create encrypted connection
                    Client = new Connection(this, TcpClient, (IPEndPoint)TcpClient.Client.RemoteEndPoint!, SslStream, NetworkStream);
                }
                // Plain
                else {
                    // Create plain connection
                    Client = new Connection(this, TcpClient, (IPEndPoint)TcpClient.Client.RemoteEndPoint!, NetworkStream, NetworkStream);
                }
            }
            // Failed to create connection
            catch (Exception) {
                return;
            }

            // Disconnect if there are too many clients
            if (Options.MaxClientCount is not null && Clients.Count >= Options.MaxClientCount) {
                await Client.DisconnectAsync(DisconnectReason.TooManyClients);
            }

            // Listen to disconnect event
            Client.OnDisconnect += (ByRemote, Reason) => OnDisconnect?.Invoke(Client, ByRemote, Reason);
            // Invoke connect event
            OnConnect?.Invoke(Client);
            // Add client to connected list
            Clients.TryAdd(Client, 0);
            // Listen to client
            _ = ListenForMessages(Client);
            // Start measuring ping
            _ = StartPingPong(Client);
        }
    }
    public sealed class ServerOptions : BaseOptions {
        /// <summary>The maximum number of clients that can connect to the server at once.</summary>
        public int? MaxClientCount = null;
        /// <summary>The maximum number of pending bytes from the client. Default: 4MB</summary>
        public int MaxPendingSize = 4_000_000;
    }
}
