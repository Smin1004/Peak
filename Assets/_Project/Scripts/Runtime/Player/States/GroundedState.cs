using UnityEngine;

namespace Peak.Player.States
{
    /// <summary>
    /// 걷기·달리기 (Docs/202_gameplay.md 3장). 진입 조건: 지면 경사 &lt; walkSlope.
    /// 카메라 yaw 기준 입력 → 지면 법선에 투영 → walkSpeed / sprintSpeed. velocity 를 직접 제어한다.
    /// 경사 정지: Rigidbody 중력을 끄고 **중력의 법선 성분만** 직접 더한다. 접선 성분이 없으므로 마찰 0 캡슐이 30° 경사에서 정지해도 미끄러지지 않고,
    /// 법선 성분은 지면에 눌러 붙게 한다 (작은 턱·내리막에서 뜨지 않음). 법선 방향으로 멀어지는 속도는 버린다.
    /// </summary>
    public sealed class GroundedState : PlayerState
    {
        public GroundedState(PlayerController controller) : base(controller)
        {
        }

        public override void Enter()
        {
            Controller.Body.useGravity = false;
        }

        public override void FixedTick(float deltaTime)
        {
            var tuning = Controller.Tuning;
            var body = Controller.Body;
            Vector3 normal = Controller.Ground.Normal;
            Vector3 wish = Controller.GetWishDirection();

            Vector3 target = Vector3.zero;
            if (wish.sqrMagnitude > PlayerController.MinDirectionSqrMagnitude)
            {
                float speed = Controller.SprintHeld ? tuning.sprintSpeed : tuning.walkSpeed;
                target = Vector3.ProjectOnPlane(wish, normal).normalized * (wish.magnitude * speed);
            }

            Vector3 velocity = body.linearVelocity;
            float normalSpeed = Vector3.Dot(velocity, normal);
            Vector3 tangent = velocity - normal * normalSpeed;
            tangent = PlayerController.Accelerate(tangent, target, tuning.groundAccelTime, 1f, deltaTime);

            normalSpeed = Mathf.Min(normalSpeed, 0f) + Vector3.Dot(Physics.gravity, normal) * deltaTime;
            body.linearVelocity = tangent + normal * normalSpeed;

            Controller.FaceDirection(wish, deltaTime);
        }
    }
}
