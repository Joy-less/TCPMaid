using MemoryPack;

namespace TCPMaid;

[MemoryPackable]
public sealed partial record DisconnectMessage : Message {
    public string Reason { get; }

    internal DisconnectMessage(string Reason) {
        this.Reason = Reason;
    }
}
[MemoryPackable]
public sealed partial record NextFragmentMessage : Message {
    public long MessageId { get; }

    internal NextFragmentMessage(long MessageId) {
        this.MessageId = MessageId;
    }
}
[MemoryPackable]
public sealed partial record PingRequest : Message {
    internal PingRequest() {
    }
}
[MemoryPackable]
public sealed partial record PingResponse : Message {
    internal PingResponse(long Id)
        : base(Id) {
    }
}
[MemoryPackable]
public sealed partial record StreamMessage : Message {
    public string Identifier { get; }
    public long TotalLength { get; }
    public byte[] Fragment { get; }

    internal StreamMessage(long Id, string Identifier, long TotalLength, byte[] Fragment)
        : base(Id) {
        this.Identifier = Identifier;
        this.TotalLength = TotalLength;
        this.Fragment = Fragment;
    }
}