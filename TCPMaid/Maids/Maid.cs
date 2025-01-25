namespace TCPMaid;

/// <summary>
/// The base class for maids that help setup channels.
/// </summary>
public abstract class Maid {
    internal MaidOptions Options { get; }

    internal Maid(MaidOptions options) {
        Options = options;
    }
}

/// <summary>
/// The base class for maid preferences.
/// </summary>
public abstract class MaidOptions {
    /// <summary>
    /// How many seconds of silence before a connection is dropped.<br/>
    /// Default: 10
    /// </summary>
    public double Timeout { get; set; } = 10;
    /// <summary>
    /// The size of the network buffer in bytes. Uses more memory, but receives large messages faster.<br/>
    /// Default: 32kiB
    /// </summary>
    public int BufferSize { get; set; } = 32_768;
    /// <summary>
    /// The maximum size of a message in bytes before breaking it up to avoid congestion.<br/>
    /// Default: 1MB
    /// </summary>
    public int MaxFragmentSize { get; set; } = 1_000_000;
    /// <summary>
    /// How many seconds before sending another <see cref="PingRequest"/> to measure the channel's latency and prevent a timeout.<br/>
    /// Default: 1
    /// </summary>
    public double PingInterval { get; set; } = 1;
}