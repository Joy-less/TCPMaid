using MemoryPack;

namespace TCPMaid;

[MemoryPackable]
public sealed partial class DisconnectMessage : Message {
    public string Reason { get; }

    internal DisconnectMessage(string Reason) {
        this.Reason = Reason;
    }
}
[MemoryPackable]
public sealed partial class NextFragmentMessage : Message {
    public long MessageId { get; }

    internal NextFragmentMessage(long MessageId) {
        this.MessageId = MessageId;
    }
}
[MemoryPackable]
public sealed partial class PingRequest : Message {
    internal PingRequest() { }
}
[MemoryPackable]
public sealed partial class PingResponse : Message {
    internal PingResponse(long Id) : base(Id) { }
}
[MemoryPackable]
public sealed partial class StreamMessage : Message {
    public string Identifier { get; }
    public long TotalLength { get; }
    public byte[] Fragment { get; }

    internal StreamMessage(long Id, string Identifier, long TotalLength, byte[] Fragment) : base(Id) {
        this.Identifier = Identifier;
        this.TotalLength = TotalLength;
        this.Fragment = Fragment;
    }
}