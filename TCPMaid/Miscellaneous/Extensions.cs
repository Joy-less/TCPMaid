using MemoryPack;
using System.Buffers;

namespace TCPMaid;

internal static class Extensions {
    /// <summary>
    /// Breaks up an array into multiple arrays, each of the given size except the last.
    /// </summary>
    public static T[][] Fragment<T>(T[] Array, int MaxFragmentSize) {
        // Create fragments array to store all fragments
        int FragmentsCount = (Array.Length + MaxFragmentSize - 1) / MaxFragmentSize;
        T[][] Fragments = new T[FragmentsCount][];
        // Loop until all fragments processed
        for (int i = 0; i < Fragments.Length; i++) {
            // Get current array index
            int ArrayIndex = i * MaxFragmentSize;
            // Calculate size of current fragment
            int FragmentSize = Math.Min(MaxFragmentSize, Array.Length - ArrayIndex);
            // Copy fragment
            Fragments[i] = Array[ArrayIndex..(ArrayIndex + FragmentSize)];
        }
        return Fragments;
    }
    /// <summary>
    /// Converts a message into an array of packets to be sent via a network stream.
    /// </summary>
    public static byte[][] CreatePackets(Message Message, int MaxFragmentSize) {
        // Get bytes
        byte[] Bytes = Message.ToBytes();
        // Fragment bytes
        byte[][] Fragments = Fragment(Bytes, MaxFragmentSize);
        // Create packets array
        byte[][] Packets = new byte[Fragments.Length][];
        // Create each packet
        for (int Index = 0; Index < Fragments.Length; Index++) {
            // Get current fragment
            byte[] Fragment = Fragments[Index];
            // Build packet
            Packets[Index] = MemoryPackSerializer.Serialize(new Packet(Message.Id, Bytes.Length, Fragment));
        }
        // Return packets
        return Packets;
    }
    /// <summary>
    /// Reads bytes from a stream using a buffer of the given size.
    /// </summary>
    public static async Task<byte[]> ReadBytesAsync(this Stream Stream, int BufferSize, CancellationToken CancelToken = default) {
        // Rent buffer
        byte[] ReceiveBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try {
            // Read bytes into buffer
            // Note: cancel token is passed to Task.Run, because NetworkStream.ReadAsync's cancel token does nothing.
            int BytesRead = await Task.Run(async () => {
                return await Stream.ReadAsync(ReceiveBuffer, CancelToken).ConfigureAwait(false);
            }, CancelToken).ConfigureAwait(false);
            // Return bytes
            return ReceiveBuffer[..BytesRead];
        }
        finally {
            // Return buffer
            ArrayPool<byte>.Shared.Return(ReceiveBuffer);
        }
    }
}