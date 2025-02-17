using System.Collections.Frozen;
using System.Text;
using MemoryPack;

namespace TCPMaid;

/// <summary>
/// The base class for messages that can be serialised and sent across a channel.
/// </summary>
public abstract record Message() {
    /// <summary>
    /// The generated identifier for the message. If this is a response, it should be set to the ID of the request.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// A lookup table for the available <see cref="Message"/> sub-types.
    /// </summary>
    /// <remarks>
    /// This is automatically filled with types found in available assemblies.
    /// </remarks>
    public static FrozenDictionary<string, Type> MessageTypes { get; set; } = FindMessageSubTypes();

    /// <summary>
    /// Constructs a message with a predetermined ID.
    /// </summary>
    public Message(Guid Id) : this() {
        this.Id = Id;
    }

    /// <summary>
    /// Serialises the message and its name as an array of bytes.
    /// </summary>
    public byte[] ToBytes() {
        // Get message type
        Type MessageType = GetType();
        // Get message name length bytes
        byte[] MessageNameLengthBytes = BitConverter.GetBytes(MessageType.Name.Length);
        // Get message name bytes
        byte[] MessageNameBytes = Encoding.UTF8.GetBytes(MessageType.Name);
        // Get message bytes
        byte[] MessageBytes = MemoryPackSerializer.Serialize(MessageType, this);
        // Create message bytes
        return [.. MessageNameLengthBytes, .. MessageNameBytes, .. MessageBytes];
    }
    /// <summary>
    /// Deserialises an array of bytes as a message.
    /// </summary>
    public static Message FromBytes(ReadOnlySpan<byte> Bytes) {
        // Get message name length
        int MessageNameLength = BitConverter.ToInt32(Bytes[..sizeof(int)]);
        // Get message name
        string MessageName = Encoding.UTF8.GetString(Bytes[sizeof(int)..(sizeof(int) + MessageNameLength)]);
        // Get message bytes
        ReadOnlySpan<byte> MessageBytes = Bytes[(sizeof(int) + MessageNameLength)..];
        // Get message type from name
        Type MessageType = MessageTypes.GetValueOrDefault(MessageName)!;
        // Create message
        return (Message)MemoryPackSerializer.Deserialize(MessageType, MessageBytes)!;
    }

    /// <summary>
    /// Whether the message is only for internal <see cref="TCPMaid"/> use and should be ignored by your application.
    /// </summary>
    public bool IsInternal() {
        return this is DisconnectMessage or NextFragmentMessage or PingRequest or PingResponse or StreamMessage;
    }

    private static FrozenDictionary<string, Type> FindMessageSubTypes() {
        return AppDomain.CurrentDomain.GetAssemblies().SelectMany(Assembly => Assembly.GetTypes())
            .Where(Type => Type.IsClass && !Type.IsAbstract && Type.IsSubclassOf(typeof(Message))
        ).ToFrozenDictionary(Type => Type.Name);
    }
}