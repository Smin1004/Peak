using UnityEngine;

namespace Peak.Player.States
{
    /// <summary>
    /// 모서리 올라서기 (Docs/202_gameplay.md 3장, 4장 보강 규칙, 12.5). 진입: Climbing 중 위 입력 + <see cref="ClimbSensor.TryFindLedge"/> 성공.
    /// mantleDuration 동안 발을 경로 위로 보간한다: 위로(발이 윗면 + mantleClearance 에 닿을 때까지) → 앞으로(올라설 자리 위) → 윗면에 내려놓기.
    /// 경로 전체 길이에 smoothstep 을 걸어 속도가 연속이다 — 순간이동 없음. kinematic MovePosition 이라 Rigidbody 보간이 카메라를 부드럽게 끈다.
    /// 입력 무시, 몸 방향은 진입 때(벽을 향함) 그대로. 경로가 막히지 않는지는 ClimbSensor 가 진입 전에 확인했다.
    /// </summary>
    public sealed class MantlingState : PlayerState
    {
        private const int PathPointCount = 4;

        private readonly Vector3[] _path = new Vector3[PathPointCount];
        private readonly float[] _segmentLengths = new float[PathPointCount - 1];
        private float _totalLength;
        private float _elapsed;

        public MantlingState(PlayerController controller) : base(controller)
        {
        }

        public override bool BodyFollowsCameraYaw => false;

        /// <summary>경로 끝에 닿았다 → 컨트롤러가 Grounded 로 보낸다.</summary>
        public bool IsComplete { get; private set; }

        public override void Enter()
        {
            var body = Controller.Body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;

            Vector3 start = body.position;
            Vector3 stand = Controller.MantleTarget;
            float lift = Mathf.Max(stand.y + Controller.Tuning.mantleClearance, start.y);
            _path[0] = start;
            _path[1] = new Vector3(start.x, lift, start.z);
            _path[2] = new Vector3(stand.x, lift, stand.z);
            _path[3] = stand;

            _totalLength = 0f;
            for (int i = 0; i < _segmentLengths.Length; i++)
            {
                _segmentLengths[i] = Vector3.Distance(_path[i], _path[i + 1]);
                _totalLength += _segmentLengths[i];
            }
            _elapsed = 0f;
            IsComplete = false;
        }

        public override void FixedTick(float deltaTime)
        {
            if (IsComplete)
            {
                return;
            }
            float duration = Controller.Tuning.mantleDuration;
            _elapsed += deltaTime;
            float progress = duration > 0f ? Mathf.Clamp01(_elapsed / duration) : 1f;
            Controller.Body.MovePosition(Evaluate(Mathf.SmoothStep(0f, 1f, progress) * _totalLength));
            if (progress >= 1f)
            {
                IsComplete = true;
            }
        }

        public override void Exit()
        {
            var body = Controller.Body;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
        }

        /// <summary>경로 시작에서 호 길이 <paramref name="distance"/> 인 점.</summary>
        private Vector3 Evaluate(float distance)
        {
            for (int i = 0; i < _segmentLengths.Length; i++)
            {
                float length = _segmentLengths[i];
                if (distance <= length)
                {
                    return length > 0f ? Vector3.Lerp(_path[i], _path[i + 1], distance / length) : _path[i + 1];
                }
                distance -= length;
            }
            return _path[PathPointCount - 1];
        }
    }
}
