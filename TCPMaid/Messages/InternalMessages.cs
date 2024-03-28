using MemoryPack;

namespace TCPMaid;

[MemoryPackable]
public sealed partial class DisconnectMessage : Message {
    public readonly string Reason;
    internal DisconnectMessage(string Reason) {
        this.Reason = Reason;
    }
}
[MemoryPackable]
public sealed partial class NextFragmentMessage : Message {
    public readonly ulong MessageID;
    internal NextFragmentMessage(ulong MessageID) {
        this.MessageID = MessageID;
    }
}
[MemoryPackable]
public sealed partial class PingRequest : Request {
    internal PingRequest() { }
}
[MemoryPackable]
public sealed partial class PingResponse : Response {
    internal PingResponse(ulong RequestID) : base(RequestID) { }
}