using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TransportStress
{
    public class Player : NetworkBehaviour
    {
        [Range(10, 5000), Tooltip("Interval to send messages (ms)")]
        public int sendInterval = 100;

        [Range(3, 300), Tooltip("Interval to update stats (seconds)")]
        public int updateInterval = 30;

        [Range(100,1000), Tooltip("Sanity limit of backlog to disconnect client")]
        public int backlogLimit = 500;

        long totalDeltaTime = 0;
        long sentMessages = 0;
        long receivedMessages = 0;
        long unknownMessages = 0;
        long outOfOrderMessages = 0;

        Stopwatch stopwatch = Stopwatch.StartNew();

        public struct MessageData
        {
            public long messages;
            public long timestamp;
        }

        // Messages are added to this list when sent and removed when returned from server
        List<MessageData> SentMessages = new List<MessageData>();

        [SyncVar(hook = nameof(ReceiveData))]
        public MessageData serverData = new MessageData();

        public void ReceiveData(MessageData receivedData)
        {
            if (isLocalPlayer)
            {
                // This can be true if server double-sends SyncVar or if messages are not ack'ed and resent from server
                if (!SentMessages.Contains(receivedData))
                    unknownMessages += 1;

                // This should only be possible with unordered channels in some transports
                if (SentMessages.IndexOf(receivedData) > 0)
                    outOfOrderMessages += 1;

                // Remove the message from the backlog and update local stats
                if (SentMessages.Remove(receivedData))
                {
                    receivedMessages += 1;
                    totalDeltaTime += stopwatch.ElapsedMilliseconds - receivedData.timestamp;
                }
            }
        }

        long lastCmdSyncData = 0;
        long lastUpdateStats = 0;

        bool shuttingDown = false;

        void Update()
        {
            if (NetworkClient.active && isLocalPlayer && !shuttingDown)
            {
                if (SentMessages.Count >= backlogLimit)
                {
                    shuttingDown = true;

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Excessive message backlog detected ({0}) -- Shutting down", SentMessages.Count);
                    Console.ResetColor();

                    connectionToServer.Disconnect();
                    return;
                }

                if (stopwatch.ElapsedMilliseconds > lastCmdSyncData + sendInterval)
                {
                    lastCmdSyncData = stopwatch.ElapsedMilliseconds;
                    sentMessages += 1;

                    MessageData clientData = new MessageData();
                    clientData.messages = sentMessages;
                    clientData.timestamp = lastCmdSyncData;

                    // Add msg to backlog...SyncVar hook will remove it
                    SentMessages.Add(clientData);

                    CmdSyncData(clientData);
                }

                if (stopwatch.ElapsedMilliseconds > lastUpdateStats + (updateInterval * 1000))
                {
                    lastUpdateStats = stopwatch.ElapsedMilliseconds;

                    NetworkManagerExt.ClientStats stats;
                    stats.sentMessages = sentMessages;
                    stats.receivedMessages = receivedMessages;
                    stats.pendingMessages = SentMessages.Count;
                    stats.unknownMessages = unknownMessages;
                    stats.outOfOrderMessages = outOfOrderMessages;
                    stats.totalDeltaTime = totalDeltaTime;

                    CmdUpdateStats(stats);
                }
            }
        }

        [Command]
        public void CmdSyncData(MessageData clientData)
        {
            serverData = clientData;
        }

        [Command]
        public void CmdUpdateStats(NetworkManagerExt.ClientStats stats)
        {
            NetworkManagerExt.clientStats[connectionToClient] = stats;
        }
    }
}