namespace TCPMaid {
    public abstract class Maid {
        internal readonly Options Options;

        internal Maid(Options options) {
            Options = options;
        }
    }
    public abstract class Options {
        /// <summary>How many seconds of silence before a connection is dropped.<br/>
        /// Default: 10</summary>
        public double Timeout = 10;
        /// <summary>The size of the network buffer in bytes. Uses more memory, but speeds up transmission of larger messages.<br/>
        /// Default: 30kB</summary>
        public int BufferSize = 30_000;
        /// <summary>The maximum size of a message in bytes before it will be broken up to avoid congestion.<br/>
        /// Default: 1MB</summary>
        public int MaxFragmentSize = 1_000_000;
        /// <summary>How many seconds before sending another <see cref="PingRequest"/> to measure the connection's latency and prevent a timeout.<br/>
        /// Default: 1</summary>
        public double PingInterval = 1;
    }
}