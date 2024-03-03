using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using Newtonsoft.Json;
using static TCPMaid.Maid;

namespace TCPMaid {
    public abstract class Maid {
        internal readonly Options Options;

        // Sizes of packet components
        internal const int PacketLengthSize = sizeof(int);
        internal const int MessageIdSize = sizeof(ulong);
        internal const int MessageLengthSize = sizeof(int);

        internal Maid(Options options) {
            Options = options;
        }

        protected async Task ListenAsync(Connection Connection) {
            // Listen for disconnect messages
            Connection.OnReceive += (Message Message) => {
                if (Message is DisconnectMessage DisconnectMessage) {
                    Connection.DisconnectSilently(DisconnectMessage.Reason, ByRemote: true);
                }
            };
            // Listen for incoming packets
            try {
                // Create collections for bytes waiting to be processed
                List<byte> PendingBytes = new();
                Dictionary<ulong, (int Length, byte[] Bytes)> PendingMessages = new();

                // Read messages while connected
                while (Connection.Connected) {
                    // Wait for bytes from the network stream
                    await ReadBytesFromStreamAsync(Connection.Stream, PendingBytes, Options.BufferSize, Options.DisconnectTimeout);

                    // Limit memory usage on server
                    if (Options is ServerOptions ServerOptions) {
                        // Calculate total bytes used in pending messages from client
                        int PendingSize = PendingBytes.Count + PendingMessages.Sum(PendingMessage => PendingMessage.Value.Bytes.Length);
                        // Check if total exceeds limit
                        if (PendingSize > ServerOptions.MaxPendingSize) {
                            // Disconnect client for using too much memory
                            await Connection.DisconnectAsync(DisconnectReason.HighMemoryUsage);
                            return;
                        }
                    }

                    // While length bytes available
                    while (PendingBytes.Count > PacketLengthSize) {
                        // Get length of packet
                        int PacketLength = BitConverter.ToInt32(PendingBytes.GetRange(0, PacketLengthSize).ToArray());

                        // Ensure packet fully received
                        if (PendingBytes.Count < PacketLengthSize + PacketLength) {
                            break;
                        }
                        // Get packet data
                        byte[] PacketData = PendingBytes.GetRange(PacketLengthSize, PacketLength).ToArray();

                        // Remove packet length and packet from pending bytes
                        PendingBytes.RemoveRange(0, PacketLengthSize + PacketLength);

                        // Get message ID from packet data
                        ulong MessageId = BitConverter.ToUInt64(PacketData);

                        // Pending message
                        if (PendingMessages.TryGetValue(MessageId, out (int Length, byte[] Bytes) PendingMessage)) {
                            // Add new message data
                            PendingMessage.Bytes = Concat(PendingMessage.Bytes, PacketData[MessageIdSize..]);
                            // Remove pending message if complete
                            if (PendingMessage.Bytes.Length >= PendingMessage.Length) {
                                PendingMessages.Remove(MessageId);
                            }
                        }
                        // New message
                        else {
                            // Get expected message length
                            PendingMessage.Length = BitConverter.ToInt32(PacketData, MessageIdSize);
                            // Add new message data
                            PendingMessage.Bytes = PacketData[(MessageIdSize + MessageLengthSize)..];
                        }

                        // Incomplete message
                        if (PendingMessage.Bytes.Length < PendingMessage.Length) {
                            // Store pending message
                            PendingMessages[MessageId] = PendingMessage;
                            // Ask for next packet
                            _ = Connection.SendAsync(new SendNextPacketMessage(MessageId));
                            break;
                        }

                        // Construct message
                        Message? Message = Message.FromBytes(PendingMessage.Bytes);
                        // Handle message
                        if (Message is not null) {
                            Connection.InvokeOnReceive(Message);
                        }
                    }
                }
            }
            // Timeout - close connection
            catch (OperationCanceledException) {
                await Connection.DisconnectAsync(DisconnectReason.Timeout);
                return;
            }
            // Disconnected - close connection
            catch (Exception) {
                await Connection.DisconnectAsync(DisconnectReason.Error);
                return;
            }
        }
        protected async Task PingPongAsync(Connection Connection) {
            // Respond to pings
            Connection.OnReceive += async (Message Message) => {
                if (Message is PingRequest PingRequest) {
                    await Connection.SendAsync(new PingResponse(PingRequest.RequestId));
                }
            };
            // Create timer
            Stopwatch Timer = new();
            // Request pings
            while (Connection.Connected) {
                // Wait until next ping
                await Task.Delay(TimeSpan.FromSeconds(Options.PingRequestInterval));
                // Restart timer
                Timer.Restart();
                // Request ping response
                await Connection.RequestAsync<PingResponse>(new PingRequest());
                // Set ping
                Connection.Ping = Timer.Elapsed.TotalSeconds / 2;
            }
        }
        private static async Task<int> ReadBytesFromStreamAsync(Stream Stream, List<byte> PendingBytes, int BufferSize, double TimeoutInSeconds) {
            // Create a token to cancel read after timeout duration
            CancellationToken TimeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutInSeconds)).Token;
            // Rent a buffer
            byte[] ReceiveBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try {
                // Read bytes into the buffer
                int BytesRead = await Stream.ReadAsync(ReceiveBuffer, TimeoutToken);
                // Add bytes from the buffer to the pending list
                PendingBytes.AddRange(ReceiveBuffer[..BytesRead]);
                // Return the number of bytes read
                return BytesRead;
            }
            finally {
                // Return the buffer
                ArrayPool<byte>.Shared.Return(ReceiveBuffer);
            }
        }
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
    }
    public sealed class Connection : IDisposable {
        /// <summary>The Maid instance this connection belongs to.</summary>
        public readonly Maid Maid;
        /// <summary>On the server, this is the remote client. On the client, this is the local client.</summary>
        public readonly TcpClient Client;
        /// <summary>If the connection is encrypted, this is an <see cref="SslStream"/> that wraps the <see cref="NetworkStream"/>. Otherwise, it's the <see cref="NetworkStream"/>.</summary>
        public readonly Stream Stream;
        /// <summary>The IP address and port of the remote connection.</summary>
        public readonly IPEndPoint RemoteEndPoint;
        /// <summary>The IP address and port of the local connection.</summary>
        public readonly IPEndPoint LocalEndPoint;

        public bool Connected { get; private set; } = true;
        public double Ping { get; internal set; }
        public bool Encrypted => Stream is SslStream;

        public event Action<bool, string>? OnDisconnect;
        public event Action<Message>? OnReceive;

        private readonly SemaphoreSlim NetworkSemaphore = new(1, 1);
        private static ulong LastMessageId;

        internal Connection(Maid maid, TcpClient client, Stream stream) {
            Maid = maid;
            Client = client;
            Stream = stream;
            RemoteEndPoint = (IPEndPoint)Client.Client.RemoteEndPoint!;
            LocalEndPoint = (IPEndPoint)Client.Client.LocalEndPoint!;
        }
        public async Task<bool> SendAsync(Message Message) {
            // Generate message ID
            ulong MessageId = Interlocked.Increment(ref LastMessageId);

            // Get bytes from message
            byte[] Bytes = Message.ToBytes();
            // Split bytes into smaller fragments
            byte[][] ByteFragments = Fragment(Bytes, Maid.Options.MessageFragmentSize);

            // Create packets from byte fragments
            byte[][] Packets = new byte[ByteFragments.Length][];
            Packets[0] = CreateFirstPacket(MessageId, Bytes.Length, ByteFragments[0]);
            for (int i = 1; i < ByteFragments.Length; i++) {
                Packets[i] = CreateExtraPacket(MessageId, ByteFragments[i]);
            }

            // Write bytes to message stream
            try {
                // Send packets
                for (int i = 0; i < Packets.Length; i++) {
                    // Await send packet message
                    if (i != 0) await WaitForMessageAsync<SendNextPacketMessage>(Message => Message.MessageId == MessageId);

                    // Await semaphore
                    await NetworkSemaphore.WaitAsync();
                    try {
                        // Send packet
                        await Stream.WriteAsync(Packets[i]);
                    }
                    finally {
                        // Release semaphore
                        NetworkSemaphore.Release();
                    }
                }
                // Send success!
                return true;
            }
            // Failed to send message
            catch (Exception) {
                DisconnectSilently(DisconnectReason.Error);
                return false;
            }
        }
        public async Task<TResponse?> RequestAsync<TResponse>(Request Request, Predicate<TResponse>? Filter = null) where TResponse : Response {
            // Send request
            bool Success = await SendAsync(Request);
            // Send failure
            if (!Success) {
                return null;
            }
            // Return response
            return await WaitForMessageAsync<TResponse>(Response => Response.RequestId == Request.RequestId && (Filter is null || Filter(Response)));
        }
        public async Task<TMessage> WaitForMessageAsync<TMessage>(Predicate<TMessage>? Where = null) where TMessage : Message {
            // Create return variable and receive signal 
            TaskCompletionSource<TMessage> OnComplete = new();
            // Filter received messages
            void Filter(Message Message) {
                // Check if message is of the given type and meets the predicate
                if (Message is TMessage MessageOfT && (Where is null || Where(MessageOfT))) {
                    // Set return variable and signal
                    OnComplete.TrySetResult(MessageOfT);
                }
            }
            // Listen for messages
            OnReceive += Filter;
            // Await a matching message
            TMessage ReturnMessage = await OnComplete.Task;
            // Stop listening for messages
            OnReceive -= Filter;
            // Return the matched message
            return ReturnMessage;
        }
        public async Task DisconnectAsync(string Reason = DisconnectReason.NoReasonGiven) {
            // Debounce
            if (!Connected) return;
            // Send disconnect message
            await SendAsync(new DisconnectMessage(Reason));
            // Dispose
            Dispose();
            // Invoke disconnect event
            OnDisconnect?.Invoke(false, Reason);
        }
        internal void DisconnectSilently(string Reason = DisconnectReason.NoReasonGiven, bool ByRemote = false) {
            // Debounce
            if (!Connected) return;
            // Dispose
            Dispose();
            // Invoke disconnect event
            OnDisconnect?.Invoke(ByRemote, Reason);
        }
        public void Dispose() {
            // Mark as disconnected
            if (!Connected) return;
            Connected = false;
            // Close client
            Client.Close();
            // Dispose semaphore
            NetworkSemaphore.Dispose();
        }
        internal void InvokeOnReceive(Message Message) {
            try {
                // Invoke receive event
                OnReceive?.Invoke(Message);
            }
            catch (Exception Ex) {
                // Get error info (message hidden for server errors)
                string Error = Maid is ClientMaid
                    ? $"{Ex.GetType().Name}: {Ex.Message}"
                    : Ex.GetType().Name;
                // Disconnect on error
                _ = DisconnectAsync($"{DisconnectReason.Error} ({Error})");
            }
        }
        private static byte[] CreateFirstPacket(ulong MessageId, int MessageLength, byte[] Bytes) {
            byte[] MessageIdBytes = BitConverter.GetBytes(MessageId);
            byte[] MessageLengthBytes = BitConverter.GetBytes(MessageLength);
            byte[] LengthBytes = BitConverter.GetBytes(MessageIdSize + MessageLengthSize + Bytes.Length);
            return Concat(LengthBytes, MessageIdBytes, MessageLengthBytes, Bytes);
        }
        private static byte[] CreateExtraPacket(ulong MessageId, byte[] Bytes) {
            byte[] MessageIdBytes = BitConverter.GetBytes(MessageId);
            byte[] LengthBytes = BitConverter.GetBytes(MessageIdSize + Bytes.Length);
            return Concat(LengthBytes, MessageIdBytes, Bytes);
        }
    }
    public abstract class Options {
        /// <summary>How many seconds of silence before a connection is dropped.<br/>
        /// Default: 10</summary>
        public double DisconnectTimeout = 10;
        /// <summary>The size of the network buffer in bytes. Uses more memory, but speeds up transmission of larger messages.<br/>
        /// Default: 30kB</summary>
        public int BufferSize = 30_000;
        /// <summary>The maximum size of a message in bytes before it will be broken up to avoid congestion.<br/>
        /// Default: 1MB</summary>
        public int MessageFragmentSize = 1_000_000;
        /// <summary>How many seconds before sending another <see cref="PingRequest"/> to measure the connection's latency and prevent a timeout.<br/>
        /// Default: 1</summary>
        public double PingRequestInterval = 1;
    }
    public static class DisconnectReason {
        /// <summary>The disconnect reason is unknown.</summary>
        public const string Unknown = "Unknown.";
        /// <summary>No disconnect reason was given.</summary>
        public const string NoReasonGiven = "No reason given.";
        /// <summary>There was an error.</summary>
        public const string Error = "There was an error.";
        /// <summary>The client or server has not sent data for too long (usually due to a bad internet connection).</summary>
        public const string Timeout = "Connection timed out.";
        /// <summary>The server has reached the maximum number of clients.</summary>
        public const string TooManyClients = "The server is full.";
        /// <summary>The server has kicked the client.</summary>
        public const string Kicked = "Kicked by the server.";
        /// <summary>The client is closing or logging out.</summary>
        public const string ClientShutdown = "The client is closing.";
        /// <summary>The server is shutting down.</summary>
        public const string ServerShutdown = "The server is shutting down.";
        /// <summary>The client is using too much memory on the server.</summary>
        public const string HighMemoryUsage = "The client is using too much memory on the server.";
    }
    public abstract class Message {
        private static readonly IReadOnlyDictionary<string, Type> MessageTypes = GetMessageTypes();
        private const char NameDataSeparator = ':';

        public bool Internal => this is DisconnectMessage or SendNextPacketMessage or SetupUDPRequest or SetupUDPResponse or PingRequest or PingResponse;
        public byte[] ToBytes() {
            // Get message name and serialise message data
            (string Name, string Serialised) = (GetType().Name, JsonConvert.SerializeObject(this));
            // Create message bytes
            return Encoding.UTF8.GetBytes(Name + NameDataSeparator + Serialised);
        }
        public static Message? FromBytes(byte[] Bytes) {
            // Get message parts from bytes
            string[] Parts = Encoding.UTF8.GetString(Bytes).Split(NameDataSeparator, 2);
            // Ensure bytes are correctly formatted
            if (Parts.Length != 2) return null;
            // Get message name and serialised message data
            (string Name, string Serialised) = (Parts[0], Parts[1]);
            // Get message type from name
            Type? MessageType = GetMessageTypeFromName(Name);
            // Ensure message type exists
            if (MessageType is null) return null;
            // Create message
            return (Message?)JsonConvert.DeserializeObject(Serialised, MessageType);
        }
        public static Type? GetMessageTypeFromName(string Name) {
            MessageTypes.TryGetValue(Name, out Type? Type);
            return Type;
        }

        private static Dictionary<string, Type> GetMessageTypes() {
            return AppDomain.CurrentDomain.GetAssemblies().SelectMany(Asm => Asm.GetTypes())
                .Where(Type => Type.IsClass && !Type.IsAbstract && Type.IsSubclassOf(typeof(Message))
            ).ToDictionary(Type => Type.Name);
        }
    }
    public abstract class Request : Message {
        [JsonProperty] public readonly ulong RequestId = Interlocked.Increment(ref LastRequestId);

        private static ulong LastRequestId;
    }
    public abstract class Response : Message {
        [JsonProperty] public readonly ulong RequestId;
        public Response(ulong request_id) {
            RequestId = request_id;
        }
    }
    public sealed class DisconnectMessage : Message {
        [JsonProperty] public readonly string Reason;
        [JsonConstructor]
        internal DisconnectMessage(string reason) {
            Reason = reason;
        }
    }
    public sealed class SendNextPacketMessage : Message {
        [JsonProperty] public readonly ulong MessageId;
        [JsonConstructor]
        internal SendNextPacketMessage(ulong message_id) {
            MessageId = message_id;
        }
    }
    public sealed class SetupUDPRequest : Request {
        [JsonProperty] public readonly int ServerPort;
        [JsonProperty] public readonly byte[]? Secret;
        [JsonConstructor]
        internal SetupUDPRequest(int server_port, byte[]? secret) {
            ServerPort = server_port;
            Secret = secret;
        }
    }
    public sealed class SetupUDPResponse : Response {
        [JsonConstructor]
        internal SetupUDPResponse(ulong request_id) : base(request_id) {
        }
    }
    public sealed class PingRequest : Request {
        [JsonConstructor]
        internal PingRequest() {
        }
    }
    public sealed class PingResponse : Response {
        [JsonConstructor]
        internal PingResponse(ulong request_id) : base(request_id) {
        }
    }
}