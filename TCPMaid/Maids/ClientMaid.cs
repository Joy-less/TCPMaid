using System.Net.Sockets;
using System.Net.Security;

namespace TCPMaid;

/// <summary>
/// A maid that helps setup a channel to a server.
/// </summary>
public sealed class ClientMaid : Maid, IDisposable {
    /// <summary>
    /// The preferences for this maid.
    /// </summary>
    public new ClientMaidOptions Options => (ClientMaidOptions)base.Options;
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
    public ClientMaid(ClientMaidOptions? options = null) : base(options ?? new ClientMaidOptions()) {
    }
    /// <inheritdoc/>
    public override void Dispose() {
        Channel?.Dispose();
    }
    /// <summary>
    /// Attempts to connect to the server.
    /// </summary>
    /// <param name="ServerAddress">The domain or IP address of the server.</param>
    /// <param name="ServerPort">The port number the server is listening on.</param>
    /// <returns>
    /// <see langword="true"/> if connected successfully; <see langword="false"/> otherwise.
    /// </returns>
    public async Task<bool> ConnectAsync(string ServerAddress, int ServerPort) {
        // Fail if already connected
        if (Connected) return false;

        // Create channel
        TcpClient? TcpClient = null;
        NetworkStream? NetworkStream = null;
        SslStream? SslStream = null;
        try {
            // Create TCP client
            TcpClient = new TcpClient() { NoDelay = true };
            // Connect TCP client
            await TcpClient.ConnectAsync(ServerAddress, ServerPort).ConfigureAwait(false);
            // Get the network stream
            NetworkStream = TcpClient.GetStream();

            // SSL (encrypted)
            if (Options.Ssl) {
                // Create SSL stream
                SslStream = new SslStream(NetworkStream, false);
                // Authenticate stream
                await SslStream.AuthenticateAsClientAsync(Options.ServerName ?? ServerAddress).ConfigureAwait(false);
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
}

/// <summary>
/// The preferences for a client maid.
/// </summary>
public sealed record ClientMaidOptions : MaidOptions {
    /// <summary>
    /// Whether to use the server certificate to encrypt the connection.<br/>
    /// Default: <see langword="false"/>
    /// </summary>
    public bool Ssl { get; set; } = false;
    /// <summary>
    /// The common name of the server certificate. Defaults to the server address if <see langword="null"/>.<br/>
    /// Default: <see langword="null"/>
    /// </summary>
    public string? ServerName { get; set; } = null;
}