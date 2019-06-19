using ENet;
using Event = ENet.Event;
using EventType = ENet.EventType;
using System;
using UnityEngine;
using System.Collections.Generic;

namespace Mirror
{
    public class Ignorance : Transport, ISegmentTransport
    {
        // debug
        public bool DebugEnabled = false;
        // server bind to all and addresses
        public bool ServerBindAll = true;
        public string ServerBindAddress = "127.0.0.1";
        public int CommunicationPort = 7777;
        // maximum packet sizes
        public int MaxPacketSize = 64;
        // Channels
        public ChannelTypes[] Channels;
        // custom peer limits
        public bool CustomMaxPeerLimit = false;
        public int CustomMaxPeers = 1000;
        // custom timeouts
        public bool CustomTimeoutLimit = false;
        public uint CustomTimeoutBaseTicks = 5000;
        public uint CustomTimeoutMultiplier = 3;
        // ping calculation timer
        public bool PingCalculationEnabled = true;
        public int PingCalculationFrameTimer = 120;    // assuming 60 frames per second, 2 second interval.
        // version of this transport
        private readonly string Version = "1.3.0 RC 2";
        // enet engine related things
        private bool ENETInitialized = false, ServerStarted = false, ClientStarted = false;
        private Host ENETHost = new Host(), ENETClientHost = new Host();                    // Didn't want to have to do this but i don't want to risk crashes.
        private Peer ENETPeer = new Peer();
        private Address ENETAddress = new Address();
        // lookup and reverse lookup dictionaries
        private Dictionary<int, Peer> ConnectionIDToPeers = new Dictionary<int, Peer>();
        private Dictionary<Peer, int> PeersToConnectionIDs = new Dictionary<Peer, int>();
        // mirror related things
        private byte[] PacketCache;
        private int NextConnectionID = 1;   // DO NOT MODIFY.
        // used for latency calculation
        private int PingCalculationFrames = 0;
        private int CurrentClientPing = 0;

        #region Client
        public override void ClientConnect(string address)
        {
            if (!ENETInitialized)
            {
                if (InitializeENET())
                {
                    Debug.Log($"Ignorance successfully initialized ENET.");
                    ENETInitialized = true;
                }
                else
                {
                    Debug.LogError($"Ignorance failed to initialize ENET! Cannot continue.");
                    return;
                }
            }

            if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - ClientConnect({address})");

            if (Channels.Length > 255)
            {
                Debug.LogError($"Ignorance: Too many channels. Channel limit is 255, you have {Channels.Length}. This would probably crash ENET. Aborting connection.");
                return;
            }

            if (CommunicationPort < ushort.MinValue || CommunicationPort > ushort.MaxValue)
            {
                Debug.LogError($"Ignorance: Bad communication port number. You need to set it between port 0 and 65535. Aborting connection.");
                return;
            }

            if (ENETClientHost == null || !ENETClientHost.IsSet) ENETClientHost.Create(null, 1, Channels.Length);
            if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - Created ENET Host object");

            ENETAddress.SetHost(address);
            ENETAddress.Port = (ushort)CommunicationPort;

            ENETPeer = ENETClientHost.Connect(ENETAddress, Channels.Length);
            if (CustomTimeoutLimit) ENETPeer.Timeout(Library.throttleScale, CustomTimeoutBaseTicks, CustomTimeoutBaseTicks * CustomTimeoutMultiplier);
            ClientStarted = true;

            if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - Client has been started!");
        }

        public override bool ClientConnected()
        {
            // No debug here. this gets spammed many times a tick
            return ENETPeer.IsSet && ENETPeer.State == PeerState.Connected;
        }

        public override void ClientDisconnect()
        {
            if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - ClientDisconnect()");

            if (ServerStarted)
            {
                Debug.LogWarning("MIRROR BUG: ClientDisconnect called even when we're in HostClient/Dedicated Server mode");
                return;
            }

            if (!IsValid(ENETClientHost)) return;
            if (ENETPeer.IsSet) ENETPeer.DisconnectNow(0);

            // Flush and free resources.
            if (IsValid(ENETClientHost))
            {
                ENETClientHost.Flush();
                ENETClientHost.Dispose();
            }
        }

        public override bool ClientSend(int channelId, byte[] data)
        {
            // redirect it to the ArraySegment version.
            return ClientSend(channelId, new ArraySegment<byte>(data));
        }

