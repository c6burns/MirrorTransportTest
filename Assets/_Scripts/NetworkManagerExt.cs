using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using System;

namespace TransportStress
{
    public class NetworkManagerExt : NetworkManager
    {
        public struct ClientStats
        {
            public long sentMessages;
            public long receivedMessages;
            public long pendingMessages;
            public long unknownMessages;
            public long outOfOrderMessages;
            public long totalDeltaTime;
        }

        [Header("Custom Settings")]
        [Range(3, 300), Tooltip("Interval to print stats (seconds)")]
        public int printStatsInterval = 30;

        [Range(3, 300), Tooltip("Repeat column headings every n lines")]
        public int repeatHeadersLines = 30;

        [Tooltip("Enable physics-based proximity observers")]
        public bool customObservers = false;

        [Tooltip("Total size of the area for random spawning")]
        public Vector3 spawnVolume = new Vector3(600, 600, 600);

        public static Dictionary<NetworkConnection, ClientStats> clientStats = new Dictionary<NetworkConnection, ClientStats>();

        public override void OnServerAddPlayer(NetworkConnection conn, AddPlayerMessage extraMessage)
        {
            float x = UnityEngine.Random.Range(-spawnVolume.x / 2, spawnVolume.x / 2);
            float y = UnityEngine.Random.Range(-spawnVolume.y / 2, spawnVolume.y / 2);
            float z = UnityEngine.Random.Range(-spawnVolume.z / 2, spawnVolume.z / 2);
            Vector3 pos = new Vector3(x, y, z);

            GameObject player = Instantiate(playerPrefab, pos, Quaternion.identity);
            player.GetComponent<SphereCollider>().enabled = customObservers;
            player.GetComponent<AreaOfInterest>().customObservers = customObservers;
            NetworkServer.AddPlayerForConnection(conn, player);

            clientStats.Add(conn, new ClientStats());

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OnServerAddPlayer ID: {0:0000}  Observers: {1:0000}  Pos: {2}", conn.connectionId, conn.playerController.observers.Count, conn.playerController.gameObject.transform.position);
            Console.ResetColor();
        }

        public override void OnServerDisconnect(NetworkConnection conn)
        {
            base.OnServerDisconnect(conn);
            clientStats.Remove(conn);

            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("OnServerDisconnect: ID {0}", conn.connectionId);
            Console.ResetColor();
        }

        public override void OnClientConnect(NetworkConnection conn)
        {
            base.OnClientConnect(conn);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OnClientConnect: {0}", conn.address);
            Console.ResetColor();
        }

        public override void OnClientError(NetworkConnection conn, int errorCode)
        {
            base.OnClientError(conn, errorCode);

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("OnClientError: Error Code {1}", errorCode);
            Console.ResetColor();
        }

        public override void OnClientDisconnect(NetworkConnection conn)
        {
            base.OnClientDisconnect(conn);

            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("OnClientDisconnect");
            Console.ResetColor();
        }

        //public override void Start()
        //{
        //    base.Start();
        //    Resources.UnloadUnusedAssets();
        //}

        public override void Start()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                // After 5 seconds, UnloadUnusedAssets
                Invoke(nameof(UnloadAssets), 5);

                foreach (string arg in Environment.GetCommandLineArgs())
                {
                    if (arg == "client")
                    {
                        Application.targetFrameRate = 30;
                        StartClient();
                        return;
                    }
                }

                // no startup argument found...assume server
                StartServer();
            }
        }

        void UnloadAssets()
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("Unloading unused assets");
            Console.ResetColor();

            Resources.UnloadUnusedAssets();
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        long lastPrintStats = 0;
        byte printedLines = 0;

        void Update()
        {
            if (NetworkServer.active && clientStats.Count > 0 && stopwatch.ElapsedMilliseconds > lastPrintStats + printStatsInterval * 1000)
            {
                lastPrintStats = stopwatch.ElapsedMilliseconds;

                if (printedLines % repeatHeadersLines == 0)
                {
                    // blank line above headers
                    Console.WriteLine();

                    if (customObservers)
                        Console.WriteLine("Clients    Obs-A       Sent       Rcvd    Pends-A    Unks     OOOs     Delta-T    Delta-A");
                    else
                        Console.WriteLine("Clients      Sent       Rcvd    Pends-A    Unks     OOOs     Delta-T    Delta-A");
                }

                long observers = 0;
                long sentMessages = 0;
                long receivedMessages = 0;
                long pendingMessages = 0;
                long unknownMessages = 0;
                long outOfOrderMessages = 0;
                long totalDeltaTime = 0;
                long avgDeltaTime = 0;
                long avgPendingMsgs = 0;

                foreach (KeyValuePair<NetworkConnection, ClientStats> kvp in clientStats)
                {
                    observers += kvp.Key.playerController.observers.Count;
                    sentMessages += kvp.Value.sentMessages;
                    receivedMessages += kvp.Value.receivedMessages;
                    pendingMessages += kvp.Value.pendingMessages;
                    unknownMessages += kvp.Value.unknownMessages;
                    outOfOrderMessages += kvp.Value.outOfOrderMessages;
                    totalDeltaTime += kvp.Value.totalDeltaTime;
                }

                if (sentMessages > 0)
                    avgDeltaTime = totalDeltaTime / sentMessages;

                if (clientStats.Count > 0)
                    avgPendingMsgs = pendingMessages / clientStats.Count;

                if (customObservers)
                    Console.WriteLine("  {0:0000}      {1:0000}     {2:0000000}    {3:0000000}    {4:00000}    {5:00000}    {6:00000}    {7:000000000}    {8:00000}", clientStats.Count, observers / clientStats.Count, sentMessages, receivedMessages, pendingMessages, unknownMessages, outOfOrderMessages, totalDeltaTime, avgDeltaTime);
                else
                    Console.WriteLine("  {0:0000}     {1:0000000}    {2:0000000}    {3:00000}    {4:00000}    {5:00000}    {6:000000000}    {7:00000}", clientStats.Count, sentMessages, receivedMessages, pendingMessages, unknownMessages, outOfOrderMessages, totalDeltaTime, avgDeltaTime);

                printedLines += 1;
            }
        }
    }
}
