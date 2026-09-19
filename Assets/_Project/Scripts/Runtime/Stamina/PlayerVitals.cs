using System.Text;
using Peak.Core;
using Peak.UI;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace Peak.Stamina
{
    /// <summary>
    /// 플레이어 생체 값 (Docs/201_common.md 5장 <c>PlayerVitals</c>, Docs/202_gameplay.md 5·6·7장). Player 루트.
    /// 스태미나(<see cref="StaminaSystem"/>)와 상태이상(<see cref="StatusEffectStack"/>)을 소유하고 <see cref="Phase"/>(Alive/Unconscious)를 판정한다.
    /// 오너만 동작한다. 스스로 FixedUpdate 하지 않는다 — PlayerController 가 매 물리 스텝 소모(상태 FixedTick) 뒤에 <see cref="FixedTick"/> 를 부른다.
    /// 의존 방향 Player → Stamina: 이 파일은 Peak.Player 를 참조하지 않는다.
    /// 네트워크 복제는 M4 (205 3장): 복제할 값(스태미나·보너스·종류별 양·Phase·기절 시각)이 전부 이 클래스에 모여 있다.
    /// Dead 와 기절 상태 기계 전이는 M3 — 지금은 Phase 값과 로그 한 줄만.
    /// </summary>
    public sealed class PlayerVitals : NetworkBehaviour
    {
        private const string OverlayKey = "vitals";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>F4 = 로컬 플레이어 생체 값 초기화 (204 4장). DebugOverlay 의 F3 처럼 Keyboard 디바이스를 직접 읽는다.</summary>
        private const Key ResetKey = Key.F4;
#endif

        [Tooltip("상태이상 정의 (Data/StatusEffectDef). Peak > Setup > Rebuild Player Prefab 이 연결")]
        [SerializeField] private StatusEffectDef[] effectDefs;

        private StaminaSystem _stamina;
        private StatusEffectStack _effects;
        private GameTuning _tuning;
        private readonly StringBuilder _overlay = new StringBuilder();

        /// <summary>오너에서 스폰되어 값이 준비됐는가.</summary>
        public bool IsReady => _stamina != null;

        public PlayerPhase Phase { get; private set; } = PlayerPhase.Alive;

        /// <summary>마지막으로 Unconscious 가 된 시각 (Time.time). M3 의 기절 → 사망 타이머가 쓴다.</summary>
        public float UnconsciousSince { get; private set; }

        public float Stamina => IsReady ? _stamina.Stamina : 0f;

        public float Bonus => IsReady ? _stamina.Bonus : 0f;

        public float EffectSum => IsReady ? _effects.Sum : 0f;

        /// <summary>실제로 쓸 수 있는 최대치 = max(0, maxStamina − 상태이상 합).</summary>
        public float Usable => IsReady ? _stamina.Usable(_effects.Sum) : 0f;

        /// <summary>스태미나 + 보너스 &gt; 0 — 등반 진입·달리기 조건 (202 5장).</summary>
        public bool HasStamina => IsReady && _stamina.Stamina + _stamina.Bonus > 0f;

        /// <summary>상태이상 스택 (읽기용: 표시 순서별 정의·양). 오너에서만 준비된다.</summary>
        public StatusEffectStack Effects => _effects;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsOwner)
            {
                return;
            }
            _tuning = GameConfig.Tuning;
            if (_tuning == null)
            {
                Log.Error(LogCategory.Player, "PlayerVitals: GameTuning 이 바인딩되지 않았다 (Boot 미로드)");
                return;
            }
            if (effectDefs == null || effectDefs.Length == 0)
            {
                Log.Warn(LogCategory.Player, "PlayerVitals: effectDefs 가 비어 있다 (Peak > Setup > Rebuild Player Prefab)");
            }
            _stamina = new StaminaSystem(_tuning);
            _effects = new StatusEffectStack(effectDefs, _tuning.statusEffectTotalCap);
            Phase = PlayerPhase.Alive;
            DebugOverlay.Register(OverlayKey, BuildOverlayLine);
        }

        public override void OnNetworkDespawn()
        {
            Deactivate();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            Deactivate();
            base.OnDestroy();
        }

        private void Deactivate()
        {
            if (!IsReady)
            {
                return;
            }
            DebugOverlay.Unregister(OverlayKey);
            _stamina = null;
            _effects = null;
        }

        /// <summary>
        /// PlayerController 가 매 물리 스텝, 그 스텝의 소모가 끝난 뒤 부른다 (순서 고정).
        /// 상태이상 자연 회복 → 스태미나 회복·usable 클램프 → 기절/기상 판정.
        /// </summary>
        public void FixedTick(bool canRegen, float deltaTime)
        {
            if (!IsReady)
            {
                return;
            }
            _effects.Tick(deltaTime);
            _stamina.Tick(canRegen, deltaTime, _effects.Sum);
            UpdatePhase();
        }

        /// <summary>이산 소모 (점프). 모자라면 아무것도 빼지 않고 false.</summary>
        public bool TrySpend(float amount) => IsReady && _stamina.TrySpend(amount);

        /// <summary>연속 소모 (등반·달리기). 실제로 뺀 양.</summary>
        public float Drain(float amount) => IsReady ? _stamina.Drain(amount) : 0f;

        /// <summary>
        /// 착지 낙하 피해 (202 7장). 부상량(탈진 배율 적용, 상한 적용 전)을 돌려주고 스택에 부상으로 더한다 — 넘치는 만큼은 스택이 버린다.
        /// </summary>
        public float ApplyFall(float speed, bool exhausted)
        {
            if (!IsReady)
            {
                return 0f;
            }
            float injury = FallDamage.ComputeInjury(speed, exhausted, _tuning);
            if (injury > 0f)
            {
                _effects.Add(StatusKind.Injury, injury);
            }
            return injury;
        }

        /// <summary>기절: usable ≤ 0. 기상: Unconscious 중 usable &gt; 0 → Alive, stamina 0 부터 회복 (202 5장).</summary>
        private void UpdatePhase()
        {
            float usable = _stamina.Usable(_effects.Sum);
            if (Phase == PlayerPhase.Alive && usable <= 0f)
            {
                Phase = PlayerPhase.Unconscious;
                UnconsciousSince = Time.time;
                Log.Info(LogCategory.Player, $"기절: 상태이상 합 {_effects.Sum:0} (usable 0) — 상태 기계 전이는 M3");
            }
            else if (Phase == PlayerPhase.Unconscious && usable > 0f)
            {
                Phase = PlayerPhase.Alive;
                _stamina.Set(0f, _stamina.Bonus);
                Log.Info(LogCategory.Player, $"기상: usable {usable:0}");
            }
        }

        private string BuildOverlayLine()
        {
            if (!IsReady)
            {
                return string.Empty;
            }
            _overlay.Clear();
            _overlay.Append($"stamina {_stamina.Stamina:0.0} +{_stamina.Bonus:0} | usable {Usable:0} |");
            for (int i = 0; i < _effects.Count; i++)
            {
                _overlay.Append($" {_effects.GetDefinition(i).displayName} {_effects.GetAmount(i):0}");
            }
            _overlay.Append($" | {Phase}");
            return _overlay.ToString();
        }

        // ── 개발 전용 (204 4장 F4, 자동 확인) ─────────────────────────────
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (!IsReady)
            {
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[ResetKey].wasPressedThisFrame)
            {
                DebugReset();
                Log.Info(LogCategory.Player, "F4: 생체 값 초기화");
            }
        }

        /// <summary>스태미나·보너스 직접 지정 (다음 FixedTick 에서 usable 로 클램프).</summary>
        public void DebugSet(float stamina, float bonus)
        {
            if (IsReady)
            {
                _stamina.Set(stamina, bonus);
            }
        }

        /// <summary>상태이상 누적 (상한 적용). 실제로 더해진 양.</summary>
        public float DebugAddEffect(StatusKind kind, float amount) => IsReady ? _effects.Add(kind, amount) : 0f;

        /// <summary>스태미나 maxStamina, 보너스 0, 상태이상 0, Alive.</summary>
        public void DebugReset()
        {
            if (!IsReady)
            {
                return;
            }
            _stamina.Reset();
            _effects.Clear();
            Phase = PlayerPhase.Alive;
        }
#endif
    }
}