        public bool ClientSend(int channelId, ArraySegment<byte> data)
        {
            // Log spam if you really want that...
            // if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - ClientSend({channelId}, ({data.Count} bytes not shown))");
            if (!ENETClientHost.IsSet) return false;
            if (channelId > Channels.Length)
            {
                Debug.LogWarning($"Ignorance: Attempted to send data on channel {channelId} when we only have {Channels.Length} channels defined");
                return false;
            }

            Packet payload = default;
            payload.Create(data.Array, data.Offset, data.Count + data.Offset, (PacketFlags)Channels[channelId]);

            if (ENETPeer.Send((byte)channelId, ref payload))
            {
                if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - Outgoing packet on channel {channelId} OK");
                return true;
            }
            else
            {
                if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - Outgoing packet on channel {channelId} FAIL");
                return false;
            }
        }

        public string GetClientPing()
        {
            return CurrentClientPing.ToString();
        }
        #endregion

        // TODO: Check this out.
        public override int GetMaxPacketSize(int channelId = 0)
        {
            return PacketCache.Length;
        }

        // Server
        #region Server
        public override bool ServerActive()
        {
            return IsValid(ENETHost);
        }

        public override bool ServerDisconnect(int connectionId)
        {
            if (ConnectionIDToPeers.TryGetValue(connectionId, out Peer result))
            {
                result.DisconnectNow(0);
                return true;
            }
            else return false;
        }

        public override string ServerGetClientAddress(int connectionId)
        {
            if (ConnectionIDToPeers.TryGetValue(connectionId, out Peer result)) return $"{result.IP}:{result.Port}";
            else return "UNKNOWN";
        }

        public override bool ServerSend(int connectionId, int channelId, byte[] data)
        {
            return ServerSend(connectionId, channelId, new ArraySegment<byte>(data));
        }

        public bool ServerSend(int connectionId, int channelId, ArraySegment<byte> data)
        {
            if (!ENETHost.IsSet) return false;
            if (channelId > Channels.Length)
            {
                Debug.LogWarning($"Ignorance: Attempted to send data on channel {channelId} when we only have {Channels.Length} channels defined");
                return false;
            }

            Packet payload = default;

            if (ConnectionIDToPeers.TryGetValue(connectionId, out Peer targetPeer))
            {
                payload.Create(data.Array, data.Offset, data.Count + data.Offset, (PacketFlags)Channels[channelId]);
                if (targetPeer.Send((byte)channelId, ref payload))
                {
                    if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - Outgoing packet on channel {channelId} to connection id {connectionId} OK");
                    return true;
                }
                else
                {
                    if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - Outgoing packet on channel {channelId} to connection id {connectionId} FAIL");
                    return false;
                }
            }
            else
            {
                if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - Unknown connection id {connectionId}");
                return false;
            }
        }

        public override void ServerStart()
        {
            if (!ENETInitialized)
            {
                if (InitializeENET())
                {
                    Debug.Log($"Ignorance successfully initialized ENET.");
                    ENETInitialized = true;
                }
                else
                {
                    Debug.LogError($"Ignorance failed to initialize ENET! Cannot continue.");
                    return;
                }
            }

            // Setup.
#if UNITY_EDITOR_OSX
            ENETAddress.SetHost("::0");
            Debug.Log("Mac OS Unity Editor workaround applied.");
#else
            if (Application.platform == RuntimePlatform.OSXPlayer)
            {
                ENETAddress.SetHost("::0");
            }
            else
            {
                ENETAddress.SetHost((ServerBindAll ? "0.0.0.0" : ServerBindAddress));
            }
#endif
            ENETAddress.Port = (ushort)CommunicationPort;
            if (ENETHost == null || !ENETHost.IsSet) ENETHost = new Host();

            // Go go go! Clear those corners!
            ENETHost.Create(ENETAddress, CustomMaxPeerLimit ? CustomMaxPeers : (int)Library.maxPeers, Channels.Length, 0, 0);

            if (DebugEnabled) Debug.Log($"Ignorance: DEBUGGING MODE - Server should be created now... If Ignorance immediately crashes after this line, please file a bug report on the GitHub.");
            ServerStarted = true;
        }

        public override void ServerStop()
        {
            if (DebugEnabled)
            {
                Debug.Log("Ignorance: DEBUGGING MODE - ServerStop()");
                Debug.Log("Ignorance: Cleaning the packet cache...");
            }

            PacketCache = new byte[MaxPacketSize * 1024];

            if (DebugEnabled) Debug.Log("Ignorance: Cleaning up lookup dictonaries");
            ConnectionIDToPeers.Clear();
            PeersToConnectionIDs.Clear();

            if (IsValid(ENETHost))
            {
                ENETHost.Dispose();
            }

            ServerStarted = false;
        }
        #endregion

