using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using static TCPMaid.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace TCPMaid {
    public sealed class Connection : IDisposable {
        /// <summary>
        /// The maid this connection belongs to.
        /// </summary>
        public readonly Maid Maid;
        /// <summary>
        /// On the server, this is the remote client. On the client, this is the local client.
        /// </summary>
        public readonly TcpClient Client;
        /// <summary>
        /// A thread-safe <see cref="SslStream"/> or <see cref="NetworkStream"/>.
        /// </summary>
        public readonly Stream Stream;
        /// <summary>
        /// The IP address and port of the remote connection.
        /// </summary>
        public readonly IPEndPoint RemotePoint;
        /// <summary>
        /// The IP address and port of the local connection.
        /// </summary>
        public readonly IPEndPoint LocalPoint;

        public bool Connected { get; private set; } = true;
        public double Latency { get; private set; } = -1;
        public bool Encrypted => Stream is SslStream;

        public event Action<string, bool>? OnDisconnect;
        public event Action<Message>? OnReceive;

        private static ulong LastMessageId;

        internal Connection(Maid maid, TcpClient client, Stream stream) {
            Maid = maid;
            Client = client;
            Stream = Stream.Synchronized(stream);
            RemotePoint = (IPEndPoint)Client.Client.RemoteEndPoint!;
            LocalPoint = (IPEndPoint)Client.Client.LocalEndPoint!;

            _ = PingPongAsync();
            _ = ListenAsync();
        }
        public async Task<bool> SendAsync(Message Message) {
            // Generate message ID
            ulong MessageId = Interlocked.Increment(ref LastMessageId);

            // Create packets from message bytes
            byte[][] Packets = CreatePackets(Message, Maid.Options.MaxFragmentSize);

            // Write bytes to message stream
            try {
                // Send packets
                for (int i = 0; i < Packets.Length; i++) {
                    // Await send packet message
                    if (i != 0) await WaitAsync<NextFragmentMessage>(Message => Message.MessageID == MessageId);
                    // Send packet
                    await Stream.WriteAsync(Packets[i]);
                }
                // Send success!
                return true;
            }
            // Failed to send message
            catch (Exception) {
                await DisconnectAsync(Silently: true);
                return false;
            }
        }
        public async Task<TResponse?> RequestAsync<TResponse>(Request Request, Predicate<TResponse>? Filter = null, CancellationToken CancelToken = default) where TResponse : Response {
            // Send request
            bool Success = await SendAsync(Request);
            // Send failure
            if (!Success) {
                return null;
            }
            // Return response
            return await WaitAsync<TResponse>(Response => Response.RequestID == Request.ID && (Filter is null || Filter(Response)), CancelToken);
        }
        public async Task<TMessage?> WaitAsync<TMessage>(Predicate<TMessage>? Where = null, CancellationToken CancelToken = default) where TMessage : Message {
            // Create return variable and receive signal
            TaskCompletionSource<TMessage?> OnComplete = new();
            // Filter received messages
            void Filter(Message Message) {
                // Check if message is of the given type and meets the predicate
                if (Message is TMessage MessageOfT && (Where is null || Where(MessageOfT))) {
                    // Set return variable and signal
                    OnComplete.TrySetResult(MessageOfT);
                }
            }
            void CancelWait(string Reason, bool ByRemote) {
                OnComplete.TrySetResult(null);
            }
            // Listen for disconnect
            OnDisconnect += CancelWait;
            // Listen for messages
            OnReceive += Filter;
            // Await a matching message
            TMessage? ReturnMessage = await OnComplete.Task.WaitAsync(CancelToken);
            // Stop listening for messages
            OnReceive -= Filter;
            // Stop listening for disconnect
            OnDisconnect -= CancelWait;
            // Return the matched message
            return ReturnMessage;
        }
        public async Task<double> PingAsync(CancellationToken CancelToken = default) {
            // Create timer
            Stopwatch Timer = Stopwatch.StartNew();
            // Request ping response
            await RequestAsync<PingResponse>(new PingRequest(), CancelToken: CancelToken);
            // Calculate round trip time
            return Latency = Timer.Elapsed.TotalSeconds / 2;
        }
        public async Task DisconnectAsync(string Reason = DisconnectReason.None, bool ByRemote = false, bool Silently = false) {
            // Debounce
            if (!Connected) return;
            // Send disconnect message
            if (!Silently) {
                await SendAsync(new DisconnectMessage(Reason));
            }
            // Dispose
            Dispose();
            // Invoke disconnect event
            OnDisconnect?.Invoke(Reason, ByRemote);
        }
        public void Dispose() {
            // Mark as disconnected
            if (!Connected) return;
            Connected = false;
            // Close client
            Client.Close();
            // Close stream
            Stream.Dispose();
        }

        private async Task PingPongAsync() {
            // Respond to pings
            OnReceive += async (Message Message) => {
                if (Message is PingRequest PingRequest) {
                    await SendAsync(new PingResponse(PingRequest.ID));
                }
            };
            // Send pings
            while (Connected) {
                // Ping
                await PingAsync();
                // Wait until next ping
                await Task.Delay(TimeSpan.FromSeconds(Maid.Options.PingInterval));
            }
        }
        private async Task ListenAsync() {
            // Listen for disconnect messages
            OnReceive += (Message Message) => {
                if (Message is DisconnectMessage DisconnectMessage) {
                    _ = DisconnectAsync(DisconnectMessage.Reason, ByRemote: true, Silently: true);
                }
            };

            // Create collections for bytes waiting to be processed
            List<byte> PendingBytes = new();
            Dictionary<ulong, PendingMessage> PendingMessages = new();

            // Listen for incoming packets
            try {
                // Read messages while connected
                while (Connected) {
                    // Create timeout token source
                    using CancellationTokenSource TimeoutTokenSource = new(TimeSpan.FromSeconds(Maid.Options.Timeout));
                    // Wait for bytes from the network stream
                    PendingBytes.AddRange(await Stream.ReadBytesAsync(Maid.Options.BufferSize, TimeoutTokenSource.Token));

                    // Limit memory usage on server
                    if (Maid.Options is ServerOptions ServerOptions) {
                        // Calculate total bytes used in pending messages from client
                        int PendingSize = PendingBytes.Count + PendingMessages.Sum(PendingMessage => PendingMessage.Value.CurrentBytes.Length);
                        // Check if total exceeds limit
                        if (PendingSize > ServerOptions.MaxPendingSize) {
                            // Disconnect client for using too much memory
                            await DisconnectAsync(DisconnectReason.MemoryUsage);
                            return;
                        }
                    }

                    // Extract all messages
                    while (true) {
                        // Ensure length of fragment is complete
                        if (PendingBytes.Count < sizeof(int)) {
                            break;
                        }
                        // Get length of fragment
                        int FragmentLength = BitConverter.ToInt32(PendingBytes.GetRange(0, sizeof(int)).ToArray());

                        // Ensure fragment is complete
                        if (PendingBytes.Count < sizeof(int) + FragmentLength) {
                            break;
                        }
                        // Get fragment
                        byte[] Fragment = PendingBytes.GetRange(sizeof(int), FragmentLength).ToArray();

                        // Remove length and fragment
                        PendingBytes.RemoveRange(0, sizeof(int) + FragmentLength);

                        // Get fragment message ID
                        ulong MessageID = BitConverter.ToUInt64(Fragment);

                        // Existing message
                        if (PendingMessages.TryGetValue(MessageID, out PendingMessage? PendingMessage)) {
                            // Add bytes to pending message
                            PendingMessage.CurrentBytes = Concat(PendingMessage.CurrentBytes, Fragment[sizeof(ulong)..]);
                        }
                        else {
                            // Create pending message
                            PendingMessage = new PendingMessage(
                                total_message_length: BitConverter.ToInt32(Fragment, sizeof(ulong)),
                                initial_bytes: Fragment[(sizeof(ulong) + sizeof(int))..]
                            );
                            // Add pending message
                            PendingMessages.Add(MessageID, PendingMessage);
                        }

                        // Ensure message is complete
                        if (PendingMessage.Incomplete) {
                            // Ask for next fragment
                            _ = SendAsync(new NextFragmentMessage(MessageID));
                            break;
                        }
                        // Remove pending message
                        PendingMessages.Remove(MessageID);

                        // Deserialise message
                        Message Message = Message.FromBytes(PendingMessage.CurrentBytes);

                        // Handle message
                        OnReceive?.Invoke(Message);
                    }
                }
            }
            // Timeout - close connection
            catch (OperationCanceledException) {
                await DisconnectAsync(DisconnectReason.Timeout);
                return;
            }
            // Disconnected - close connection
            catch (Exception) {
                await DisconnectAsync();
                return;
            }
        }
        private sealed class PendingMessage {
            public readonly int TotalMessageLength;
            public byte[] CurrentBytes;
            public PendingMessage(int total_message_length, byte[] initial_bytes) {
                TotalMessageLength = total_message_length;
                CurrentBytes = initial_bytes;
            }
            public bool Incomplete => CurrentBytes.Length < TotalMessageLength;
        }
    }
}
