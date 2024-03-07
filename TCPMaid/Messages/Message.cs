using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MemoryPack;
using static TCPMaid.Extensions;

namespace TCPMaid {
    public abstract class Message {
        public ulong ID = Interlocked.Increment(ref LastID);

        private static readonly Dictionary<string, Type> MessageTypes = GetMessageTypes();
        private static ulong LastID;

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

        [MemoryPackIgnore]
        public bool Internal => this is DisconnectMessage or NextFragmentMessage or PingRequest or PingResponse;

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
    }
    public abstract class Response : Message {
        public readonly ulong RequestID;
        public Response(ulong RequestID) {
            this.RequestID = RequestID;
        }
    }
}
