using System;
using Peak.Core;
using Peak.Input;
using Peak.Player.States;
using Peak.Stamina;
using Peak.UI;
using Peak.Visual;
using Unity.Netcode;
using UnityEngine;

namespace Peak.Player
{
    /// <summary>
    /// 플레이어 루트 로직 (Docs/202_gameplay.md 2·3장). 이동 상태 기계를 들고 **전이를 여기 한 곳에서** 결정한다.
    /// 권한 (Docs/205_network.md 3장): 입력·물리 제어·카메라·HUD·오버레이는 오너 인스턴스만. 비오너는 NetworkTransform(오너 권한)이 움직이고
    /// NetworkRigidbody 가 Rigidbody 를 kinematic 으로 둔다. 색은 모든 인스턴스가 각자 계산한다.
    /// 비주얼은 <see cref="IVisualState"/> 로만 통지한다 (Docs/102_required_assets.md 2장).
    /// 1인칭 (Docs/202_gameplay.md 12장, Docs/301_decisions.md D16): 몸 yaw 는 카메라 yaw 를 따르고, 오너 화면에서 자기 몸은 그림자만 보인다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(PlayerCameraRig))]
    [RequireComponent(typeof(PlayerLocalHud), typeof(PlayerVitals))]
    public sealed class PlayerController : NetworkBehaviour
    {
        /// <summary>방향 벡터를 "입력 없음"으로 보는 제곱 크기 (부동소수점 잡음 컷).</summary>
        internal const float MinDirectionSqrMagnitude = 1e-6f;

        /// <summary>Airborne → Grounded 착지 허용: 지면 법선 방향으로 멀어지는 속도가 이 값(m/s) 이하. 점프 직후 재착지 방지용 잡음 여유.</summary>
        private const float LandingMaxSeparationSpeed = 0.1f;

        private const string OverlayKey = "player";

        private Rigidbody _body;
        private CapsuleCollider _capsule;
        private PlayerCameraRig _cameraRig;
        private PlayerLocalHud _localHud;
        private PeakActions _actions;
        private PlayerState[] _states;
        private ClimbingState _climbing;
        private MantlingState _mantling;
        private ClimbSensor _climbSensor;
        private int _terrainMask;
        private bool _isLocalActive;

        private Vector2 _moveInput;
        private bool _sprintHeld;
        private bool _jumpRequested;

        /// <summary>등반으로 인정하는 Climb 홀드 (커서 잠김 + 잠긴 뒤 새로 누름, 202 4장 보강 규칙).</summary>
        private bool _climbHeld;

        /// <summary>커서가 잠긴 상태에서 Climb 이 한 번 떼어졌는가 — 커서 재잠금 클릭(같은 좌클릭)을 등반 시작으로 치지 않기 위해.</summary>
        private bool _climbArmed;

        private ClimbSurface _climbSurface;
        private Vector3 _mantleTarget;
        private float _lastClimbExitTime = float.NegativeInfinity;

        // ── 생체 값 연결 (M1-3, 202 5·7장) ──
        private PlayerVitals _vitals;

        /// <summary>Airborne 중 직전 물리 스텝에 기록한 낙하 속도 (−velocity.y, 아래가 양수). 착지 스텝에 이미 충돌로 0 이 됐을 때 쓴다 (202 7장 측정 주의).</summary>
        private float _previousFallSpeed;

        /// <summary>Exhausted 로 이탈한 뒤 아직 착지하지 않았다 — 첫 착지 한 번만 탈진 배율.</summary>
        private bool _exhaustedFallPending;

        /// <summary>이 시각(Time.time)까지 이동·점프·등반 입력 무시 (착지 경직).</summary>
        private float _stunUntil = float.NegativeInfinity;

        private bool _hasLanded;
        private float _lastLandingSpeed;
        private float _lastLandingInjury;

        public PlayerMoveState State { get; private set; }

