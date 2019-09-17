using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MirrorLoadTest
{
    public class Player : NetworkBehaviour
    {
        [Range(10, 5000), Tooltip("Interval to send messages (ms)")]
        public int sendInterval = 100;

        [Range(3, 300), Tooltip("Interval to update stats (seconds)")]
        public int updateInterval = 30;

        [Range(100, 1000), Tooltip("Sanity limit of backlog to disconnect client")]
        public int backlogLimit = 500;

        long totalDeltaTime;
        long sentMessages = 0;
        long receivedMessages = 0;
        long unknownMessages = 0;
        long outOfOrderMessages = 0;

        Stopwatch stopwatch = Stopwatch.StartNew();

        // Messages are added to this list when sent and removed when returned from server
        List<MessageData> SentMessages = new List<MessageData>();

        // Messages are added to this list when a later message arrives
        List<MessageData> MissingMessages = new List<MessageData>();

        MessageData[] msgRingBuffer = new MessageData[50];

        long lastCmdSyncData = 0;
        long lastUpdateStats = 0;

        bool shuttingDown = false;

        void Update()
        {
            if (!NetworkClient.active || !isLocalPlayer || shuttingDown) return;

            if (SentMessages.Count >= backlogLimit)
            {
                shuttingDown = true;

                Console.ForegroundColor = ConsoleColor.DarkRed;
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

                //Console.WriteLine("Client Sending data {0} {1}", sentMessages, lastCmdSyncData);

                CmdSendData(clientData);
            }

            if (stopwatch.ElapsedMilliseconds > lastUpdateStats + (updateInterval * 1000))
            {
                lastUpdateStats = stopwatch.ElapsedMilliseconds;

                ClientStats stats;
                stats.sentMessages = sentMessages;
                stats.receivedMessages = receivedMessages;
                stats.pendingMessages = SentMessages.Count;
                stats.missingMessages = MissingMessages.Count;
                stats.unknownMessages = unknownMessages;
                stats.outOfOrderMessages = outOfOrderMessages;
                stats.totalDeltaTime = totalDeltaTime;

                //Console.WriteLine("Client Sending stats {0} {1} {2} {3} {4} {5} {6}",
                //    stats.sentMessages,
                //    stats.receivedMessages,
                //    stats.pendingMessages,
                //    stats.missingMessages,
                //    stats.unknownMessages,
                //    stats.outOfOrderMessages,
                //    stats.totalDeltaTime);

                CmdUpdateStats(stats);
            }
        }

        [Command]
        void CmdSendData(MessageData clientData)
        {
            //Console.WriteLine("Server Received data {0} {1}", clientData.messages, clientData.timestamp);
            msgRingBuffer[clientData.messages % 50] = clientData;
            RpcRelayData(clientData);
        }

        [ClientRpc]
        void RpcRelayData(MessageData receivedData)
        {
            if (!isLocalPlayer) return;

            //Console.WriteLine("Client Received data {0} {1}", receivedData.messages, receivedData.timestamp);

            receivedMessages += 1;
            totalDeltaTime += stopwatch.ElapsedMilliseconds - receivedData.timestamp;

            TimeSpan ts = stopwatch.Elapsed;
            string timeStamp = string.Format("{0:00}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds);

            if (MissingMessages.Remove(receivedData))
            {
                // This is a missing message that has shown up out-of-order

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Missing Msg: {0} {1} {2} {3}", timeStamp, receivedData.messages, receivedData.timestamp);
                Console.ResetColor();

                CmdLostMsg(receivedData);

                outOfOrderMessages += 1;
            }
            else if (!SentMessages.Contains(receivedData))
            {
                // This can be true if server double-sends SyncVar or if messages are not ack'ed and resent from server

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Unknown Msg: {0} {1} {2}", timeStamp, receivedData.messages, receivedData.timestamp);
                Console.ResetColor();

                unknownMessages += 1;
            }
            else
            {
                int msgIndex = SentMessages.IndexOf(receivedData);

                if (msgIndex > 0)
                {
                    // This should only be possible with unordered channels in some transports

                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("Out-of-order Msg: {0} {1} {2} {3}", timeStamp, msgIndex, receivedData.messages, receivedData.timestamp);
                    Console.ResetColor();

                    // copy earlier messages to MissingMessages list
                    for (int index = 0; index < msgIndex; index++)
                    {
                        MissingMessages.Add(SentMessages[0]);
                        CmdLostMsg(SentMessages[0]);
                    }

                    // remove up to, but not including, this message
                    // may result in unknownMessages later if these eventually show up
                    SentMessages.RemoveRange(0, msgIndex);
                }
            }

            // Remove the message from the backlog
            SentMessages.Remove(receivedData);
        }

        [Command]
        void CmdUpdateStats(ClientStats stats)
        {
            //Console.WriteLine("Received stats {0} {1} {2} {3} {4} {5} {6}",
            //    stats.sentMessages,
            //    stats.receivedMessages,
            //    stats.pendingMessages,
            //    stats.missingMessages,
            //    stats.unknownMessages,
            //    stats.outOfOrderMessages,
            //    stats.totalDeltaTime);

            Stats.stats[connectionToClient] = stats;
        }

        [Command]
        void CmdLostMsg(MessageData messageData)
        {
            TimeSpan ts = stopwatch.Elapsed;
            string timeStamp = string.Format("{0:00}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds);

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("{0} Client {1} missing msg {2} {3}", timeStamp, connectionToClient.connectionId, messageData.messages, messageData.timestamp);

            if (Array.IndexOf(msgRingBuffer, messageData) >= 0)
            {
                Console.WriteLine("{0} Server has msg {1} in the ring buffer.", timeStamp, messageData.messages);
            }

            Console.ResetColor();
        }

        [Command]
        void CmdOOOMsg(MessageData messageData)
        {
            TimeSpan ts = stopwatch.Elapsed;
            string timeStamp = string.Format("{0:00}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds);

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("{0} Client {1} OOO msg {2} {3}", timeStamp, connectionToClient.connectionId, messageData.messages, messageData.timestamp);
            Console.ResetColor();
        }
    }
}