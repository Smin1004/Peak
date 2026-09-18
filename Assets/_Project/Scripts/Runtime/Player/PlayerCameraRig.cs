using Peak.Core;
using Peak.Input;
using Unity.Cinemachine;
using UnityEngine;

namespace Peak.Player
{
    /// <summary>
    /// 1인칭 카메라 (Docs/202_gameplay.md 12.1, Docs/301_decisions.md D16). **오너 전용** — <see cref="PlayerController"/> 가 오너 스폰 때
    /// <see cref="Activate"/>, 디스폰 때 <see cref="Deactivate"/> 를 부른다. 비오너 인스턴스에서는 아무것도 하지 않는다.
    /// CameraRig 프리팹(Camera + CinemachineBrain + CinemachineCamera[Hard Lock To Target, Rotate With Follow Target])을 인스턴스화하고
    /// Follow = CameraTarget(눈높이), LookAt 없음 — 카메라 위치·회전이 CameraTarget 과 같다. FOV 는 GameTuning.fieldOfView 를 런타임에 넣는다.
    /// Look 입력은 여기서만 읽어 CameraTarget 의 yaw(무제한)·pitch(제한)를 돌린다 — CinemachineInputAxisController 를 쓰지 않는다 (입력 이중 처리 방지).
    /// 몸 회전은 <see cref="PlayerController"/> 가 FixedUpdate 에서 <see cref="YawRotation"/> 을 따라간다.
    /// 커서: 플레이 중 잠금·숨김. Pause → 해제(해제 중 Look 무시), UI/Click → 재잠금.
    /// </summary>
    [DefaultExecutionOrder(PlayerCameraRig.ExecutionOrder)]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        /// <summary>CinemachineBrain(기본 순서 0)의 LateUpdate 보다 먼저 CameraTarget 회전을 확정한다.</summary>
        internal const int ExecutionOrder = -100;

        /// <summary>yaw 정규화 범위 (도).</summary>
        private const float FullTurnDegrees = 360f;

        [Tooltip("Prefabs/CameraRig.prefab (Peak > Setup > Rebuild Player Prefab 이 연결)")]
        [SerializeField] private GameObject rigPrefab;

        [Tooltip("플레이어 자식 CameraTarget (눈높이). 1인칭 카메라의 위치·회전")]
        [SerializeField] private Transform cameraTarget;

        private GameObject _rig;
        private PeakActions _actions;
        private float _yaw;
        private float _pitch;
        private bool _cursorLocked;

        /// <summary>카메라 수평 방향 (도). 이동 입력의 기준 — M1 ClimbSensor 의 "카메라 전방(수평 성분)" 도 여기서.</summary>
        public float Yaw => _yaw;

        public float Pitch => _pitch;

        public Quaternion YawRotation => Quaternion.Euler(0f, _yaw, 0f);

        public Transform CameraTarget => cameraTarget;

        public bool IsActive => _rig != null;

        public bool IsCursorLocked => _cursorLocked;

        public void Activate(PeakActions actions)
        {
            if (rigPrefab == null || cameraTarget == null)
            {
                Log.Error(LogCategory.Player, "PlayerCameraRig: rigPrefab / cameraTarget 참조가 비어 있다 (Peak > Setup > Rebuild Player Prefab)");
                return;
            }
            if (_rig != null)
            {
                return;
            }

            _actions = actions;
            _yaw = transform.eulerAngles.y;
            _pitch = 0f;
            ApplyTargetRotation();

            _rig = Instantiate(rigPrefab);
            _rig.name = rigPrefab.name;
            var virtualCamera = _rig.GetComponentInChildren<CinemachineCamera>();
            if (virtualCamera == null)
            {
                Log.Error(LogCategory.Player, "PlayerCameraRig: CameraRig 프리팹에 CinemachineCamera 가 없다");
            }
            else
            {
                // Rotate With Follow Target 이 Follow 의 회전을 쓰므로 LookAt 은 비운다
                virtualCamera.Follow = cameraTarget;
                virtualCamera.LookAt = null;
                var tuning = GameConfig.Tuning;
                if (tuning != null)
                {
                    var lens = virtualCamera.Lens;
                    lens.FieldOfView = tuning.fieldOfView;
                    virtualCamera.Lens = lens;
                }
            }
            SetCursorLocked(true);
        }

        /// <summary>
        /// 시선을 즉시 지정한다 — 스폰 방향 지정·디버그 순간이동용 (Docs/202_gameplay.md 12.1 API).
        /// yaw 는 0..360 으로 정규화, pitch 는 GameTuning.pitchMin..pitchMax 로 제한. 몸은 다음 FixedUpdate 에 yaw 를 따라간다.
        /// </summary>
        public void SetLook(float yaw, float pitch)
        {
            var tuning = GameConfig.Tuning;
            if (tuning == null)
            {
                Log.Error(LogCategory.Player, "PlayerCameraRig.SetLook: GameTuning 이 바인딩되지 않았다 (Boot 미로드)");
                return;
            }
            _yaw = Mathf.Repeat(yaw, FullTurnDegrees);
            _pitch = Mathf.Clamp(pitch, tuning.pitchMin, tuning.pitchMax);
            if (_rig != null)
            {
                ApplyTargetRotation();
            }
        }

        public void Deactivate()
        {
            if (_rig != null)
            {
                Destroy(_rig);
                _rig = null;
                SetCursorLocked(false);
            }
            _actions = null;
        }

        private void OnDestroy()
        {
            Deactivate();
        }

        private void Update()
        {
            if (_rig == null || _actions == null)
            {
                return;
            }

            if (!_cursorLocked)
            {
                if (_actions.UI.Click.WasPressedThisFrame())
                {
                    SetCursorLocked(true);
                }
                return;
            }

            if (_actions.Player.Pause.WasPressedThisFrame())
            {
                SetCursorLocked(false);
                return;
            }

            var tuning = GameConfig.Tuning;
            if (tuning == null)
            {
                return;
            }
            // 마우스 델타는 이미 프레임당 픽셀이므로 deltaTime 을 곱하지 않는다
            Vector2 look = _actions.Player.Look.ReadValue<Vector2>() * tuning.lookSensitivity;
            _yaw = Mathf.Repeat(_yaw + look.x, FullTurnDegrees);
            _pitch = Mathf.Clamp(_pitch - look.y, tuning.pitchMin, tuning.pitchMax);
        }

        private void LateUpdate()
        {
            if (_rig != null)
            {
                ApplyTargetRotation();
            }
        }

        /// <summary>플레이어 본체 회전과 무관하게 CameraTarget 의 월드 회전을 yaw·pitch 로 고정한다.</summary>
        private void ApplyTargetRotation()
        {
            cameraTarget.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void SetCursorLocked(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