        /// <summary>
        /// Airborne → Grounded 착지마다 한 번 (착지 수직 속도 m/s, 부상량). 부상 0 착지도 발생한다. 오너에서만.
        /// M1-4 가 카메라 흔들림·부상 플래시를 건다. 벽에 다시 붙거나 벽을 타고 내려와 서는 것·맨틀은 착지가 아니다 (202 7장).
        /// </summary>
        public event Action<float, float> Landed;

        /// <summary>착지 경직 중 (이동·점프·등반 입력 무시).</summary>
        public bool IsStunned => Time.time < _stunUntil;

        /// <summary>마지막으로 Climbing 을 떠난 사유 (M1-3 탈진 낙하 배율이 본다).</summary>
        public ClimbExitReason LastClimbExitReason { get; private set; }

        /// <summary>현재 붙어 있는 표면 — Climbing 중에만 의미가 있다 (Mantling 중에는 올라서기 직전 표면).</summary>
        public ClimbSurface CurrentClimbSurface => _climbSurface;

        /// <summary>마지막 FixedUpdate 의 지면 판정 (오너만 갱신).</summary>
        public GroundInfo Ground { get; private set; } = GroundInfo.None;

        /// <summary>오버레이·테스트용: 지면 판정이 걷기 가능 면인지.</summary>
        public bool IsGrounded => Ground.IsWalkable;

        public PlayerCameraRig CameraRig => _cameraRig;

        internal Rigidbody Body => _body;
        internal GameTuning Tuning => GameConfig.Tuning;
        /// <summary>달리기 = Sprint 홀드 + 이동 입력 + 스태미나 있음 (202 5장). 경직 중 아님. Grounded·Airborne 목표 속도와 Grounded 소모가 쓴다.</summary>
        internal bool IsSprinting => _sprintHeld && MoveInput.sqrMagnitude > MinDirectionSqrMagnitude && _vitals.HasStamina;

        /// <summary>이동 입력. 착지 경직 중에는 0 (202 7장).</summary>
        internal Vector2 MoveInput => IsStunned ? Vector2.zero : _moveInput;

        internal ClimbSensor Sensor => _climbSensor;
        internal PlayerVitals Vitals => _vitals;

        /// <summary>Climbing 상태가 읽고 갱신하는 현재 표면.</summary>
        internal ClimbSurface ClimbSurfaceInternal
        {
            get => _climbSurface;
            set => _climbSurface = value;
        }

        /// <summary>Mantling 이 올라설 발 위치 (진입 전에 컨트롤러가 정한다).</summary>
        internal Vector3 MantleTarget => _mantleTarget;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _cameraRig = GetComponent<PlayerCameraRig>();
            _localHud = GetComponent<PlayerLocalHud>();
            _vitals = GetComponent<PlayerVitals>();
            _terrainMask = LayerMask.GetMask(Layers.Terrain);
            _climbSensor = new ClimbSensor(_capsule, _cameraRig, _terrainMask);
            _climbing = new ClimbingState(this);
            _mantling = new MantlingState(this);
            _states = new PlayerState[]
            {
                new GroundedState(this),
                new AirborneState(this),
                _climbing,
                _mantling,
                new HangingState(this),
                new CarryingState(this),
                new UnconsciousState(this),
                new DeadState(this),
            };
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // NGO 는 클라이언트에서 프리팹을 원점에 Instantiate 한 뒤 Transform 만 옮긴다 (NetworkSpawnManager.InstantiateNetworkPrefab).
            // Rigidbody 포즈가 원점에 남으면 첫 물리 스텝까지 원점 캡슐이 되어 다른 플레이어를 밀어낸다 → 스폰 Transform 으로 맞춘다
            _body.position = transform.position;
            _body.rotation = transform.rotation;
            ApplyVisual();

            if (!IsOwner)
            {
                return;
            }
            if (GameConfig.Tuning == null)
            {
                Log.Error(LogCategory.Player, "PlayerController: GameTuning 이 바인딩되지 않았다 (Boot 미로드)");
                return;
            }

            _actions = new PeakActions();
            _actions.Player.Enable();
            _actions.UI.Enable();
            _cameraRig.Activate(_actions);
            _localHud.Activate();

