using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using static TCPMaid.Extensions;

namespace TCPMaid;

/// <summary>
/// A TCP connection to a remote client or server.
/// </summary>
public sealed class Channel : IDisposable {
    /// <summary>
    /// The maid this channel belongs to.
    /// </summary>
    public Maid Maid { get; }
    /// <summary>
    /// On the server, this is the remote client. On the client, this is the local client.
    /// </summary>
    public TcpClient Client { get; }
    /// <summary>
    /// A thread-safe <see cref="SslStream"/> or <see cref="NetworkStream"/>.
    /// </summary>
    public Stream Stream { get; }
    /// <summary>
    /// The IP address and port of the remote connection.
    /// </summary>
    public IPEndPoint RemotePoint { get; }
    /// <summary>
    /// The IP address and port of the local connection.
    /// </summary>
    public IPEndPoint LocalPoint { get; }

    /// <summary>
    /// Whether the channel is still open to send and receive messages.
    /// </summary>
    public bool Connected { get; private set; } = true;
    /// <summary>
    /// The time in seconds for a message to reach the remote, estimated by half of the last <see cref="PingRequest"/>'s round trip time.
    /// </summary>
    public double Latency { get; private set; } = -1;
    /// <summary>
    /// The time in milliseconds for a message to reach the remote, estimated by half of the last <see cref="PingRequest"/>'s round trip time.
    /// </summary>
    public int LatencyMs => (int)Math.Round(Latency * 1000);
    /// <summary>
    /// Whether the server is using a certificate to encrypt the channel stream.
    /// </summary>
    public bool Encrypted => Stream is SslStream;

    /// <summary>
    /// Triggers when the channel is abandoned. (Reason, ByRemote)
    /// </summary>
    public event Action<string, bool>? OnDisconnect;
    /// <summary>
    /// Triggers when the channel receives a message.
    /// </summary>
    public event Action<Message>? OnReceive;
    /// <summary>
    /// Triggers when the channel receives a fragment of a message. (MessageID, CurrentBytes, TotalBytes)
    /// </summary>
    public event Action<long, int, int>? OnReceiveFragment;

