using MemoryPack;

namespace TCPMaid {
    [MemoryPackable]
    public sealed partial class DisconnectMessage : Message {
        public readonly string Reason;
        public DisconnectMessage(string Reason) {
            this.Reason = Reason;
        }
    }
    [MemoryPackable]
    public sealed partial class NextFragmentMessage : Message {
        public readonly ulong MessageID;
        public NextFragmentMessage(ulong MessageID) {
            this.MessageID = MessageID;
        }
    }
    [MemoryPackable]
    public sealed partial class PingRequest : Request {
    }
    [MemoryPackable]
    public sealed partial class PingResponse : Response {
        public PingResponse(ulong RequestID) : base(RequestID) { }
    }
}
