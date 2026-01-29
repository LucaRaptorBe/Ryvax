// GameBootstrap.cs - Game initialization and mode selection
// Entry point for starting server, client, or host mode

using UnityEngine;
using MOBANet.NetAdapter;
using MOBANet.NetAdapter.FishNet;

namespace MOBANet.UnityView.Core
{
    /// <summary>
    /// Game bootstrap and mode selection.
    /// Handles command-line arguments and UI for mode selection.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Components")]
        [SerializeField] private FishNetAdapter _netAdapter;
        [SerializeField] private ServerGameLoop _serverLoop;
        [SerializeField] private NetworkClient _networkClient;

        [Header("Settings")]
        [SerializeField] private ushort _defaultPort = 7777;
        [SerializeField] private string _defaultAddress = "127.0.0.1";
        [SerializeField] private bool _showDebugUI = true;

        #endregion

        #region Private Fields

        private string _addressInput = "127.0.0.1";
        private string _portInput = "7777";
        private bool _modeSelected = false;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Find components if not assigned
            if (_netAdapter == null) _netAdapter = FindAnyObjectByType<FishNetAdapter>();
            if (_serverLoop == null) _serverLoop = FindAnyObjectByType<ServerGameLoop>();
            if (_networkClient == null) _networkClient = FindAnyObjectByType<NetworkClient>();