        public override void Shutdown()
        {
            if (DebugEnabled) Debug.Log("Ignorance: Cleaning the packet cache...");

            ServerStarted = false;
            ClientStarted = false;

            ENETInitialized = false;
            Library.Deinitialize();
        }

        // core
        #region Core Transport
        private bool InitializeENET()
        {
            PacketCache = new byte[MaxPacketSize * 1024];
            if (DebugEnabled) Debug.Log($"Initialized new packet cache, {MaxPacketSize * 1024} bytes capacity.");

            return Library.Initialize();
        }

        // server pump
        private bool ProcessServerMessages()
        {
            // Never attempt process anything if we're not initialized
            if (!ENETInitialized) return false;
            // Never attempt to process anything if the server is not valid.
            if (!IsValid(ENETHost)) return false;
            // Never attempt to process anything if the server is not active.
            if (!ServerStarted) return false;

            bool serverWasPolled = false;
            int newConnectionID = NextConnectionID;

            while (!serverWasPolled)
            {
                if (ENETHost.CheckEvents(out Event networkEvent) <= 0)
                {
                    if (ENETHost.Service(0, out networkEvent) <= 0) break;

                    serverWasPolled = true;
                }

                switch (networkEvent.Type)
                {
                    case EventType.Connect:
                        // A client connected to the server. Assign a new ID to them.
                        if (DebugEnabled)
                        {
                            Debug.Log($"Ignorance: New connection from {networkEvent.Peer.IP}:{networkEvent.Peer.Port}");
                            Debug.Log($"Ignorance: Map {networkEvent.Peer.IP}:{networkEvent.Peer.Port} (ENET Peer {networkEvent.Peer.ID}) => Mirror World Connection {newConnectionID}");
                        }

                        if (CustomTimeoutLimit) networkEvent.Peer.Timeout(Library.throttleScale, CustomTimeoutBaseTicks, CustomTimeoutBaseTicks * CustomTimeoutMultiplier);

                        // Map them into our dictonaries.
                        PeersToConnectionIDs.Add(networkEvent.Peer, newConnectionID);
                        ConnectionIDToPeers.Add(newConnectionID, networkEvent.Peer);

                        OnServerConnected.Invoke(newConnectionID);
                        NextConnectionID++;
                        break;

                    case EventType.Disconnect:
                    case EventType.Timeout:
                        // A client disconnected.
                        if (PeersToConnectionIDs.TryGetValue(networkEvent.Peer, out int deadPeerConnID))
                        {
                            if (DebugEnabled) Debug.Log($"Ignorance: Dead Peer. {networkEvent.Peer.ID} (Mirror connection {deadPeerConnID}) died.");
                            OnServerDisconnected.Invoke(deadPeerConnID);
                            // cleanup
                            PeersToConnectionIDs.Remove(networkEvent.Peer);
                            ConnectionIDToPeers.Remove(deadPeerConnID);
                        }
                        // We don't give a shit about any other connections. if they are bogus then Mirror doesn't need to know about them. Could be a performance impact.
                        break;
                    case EventType.Receive:
                        // Only process data from known peers.
                        if (PeersToConnectionIDs.TryGetValue(networkEvent.Peer, out int knownConnectionID))
                        {
                            if (networkEvent.Packet.Length > PacketCache.Length)
                            {
                                if (DebugEnabled) Debug.Log($"Ignorance: Packet too big to fit in buffer. {networkEvent.Packet.Length} packet bytes vs {PacketCache.Length} cache bytes {networkEvent.Peer.ID}.");
                                networkEvent.Packet.Dispose();
                            }
                            else
                            {
                                // invoke on the server.
                                networkEvent.Packet.CopyTo(PacketCache);
                                int spLength = networkEvent.Packet.Length;
                                networkEvent.Packet.Dispose();

                                OnServerDataReceived.Invoke(knownConnectionID, new ArraySegment<byte>(PacketCache, 0, spLength));
                            }
                        }
                        else
                        {
                            // Emit a warning and clean the packet. We don't want it in memory.
                            if (DebugEnabled) Debug.LogWarning($"Ignorance: Unknown packet from Peer {networkEvent.Peer.ID}. Be cautious - if you get this error too many times, you're likely being attacked.");
                            networkEvent.Packet.Dispose();
                        }
                        break;
                }
            }

            // We're done here. Return.
            return true;
        }

