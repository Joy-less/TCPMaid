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
public sealed partial class PingRequest : Message {
    internal PingRequest() { }
}
[MemoryPackable]
public sealed partial class PingResponse : Message {
    internal PingResponse(ulong ID) : base(ID) { }
}
[MemoryPackable]
public sealed partial class StreamMessage : Message {
    public string Identifier;
    public long TotalLength;
    public byte[] Fragment;
    internal StreamMessage(ulong ID, string Identifier, long TotalLength, byte[] Fragment) : base(ID) {
        this.Identifier = Identifier;
        this.TotalLength = TotalLength;
        this.Fragment = Fragment;
    }
}