            Ground = ProbeGround();
            State = Ground.IsWalkable ? PlayerMoveState.Grounded : PlayerMoveState.Airborne;
            CurrentState.Enter();

            DebugOverlay.Register(OverlayKey, BuildOverlayLine);
            _isLocalActive = true;
            Log.Info(LogCategory.Player, $"로컬 플레이어 스폰 (clientId={OwnerClientId}, {State})");
        }

        public override void OnNetworkDespawn()
        {
            DeactivateLocal();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            DeactivateLocal();
            base.OnDestroy();
        }

        private void DeactivateLocal()
        {
            if (!_isLocalActive)
            {
                return;
            }
            _isLocalActive = false;
            DebugOverlay.Unregister(OverlayKey);
            CurrentState.Exit();
            _localHud.Deactivate();
            _cameraRig.Deactivate();
            _actions.Dispose();
            _actions = null;
        }

        private PlayerState CurrentState => _states[(int)State];

        // ── 루프 (오너) ─────────────────────────────────────────────────

        private void Update()
        {
            if (!_isLocalActive)
            {
                return;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_debugInputActive)
            {
                _moveInput = _debugMove;
                _sprintHeld = _debugSprintHeld;
                _climbHeld = _debugClimbHeld;
                CurrentState.Tick(Time.deltaTime);
                return;
            }
#endif
            var player = _actions.Player;
            _moveInput = Vector2.ClampMagnitude(player.Move.ReadValue<Vector2>(), 1f);
            _sprintHeld = player.Sprint.IsPressed();
            if (player.Jump.WasPressedThisFrame())
            {
                _jumpRequested = true;
            }
            _climbHeld = ReadClimbHeld(player.Climb.IsPressed());
            CurrentState.Tick(Time.deltaTime);
        }

        /// <summary>
        /// 커서 해제 중 Climb 무시. Climb 과 커서 재잠금(UI.Click)이 같은 좌클릭이고 카메라 리그(실행 순서 −100)가 같은 프레임에 먼저 잠그므로,
        /// 잠긴 상태에서 한 번 뗀 뒤 새로 누른 Climb 만 인정한다 (202 4장 보강 규칙).
        /// </summary>
        private bool ReadClimbHeld(bool pressed)
        {
            if (!_cameraRig.IsCursorLocked)
            {
                _climbArmed = false;
                return false;
            }
            if (!pressed)
            {
                _climbArmed = true;
                return false;
            }
            return _climbArmed;
        }

        private void FixedUpdate()
        {
            if (!_isLocalActive)
            {
                return;
            }
            Ground = ProbeGround();
            float fallSpeed = Mathf.Max(0f, -_body.linearVelocity.y);
            UpdateTransitions();
            // 방금 Airborne 이 된 스텝(벽에서 놓침 등)은 그 전 상태의 속도라 0 에 가깝다 — 이전 낙하의 값이 남지 않게 매 스텝 덮어쓴다
            _previousFallSpeed = State == PlayerMoveState.Airborne ? fallSpeed : 0f;
            CurrentState.FixedTick(Time.fixedDeltaTime);
            // 소모(상태 FixedTick) 뒤에 회복·클램프·기절 판정 — 순서 고정 (202 5장). 회복은 Grounded 만
            _vitals.FixedTick(State == PlayerMoveState.Grounded, Time.fixedDeltaTime);

            // 1인칭: 몸 yaw = 카메라 yaw (202 12.1). 비오너는 NetworkTransform 으로 이 회전을 본다
            if (CurrentState.BodyFollowsCameraYaw)
            {
                _body.MoveRotation(_cameraRig.YawRotation);
            }
        }

        // ── 전이 (202 3장 — 전부 여기서) ─────────────────────────────────

