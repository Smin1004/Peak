using Peak.Core;
using Peak.Input;
using Peak.Player.States;
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
    [RequireComponent(typeof(PlayerLocalHud))]
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
        private int _terrainMask;
        private bool _isLocalActive;

        private Vector2 _moveInput;
        private bool _sprintHeld;
        private bool _jumpRequested;

        public PlayerMoveState State { get; private set; }

        /// <summary>마지막 FixedUpdate 의 지면 판정 (오너만 갱신).</summary>
        public GroundInfo Ground { get; private set; } = GroundInfo.None;

        /// <summary>오버레이·테스트용: 지면 판정이 걷기 가능 면인지.</summary>
        public bool IsGrounded => Ground.IsWalkable;

        public PlayerCameraRig CameraRig => _cameraRig;

        internal Rigidbody Body => _body;
        internal GameTuning Tuning => GameConfig.Tuning;
        internal bool SprintHeld => _sprintHeld;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _cameraRig = GetComponent<PlayerCameraRig>();
            _localHud = GetComponent<PlayerLocalHud>();
            _states = new PlayerState[]
            {
                new GroundedState(this),
                new AirborneState(this),
                new ClimbingState(this),
                new MantlingState(this),
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

            _terrainMask = LayerMask.GetMask(Layers.Terrain);
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
            var player = _actions.Player;
            _moveInput = Vector2.ClampMagnitude(player.Move.ReadValue<Vector2>(), 1f);
            _sprintHeld = player.Sprint.IsPressed();
            if (player.Jump.WasPressedThisFrame())
            {
                _jumpRequested = true;
            }
            CurrentState.Tick(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!_isLocalActive)
            {
                return;
            }
            Ground = ProbeGround();
            UpdateTransitions();
            CurrentState.FixedTick(Time.fixedDeltaTime);

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

            switch (State)
            {
                case PlayerMoveState.Grounded:
                    if (jump)
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
                        ChangeState(PlayerMoveState.Grounded);
                    }
                    break;
            }
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

        /// <summary>Grounded 에서만: 수직 속도 = jumpSpeed. 스태미나 소모는 M1 (202 9장 jumpCost).</summary>
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

        /// <summary>카메라 yaw 기준 입력 방향 (월드 수평, 크기 0..1).</summary>
        internal Vector3 GetWishDirection()
        {
            var local = new Vector3(_moveInput.x, 0f, _moveInput.y);
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
            return $"Player {State} | {horizontalSpeed:0.0} m/s | grounded {IsGrounded} | slope {slope} | look {_cameraRig.Yaw:0} / {_cameraRig.Pitch:0}";
        }
    }
}
