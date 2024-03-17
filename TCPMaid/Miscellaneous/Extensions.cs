using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;

namespace TCPMaid {
    internal static class Extensions {
        public static T[] Concat<T>(params T[][] Arrays) {
            // Create merged array
            int MergedLength = Arrays.Sum(Array => Array.Length);
            T[] MergedArray = new T[MergedLength];
            // Copy arrays to merged array
            int CurrentIndex = 0;
            foreach (T[] Array in Arrays) {
                Array.CopyTo(MergedArray, CurrentIndex);
                CurrentIndex += Array.Length;
            }
            return MergedArray;
        }
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
        public static byte[][] CreatePackets(Message Message, int MaxFragmentSize) {
            // Get bytes
            byte[] Bytes = Message.ToBytes();
            // Fragment bytes
            byte[][] Fragments = Fragment(Bytes, MaxFragmentSize);
            // Create packets array
            byte[][] Packets = new byte[Fragments.Length][];
            // Create each packet
            for (int i = 0; i < Fragments.Length; i++) {
                // Get current fragment
                byte[] Fragment = Fragments[i];
                // Build packet
                Packets[i] = Concat(
                    // Packet length
                    BitConverter.GetBytes(sizeof(ulong) + sizeof(int) + Fragment.Length),
                    // Message ID
                    BitConverter.GetBytes(Message.ID),
                    // Total message length
                    BitConverter.GetBytes(Bytes.Length),
                    // Fragment data
                    Fragment
                );
            }
            // Return packets
            return Packets;
        }
        public static async Task<byte[]> ReadBytesAsync(this Stream Stream, int BufferSize, CancellationToken CancelToken = default) {
            // Rent buffer
            byte[] ReceiveBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try {
                // Read bytes into buffer
                // Note: Cancel token passed to Task.Run, because NetworkStream.ReadAsync's cancel token doesn't work
                int BytesRead = await Task.Run(async() => await Stream.ReadAsync(ReceiveBuffer), CancelToken);
                // Return bytes
                return ReceiveBuffer[..BytesRead];
            }
            finally {
                // Return buffer
                ArrayPool<byte>.Shared.Return(ReceiveBuffer);
            }
        }
    }
}
