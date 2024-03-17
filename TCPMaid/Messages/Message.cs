using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MemoryPack;
using static TCPMaid.Extensions;

namespace TCPMaid {
    /// <summary>
    /// The base class for messages that can be serialised and sent across a channel. Should not be reused.
    /// </summary>
    public abstract class Message {
        /// <summary>
        /// The generated identifier for the message.
        /// </summary>
        public ulong ID = Interlocked.Increment(ref LastID);

        private static readonly Dictionary<string, Type> MessageTypes = GetMessageTypes();
        private static ulong LastID;

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
        public static Message FromBytes(byte[] Bytes) {
            // Get message name length
            int MessageNameLength = BitConverter.ToInt32(Bytes.AsSpan(0, sizeof(int)));
            // Get message name
            string MessageName = Encoding.UTF8.GetString(Bytes[sizeof(int)..(sizeof(int) + MessageNameLength)]);
            // Get message bytes
            byte[] MessageBytes = Bytes[(sizeof(int) + MessageNameLength)..];
            // Get message type from name
            Type MessageType = GetMessageTypeFromName(MessageName)!;
            // Create message
            return (Message)MemoryPackSerializer.Deserialize(MessageType, MessageBytes)!;
        }

        /// <summary>
        /// Whether the message is only for internal use.
        /// </summary>
        [MemoryPackIgnore]
        public bool Internal => this is DisconnectMessage or NextFragmentMessage or PingRequest or PingResponse;

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
    /// <summary>
    /// The base class for messages that expect a response. Should not be reused.
    /// </summary>
    public abstract class Request : Message {
    }
    /// <summary>
    /// The base class for messages that respond to a request. Should not be reused.
    /// </summary>
    public abstract class Response : Message {
        /// <summary>
        /// The ID of the request this is a response to.
        /// </summary>
        public readonly ulong RequestID;
        /// <summary>
        /// Creates a new response for the given request ID.
        /// </summary>
        public Response(ulong RequestID) {
            this.RequestID = RequestID;
        }
    }
}
