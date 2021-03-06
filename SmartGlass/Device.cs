using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using SmartGlass.Common;
using SmartGlass.Messaging;
using SmartGlass.Messaging.Discovery;
using SmartGlass.Messaging.Power;
using Org.BouncyCastle.X509;

namespace SmartGlass
{
    public class Device
    {
        private static readonly TimeSpan discoveryListenTime = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan pingTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan[] pingRetries = new TimeSpan[]
        {
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromSeconds(5)
        };

        public static Task<IEnumerable<Device>> DiscoverAsync()
        {
            return Task.Run(() =>
            {
                using (var messageTransport = new MessageTransport())
                {
                    var requestMessage = new PresenceRequestMessage();

                    return messageTransport.ReadMessages(discoveryListenTime,
                        () => messageTransport.SendAsync(requestMessage).GetAwaiter().GetResult())
                        .OfType<PresenceResponseMessage>()
                        .DistinctBy(m => m.HardwareId)
                        .Select(m => new Device(m)).ToArray().AsEnumerable();
                }
            });
        }

        public static async Task<Device> PingAsync(string addressOrHostname)
        {
            using (var messageTransport = new MessageTransport(addressOrHostname))
            {
                var requestMessage = new PresenceRequestMessage();

                var response = await TaskExtensions.WithRetries(() =>
                    messageTransport.WaitForMessageAsync<PresenceResponseMessage>(pingTimeout,
                    () => messageTransport.SendAsync(requestMessage).GetAwaiter().GetResult()),
                        pingRetries);

                return new Device(response);
            }
        }

        public static async Task<Device> PowerOnAsync(string liveId, int times = 5, int delay = 1000)
        {
            using (var messageTransport = new MessageTransport())
            {
                var poweronRequestMessage = new PowerOnMessage { LiveId = liveId };

                for (var i = 0; i < times; i++)
                {
                    await messageTransport.SendAsync(poweronRequestMessage);
                    await Task.Delay(delay);
                }

                var presenceRequestMessage = new PresenceRequestMessage();

                var response = await TaskExtensions.WithRetries(() =>
                    messageTransport.WaitForMessageAsync<PresenceResponseMessage>(pingTimeout,
                    () => messageTransport.SendAsync(presenceRequestMessage).GetAwaiter().GetResult()),
                        pingRetries);

                return new Device(response);
            }
        }

        public IPAddress Address { get; private set; }
        public DeviceFlags Flags { get; private set; }
        public DeviceType DeviceType { get; private set; }
        public string Name { get; private set; }
        public Guid HardwareId { get; private set; }
        public X509Certificate Certificate { get; private set; }

        private Device(PresenceResponseMessage message)
        {
            Address = message.Origin.Address;
            Flags = message.Flags;
            DeviceType = message.DeviceType;
            Name = message.Name;
            HardwareId = message.HardwareId;
            Certificate = message.Certificate;
        }
    }
}
