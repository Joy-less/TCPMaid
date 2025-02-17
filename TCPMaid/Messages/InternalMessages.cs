using MemoryPack;

namespace TCPMaid;

[MemoryPackable]
internal sealed partial record DisconnectMessage(string Reason) : Message;
[MemoryPackable]
internal sealed partial record NextFragmentMessage(Guid MessageId) : Message;
[MemoryPackable]
internal sealed partial record PingRequest() : Message;
[MemoryPackable]
internal sealed partial record PingResponse(Guid Id) : Message(Id);
[MemoryPackable]
internal sealed partial record StreamMessage(Guid Id, string Identifier, long TotalLength, byte[] Fragment) : Message(Id);