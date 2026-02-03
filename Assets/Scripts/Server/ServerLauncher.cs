// ServerLauncher.cs - Auto-start server in headless mode
// Detects command-line arguments and starts server automatically

using UnityEngine;

namespace MOBANet.Server
{
    /// <summary>
    /// Handles automatic server startup in headless/dedicated server builds.
    /// Parses command-line arguments to configure server settings.
    /// </summary>
    public class ServerLauncher : MonoBehaviour
    {
        [Header("Server Settings")]
        [SerializeField] private ServerGameLoop _serverGameLoop;
        [SerializeField] private ushort _defaultPort = 7777;
        [SerializeField] private bool _autoStartInHeadless = true;

        private void Awake()
        {
            // Find ServerGameLoop if not assigned
            if (_serverGameLoop == null)
            {
                _serverGameLoop = FindAnyObjectByType<ServerGameLoop>();
            }

            if (_serverGameLoop == null)
            {
                Debug.LogError("[ServerLauncher] ServerGameLoop not found!");
                return;
            }

            // Parse command-line arguments
            string[] args = System.Environment.GetCommandLineArgs();
            bool isServerMode = false;
            ushort port = _defaultPort;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-server":
                    case "--server":
                        isServerMode = true;
                        Debug.Log("[ServerLauncher] Server mode enabled via command-line");
                        break;

                    case "-port":
                    case "--port":
                        if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort parsedPort))
                        {
                            port = parsedPort;
                            Debug.Log($"[ServerLauncher] Port set to {port} via command-line");
                        }
                        break;

                    case "-batchmode":
                        // Unity's headless mode flag
                        isServerMode = true;
                        Debug.Log("[ServerLauncher] Batchmode detected, enabling server mode");
                        break;
                }
            }

            // Auto-start if headless build
            if (_autoStartInHeadless && Application.isBatchMode)
            {
                isServerMode = true;
                Debug.Log("[ServerLauncher] Headless/Batch mode detected, auto-starting server");
            }

            // Start server if in server mode
            if (isServerMode)
            {
                StartServer(port);
            }
        }

        /// <summary>
        /// Start the dedicated server
        /// </summary>
        private void StartServer(ushort port)
        {
            Debug.Log($"[ServerLauncher] Starting dedicated server on port {port}...");

            // Disable vsync and set target framerate for server
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 144;

            // Use reflection to set port on ServerGameLoop if needed
            // This allows command-line port override
            var portField = typeof(ServerGameLoop).GetField("_port",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (portField != null)
            {
                portField.SetValue(_serverGameLoop, port);
                Debug.Log($"[ServerLauncher] Port override set to {port}");
            }

            // Start server (will use the port we just set)
            _serverGameLoop.StartServer();

            Debug.Log($"[ServerLauncher] Server started successfully on port {port}");
            Debug.Log($"[ServerLauncher] Server listening for connections...");
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[ServerLauncher] Server shutting down...");
        }
    }
}
