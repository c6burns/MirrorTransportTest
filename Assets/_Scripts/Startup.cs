using System;
using UnityEngine;
using UnityEngine.Rendering;
using Mirror;

namespace TransportStress
{
    public class Startup : MonoBehaviour
    {
        [SerializeField]
        public NetworkManager manager;

        void Start()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                foreach (string arg in Environment.GetCommandLineArgs())
                {
                    if (arg == "server")
                    {
                        manager.StartServer();
                        return;
                    }
                }

                // no startup argument found...assume client
                Application.targetFrameRate = 30;
                manager.StartClient();
            }
        }
    }
}