    /// <summary>
    /// Creates a new channel that listens to the given stream.
    /// </summary>
    public Channel(Maid maid, TcpClient client, Stream stream) {
        Maid = maid;
        Client = client;
        Stream = Stream.Synchronized(stream);
        RemotePoint = (IPEndPoint)Client.Client.RemoteEndPoint!;
        LocalPoint = (IPEndPoint)Client.Client.LocalEndPoint!;

        _ = PingPongAsync();
        _ = ListenAsync();
    }
    /// <summary>
    /// Serialises and sends a message to the remote.
    /// </summary>
    /// <returns><see langword="true"/> if the message was fully sent successfully; <see langword="false"/> otherwise.</returns>
    public async Task<bool> SendAsync(Message Message, CancellationToken CancelToken = default) {
        // Create packets from message bytes
        byte[][] Packets = CreatePackets(Message, Maid.Options.MaxFragmentSize);

        // Write bytes to message stream
        try {
            // Send packets
            for (int i = 0; i < Packets.Length; i++) {
                // Await send packet message
                if (i != 0) await WaitAsync<NextFragmentMessage>(NextFragmentMessage => NextFragmentMessage.MessageId == Message.Id, CancelToken);
                // Send packet
                await Stream.WriteAsync(Packets[i], CancelToken);
            }
            // Send success!
            return true;
        }
        // Send cancelled
        catch (OperationCanceledException) {
            return false;
        }
        // Failed to send message
        catch (Exception) {
            await DisconnectAsync(Silently: true);
            return false;
        }
    }
    /// <summary>
    /// Serialises and sends a request to the remote and waits for a response.
    /// </summary>
    /// <returns><typeparamref name="TResponse"/>, or <see langword="null"/> if cancelled or the channel was disconnected.</returns>
    /// <param name="OnFragment">Called when a fragment of the response has been received, useful for progress bars. (CurrentBytes, TotalBytes)</param>
    public async Task<TResponse?> RequestAsync<TResponse>(Message Request, Action<int, int>? OnFragment = null, CancellationToken CancelToken = default) where TResponse : Message {
        // Create receive signal
        TaskCompletionSource<TResponse?> OnComplete = new();
        // Filter received messages
        void Filter(Message Message) {
            // Check if message is of the given type and meets the predicate
            if (Message is TResponse MessageOfT && MessageOfT.Id == Request.Id) {
                // Set return variable and signal
                OnComplete.TrySetResult(MessageOfT);
            }
        }
        // Stop waiting and return null
        void CancelWait(string Reason, bool ByRemote) {
            OnComplete.TrySetResult(null);
        }
        // Receive fragment callback
        void ReceiveFragment(long MessageId, int CurrentBytes, int TotalBytes) {
            if (MessageId == Request.Id) {
                OnFragment?.Invoke(CurrentBytes, TotalBytes);
            }
        }

        try {
            // Listen for disconnect
            OnDisconnect += CancelWait;
            // Listen for messages
            OnReceive += Filter;
            // Listen for fragments
            OnReceiveFragment += ReceiveFragment;
            // Send request
            bool Success = await SendAsync(Request, CancelToken);
            // Send failure
            if (!Success) {
                return null;
            }
            // Await a response
            return await OnComplete.Task.WaitAsync(CancelToken);
        }
        finally {
            // Stop listening for fragments
            OnReceiveFragment -= ReceiveFragment;
            // Stop listening for messages
            OnReceive -= Filter;
            // Stop listening for disconnect
            OnDisconnect -= CancelWait;
        }
    }
    /// <summary>
    /// Waits for a message from the remote.
    /// </summary>
    /// <returns><typeparamref name="TMessage"/>, or <see langword="null"/> if cancelled or the channel was disconnected.</returns>
    public async Task<TMessage?> WaitAsync<TMessage>(Predicate<TMessage>? Where = null, CancellationToken CancelToken = default) where TMessage : Message {
        // Create receive signal
        TaskCompletionSource<TMessage?> OnComplete = new();
        // Filter received messages
        void Filter(Message Message) {
            // Check if message is of the given type and meets the predicate
            if (Message is TMessage MessageOfT && (Where is null || Where(MessageOfT))) {
                // Set return variable and signal
                OnComplete.TrySetResult(MessageOfT);
            }
        }
        // Stop waiting and return null
        void CancelWait(string Reason, bool ByRemote) {
            OnComplete.TrySetResult(null);
        }

        try {
            // Listen for disconnect
            OnDisconnect += CancelWait;
            // Listen for messages
            OnReceive += Filter;
            // Await a matching message
            return await OnComplete.Task.WaitAsync(CancelToken);
        }
        finally {
            // Stop listening for messages
            OnReceive -= Filter;
            // Stop listening for disconnect
            OnDisconnect -= CancelWait;
        }
    }
    /// <summary>
    /// Sends a <see cref="PingRequest"/> to the remote and waits for a <see cref="PingResponse"/>.
    /// </summary>
    /// <returns>An estimate of the channel's latency (half of the round trip time).</returns>
    public async Task<double> PingAsync(CancellationToken CancelToken = default) {
        // Get send timestamp
        long SendTimestamp = Stopwatch.GetTimestamp();
        // Request ping response
        await RequestAsync<PingResponse>(new PingRequest(), CancelToken: CancelToken);
        // Calculate ping time
        TimeSpan ElapsedTime = CompatibilityExtensions.GetElapsedTime(SendTimestamp, Stopwatch.GetTimestamp());
        // Get round trip time
        return Latency = ElapsedTime.TotalSeconds / 2;
    }
    /// <summary>
    /// Sends bytes from a stream to the remote in a series of <see cref="StreamMessage"/>s, useful for sending large files.
    /// </summary>
    /// <returns><see langword="true"/> if the stream was fully sent successfully; <see langword="false"/> otherwise.</returns>
    public async Task<bool> SendStreamAsync(string Identifier, Stream FromStream, CancellationToken CancelToken = default) {
        // Send stream data
        try {
            // Generate message ID
            long MessageId = Message.GenerateId();
            // Enable first packet flag
            bool IsFirstPacket = true;
            // Send stream data fragments
            while (FromStream.Position < FromStream.Length) {
                // Await send packet message
                if (!IsFirstPacket) {
                    await WaitAsync<NextFragmentMessage>(NextFragmentMessage => NextFragmentMessage.MessageId == MessageId, CancelToken);
                }
                // Read fragment from stream
                byte[] Fragment = await FromStream.ReadBytesAsync(Maid.Options.MaxFragmentSize, CancelToken);
                // Create stream message from fragment
                StreamMessage StreamMessage = new(MessageId, Identifier, FromStream.Length, Fragment);
                // Create packet from stream message
                byte[] Packet = CreatePacket(MessageId, StreamMessage.ToBytes());
                // Send packet
                await Stream.WriteAsync(Packet, CancelToken);
                // Disable first packet flag
                IsFirstPacket = false;
            }
        }
        // Send cancelled
        catch (OperationCanceledException) {
            return false;
        }
        // Failed to send message
        catch (Exception) {
            await DisconnectAsync(Silently: true);
            return false;
        }
        // Send success
        return true;
    }
    /// <summary>
    /// Receives a stream of bytes from a series of <see cref="StreamMessage"/>s, useful for sending large files.
    /// </summary>
    /// <returns><see langword="true"/> if the stream was fully received successfully; <see langword="false"/> otherwise.</returns>
    /// <param name="OnFragment">Called when a fragment of the response has been received, useful for progress bars. (CurrentBytes, TotalBytes)</param>
    public async Task<bool> ReceiveStreamAsync(string Identifier, Stream ToStream, Action<long, long>? OnFragment = null, CancellationToken CancelToken = default) {
        while (true) {
            // Wait for stream message
            StreamMessage? StreamMessage = await WaitAsync<StreamMessage>(StreamMessage => StreamMessage.Identifier == Identifier, CancelToken);
            // Wait cancelled
            if (StreamMessage is null) {
                return false;
            }
            // Write fragment to receive stream
            await ToStream.WriteAsync(StreamMessage.Fragment, CancelToken);
            // Invoke receive fragment callback
            OnFragment?.Invoke(ToStream.Length, StreamMessage.TotalLength);
            // Fully received
            if (ToStream.Length >= StreamMessage.TotalLength) {
                return true;
            }
            // Ask for next fragment
            bool SendSuccess = await SendAsync(new NextFragmentMessage(StreamMessage.Id), CancelToken);
            // Failed to ask for next fragment
            if (!SendSuccess) {
                return false;
            }
        }
    }
    /// <summary>
    /// Sends the disconnect reason and disposes the channel.
    /// </summary>
    public async Task DisconnectAsync(string Reason = DisconnectReason.None, bool ByRemote = false, bool Silently = false) {
        // Debounce
        if (!Connected) return;
        // Send disconnect message
        if (!Silently) {
            await SendAsync(new DisconnectMessage(Reason));
        }
        // Dispose channel
        Dispose();
        // Invoke disconnect event
        OnDisconnect?.Invoke(Reason, ByRemote);
    }
    /// <summary>
    /// Closes the channel without telling the remote or invoking the OnDisconnect event.
    /// </summary>
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
                await SendAsync(new PingResponse(PingRequest.Id));
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
        List<byte> PendingBytes = [];
        Dictionary<long, PendingMessage> PendingMessages = [];

