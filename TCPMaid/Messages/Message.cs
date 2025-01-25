using System.Text;
using MemoryPack;
using static TCPMaid.Extensions;

namespace TCPMaid;

/// <summary>
/// The base class for messages that can be serialised and sent across a channel. Should not be reused.
/// </summary>
public abstract class Message(long? Id = null) {
    /// <summary>
    /// The generated identifier for the message. If this is a response, it should be set to the ID of the request.
    /// </summary>
    public long Id { get; set; } = Id ?? GenerateId();

    private static readonly Dictionary<string, Type> MessageTypes = GetMessageTypes();
    private static long LastId;

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
        return Concat(MessageNameLengthBytes, MessageNameBytes, MessageBytes);
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
        Type MessageType = GetMessageTypeFromName(MessageName)!;
        // Create message
        return (Message)MemoryPackSerializer.Deserialize(MessageType, MessageBytes)!;
    }
    /// <summary>
    /// Generates a unique message identifier.
    /// </summary>
    public static long GenerateId() {
        return Interlocked.Increment(ref LastId);
    }

    /// <summary>
    /// Whether the message is only for internal <see cref="TCPMaid"/> use and should be ignored by your application.
    /// </summary>
    public bool IsInternal() {
        return this is DisconnectMessage or NextFragmentMessage or PingRequest or PingResponse or StreamMessage;
    }

    /// <summary>
    /// Searches the cached available assemblies for a message type with the given name.
    /// </summary>
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