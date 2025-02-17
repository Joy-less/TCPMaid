using MemoryPack;

namespace TCPMaid;

[MemoryPackable]
public readonly partial record struct Packet(Guid MessageId, int MessageLength, byte[] Bytes);