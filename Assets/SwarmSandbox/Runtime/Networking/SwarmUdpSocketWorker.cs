using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SwarmECS.Simulation.Netcode.Transport;

namespace SwarmECS.Runtime.Networking
{
    /// <summary>
    /// Owns the blocking UDP receive thread. The worker validates envelopes and
    /// copies datagrams into a fixed queue; it never observes or mutates SwarmWorld.
    /// </summary>
    internal sealed class SwarmUdpSocketWorker : IDisposable
    {
        private const int MaximumPeerId = 2;

        private readonly Socket _socket;
        private readonly FixedDatagramQueue _receiveQueue;
        private readonly IPEndPoint[] _peerEndpoints = new IPEndPoint[MaximumPeerId + 1];
        private readonly object _endpointGate = new object();
        private readonly Thread _receiveThread;
        private volatile bool _running;
        private bool _disposed;

        public SwarmUdpSocketWorker(int bindPort, int queueCapacity)
        {
            _receiveQueue = new FixedDatagramQueue(queueCapacity);
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveTimeout = 100,
                SendTimeout = 100,
            };
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, bindPort));
            LocalPort = ((IPEndPoint)_socket.LocalEndPoint).Port;
            _running = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = $"SwarmUdpReceive:{LocalPort}",
            };
            _receiveThread.Start();
        }

        public int LocalPort { get; }

        public FixedDatagramQueue ReceiveQueue => _receiveQueue;

        public long ReceivedDatagrams { get; private set; }

        public long RejectedDatagrams { get; private set; }

        public long SentDatagrams { get; private set; }

        public long SentBytes { get; private set; }

        public long SocketErrors { get; private set; }

        public void SetPeerEndpoint(uint peerId, string address, int port)
        {
            if (peerId > MaximumPeerId || port <= 0 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException();
            }

            IPAddress parsed = IPAddress.Parse(address);
            lock (_endpointGate)
            {
                _peerEndpoints[peerId] = new IPEndPoint(parsed, port);
            }
        }

        public bool HasPeerEndpoint(uint peerId)
        {
            if (peerId > MaximumPeerId)
            {
                return false;
            }

            lock (_endpointGate)
            {
                return _peerEndpoints[peerId] != null;
            }
        }

        public bool TrySend(uint peerId, byte[] bytes, int count)
        {
            if (_disposed || peerId > MaximumPeerId || bytes == null || count <= 0 ||
                count > bytes.Length || count > SwarmUdpPacketCodec.MaxDatagramBytes)
            {
                return false;
            }

            IPEndPoint endpoint;
            lock (_endpointGate)
            {
                endpoint = _peerEndpoints[peerId];
            }

            if (endpoint == null)
            {
                return false;
            }

            try
            {
                int sent = _socket.SendTo(bytes, 0, count, SocketFlags.None, endpoint);
                if (sent != count)
                {
                    SocketErrors++;
                    return false;
                }

                SentDatagrams++;
                SentBytes += sent;
                return true;
            }
            catch (SocketException)
            {
                SocketErrors++;
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _running = false;
            try
            {
                _socket.Close();
            }
            catch (SocketException)
            {
                // Shutdown is best effort.
            }

            if (_receiveThread.IsAlive)
            {
                _receiveThread.Join(500);
            }

            _socket.Dispose();
        }

        private void ReceiveLoop()
        {
            byte[] receiveBuffer = new byte[SwarmUdpPacketCodec.MaxDatagramBytes];
            while (_running)
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    int count = _socket.ReceiveFrom(
                        receiveBuffer,
                        0,
                        receiveBuffer.Length,
                        SocketFlags.None,
                        ref remote);
                    if (!SwarmUdpPacketCodec.TryRead(
                            receiveBuffer,
                            count,
                            out SwarmPacketHeader header,
                            out _) ||
                        header.PeerId > MaximumPeerId)
                    {
                        RejectedDatagrams++;
                        continue;
                    }

                    var remoteEndpoint = (IPEndPoint)remote;
                    lock (_endpointGate)
                    {
                        _peerEndpoints[header.PeerId] = new IPEndPoint(
                            remoteEndpoint.Address,
                            remoteEndpoint.Port);
                    }

                    if (_receiveQueue.TryEnqueue(header.PeerId, receiveBuffer, count))
                    {
                        ReceivedDatagrams++;
                    }
                }
                catch (SocketException exception)
                {
                    if (!_running)
                    {
                        break;
                    }

                    if (exception.SocketErrorCode != SocketError.TimedOut &&
                        exception.SocketErrorCode != SocketError.WouldBlock)
                    {
                        SocketErrors++;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
    }
}
