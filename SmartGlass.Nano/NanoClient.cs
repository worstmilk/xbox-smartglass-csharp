using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SmartGlass.Common;

namespace SmartGlass.Nano
{
    public class NanoClient : IDisposable
    {
        private readonly NanoRdpTransport _transport;
        private readonly Channels.ChannelManager _channelManager;

        internal List<Consumer.IConsumer> _consumers { get; set; }
        internal Provider.IProvider _provider { get; set; }

        public Guid SessionId { get; internal set; }
        public ushort ConnectionId { get; private set; }
        public ushort RemoteConnectionId { get; private set; }

        public bool ControlHandshakeDone { get; internal set; }

        public NanoClient(string address, int tcpPort, int udpPort, Guid sessionId)
        {
            _transport = new NanoRdpTransport(address, tcpPort, udpPort);
            _transport.MessageReceived += MessageReceived;
            _channelManager = new Channels.ChannelManager(this);
            _consumers = new List<Consumer.IConsumer>();
            _provider = null;
            ControlHandshakeDone = false;
            SessionId = sessionId;
            ConnectionId = (ushort)new Random().Next(5000);
        }

        public void AddConsumer(Consumer.IConsumer consumer)
        {
            _channelManager.Audio.FeedAudioFormat += consumer.ConsumeAudioFormat;
            _channelManager.Audio.FeedAudioData += consumer.ConsumeAudioData;
            _channelManager.Video.FeedVideoFormat += consumer.ConsumeVideoFormat;
            _channelManager.Video.FeedVideoData += consumer.ConsumeVideoData;

            _channelManager.InputFeedback.FeedInputFeedbackConfig += consumer.ConsumeInputFeedbackConfig;
            _channelManager.InputFeedback.FeedInputFeedbackFrame += consumer.ConsumeInputFeedbackFrame;
            _consumers.Add(consumer);
        }

        public bool RemoveConsumer(Consumer.IConsumer consumer)
        {
            _channelManager.Audio.FeedAudioFormat -= consumer.ConsumeAudioFormat;
            _channelManager.Audio.FeedAudioData -= consumer.ConsumeAudioData;
            _channelManager.Video.FeedVideoFormat -= consumer.ConsumeVideoFormat;
            _channelManager.Video.FeedVideoData -= consumer.ConsumeVideoData;

            _channelManager.InputFeedback.FeedInputFeedbackConfig -= consumer.ConsumeInputFeedbackConfig;
            _channelManager.InputFeedback.FeedInputFeedbackFrame -= consumer.ConsumeInputFeedbackFrame;
            return _consumers.Remove(consumer);
        }

        public bool AddProvider(Provider.IProvider provider)
        {
            if (_provider != null)
            {
                Debug.WriteLine("Already got a provider!");
                return false;
            }
            _provider = provider;

            _provider.FeedInputConfig += _channelManager.Input.OnInputConfigReceived;
            _provider.FeedInputFrame += _channelManager.Input.OnInputFrameReceived;
            _provider.FeedChatAudioFormat += _channelManager.ChatAudio.OnChatAudioConfigReceived;
            _provider.FeedChatAudioData += _channelManager.ChatAudio.OnChatAudioDataReceived;
            return true;
        }

        public bool RemoveProvider()
        {
            if (_provider == null)
            {
                Debug.WriteLine("Got no provider to remove!");
                return false;
            }

            _provider.FeedInputConfig -= _channelManager.Input.OnInputConfigReceived;
            _provider.FeedInputFrame -= _channelManager.Input.OnInputFrameReceived;
            _provider.FeedChatAudioFormat -= _channelManager.ChatAudio.OnChatAudioConfigReceived;
            _provider.FeedChatAudioData -= _channelManager.ChatAudio.OnChatAudioDataReceived;

            _provider = null;
            return true;
        }

        // TODO: Need to improve the robustness of this (async await on both handshakes, create client with an async static method?)
        public async Task Initialize()
        {
            SendControlHandshake();

            await Task.Run(() =>
            {
                while (!ControlHandshakeDone ||
                       !_channelManager.Video.HandshakeDone ||
                       !_channelManager.Audio.HandshakeDone)
                {
                    Thread.Sleep(250);
                }
            });
        }

        public async Task StartStream()
        {
            if (!_channelManager.Video.HandshakeDone || !_channelManager.Audio.HandshakeDone)
            {
                Console.WriteLine("Audio or Video handshake not done yet, cant start stream");
            }

            var udpHandshakeTask = Task.Run(() =>
            {
                while (!_transport.udpDataActive)
                {
                    SendUdpHandshake();
                    Thread.Sleep(250);
                }
            });

            _channelManager.Video.StartStream();
            _channelManager.Audio.StartStream();

            await udpHandshakeTask;
        }

        internal void MessageReceived(object sender, MessageReceivedEventArgs<RtpPacket> message)
        {
            var packet = message.Message;
            RtpPayloadType ptype = packet.Header.PayloadType;

            if (ptype != RtpPayloadType.Streamer)
            {
                Debug.WriteLine($"Received {ptype}");
            }

            switch (ptype)
            {
                case RtpPayloadType.Control:
                    OnControlHandshake(packet);
                    break;
                case RtpPayloadType.ChannelControl:
                    var controlMsg = packet.Payload as Packets.ChannelControl;
                    _channelManager.HandleChannelControl(controlMsg, packet.Header.ChannelId);
                    break;
                case RtpPayloadType.Streamer:
                    var streamer = packet.Payload as Packets.Streamer;
                    _channelManager.HandleStreamer(streamer, packet.Header.ChannelId);
                    break;
                case RtpPayloadType.UDPHandshake:
                    throw new NotSupportedException($"UDP HandshakeType from server received");
                default:
                    throw new NotSupportedException($"Unsupported payload type: {ptype}");
            }
        }

        internal void OnControlHandshake(RtpPacket packet)
        {
            var handshake = packet.Payload as Packets.ControlHandshake;
            if (handshake.Type != ControlHandshakeType.ACK)
            {
                throw new NotSupportedException($"Invalid Control HandshakeType received {handshake.Type}");
            }
            RemoteConnectionId = handshake.ConnectionId;
            ControlHandshakeDone = true;
        }

        internal void SendControlHandshake()
        {
            var payload = new Packets.ControlHandshake(ControlHandshakeType.SYN,
                                                            ConnectionId);
            var packet = new RtpPacket(RtpPayloadType.Control, payload);
            SendOnControlSocket(packet);
        }

        internal void SendUdpHandshake()
        {
            var payload = new Packets.UdpHandshake(ControlHandshakeType.ACK);
            var packet = new RtpPacket(RtpPayloadType.UDPHandshake, payload);
            SendOnStreamingSocket(packet);
        }

        internal async void SendOnStreamingSocket(RtpPacket packet)
        {
            packet.Header.ConnectionId = RemoteConnectionId;
            await _transport.SendAsyncStreaming(packet);
        }

        internal async void SendOnControlSocket(RtpPacket packet)
        {
            await _transport.SendAsyncControl(packet);
        }

        public void Dispose()
        {
            _transport.Dispose();
        }
    }
}
