using System.Collections;
using Peak.Network;
using Peak.Visual;
using Unity.Netcode;
using UnityEngine;

namespace Peak.Core
{
    /// <summary>
    /// 매치 FSM (Docs/205_network.md 2장). Boot 씬 <c>[Boot]</c> 에 있는 in-scene NetworkObject.
    /// <see cref="Phase"/>·<see cref="Config"/> 는 NetworkVariable — 쓰기는 호스트(서버)만.
    /// Lobby ──StartSingle──→ Generating ──(Game 씬 로드)──→ Playing. M0 에서는 생성이 없으므로 Generating 이 즉시 Playing 으로 넘어간다 (생성은 M2).
    /// 튜닝·테마 에셋을 인스펙터 참조로 들고 <see cref="GameConfig"/> 에 바인딩한다 (Resources 미사용).
    /// </summary>
    public sealed class GameManager : NetworkBehaviour
    {
        /// <summary>호스트 시작 후 in-scene 스폰을 기다리는 최대 시간(초). 초과하면 Boot 씬 구성 오류로 본다.</summary>
        private const float SpawnTimeoutSeconds = 5f;

        public static GameManager Instance { get; private set; }

        [SerializeField] private GameTuning tuning;
        [SerializeField] private VisualTheme theme;

        public NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        public NetworkVariable<RunConfig> Config = new NetworkVariable<RunConfig>(RunConfig.CreateDefault(RunConfig.DefaultSeed));

        public MatchPhase CurrentPhase => Phase.Value;
        public RunConfig CurrentConfig => Config.Value;

        /// <summary>전이 코루틴이 도는 동안 true. 버튼 연타 방지.</summary>
        public bool IsTransitioning { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Log.Warn(LogCategory.Core, "GameManager 중복 — 나중 것을 제거");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            GameConfig.Bind(tuning, theme);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Phase.OnValueChanged += OnPhaseChanged;
            Log.Info(LogCategory.Net, $"GameManager 스폰 (IsServer={IsServer}, IsHost={IsHost}, Phase={Phase.Value})");
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= OnPhaseChanged;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                GameConfig.Unbind();
            }
            base.OnDestroy();
        }

        private static void OnPhaseChanged(MatchPhase previous, MatchPhase current)
        {
            Log.Info(LogCategory.Core, $"MatchPhase {previous} → {current}");
        }

        // ── 전이 (호스트 전용) ─────────────────────────────────────────────

        /// <summary>Boot 단독 실행 시 호출. 호스트 시작 전이므로 일반 SceneManager 로 Lobby 를 얹는다.</summary>
        public void EnterLobby()
        {
            SceneFlow.Instance.Transition(SceneNames.Lobby, null);
        }

        /// <summary>
        /// Lobby [싱글 시작]. 호스트가 아직 없으면 로컬 호스트를 띄우고 Lobby → Generating → Game 씬 → Playing.
        /// </summary>
        public void StartSingle(int seed)
        {
            if (IsTransitioning)
            {
                Log.Warn(LogCategory.Core, "StartSingle: 이미 전이 중");
                return;
            }
            StartCoroutine(StartSingleRoutine(seed));
        }

        /// <summary>
        /// Bootstrapper 경로 (Docs/205_network.md 6장): 이미 열려 있는 Game/Sandbox 씬을 그대로 플레이한다.
        /// 호스트가 시작된 뒤 호출 — Lobby 를 거치지 않고 Playing 으로.
        /// </summary>
        public void BeginDirectPlay()
        {
            if (IsTransitioning)
            {
                return;
            }
            StartCoroutine(DirectPlayRoutine());
        }

        /// <summary>Game → Lobby 복귀. 호스트는 유지한다 (Result 화면은 M3).</summary>
        public void ReturnToLobby()
        {
            if (IsTransitioning)
            {
                Log.Warn(LogCategory.Core, "ReturnToLobby: 이미 전이 중");
                return;
            }
            if (!NetService.IsHost)
            {
                Log.Warn(LogCategory.Core, "ReturnToLobby: 호스트만 호출할 수 있다");
                return;
            }
            StartCoroutine(ReturnToLobbyRoutine());
        }

        private IEnumerator StartSingleRoutine(int seed)
        {
            IsTransitioning = true;

            if (!NetService.IsHost)
            {
                if (!NetService.StartHostLocal())
                {
                    Log.Error(LogCategory.Net, "StartSingle: 로컬 호스트 시작 실패");
                    IsTransitioning = false;
                    yield break;
                }
            }

            yield return WaitUntilSpawned();
            if (!IsSpawned || !IsServer)
            {
                IsTransitioning = false;
                yield break;
            }

            Config.Value = RunConfig.CreateDefault(seed);
            Phase.Value = MatchPhase.Generating;
            Log.Info(LogCategory.Core, $"StartSingle: {Config.Value}");

            bool done = false;
            SceneFlow.Instance.Transition(SceneNames.Game, () => done = true);
            yield return new WaitUntil(() => done);

            // M0: 산 생성 없음 → 즉시 Playing. M2 부터 MountainGenerator 완료(전원 GenerationDone)를 기다린다 (205 4장)
            Phase.Value = MatchPhase.Playing;
            IsTransitioning = false;
        }

        private IEnumerator DirectPlayRoutine()
        {
            IsTransitioning = true;
            yield return WaitUntilSpawned();
            if (!IsSpawned || !IsServer)
            {
                IsTransitioning = false;
                yield break;
            }

            SceneFlow.Instance.AdoptActiveScene();
            Config.Value = RunConfig.CreateDefault(RunConfig.DefaultSeed);
            Phase.Value = MatchPhase.Playing;
            Log.Info(LogCategory.Core, $"DirectPlay: {SceneFlow.Instance.CurrentSceneName} / {Config.Value}");
            IsTransitioning = false;
        }

        private IEnumerator ReturnToLobbyRoutine()
        {
            IsTransitioning = true;
            Phase.Value = MatchPhase.Lobby;
            bool done = false;
            SceneFlow.Instance.Transition(SceneNames.Lobby, () => done = true);
            yield return new WaitUntil(() => done);
            IsTransitioning = false;
        }

        /// <summary>호스트 시작 직후 in-scene NetworkObject 스폰이 끝날 때까지 대기. 시간 초과면 에러 로그.</summary>
        private IEnumerator WaitUntilSpawned()
        {
            float elapsed = 0f;
            while (!IsSpawned && elapsed < SpawnTimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!IsSpawned)
            {
                Log.Error(LogCategory.Net, "GameManager 가 스폰되지 않았다. Boot 씬의 [Boot] NetworkObject 를 확인 (Peak > Setup > Rebuild M0 Scenes)");
            }
        }
    }
}
