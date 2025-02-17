using System.Collections.Frozen;
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
        // Serialize message
        byte[] MessageBytes = MemoryPackSerializer.Serialize(MessageType, this);
        // Create packed message
        PackedMessage PackedMessage = new(MessageType.Name, MessageBytes);
        // Serialize packed message
        return MemoryPackSerializer.Serialize(PackedMessage);
    }
    /// <summary>
    /// Deserialises an array of bytes as a message.
    /// </summary>
    public static Message FromBytes(in ReadOnlySpan<byte> Bytes) {
        // Deserialize packed message
        PackedMessage PackedMessage = MemoryPackSerializer.Deserialize<PackedMessage>(Bytes);
        // Get message type from name
        Type MessageType = MessageTypes[PackedMessage.TypeName];
        // Create message
        return (Message)MemoryPackSerializer.Deserialize(MessageType, PackedMessage.Bytes)!;
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