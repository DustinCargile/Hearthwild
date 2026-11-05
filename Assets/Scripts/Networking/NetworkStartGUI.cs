using Unity.Netcode;
using UnityEngine;

namespace TopDownRPG.Networking
{
    // Optional local testing UI; remove for production builds.
    public sealed class NetworkStartGUI : MonoBehaviour
    {
        private void OnGUI()
        {
            NetworkManager net = NetworkManager.Singleton;
            if (net == null || net.IsListening) return;
            GUILayout.BeginArea(new Rect(15, 15, 180, 130), GUI.skin.box);
            if (GUILayout.Button("Start Host")) net.StartHost();
            if (GUILayout.Button("Start Client")) net.StartClient();
            if (GUILayout.Button("Start Dedicated Server")) net.StartServer();
            GUILayout.EndArea();
        }
    }
}
