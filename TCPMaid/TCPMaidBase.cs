using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.IO;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using Newtonsoft.Json;
using static TCPMaid.TCPMaidBase;

namespace TCPMaid {
    public abstract class TCPMaidBase {
        internal readonly BaseOptions BaseOptions;

        // Sizes of packet components
        internal const int PacketLengthSize = sizeof(int);
        internal const int MessageIdSize = sizeof(ulong);
        internal const int MessageLengthSize = sizeof(int);

        internal TCPMaidBase(BaseOptions base_options) {
            BaseOptions = base_options;
        }

        protected async Task ListenForMessages(Connection Connection) {
            // Listen for disconnect messages
            Connection.OnReceive += (Message Message) => {
                if (Message is DisconnectMessage DisconnectMessage) {
                    Connection.DisconnectByRequest(DisconnectMessage.Reason);
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
                    await ReadBytesFromStreamAsync(Connection.Stream, PendingBytes, BaseOptions.BufferSize, BaseOptions.DisconnectTimeout);

                    // Prevent high memory usage on server
                    if (BaseOptions is ServerOptions ServerOptions) {
                        // Calculate total bytes used in pending messages from client
                        int PendingSize = PendingBytes.Count + PendingMessages.Sum(PendingMessage => PendingMessage.Value.Bytes.Length);
                        // Ensure total bytes under limit
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
                        ulong MessageId = BitConverter.ToUInt64(PacketData, 0);

                        // Existing message
                        if (PendingMessages.TryGetValue(MessageId, out (int Length, byte[] Bytes) PendingMessage)) {
                            // Get partial message data
                            PendingMessage.Bytes = Concat(PendingMessage.Bytes, PacketData[MessageIdSize..]);
                        }
                        // New message
                        else {
                            // Get expected message length
                            PendingMessage.Length = BitConverter.ToInt32(PacketData, MessageIdSize);
                            // Get partial message data
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

                        // Remove pending message
                        PendingMessages.Remove(MessageId);
                        // Get full message
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
                await Connection.DisconnectAsync(DisconnectReason.Unknown);
                return;
            }
        }
        protected async Task StartPingPong(Connection Connection) {
            // Respond to pings
            Connection.OnReceive += async (Message Message) => {
                if (Message is PingRequest PingRequest) {
                    await Connection.SendAsync(new PingResponse(PingRequest.RequestId));
                }
            };
            // Request pings
            while (Connection.Connected) {
                // Wait until next ping
                await Task.Delay(TimeSpan.FromSeconds(BaseOptions.PingRequestInterval));
                // Start timer
                double TimeOfPing = GetTimestamp();
                // Request ping response
                await Connection.RequestAsync<PingResponse>(new PingRequest());
                // Stop timer
                double TimeOfPong = GetTimestamp();
                // Set ping
                Connection.Ping = (TimeOfPong - TimeOfPing) / 2;
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
        public static double GetTimestamp() {
            return DateTimeOffset.UtcNow.Subtract(DateTimeOffset.UnixEpoch).TotalSeconds;
        }
    }
    public sealed class Connection {
        /// <summary>The TCPMaid instance this connection belongs to.</summary>
        public readonly TCPMaidBase TCPMaid;
        /// <summary>On the server, this is the remote client. On the client, this is the local client.</summary>
        public readonly TcpClient Client;
        /// <summary>The IP address and port of the remote connection.</summary>
        public readonly IPEndPoint EndPoint;
        /// <summary>If the connection is encrypted, this is an <see cref="SslStream"/> that wraps around the <see cref="NetworkStream"/>. Otherwise, it's the <see cref="NetworkStream"/>.</summary>
        public readonly Stream Stream;
        /// <summary>The <see cref="NetworkStream"/> of the connection.</summary>
        public readonly NetworkStream InnerStream;

        public bool Connected { get; private set; } = true;
        public double Ping { get; internal set; }

        public event Action<Message>? OnReceive;
        public event Action<bool, string>? OnDisconnect;

        private readonly SemaphoreSlim NetworkSemaphore = new(1, 1);

        private static ulong LastMessageId;

        internal Connection(TCPMaidBase tcp_maid_base, TcpClient client, IPEndPoint end_point, Stream stream, NetworkStream inner_stream) {
            TCPMaid = tcp_maid_base;
            Client = client;
            EndPoint = end_point;
            Stream = stream;
            InnerStream = inner_stream;
        }

        public async Task<bool> SendAsync(Message Message) {
            // Generate message ID
            ulong MessageId = Interlocked.Increment(ref LastMessageId);

            // Get bytes from message
            byte[] Bytes = Message.ToBytes();
            // Split bytes into smaller fragments
            byte[][] ByteFragments = FragmentArray(Bytes, TCPMaid.BaseOptions.MaxPacketSize);

            // Create packets from byte fragments
            byte[][] Packets = new byte[ByteFragments.Length][];
            Packets[0] = CreateFirstPacket(MessageId, Bytes.Length, ByteFragments[0]);
            for (int i = 1; i < ByteFragments.Length; i++) {
                Packets[i] = CreateExtraPacket(MessageId, ByteFragments[i]);
            }

            // Write bytes to message stream
            try {
                // Send each packet
                for (int i = 0; i < Packets.Length; i++) {
                    // Await send packet message
                    if (i != 0) {
                        await WaitForMessageAsync<SendNextPacketMessage>(Message => Message.MessageId == MessageId);
                    }
                    // Send one packet
                    await SendRawAsync(Packets[i]);
                }
                // Send success!
                return true;
            }
            catch (Exception) {
                // Send failure
                await DisconnectAsync(DisconnectReason.Unknown);
                return false;
            }
        }
        public async Task<TResponse?> RequestAsync<TResponse>(Request Request, Predicate<TResponse>? Filter = null) where TResponse : Response {
            // Send request
            if (await SendAsync(Request)) {
                // Return response
                return await WaitForMessageAsync<TResponse>(Response => Response.RequestId == Request.RequestId && (Filter is null || Filter(Response)));
            }
            // Send failure
            else {
                return null;
            }
        }
        public async Task<TMessage> WaitForMessageAsync<TMessage>(Predicate<TMessage>? Where = null) where TMessage : Message {
            // Create return variable and receive signal 
            TMessage? ReturnMessage = null;
            SemaphoreSlim ReceiveSignal = new(0, 1);
            // Filter received messages
            void Filter(Message Message) {
                // Check if message is of the given type and meets the predicate
                if (Message is TMessage MessageOfT && (Where is null || Where(MessageOfT))) {
                    // Set return variable and signal
                    ReturnMessage = MessageOfT;
                    ReceiveSignal.Release();
                }
            }
            // Listen for messages
            OnReceive += Filter;
            // Await a matching message
            await ReceiveSignal.WaitAsync();
            // Stop listening for messages
            OnReceive -= Filter;
            // Return the matched message
            return ReturnMessage!;
        }
        public async Task DisconnectAsync(string Reason = DisconnectReason.NoReasonGiven) {
            // Mark as disconnected
            if (!Connected) return;
            Connected = false;
            // Send disconnect message
            await SendAsync(new DisconnectMessage(Reason));
            // Close client
            Client.Client?.Close();
            // Invoke on disconnect
            OnDisconnect?.Invoke(false, Reason);
        }
        internal void DisconnectByRequest(string Reason = DisconnectReason.NoReasonGiven) {
            // Mark as disconnected
            if (!Connected) return;
            Connected = false;
            // Close client
            Client.Client?.Close();
            // Invoke on disconnect
            OnDisconnect?.Invoke(true, Reason);
        }
        internal void InvokeOnReceive(Message Message) {
            try {
                OnReceive?.Invoke(Message);
            }
            catch (Exception Ex) {
                // Get error message (message hidden for server errors)
                string Error = TCPMaid is TCPMaidClient
                    ? $"{Ex.GetType().Name}: {Ex.Message}"
                    : Ex.GetType().Name;
                // Disconnect on error
                _ = DisconnectAsync($"{DisconnectReason.Error} ({Error})");
            }
        }
        private async Task SendRawAsync(byte[] Data) {
            // Await semaphore
            await NetworkSemaphore.WaitAsync();
            try {
                // Send data
                await Stream.WriteAsync(Data);
            }
            finally {
                // Release semaphore
                NetworkSemaphore.Release();
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
        private static T[][] FragmentArray<T>(T[] Array, int MaxFragmentSize) {
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
    public abstract class BaseOptions {
        /// <summary>How many seconds of silence before a connection is dropped. Default: 10</summary>
        public double DisconnectTimeout = 10;
        /// <summary>The size of the network buffer in bytes. Uses more memory, but speeds up transmission of larger messages. Default: 30kB</summary>
        public int BufferSize = 30_000;
        /// <summary>The maximum size of a packet in bytes before it will be broken up to avoid congestion. Default: 750kB</summary>
        public int MaxPacketSize = 750_000;
        /// <summary>How many seconds before sending another <see cref="PingRequest"/> to measure the connection's ping. Default: 0.5</summary>
        public double PingRequestInterval = 0.5;
    }
    public static class DisconnectReason {
        /// <summary>The disconnect reason is unknown.</summary>
        public const string Unknown = "Unknown.";
        /// <summary>No disconnect reason was given.</summary>
        public const string NoReasonGiven = "No reason given.";
        /// <summary>The client/server has not sent data for too long (usually due to a bad internet connection).</summary>
        public const string Timeout = "Connection timed out.";
        /// <summary>The server has too many clients.</summary>
        public const string TooManyClients = "The server has too many clients.";
        /// <summary>The server has kicked the client.</summary>
        public const string Kicked = "Kicked by the server.";
        /// <summary>The client is closing or logging out.</summary>
        public const string ClientShutdown = "The client is closing.";
        /// <summary>The server is shutting down.</summary>
        public const string ServerShutdown = "The server is shutting down.";
        /// <summary>There was an error.</summary>
        public const string Error = "There was an error.";
        /// <summary>The client is using too much memory on the server.</summary>
        public const string HighMemoryUsage = "The client is using too much memory on the server.";
    }
    public abstract class Message {
        private static readonly ReadOnlyDictionary<string, Type> MessageTypes = GetMessageTypes().AsReadOnly();
        private const char NameDataSeparator = ' ';

        public static Type? GetMessageTypeFromName(string Name) {
            MessageTypes.TryGetValue(Name, out Type? Type);
            return Type;
        }
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
    public sealed class SendNextPacketMessage : Message {
        [JsonProperty] public readonly ulong MessageId;
        public SendNextPacketMessage(ulong message_id) {
            MessageId = message_id;
        }
    }
    public sealed class DisconnectMessage : Message {
        [JsonProperty] public readonly string Reason;
        public DisconnectMessage(string reason) {
            Reason = reason;
        }
    }
    public sealed class PingRequest : Request {
    }
    public sealed class PingResponse : Response {
        public PingResponse(ulong request_id) : base(request_id) {
        }
    }
}
