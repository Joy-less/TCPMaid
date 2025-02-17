using System.Buffers;
using System.Buffers.Binary;
using MemoryPack;

namespace TCPMaid;

internal static class Extensions {
    /// <summary>
    /// Prepends the length of <paramref name="Bytes"/> to itself.
    /// </summary>
    public static byte[] PrependLength(in ReadOnlySpan<byte> Bytes) {
        // Serialize length
        Span<byte> LengthBytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(LengthBytes, Bytes.Length);
        // Combine serialized length and bytes
        return [.. LengthBytes, .. Bytes];
    }
    /// <summary>
    /// Breaks up an array into multiple arrays, each of the given size except the last.
    /// </summary>
    public static T[][] SplitFragments<T>(T[] Array, int MaxFragmentSize) {
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
    /// Creates a fragment of a <see cref="Message"/> to be sent via a network stream.
    /// </summary>
    public static byte[] CreateMessageFragment(Guid MessageId, int MessageLength, byte[] Bytes) {
        // Create message fragment
        PackedMessageFragment MessageFragment = new(MessageId, MessageLength, Bytes);
        // Serialize message fragment
        byte[] MessageFragmentBytes = MemoryPackSerializer.Serialize(MessageFragment);
        // Prepend message fragment length
        return PrependLength(MessageFragmentBytes);
    }
    /// <summary>
    /// Converts a message into an array of message fragments to be sent via a network stream.
    /// </summary>
    public static byte[][] CreateMessageFragments(Message Message, int MaxFragmentSize) {
        // Get bytes
        byte[] Bytes = Message.ToBytes();
        // Split bytes into fragments
        byte[][] BytesFragments = SplitFragments(Bytes, MaxFragmentSize);
        // Create message fragments array
        byte[][] MessageFragments = new byte[BytesFragments.Length][];
        // Create each message fragment
        for (int Index = 0; Index < BytesFragments.Length; Index++) {
            // Get current fragment
            byte[] BytesFragment = BytesFragments[Index];
            // Create message fragment
            MessageFragments[Index] = CreateMessageFragment(Message.Id, Bytes.Length, BytesFragment);
        }
        // Return packets
        return MessageFragments;
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