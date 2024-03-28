namespace TCPMaid;

/// <summary>
/// A collection of common disconnect reasons.
/// </summary>
public static class DisconnectReason {
    /// <summary>
    /// The disconnect reason is unknown.
    /// </summary>
    public const string None = "No reason given.";
    /// <summary>
    /// The client or server has not sent data for too long (usually due to a bad internet connection).
    /// </summary>
    public const string Timeout = "Connection timed out.";
    /// <summary>
    /// The server has reached the maximum number of clients.
    /// </summary>
    public const string TooManyClients = "The server is full.";
    /// <summary>
    /// The server has kicked the client.
    /// </summary>
    public const string Kicked = "Kicked by the server.";
    /// <summary>
    /// The client is closing or logging out.
    /// </summary>
    public const string ClientShutdown = "The client is closing.";
    /// <summary>
    /// The server is stopping.
    /// </summary>
    public const string ServerShutdown = "The server is closing.";
    /// <summary>
    /// The client is using too much memory on the server.
    /// </summary>
    public const string MemoryUsage = "The client exceeded the server memory limit.";
}