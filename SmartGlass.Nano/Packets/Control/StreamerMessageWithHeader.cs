using System;
using System.IO;
using SmartGlass.Common;
using SmartGlass.Nano;

namespace SmartGlass.Nano.Packets
{
    internal class StreamerMessageWithHeader : ISerializableLE
    {
        private static ISerializableLE CreateFromControlOpCode(ControlOpCode opCode)
        {
            var type = ControlOpCodeAttribute.GetTypeForMessageType(opCode);
            if (type == null)
            {
                return null;
            }

            return (ISerializableLE)Activator.CreateInstance(type);
        }

        public ControlHeader Header { get; private set; }
        public ISerializableLE Payload { get; private set; }

        public StreamerMessageWithHeader()
        {
            Header = new ControlHeader();
        }

        public StreamerMessageWithHeader(ControlHeader header, ISerializableLE payload)
        {
            Header = header;
            Payload = payload;
        }

        public void Deserialize(BinaryReader br)
        {
            Header.Deserialize(br);
            Payload = CreateFromControlOpCode(Header.OpCode);
            Payload.Deserialize(br);
        }

        public void Serialize(BinaryWriter bw)
        {
            Header.Serialize(bw);
            Payload.Serialize(bw);
        }
    }
}
