using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace MirrorLoadTest
{
    public class AreaOfInterest : NetworkBehaviour
    {
        [HideInInspector]
        public bool customObservers = false;

        public LayerMask castLayers = ~0;
        public int visibleRange = 200;
        static Collider[] hitsBuffer3D = new Collider[10000];

        // called when a new player enters
        public override bool OnCheckObserver(NetworkConnection newObserver)
        {
            if (!customObservers) return base.OnCheckObserver(newObserver);

            //Debug.LogFormat("OnCheckObserver for {0} {1} {2}", newObserver, newObserver.playerController.transform.position, transform.position);

            if (newObserver.playerController == null)
            {
                Debug.LogWarningFormat("OnCheckObserver: newObserver {0} playerController is null", newObserver.connectionId);
                return false;
            }

            return visibleRange > Vector3.Distance(newObserver.playerController.transform.position, transform.position);
        }

        public override bool OnRebuildObservers(HashSet<NetworkConnection> observers, bool initial)
        {
            if (!customObservers) return false;

            //Debug.LogFormat("OnRebuildObservers for {0}", connectionToClient);

            // Find players within range
            // Cast without allocating garbage for performance
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, visibleRange, hitsBuffer3D, castLayers);
            if (hitCount == hitsBuffer3D.Length) Debug.LogWarning("NetworkProximityChecker's OverlapSphere test for " + name + " has filled the whole buffer(" + hitsBuffer3D.Length + "). Some results might have been omitted. Consider increasing buffer size.");

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hitsBuffer3D[i];

                // Collider might be on pelvis, often the NetworkIdentity is in a parent
                // GetComponentInParent looks in the object itself and then parents
                NetworkIdentity identity = hit.GetComponentInParent<NetworkIdentity>();

                // (if an object has a connectionToClient, it is a player)
                if (identity != null && identity.connectionToClient != null)
                    observers.Add(identity.connectionToClient);
            }

            // Always return true when overriding OnRebuildObservers so
            // Mirror knows not to use the built-in rebuild method.
            return true;
        }
    }
}
