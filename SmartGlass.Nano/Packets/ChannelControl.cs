using System;
using System.IO;
using SmartGlass.Common;
using SmartGlass.Nano;

namespace SmartGlass.Nano.Packets
{
    [RtpPayloadType(RtpPayloadType.ChannelControl)]
    internal class ChannelControl : ISerializableLE
    {
        private static ISerializableLE CreateFromControlType(ChannelControlType controlType)
        {
            var type = ChannelControlTypeAttribute.GetTypeForMessageType(controlType);
            if (type == null)
            {
                return null;
            }

            return (ISerializableLE)Activator.CreateInstance(type);
        }

        public ChannelControlType Type { get; internal set; }
        public ISerializableLE Data { get; internal set; }

        public ChannelControl()
        {
        }

        public ChannelControl(ChannelControlType type, ISerializableLE data)
        {
            Type = type;
            Data = data;
        }

        public void Deserialize(BinaryReader br)
        {
            Type = (ChannelControlType)br.ReadUInt32();
            Data = CreateFromControlType(Type);
            Data.Deserialize(br);
        }

        public void Serialize(BinaryWriter bw)
        {
            bw.Write((uint)Type);
            Data.Serialize(bw);
        }
    }
}