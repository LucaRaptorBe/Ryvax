using UnityEngine;
using MOBANet.UnityView.Core;

/// <summary>
/// Mode de fonctionnement du jeu (réseau uniquement).
/// </summary>
public enum NetworkMode
{
    /// <summary>
    /// Mode serveur dédié. Pas de joueur local.
    /// </summary>
    Server,

    /// <summary>
    /// Mode client. Le joueur est spawné par le serveur.
    /// </summary>
    Client,

    /// <summary>
    /// Mode host (serveur + client). Le joueur local est spawné par le serveur.
    /// </summary>
    Host
}

/// <summary>
/// Gère le spawn des joueurs et la configuration de base du jeu.
/// Supporte uniquement les modes réseau (Server/Client/Host).
/// Le spawn est toujours géré par le système réseau.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Network Mode")]
    [Tooltip("Mode de fonctionnement réseau (Server/Client/Host).")]
    [SerializeField] private NetworkMode networkMode = NetworkMode.Host;

    [Header("Player Spawn")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private Vector3 defaultSpawnPosition = new Vector3(0f, 1f, 0f);

    [Header("Team Spawn Points")]
    [Tooltip("Points de spawn pour l'équipe Rouge (Team 1).")]
    [SerializeField] private Transform[] team1SpawnPoints;
    [Tooltip("Points de spawn pour l'équipe Bleue (Team 2).")]
    [SerializeField] private Transform[] team2SpawnPoints;

    [Header("References")]
    [SerializeField] private PlayerCamera mobaCamera;

    private GameObject currentPlayer;

    #region Properties

    /// <summary>
    /// Mode de fonctionnement actuel.
    /// </summary>
    public NetworkMode CurrentNetworkMode => networkMode;

    /// <summary>
    /// Préfab du joueur.
    /// </summary>
    public GameObject PlayerPrefab => playerPrefab;

    /// <summary>
    /// Caméra du joueur.
    /// </summary>
    public PlayerCamera PlayerCamera => mobaCamera;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Le spawn est toujours géré par le système réseau (GameBootstrap/ServerGameLoop)
        // Pas de spawn automatique en mode Offline (supprimé)
    }

    #endregion

    #region Spawn Methods


    /// <summary>
    /// Appelé par le système réseau quand un joueur est spawné pour ce client.
    /// </summary>
    /// <param name="player">Le GameObject du joueur.</param>
    /// <param name="isLocalPlayer">True si c'est le joueur contrôlé par ce client.</param>
    public void OnNetworkPlayerSpawned(GameObject player, bool isLocalPlayer)
    {
        if (isLocalPlayer)
        {
            currentPlayer = player;

            // Assigner la caméra au joueur local
            SetupCamera(player);

            Debug.Log($"[GameManager] Local network player spawned");
        }
        else
        {
            Debug.Log($"[GameManager] Remote player spawned");
        }
    }

    /// <summary>
    /// Configure la caméra pour suivre le joueur.
    /// </summary>
    private void SetupCamera(GameObject player)
    {
        if (mobaCamera != null)
        {
            mobaCamera.SetTarget(player.transform);
        }
        else
        {
            // Chercher la caméra si pas assignée
            PlayerCamera cam = FindFirstObjectByType<PlayerCamera>();
            if (cam != null)
            {
                cam.SetTarget(player.transform);
                mobaCamera = cam;
            }
        }
    }

    #endregion

    #region Spawn Position Helpers

    private Vector3 GetSpawnPosition()
    {
        // Si des points de spawn sont définis, en choisir un au hasard
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int randomIndex = Random.Range(0, spawnPoints.Length);
            if (spawnPoints[randomIndex] != null)
            {
                return spawnPoints[randomIndex].position;
            }
        }

        // Sinon utiliser la position par défaut
        return defaultSpawnPosition;
    }

    /// <summary>
    /// Obtient une position de spawn pour une équipe spécifique.
    /// </summary>
    /// <param name="teamId">1 = Rouge, 2 = Bleu</param>
    /// <param name="playerIndex">Index du joueur dans l'équipe</param>
    public Vector3 GetTeamSpawnPosition(byte teamId, int playerIndex)
    {
        Transform[] spawns = teamId == 1 ? team1SpawnPoints : team2SpawnPoints;

        if (spawns != null && spawns.Length > 0)
        {
            int index = playerIndex % spawns.Length;
            if (spawns[index] != null)
            {
                return spawns[index].position;
            }
        }

        // Fallback: position par défaut avec offset par équipe
        float xOffset = teamId == 1 ? -20f : 20f;
        float zOffset = playerIndex * 5f;
        return new Vector3(xOffset, 0f, zOffset);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Retourne le joueur local actuel.
    /// </summary>
    public GameObject GetLocalPlayer()
    {
        return currentPlayer;
    }

    /// <summary>
    /// Respawn le joueur. En mode réseau, doit être géré par le serveur.
    /// </summary>
    public void RespawnPlayer()
    {
        Debug.LogWarning("[GameManager] Respawn doit être géré par le serveur via le système réseau.");
    }

    /// <summary>
    /// Change le mode réseau. Doit être appelé avant le démarrage du jeu.
    /// </summary>
    public void SetNetworkMode(NetworkMode mode)
    {
        networkMode = mode;
    }

    #endregion
}
