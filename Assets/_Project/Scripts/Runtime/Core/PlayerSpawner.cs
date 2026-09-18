using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Peak.Core
{
    /// <summary>
    /// 플레이어 스폰 (Docs/205_network.md 2·4장). Boot <c>[Boot]/PlayerSpawner</c> — in-scene NetworkObject 의 자식 NetworkBehaviour. **호스트(서버) 전용**.
    /// <list type="bullet">
    /// <item>Phase 가 Playing 이 되면 (구독 시점에 이미 Playing 이면 즉시) 접속 중이면서 PlayerObject 가 없는 클라이언트를 모두 스폰</item>
    /// <item>Playing 중 새 접속도 스폰 (M0 개발용 — 늦은 참가 금지·세션 잠금은 M4, 205 1장)</item>
    /// <item>위치 = 콘텐츠 씬 루트 SpawnPoint + (접속 순서 인덱스 × spawnSpacing, 0, 0). 콘텐츠 씬에 SpawnPoint 가 없으면 스폰하지 않는다</item>
    /// <item>Phase 가 Lobby 로 바뀌면 모든 PlayerObject 를 Despawn — GameManager 가 Phase 를 바꾼 뒤 씬을 전환하므로 씬 언로드 전에 끝난다</item>
    /// </list>
    /// NetworkConfig.PlayerPrefab 은 비워 둔다 (NGO 자동 스폰 금지). 산 생성 후 시작점 스폰은 M2 가 이 클래스를 고친다.
    /// </summary>
    public sealed class PlayerSpawner : NetworkBehaviour
    {
        /// <summary>콘텐츠 씬 루트의 스폰 기준 오브젝트 이름 (M0SceneBuilder 가 배치).</summary>
        public const string SpawnPointName = "SpawnPoint";

        [Tooltip("Prefabs/Player.prefab (Peak > Setup > Rebuild M0 Scenes 가 연결)")]
        [SerializeField] private NetworkObject playerPrefab;

        private GameManager _game;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
            {
                return;
            }
            _game = GameManager.Instance;
            if (_game == null)
            {
                Log.Error(LogCategory.Net, "PlayerSpawner: GameManager 가 없다");
                return;
            }
            _game.Phase.OnValueChanged += OnPhaseChanged;
            NetworkManager.OnClientConnectedCallback += OnClientConnected;

            if (_game.CurrentPhase == MatchPhase.Playing)
            {
                SpawnAllMissing();
            }
        }

        public override void OnNetworkDespawn()
        {
            Unsubscribe();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }

        private void Unsubscribe()
        {
            if (_game == null)
            {
                return;
            }
            _game.Phase.OnValueChanged -= OnPhaseChanged;
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            }
            _game = null;
        }

        private void OnPhaseChanged(MatchPhase previous, MatchPhase current)
        {
            switch (current)
            {
                case MatchPhase.Playing:
                    SpawnAllMissing();
                    break;
                case MatchPhase.Lobby:
                    DespawnAll();
                    break;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (_game == null || _game.CurrentPhase != MatchPhase.Playing)
            {
                return;
            }
            if (!TryFindSpawnPoint(out var spawnPoint))
            {
                return;
            }
            var ids = NetworkManager.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == clientId)
                {
                    SpawnIfMissing(clientId, i, spawnPoint);
                    return;
                }
            }
        }

        private void SpawnAllMissing()
        {
            if (!TryFindSpawnPoint(out var spawnPoint))
            {
                return;
            }
            var ids = NetworkManager.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
            {
                SpawnIfMissing(ids[i], i, spawnPoint);
            }
        }

        private void SpawnIfMissing(ulong clientId, int joinIndex, Transform spawnPoint)
        {
            if (playerPrefab == null)
            {
                Log.Error(LogCategory.Player, "PlayerSpawner: Player 프리팹 참조가 비어 있다 (Peak > Setup > Rebuild Player Prefab 먼저, 그다음 Rebuild M0 Scenes)");
                return;
            }
            var tuning = GameConfig.Tuning;
            if (tuning == null)
            {
                Log.Error(LogCategory.Player, "PlayerSpawner: GameTuning 이 바인딩되지 않았다");
                return;
            }
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject != null)
            {
                return;
            }

            Vector3 position = spawnPoint.position + new Vector3(joinIndex * tuning.spawnSpacing, 0f, 0f);
            var instance = Instantiate(playerPrefab, position, spawnPoint.rotation);
            instance.SpawnAsPlayerObject(clientId, true);
            Log.Info(LogCategory.Player, $"플레이어 스폰: clientId={clientId}, index={joinIndex}, pos={position}");
        }

        private void DespawnAll()
        {
            var clients = NetworkManager.ConnectedClientsList;
            int count = 0;
            for (int i = 0; i < clients.Count; i++)
            {
                var playerObject = clients[i].PlayerObject;
                if (playerObject != null && playerObject.IsSpawned)
                {
                    playerObject.Despawn(true);
                    count++;
                }
            }
            if (count > 0)
            {
                Log.Info(LogCategory.Player, $"플레이어 디스폰: {count}명 (Lobby 복귀)");
            }
        }

        /// <summary>현재 콘텐츠 씬(<see cref="SceneFlow.CurrentSceneName"/>) 루트에서 SpawnPoint 를 찾는다. 없으면 Info 한 줄.</summary>
        private static bool TryFindSpawnPoint(out Transform spawnPoint)
        {
            spawnPoint = null;
            string sceneName = SceneFlow.Instance != null ? SceneFlow.Instance.CurrentSceneName : string.Empty;
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == SpawnPointName)
                    {
                        spawnPoint = root.transform;
                        return true;
                    }
                }
            }
            Log.Info(LogCategory.Player, $"PlayerSpawner: 콘텐츠 씬 {sceneName} 에 {SpawnPointName} 가 없다 — 플레이어를 스폰하지 않는다");
            return false;
        }
    }
}