        /// <summary>
        /// 우선순위 Dead > Unconscious > Mantling > Climbing > Hanging > Carrying > Airborne > Grounded.
        /// M1 은 위쪽 상태의 진입 조건을 이 메서드 앞부분(상태별 switch 전)에 추가한다.
        /// </summary>
        private void UpdateTransitions()
        {
            bool jump = _jumpRequested;
            _jumpRequested = false;

            // ── M1-2 등반 (Dead·Unconscious 는 M3) ──
            if (State == PlayerMoveState.Mantling)
            {
                if (_mantling.IsComplete)
                {
                    ChangeState(PlayerMoveState.Grounded);
                }
                return;
            }
            if (State == PlayerMoveState.Climbing)
            {
                // Jump 무시 (301 Q15)
                UpdateClimbingTransitions();
                return;
            }
            if ((State == PlayerMoveState.Grounded || State == PlayerMoveState.Airborne) && CanStartClimb() &&
                _climbSensor.TryAttach(_body.position, _body.rotation, Tuning, out var surface))
            {
                _climbSurface = surface;
                _exhaustedFallPending = false;
                ChangeState(PlayerMoveState.Climbing);
                return;
            }

            switch (State)
            {
                case PlayerMoveState.Grounded:
                    // 점프는 경직 중이 아니고 jumpCost 를 낼 수 있을 때만 (202 5장: 부족하면 점프하지 않는다)
                    if (jump && !IsStunned && _vitals.TrySpend(Tuning.jumpCost))
                    {
                        ChangeState(PlayerMoveState.Airborne);
                        Jump();
                    }
                    else if (!Ground.IsWalkable)
                    {
                        ChangeState(PlayerMoveState.Airborne);
                    }
                    break;

                case PlayerMoveState.Airborne:
                    if (Ground.IsWalkable && Vector3.Dot(_body.linearVelocity, Ground.Normal) <= LandingMaxSeparationSpeed)
                    {
                        float landingSpeed = Mathf.Max(_previousFallSpeed, Mathf.Max(0f, -_body.linearVelocity.y));
                        ChangeState(PlayerMoveState.Grounded);
                        Land(landingSpeed);
                    }
                    break;
            }
        }

        /// <summary>
        /// 착지 (Airborne → Grounded 만, 202 7장). 착지 속도 = 착지 스텝의 수직 속도와 직전 Airborne 스텝에 기록한 값 중 큰 쪽
        /// — 지면 탐지가 먼저 걸리면 이번 스텝 값, 물리 스텝 안에서 이미 부딪혀 0 이 됐으면 직전 값이 착지 직전 속도다.
        /// 부상 &gt; 0 이면 경직. 이벤트는 부상 0 이어도 낸다.
        /// </summary>
        private void Land(float landingSpeed)
        {
            bool exhausted = _exhaustedFallPending;
            _exhaustedFallPending = false;
            _previousFallSpeed = 0f;

            float injury = _vitals.ApplyFall(landingSpeed, exhausted);
            if (injury > 0f)
            {
                // 경직: 입력 무시 + 착지 관성도 끊는다 (지상 감속은 지수 감쇠라 걸어 떨어진 속도가 경직 내내 남는다)
                _stunUntil = Time.time + Tuning.landingStunDuration;
                _body.linearVelocity = new Vector3(0f, _body.linearVelocity.y, 0f);
            }
            _hasLanded = true;
            _lastLandingSpeed = landingSpeed;
            _lastLandingInjury = injury;
            Log.Info(LogCategory.Player, $"착지 {landingSpeed:0.0} m/s → 부상 {injury:0.0}{(exhausted ? " (탈진)" : "")}");
            Landed?.Invoke(landingSpeed, injury);
        }

