﻿using System.Diagnostics;
using UnityEngine;
using Mirror;
using System;

namespace MirrorLoadTest
{
    public class NetworkManagerExt : NetworkManager
    {
        [Header("Custom Settings")]
        [Range(3, 300), Tooltip("Interval to print stats (seconds)")]
        public int printStatsInterval = 30;

        [Range(3, 300), Tooltip("Repeat column headings every n lines")]
        public int repeatHeadersLines = 30;

        [Tooltip("Enable physics-based proximity observers")]
        public bool customObservers = false;

        [Tooltip("Total size of the area for random spawning")]
        public Vector3 spawnVolume = new Vector3(600, 600, 600);

        public override void OnServerConnect(NetworkConnection conn)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OnServerConnect: {0}", conn.connectionId);
            Console.ResetColor();

            base.OnServerConnect(conn);
        }

        public override void OnServerReady(NetworkConnection conn)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OnServerReady: {0}", conn.connectionId);
            Console.ResetColor();

            base.OnServerReady(conn);
        }

        public override void OnServerError(NetworkConnection conn, int errorCode)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("OnServerError: {0} {1}", conn.connectionId, errorCode);
            Console.ResetColor();

            base.OnServerError(conn, errorCode);
        }

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

            Stats.stats.Add(conn, new ClientStats());

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OnServerAddPlayer ID: {0:0000}  Observers: {1:0000}  Pos: {2}", conn.connectionId, conn.playerController.observers.Count, conn.playerController.gameObject.transform.position);
            Console.ResetColor();
        }

        public override void OnServerDisconnect(NetworkConnection conn)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("OnServerDisconnect: ID {0}", conn.connectionId);
            Console.ResetColor();

            base.OnServerDisconnect(conn);
            Stats.stats.Remove(conn);
        }

        public override void OnClientConnect(NetworkConnection conn)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OnClientConnect: {0}", conn.address);
            Console.ResetColor();

            base.OnClientConnect(conn);
        }

        public override void OnClientError(NetworkConnection conn, int errorCode)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("OnClientError: Error Code {1}", errorCode);
            Console.ResetColor();

            base.OnClientError(conn, errorCode);
        }

        public override void OnClientDisconnect(NetworkConnection conn)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("OnClientDisconnect");
            Console.ResetColor();

            base.OnClientDisconnect(conn);
        }

        public override void Start()
        {
            if (isHeadless)
            {
                // After 5 seconds, UnloadUnusedAssets
                Invoke(nameof(UnloadAssets), 5);

                string[] args = Environment.GetCommandLineArgs();

                if (args.Length == 1)
                {
                    StartServer();
                    return;
                }
                else if (args[1] == "client" && args.Length == 2)
                {
                    Application.targetFrameRate = 30;
                    StartClient();
                    return;
                }
                else if (args[1] == "client" && args.Length == 3)
                {
                    networkAddress = args[2];
                    Application.targetFrameRate = 30;
                    StartClient();
                    return;
                }
                else if (args[1] == "client" && args.Length == 4)
                {
                    networkAddress = args[2];

                    var telepathy = Transport.activeTransport as TelepathyTransport;
                    if (telepathy != null) ushort.TryParse(args[3], out telepathy.port);

                    var ignorance = Transport.activeTransport as Ignorance;
                    if (ignorance != null) int.TryParse(args[3], out ignorance.CommunicationPort);

                    var webSockets = Transport.activeTransport as Mirror.Websocket.WebsocketTransport;
                    if (webSockets != null) int.TryParse(args[3], out webSockets.port);

                    Application.targetFrameRate = 30;
                    StartClient();
                    return;
                }

                // invalid startup arguments...assume server
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
            if (!NetworkServer.active) return;

            if (Stats.stats.Count > 0 && stopwatch.ElapsedMilliseconds > lastPrintStats + printStatsInterval * 1000)
            {
                lastPrintStats = stopwatch.ElapsedMilliseconds;

                if (inputBuffer.Length > 0)
                    Console.WriteLine();

                Stats.PrintStats(customObservers, printedLines, repeatHeadersLines, stopwatch.Elapsed);

                printedLines += 1;

                if (inputBuffer.Length > 0)
                {
                    Console.Write(inputBuffer);
                }
            }

            if (Console.KeyAvailable) GetInput();
        }

        string inputBuffer = "";

        void GetInput()
        {
            ConsoleKeyInfo consoleKeyInfo = Console.ReadKey();
            switch (consoleKeyInfo.Key)
            {
                case ConsoleKey.Backspace:
                    if (inputBuffer.Length > 0)
                    {
                        // Delete the character (backspace + space + backspace)
                        Console.Write("\b \b");
                        inputBuffer = inputBuffer.Substring(0, inputBuffer.Length - 1);
                    }
                    break;
                case ConsoleKey.Enter:
                    if (inputBuffer.Length > 0)
                    {
                        Console.WriteLine();
                        inputBuffer = inputBuffer.Trim();
                        ProcessInput();
                        inputBuffer = "";
                    }
                    break;
                default:
                    // Restrict to ASCII printable characters by matching range from space through tilde
                    if (System.Text.RegularExpressions.Regex.IsMatch(consoleKeyInfo.KeyChar.ToString(), @"[ -~]"))
                    {
                        Console.Write(consoleKeyInfo.KeyChar);
                        inputBuffer += consoleKeyInfo.KeyChar;
                    }
                    break;
            }
        }

        void ProcessInput()
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("You entered {0}", inputBuffer);
            Console.ResetColor();
        }
    }
}
