using MemoryPack;

namespace TCPMaid;

[MemoryPackable]
internal readonly partial record struct PackedMessage(string TypeName, byte[] Bytes);

[MemoryPackable]
internal readonly partial record struct PackedMessageFragment(Guid MessageId, int MessageLength, byte[] Bytes);