using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace TCPMaid;

/// <summary>
/// A maid that helps setup channels to multiple clients.
/// </summary>
public sealed class ServerMaid : Maid, IDisposable {
    /// <summary>
    /// The preferences for this maid.
    /// </summary>
    public new ServerMaidOptions Options => (ServerMaidOptions)base.Options;
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

    private readonly ConcurrentDictionary<Channel, byte> Channels = new();
    private TcpListener? TcpListener;

    /// <summary>
    /// Creates a new server maid with the given options.
    /// </summary>
    public ServerMaid(ServerMaidOptions? options = null) : base(options ?? new ServerMaidOptions()) {
    }
    /// <summary>
    /// Starts listening for clients on the given port, unless already running.
    /// </summary>
    public void Start(int Port) {
        // Ensure server is not running
        if (Running) return;
        // Start listener
        TcpListener = TcpListener.Create(Port);
        TcpListener.Server.NoDelay = true;
        TcpListener.Start();
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
        TcpListener?.Stop();
        TcpListener = null;
        // Invoke stop event
        OnStop?.Invoke();
    }
    /// <summary>
    /// Sends a message to every connected client.
    /// </summary>
    public async Task BroadcastAsync(Message Message, Channel? Exclude = null, Predicate<Channel>? ExcludeWhere = null) {
        // Send message to each client
        await ForEachClientAsync(async Client => await Client.SendAsync(Message), Exclude, ExcludeWhere).ConfigureAwait(false);
    }
    /// <summary>
    /// Disconnects every connected client.
    /// </summary>
    public async Task DisconnectAllAsync(string Reason = DisconnectReason.None, Channel? Exclude = null, Predicate<Channel>? ExcludeWhere = null) {
        // Disconnect each client
        await ForEachClientAsync(async Client => await Client.DisconnectAsync(Reason), Exclude, ExcludeWhere).ConfigureAwait(false);
    }
    /// <summary>
    /// Runs an asynchronous action for every connected client.
    /// </summary>
    public async Task ForEachClientAsync(Func<Channel, Task> Action, Channel? Exclude, Predicate<Channel>? ExcludeWhere) {
        // Start action for each client
        List<Task> Tasks = [];
        foreach (Channel Client in Clients) {
            if (Client != Exclude && (ExcludeWhere is null || !ExcludeWhere(Client))) {
                Tasks.Add(Action(Client));
            }
        }
        // Wait until all actions are complete
        await Task.WhenAll(Tasks).ConfigureAwait(false);
    }
    /// <summary>
    /// Gets a collection of every connected client.
    /// </summary>
    public ICollection<Channel> Clients => Channels.Keys;

    private async Task AcceptAsync() {
        // Accept TCP client
        TcpClient TcpClient = await TcpListener!.AcceptTcpClientAsync().ConfigureAwait(false);
        // Accept another TCP client
        _ = AcceptAsync();

        // Create channel
        NetworkStream? NetworkStream = null;
        SslStream? SslStream = null;
        Channel? Channel = null;
        try {
            // Get the network stream
            NetworkStream = TcpClient.GetStream();

            // SSL (encrypted)
            if (Options.Certificate is X509Certificate2 Certificate) {
                // Create SSL stream
                SslStream = new SslStream(NetworkStream, false);
                // Authenticate stream
                await SslStream.AuthenticateAsServerAsync(Certificate, clientCertificateRequired: false, checkCertificateRevocation: true).ConfigureAwait(false);
                // Create encrypted channel
                Channel = new Channel(this, TcpClient, SslStream);
            }
            // Plain
            else {
                // Create plain channel
                Channel = new Channel(this, TcpClient, NetworkStream);
            }
        }
        // Failed to create channel
        catch (Exception) {
            // Dispose objects
            TcpClient?.Dispose();
            NetworkStream?.Dispose();
            SslStream?.Dispose();
            Channel?.Dispose();
            // Return failure
            return;
        }

        // Disconnect if there are too many clients
        if (Options.MaxClients is not null && Clients.Count >= Options.MaxClients) {
            await Channel.DisconnectAsync(DisconnectReason.TooManyClients).ConfigureAwait(false);
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
public sealed class ServerMaidOptions : MaidOptions {
    /// <summary>
    /// The certificate used for encryption. Ensure the client has SSL enabled.<br/>
    /// Default: <see langword="null"/>
    /// </summary>
    public X509Certificate2? Certificate { get; set; } = null;
    /// <summary>
    /// The maximum number of clients that can connect to the server at once.<br/>
    /// Default: <see langword="null"/>
    /// </summary>
    public int? MaxClients { get; set; } = null;
    /// <summary>
    /// The maximum number of pending bytes from a client before it is disconnected.<br/>
    /// Default: 3MB
    /// </summary>
    /// <remarks>
    /// This is important for mitigating DDOS attacks that consume memory on the server.
    /// </remarks>
    public int MaxPendingSize { get; set; } = 3_000_000;
}