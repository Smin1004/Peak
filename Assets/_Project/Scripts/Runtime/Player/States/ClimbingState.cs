using UnityEngine;

namespace Peak.Player.States
{
    /// <summary>
    /// 벽에 붙어 오르내림 (Docs/202_gameplay.md 3·4장). 진입: Climb 홀드 + <see cref="ClimbSensor.TryAttach"/> 성공 (전이는 컨트롤러).
    /// Rigidbody 는 kinematic — 이동은 MovePosition/MoveRotation 이라 Interpolate 가 카메라를 부드럽게 끈다. 중력은 다음 상태 Enter 가 정한다.
    /// 이동: 입력 (x, y) → 표면 접선 기저 (D = 화면 오른쪽) × climbSpeed. 매 FixedTick 후 <see cref="ClimbSensor.TryFollow"/> 로 법선·거리 재정렬.
    /// 벽과의 거리 차이(부착 순간, 코너)는 climbSnapSpeed 로 메워 한 프레임에 튀지 않는다. 몸 yaw = 벽 법선 수평 성분의 반대 (카메라는 자유).
    /// Jump 는 무시한다 (301 Q15). 스태미나는 M1-3.
    /// </summary>
    public sealed class ClimbingState : PlayerState
    {
        public ClimbingState(PlayerController controller) : base(controller)
        {
        }

        public override bool BodyFollowsCameraYaw => false;

        /// <summary>마지막 FixedTick 에서 벽 재탐지가 실패했다 → 컨트롤러가 다음 전이에서 LostSurface 로 떨어뜨린다.</summary>
        public bool SurfaceLost { get; private set; }

        public override void Enter()
        {
            var body = Controller.Body;
            // 속도는 kinematic 으로 바꾸기 전에 (kinematic 에는 속도를 쓸 수 없다)
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
            SurfaceLost = false;
        }

        public override void FixedTick(float deltaTime)
        {
            var tuning = Controller.Tuning;
            var body = Controller.Body;
            ClimbSurface surface = Controller.ClimbSurfaceInternal;

            Vector2 input = Controller.MoveInput;
            Vector3 right = ClimbSensor.RightTangent(surface.Normal);
            Vector3 up = ClimbSensor.UpTangent(surface.Normal, right);
            Vector3 delta = (right * input.x + up * input.y) * (tuning.climbSpeed * deltaTime);

            Vector3 feet = body.position;
            if (!Controller.Sensor.TryFollow(feet, delta, tuning, ref surface, out Vector3 idealFeet))
            {
                SurfaceLost = true;
                return;
            }

            Vector3 next = feet + delta;
            next += Vector3.ClampMagnitude(idealFeet - next, tuning.climbSnapSpeed * deltaTime);
            body.MovePosition(next);
            body.MoveRotation(ClimbSensor.FacingRotation(surface.Normal));
            Controller.ClimbSurfaceInternal = surface;

            // M1-3: 소모, 0 → Exhausted 이탈
        }

        public override void Exit()
        {
            var body = Controller.Body;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
        }
    }
}