        // Listen for incoming packets
        try {
            // Read messages while connected
            while (Connected) {
                // Create timeout token source
                using CancellationTokenSource TimeoutTokenSource = new(TimeSpan.FromSeconds(Maid.Options.Timeout));
                // Wait for bytes from the network stream
                PendingBytes.AddRange(await Stream.ReadBytesAsync(Maid.Options.BufferSize, TimeoutTokenSource.Token));

                // Extract all messages
                while (true) {
                    // Ensure length of fragment is complete
                    if (PendingBytes.Count < sizeof(int)) {
                        break;
                    }
                    // Get length of fragment
                    int FragmentLength = BitConverter.ToInt32([.. PendingBytes.GetRange(0, sizeof(int))]);

                    // Ensure fragment is complete
                    if (PendingBytes.Count < sizeof(int) + FragmentLength) {
                        break;
                    }
                    // Get fragment
                    byte[] Fragment = [.. PendingBytes.GetRange(sizeof(int), FragmentLength)];

                    // Remove length and fragment
                    PendingBytes.RemoveRange(0, sizeof(int) + FragmentLength);

                    // Get fragment data
                    long MessageId = BitConverter.ToInt64(Fragment, 0);
                    int TotalMessageLength = BitConverter.ToInt32(Fragment, sizeof(long));
                    byte[] FragmentData = Fragment[(sizeof(long) + sizeof(int))..];

                    // Existing message
                    if (PendingMessages.TryGetValue(MessageId, out PendingMessage? PendingMessage)) {
                        // Update pending message
                        PendingMessage.TotalMessageLength = TotalMessageLength;
                        PendingMessage.CurrentBytes = Concat(PendingMessage.CurrentBytes, FragmentData);
                    }
                    // New message
                    else {
                        // Create pending message
                        PendingMessage = new PendingMessage(TotalMessageLength, FragmentData);
                        // Add pending message
                        PendingMessages.Add(MessageId, PendingMessage);
                    }

                    // Invoke fragment received with pending message
                    OnReceiveFragment?.Invoke(MessageId, PendingMessage.CurrentBytes.Length, PendingMessage.TotalMessageLength);
                    
                    // Ensure message is complete
                    if (PendingMessage.Incomplete) {
                        // Ask for next fragment
                        _ = SendAsync(new NextFragmentMessage(MessageId));
                        break;
                    }
                    // Remove pending message
                    PendingMessages.Remove(MessageId);

                    // Deserialise message
                    Message Message = Message.FromBytes(PendingMessage.CurrentBytes);

                    // Handle message
                    OnReceive?.Invoke(Message);
                }

                // Limit memory usage on server
                if (Maid.Options is ServerMaidOptions ServerOptions) {
                    // Calculate total bytes used in pending messages from client
                    int PendingSize = PendingBytes.Count + PendingMessages.Sum(PendingMessage => PendingMessage.Value.CurrentBytes.Length);
                    // Check if total exceeds limit
                    if (PendingSize > ServerOptions.MaxPendingSize) {
                        // Disconnect client for using too much memory
                        await DisconnectAsync(DisconnectReason.MemoryUsage);
                        return;
                    }
                }
            }
        }
        // Timeout - close channel
        catch (OperationCanceledException) {
            await DisconnectAsync(DisconnectReason.Timeout);
            return;
        }
        // Disconnected - close channel
        catch (Exception) {
            await DisconnectAsync();
            return;
        }
    }
    private sealed class PendingMessage {
        public int TotalMessageLength;
        public byte[] CurrentBytes;
        public PendingMessage(int total_message_length, byte[] initial_bytes) {
            TotalMessageLength = total_message_length;
            CurrentBytes = initial_bytes;
        }
        public bool Incomplete => CurrentBytes.Length < TotalMessageLength;
    }
}