        /// <summary>우선순위 Mantling > Climbing(유지) > Airborne > Grounded.</summary>
        private void UpdateClimbingTransitions()
        {
            if (MoveInput.y > 0f && _climbSensor.TryFindLedge(_body.position, _climbSurface, Tuning, out var stand))
            {
                _mantleTarget = stand;
                ExitClimbing(ClimbExitReason.Mantled, PlayerMoveState.Mantling);
            }
            else if (!_vitals.HasStamina)
            {
                // 스태미나·보너스 모두 0 → 손을 놓친다 (202 4장 7번). 첫 착지에 탈진 배율
                _exhaustedFallPending = true;
                ExitClimbing(ClimbExitReason.Exhausted, PlayerMoveState.Airborne);
            }
            else if (!_climbHeld)
            {
                ExitClimbing(ClimbExitReason.Released, PlayerMoveState.Airborne);
            }
            else if (_climbing.SurfaceLost)
            {
                ExitClimbing(ClimbExitReason.LostSurface, PlayerMoveState.Airborne);
            }
            else if (MoveInput.y < 0f && Ground.IsWalkable)
            {
                // 아래로 내려와 걷는 면에 섰다 — 낙하가 아니므로 사유 None
                ExitClimbing(ClimbExitReason.None, PlayerMoveState.Grounded);
            }
        }

        private void ExitClimbing(ClimbExitReason reason, PlayerMoveState next)
        {
            LastClimbExitReason = reason;
            _lastClimbExitTime = Time.time;
            ChangeState(next);
        }

        /// <summary>Climb 홀드 + 재부착 지연 끝남 + 스태미나 있음 + 경직 아님. 커서 규칙은 <see cref="ReadClimbHeld"/> 가 이미 반영했다.</summary>
        private bool CanStartClimb()
        {
            return _climbHeld && !IsStunned && _vitals.HasStamina && Time.time - _lastClimbExitTime >= Tuning.climbReattachDelay;
        }

        private void ChangeState(PlayerMoveState next)
        {
            if (next == State)
            {
                return;
            }
            CurrentState.Exit();
            State = next;
            CurrentState.Enter();
        }

        /// <summary>Grounded 에서만: 수직 속도 = jumpSpeed. jumpCost 는 호출 전에 TrySpend 로 냈다 (202 5장).</summary>
        private void Jump()
        {
            Vector3 velocity = _body.linearVelocity;
            velocity.y = Tuning.jumpSpeed;
            _body.linearVelocity = velocity;
        }

        // ── 상태가 쓰는 도구 ─────────────────────────────────────────────

        /// <summary>
        /// 지면 판정: 캡슐 바닥 반구 중심에서 아래로 SphereCast → Terrain 레이어. 경사 &lt; walkSlope 면 걷기 가능.
        /// walkSlope 는 GameTuning 하나를 M1 ClimbSensor·203 생성기와 공유한다 (202 4장).
        /// </summary>
        private GroundInfo ProbeGround()
        {
            var tuning = Tuning;
            float capsuleRadius = _capsule.radius;
            float castRadius = Mathf.Min(tuning.groundCheckRadius, capsuleRadius);
            Vector3 origin = _body.position + Vector3.up * (_capsule.center.y - _capsule.height * 0.5f + capsuleRadius);
            float distance = capsuleRadius - castRadius + tuning.groundCheckDistance;

            if (!Physics.SphereCast(origin, castRadius, Vector3.down, out var hit, distance, _terrainMask, QueryTriggerInteraction.Ignore))
            {
                return GroundInfo.None;
            }
            float slope = Vector3.Angle(hit.normal, Vector3.up);
            return new GroundInfo(true, hit.normal, hit.point, slope, slope < tuning.walkSlope);
        }

        /// <summary>카메라 yaw 기준 입력 방향 (월드 수평, 크기 0..1). 착지 경직 중에는 0.</summary>
        internal Vector3 GetWishDirection()
        {
            Vector2 move = MoveInput;
            var local = new Vector3(move.x, 0f, move.y);
            return _cameraRig.YawRotation * local;
        }

        /// <summary>현재 속도를 목표 속도로 — 둘 중 큰 크기를 accelTime 에 바꾸는 가속도 × scale.</summary>
        internal static Vector3 Accelerate(Vector3 current, Vector3 target, float accelTime, float scale, float deltaTime)
        {
            if (accelTime <= 0f)
            {
                return Vector3.Lerp(current, target, scale);
            }
            float rate = Mathf.Max(current.magnitude, target.magnitude) / accelTime * scale;
            return Vector3.MoveTowards(current, target, rate * deltaTime);
        }