            _addressInput = _defaultAddress;
            _portInput = _defaultPort.ToString();
        }

        private void Start()
        {
            // Process command line arguments
            ProcessCommandLine();
        }

        #endregion

        #region Command Line Processing

        private void ProcessCommandLine()
        {
            string[] args = System.Environment.GetCommandLineArgs();

            bool isServer = false;
            bool isClient = false;
            string address = _defaultAddress;
            ushort port = _defaultPort;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-server":
                    case "--server":
                        isServer = true;
                        break;

                    case "-client":
                    case "--client":
                        isClient = true;
                        break;

                    case "-host":
                    case "--host":
                        isServer = true;
                        isClient = true;
                        break;

                    case "-port":
                    case "--port":
                        if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort p))
                        {
                            port = p;
                            i++;
                        }
                        break;

                    case "-address":
                    case "--address":
                    case "-connect":
                    case "--connect":
                        if (i + 1 < args.Length)
                        {
                            address = args[i + 1];
                            i++;
                        }
                        break;
                }
            }

            // Auto-start based on command line
            if (isServer && isClient)
            {
                StartHost(port);
            }
            else if (isServer)
            {
                StartServer(port);
            }
            else if (isClient)
            {
                StartClient(address, port);
            }
        }

        #endregion

        #region Mode Control

        /// <summary>
        /// Start as dedicated server
        /// </summary>
        public void StartServer(ushort port)
        {
            _modeSelected = true;

            // Set GameManager mode
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetNetworkMode(NetworkMode.Server);
            }

            // Disable client loop
            if (_networkClient != null) _networkClient.enabled = false;

            // Enable and start server
            if (_serverLoop != null)
            {
                _serverLoop.enabled = true;
            }

            // Start the network server via FishNetAdapter
            if (_netAdapter != null)
            {
                _netAdapter.StartServer(port);
            }

            Debug.Log($"[Bootstrap] Starting dedicated server on port {port}");
        }

        /// <summary>
        /// Connect as client
        /// </summary>
        public void StartClient(string address, ushort port)
        {
            Debug.Log($"[Bootstrap] StartClient called with address={address}, port={port}");
            _modeSelected = true;

            // Set GameManager mode
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetNetworkMode(NetworkMode.Client);
                Debug.Log("[Bootstrap] GameManager mode set to Client");
            }
            else
            {
                Debug.LogWarning("[Bootstrap] GameManager.Instance is null!");
            }

            // Disable server loop
            if (_serverLoop != null)
            {
                _serverLoop.enabled = false;
                Debug.Log("[Bootstrap] Server loop disabled");
            }

            // Enable client loop
            if (_networkClient != null)
            {
                _networkClient.enabled = true;
                Debug.Log("[Bootstrap] Client loop enabled");
            }
            else
            {
                Debug.LogError("[Bootstrap] NetworkClient is null!");
            }

            // Connect via FishNetAdapter
            if (_netAdapter != null)
            {
                Debug.Log($"[Bootstrap] Calling FishNetAdapter.StartClient({address}, {port})");
                _netAdapter.StartClient(address, port);
                Debug.Log("[Bootstrap] FishNetAdapter.StartClient returned");
            }
            else
            {
                Debug.LogError("[Bootstrap] FishNetAdapter is null!");
            }

            Debug.Log($"[Bootstrap] Connecting to {address}:{port}");
        }

        /// <summary>
        /// Start as host (server + client)
        /// </summary>
        public void StartHost(ushort port)
        {
            _modeSelected = true;
            _defaultPort = port;

            // Set GameManager mode
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetNetworkMode(NetworkMode.Host);
            }

            // Enable both loops
            if (_serverLoop != null) _serverLoop.enabled = true;
            if (_networkClient != null) _networkClient.enabled = true;

            // Start host via FishNetAdapter (server + local client)
            if (_netAdapter != null)
            {
                _netAdapter.StartHost(port);
            }

            Debug.Log($"[Bootstrap] Starting host on port {port}");
        }

        private void ConnectLocalClient()
        {
            _networkClient?.Connect("127.0.0.1", _defaultPort);
        }

        #endregion

        #region Debug UI

        private void OnGUI()
        {
            if (!_showDebugUI || _modeSelected) return;

            // Scale UI based on screen size
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            scale = Mathf.Max(scale, 1f); // Minimum scale of 1

            // Styles
            GUIStyle titleStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = Mathf.RoundToInt(24 * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(18 * scale)
            };

            GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = Mathf.RoundToInt(18 * scale)
            };

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(20 * scale)
            };

            // Mode selection UI
            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;
            float boxWidth = 450f * scale;
            float boxHeight = 320f * scale;

            GUILayout.BeginArea(new Rect(centerX - boxWidth/2, centerY - boxHeight/2, boxWidth, boxHeight));

            GUILayout.Label("DreamGame MOBA 50v50 - Network", titleStyle, GUILayout.Height(50 * scale));
            GUILayout.Space(15 * scale);

            // Address input
            GUILayout.BeginHorizontal();
            GUILayout.Label("Address:", labelStyle, GUILayout.Width(100 * scale));
            _addressInput = GUILayout.TextField(_addressInput, textFieldStyle, GUILayout.Height(30 * scale));
            GUILayout.EndHorizontal();

            GUILayout.Space(10 * scale);

            // Port input
            GUILayout.BeginHorizontal();
            GUILayout.Label("Port:", labelStyle, GUILayout.Width(100 * scale));
            _portInput = GUILayout.TextField(_portInput, textFieldStyle, GUILayout.Height(30 * scale));
            GUILayout.EndHorizontal();

            GUILayout.Space(20 * scale);

            // Buttons
            if (GUILayout.Button("Start Server", buttonStyle, GUILayout.Height(40 * scale)))
            {
                if (ushort.TryParse(_portInput, out ushort port))
                {
                    StartServer(port);
                }
            }

            GUILayout.Space(5 * scale);

            if (GUILayout.Button("Connect as Client", buttonStyle, GUILayout.Height(40 * scale)))
            {
                if (ushort.TryParse(_portInput, out ushort port))
                {
                    StartClient(_addressInput, port);
                }
            }

            GUILayout.Space(5 * scale);

            if (GUILayout.Button("Start Host", buttonStyle, GUILayout.Height(40 * scale)))
            {
                if (ushort.TryParse(_portInput, out ushort port))
                {
                    StartHost(port);
                }
            }

            GUILayout.EndArea();
        }

        #endregion
    }
}
