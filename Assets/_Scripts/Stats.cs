using System;
using System.Collections.Generic;
using Mirror;

namespace MirrorLoadTest
{
    public struct ClientStats
    {
        public long sentMessages;
        public long receivedMessages;
        public long pendingMessages;
        public long missingMessages;
        public long outOfOrderMessages;
        public long unknownMessages;
        public long totalDeltaTime;
    }

    public struct MessageData
    {
        public long messages;
        public long timestamp;
    }

    public static class Stats
    {
        public static Dictionary<NetworkConnection, ClientStats> stats = new Dictionary<NetworkConnection, ClientStats>();

        public static void PrintStats(bool customObservers, byte printedLines, int repeatHeadersLines, TimeSpan ts)
        {
            if (printedLines % repeatHeadersLines == 0)
            {
                // blank line above headers
                Console.WriteLine();

                if (customObservers)
                    Console.WriteLine("  Time     Clients     Sent      Rcvd    Pends-A   MIAs   OOOs   Unks   Delta-A     Delta-T     Obs-A");
                else
                    Console.WriteLine("  Time     Clients     Sent      Rcvd    Pends-A   MIAs   OOOs   Unks   Delta-A     Delta-T");
            }

            int observers = 0;
            long sentMessages = 0;
            long receivedMessages = 0;
            long pendingMessages = 0;
            long missingMessages = 0;
            long outOfOrderMessages = 0;
            long unknownMessages = 0;
            long totalDeltaTime = 0;
            long avgDeltaTime = 0;
            decimal avgPendingMsgs;

            foreach (KeyValuePair<NetworkConnection, ClientStats> kvp in Stats.stats)
            {
                observers += kvp.Key.playerController.observers.Count;
                sentMessages += kvp.Value.sentMessages;
                receivedMessages += kvp.Value.receivedMessages;
                pendingMessages += kvp.Value.pendingMessages;
                missingMessages += kvp.Value.missingMessages;
                outOfOrderMessages += kvp.Value.outOfOrderMessages;
                unknownMessages += kvp.Value.unknownMessages;
                totalDeltaTime += kvp.Value.totalDeltaTime;
            }

            if (sentMessages > 0)
                avgDeltaTime = totalDeltaTime / sentMessages;

            avgPendingMsgs = (decimal)pendingMessages / (decimal)Stats.stats.Count;

            string timeStamp = string.Format("{0:00}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds);

            TimeSpan deltaT = TimeSpan.FromMilliseconds(totalDeltaTime);
            string deltaString= string.Format("{0:00}:{1:00}:{2:00}:{3:00}", deltaT.Days, deltaT.Hours, deltaT.Minutes, deltaT.Seconds);

            if (customObservers)
                Console.WriteLine("{0}    {1:0000}     {2:0000000}   {3:0000000}    {4:00.00}    {5:0000}   {6:0000}   {7:0000}     {8:000}     {9}    {10:000}", timeStamp, Stats.stats.Count, sentMessages, receivedMessages, avgPendingMsgs, missingMessages, outOfOrderMessages, unknownMessages, avgDeltaTime, deltaString, observers / Stats.stats.Count);
            else
                Console.WriteLine("{0}    {1:0000}     {2:0000000}   {3:0000000}    {4:00.00}    {5:0000}   {6:0000}   {7:0000}     {8:000}     {9}", timeStamp, Stats.stats.Count, sentMessages, receivedMessages, avgPendingMsgs, missingMessages, outOfOrderMessages, unknownMessages, avgDeltaTime, deltaString);

            //                      Time     Clients     Sent      Rcvd    Pends-A   MIAs   OOOs   Unks   Delta-A     Delta-T     Obs-A
            //                    00:00:00     0000    0000000   0000000    00.00    0000   0000   0000     000     00:00:00:00    0000
        }
    }
}
