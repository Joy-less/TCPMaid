using MemoryPack;

namespace TCPMaid;

[MemoryPackable]
internal readonly partial record struct Packet(Guid MessageId, int MessageLength, byte[] Bytes);