        // client pump
        private bool ProcessClientMessages()
        {
            // Never do anything when ENET is not initialized
            if (!ENETInitialized)
            {
                return false;
            }

            // Never do anything when ENET is in a different mode
            if (!IsValid(ENETClientHost) || ENETPeer.State == PeerState.Uninitialized || !ClientStarted)
            {
                return false;
            }

            bool clientWasPolled = false;

            // Only process messages if the client is valid.
            while (!clientWasPolled)
            {
                if (!IsValid(ENETClientHost)) return false;

                if (ENETClientHost.CheckEvents(out Event networkEvent) <= 0)
                {
                    if (ENETClientHost.Service(0, out networkEvent) <= 0) break;
                    clientWasPolled = true;
                }

                switch (networkEvent.Type)
                {
                    case EventType.Connect:
                        // Client connected.
                        // Debug.Log("Connect");
                        OnClientConnected.Invoke();
                        break;
                    case EventType.Timeout:
                    case EventType.Disconnect:
                        // Client disconnected.
                        // Debug.Log("Disconnect");
                        OnClientDisconnected.Invoke();
                        break;
                    case EventType.Receive:
                        // Client recieving some data.
                        // Debug.Log("Data");
                        if (networkEvent.Packet.Length > PacketCache.Length)
                        {
                            if (DebugEnabled) Debug.Log($"Ignorance: Packet too big to fit in buffer. {networkEvent.Packet.Length} packet bytes vs {PacketCache.Length} cache bytes {networkEvent.Peer.ID}.");
                            networkEvent.Packet.Dispose();
                        }
                        else
                        {
                            // invoke on the client.
                            networkEvent.Packet.CopyTo(PacketCache);
                            int spLength = networkEvent.Packet.Length;
                            networkEvent.Packet.Dispose();

                            OnClientDataReceived.Invoke(new ArraySegment<byte>(PacketCache, 0, spLength));
                        }
                        break;
                }
            }
            // We're done here. Return.
            return true;
        }

        // utility
        private bool IsValid(Host host)
        {
            return host != null && host.IsSet;
        }
        #endregion

        // known packet types.
        [Serializable]
        public enum ChannelTypes
        {
            Reliable = PacketFlags.Reliable,
            ReliableUnsequenced = PacketFlags.Reliable | PacketFlags.Unsequenced,
            Unreliable = PacketFlags.Unsequenced,
            UnreliableFragmented = PacketFlags.UnreliableFragment,
            UnreliableSequenced = PacketFlags.None
        }

        // monobehaviour specific stuff
        public void LateUpdate()
        {
            // note: we need to check enabled in case we set it to false
            // when LateUpdate already started.
            // (https://github.com/vis2k/Mirror/pull/379)

            // Coburn: does the order here really matter? Server then client?
            if (enabled)
            {
                if (PingCalculationEnabled)
                {
                    PingCalculationFrames++;
                    if (PingCalculationFrames >= PingCalculationFrameTimer)
                    {
                        if (!ENETPeer.IsSet || !IsValid(ENETClientHost)) CurrentClientPing = 0;
                        else CurrentClientPing = (int)ENETPeer.RoundTripTime;

                        PingCalculationFrames = 0;
                    }
                }

                if (ServerStarted) ProcessServerMessages();
                if (ClientStarted) ProcessClientMessages();
            }
        }

        public override string ToString()
        {
            // A little complicated if else mess.
            if (ServerActive() && NetworkClient.active)
            {
                // HostClient Mode
                return $"Ignorance {Version} in HostClient Mode";
            }
            else if (ServerActive() && !NetworkClient.active)
            {
                // Dedicated server masterrace mode
                return $"Ignorance {Version} in Dedicated Server Mode";
            }
            else if (!ServerActive() && NetworkClient.active)
            {
                // Client mode
                return $"Ignorance {Version} in Client Mode";
            }
            else
            {
                // Unknown state. How did that happen?
                return $"Ignorance {Version} disconnected/unknown state";
            }
        }

        // Sanity checks.
        private void OnValidate()
        {
            if (Channels != null && Channels.Length >= 2)
            {
                // Check to make sure that Channel 0 and 1 are correct.
                if (Channels[0] != ChannelTypes.Reliable) Channels[0] = ChannelTypes.Reliable;
                if (Channels[1] != ChannelTypes.Unreliable) Channels[1] = ChannelTypes.Unreliable;
            }
            else
            {
                Channels = new ChannelTypes[2]
                {
                    ChannelTypes.Reliable,
                    ChannelTypes.Unreliable
                };
            }
        }
    }
}