        // ── 비주얼·오버레이 ─────────────────────────────────────────────

        /// <summary>모든 인스턴스: 1인칭 자기 몸 숨김(오너만 그림자 전용, 202 12.2) + 색.</summary>
        private void ApplyVisual()
        {
            var visual = GetComponentInChildren<IVisualState>();
            if (visual == null)
            {
                Log.Warn(LogCategory.Player, "PlayerController: Visual 자식에 IVisualState 가 없다");
                return;
            }
            visual.SetLocalView(IsOwner);
            ApplyTint(visual);
        }

        /// <summary>플레이어 색 = playerColors[OwnerClientId % PlayerCount] (102 3장). 오너·비오너 모두 각자 계산.</summary>
        private void ApplyTint(IVisualState visual)
        {
            var theme = GameConfig.Theme;
            if (theme == null)
            {
                Log.Error(LogCategory.Player, "PlayerController: VisualTheme 이 바인딩되지 않았다 (Boot 미로드)");
                return;
            }
            int index = (int)(OwnerClientId % VisualTheme.PlayerCount);
            if (theme.playerColors == null || index >= theme.playerColors.Length)
            {
                Log.Error(LogCategory.Player, $"VisualTheme.playerColors 길이가 {VisualTheme.PlayerCount} 보다 짧다");
                return;
            }
            visual.SetTint(theme.playerColors[index]);
        }

        private string BuildOverlayLine()
        {
            Vector3 velocity = _body.linearVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            string slope = Ground.HasHit ? Ground.SlopeAngle.ToString("0") : "-";
            string line = $"Player {State} | {horizontalSpeed:0.0} m/s | grounded {IsGrounded} | slope {slope} | look {_cameraRig.Yaw:0} / {_cameraRig.Pitch:0}";
            if (State == PlayerMoveState.Climbing || State == PlayerMoveState.Mantling)
            {
                line += $" | wall {_climbSurface.SlopeAngle:0}° | exit {LastClimbExitReason}";
            }
            if (_hasLanded)
            {
                line += $" | land {_lastLandingSpeed:0.0} m/s → {_lastLandingInjury:0}";
            }
            return line;
        }

        // ── 자동 검증 훅 (M1-2) ─────────────────────────────────────────
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _debugInputActive;
        private Vector2 _debugMove;
        private bool _debugClimbHeld;
        private bool _debugSprintHeld;

        /// <summary>
        /// 장치 입력 대신 이 값을 쓴다 (오너만 의미 있음). unity-mcp 로 플레이 모드를 돌릴 때 게임 뷰에 포커스가 없어 커서가 잠기지 않으므로
        /// <b>커서 잠금 규칙을 건너뛴다</b>. <paramref name="jumpPressed"/> 는 한 번 누름으로 처리한다. <paramref name="sprintHeld"/> 는 M1-3 추가 (기존 호출과 호환).
        /// M1-2 자동 확인용 — M1-3(스태미나·낙하)·M1-4(HUD·피드백) 도 이 훅으로 검증한다. <see cref="ClearDebugInput"/> 으로 장치 입력에 돌려준다.
        /// </summary>
        public void SetDebugInput(Vector2 move, bool climbHeld, bool jumpPressed = false, bool sprintHeld = false)
        {
            _debugInputActive = true;
            _debugMove = Vector2.ClampMagnitude(move, 1f);
            _debugClimbHeld = climbHeld;
            _debugSprintHeld = sprintHeld;
            if (jumpPressed)
            {
                _jumpRequested = true;
            }
        }

        /// <summary>디버그 입력을 끄고 장치 입력으로 돌아간다. Climb 은 잠긴 뒤 다시 눌러야 인정된다.</summary>
        public void ClearDebugInput()
        {
            _debugInputActive = false;
            _debugMove = Vector2.zero;
            _debugClimbHeld = false;
            _debugSprintHeld = false;
            _climbArmed = false;
        }
#endif
    }
}
