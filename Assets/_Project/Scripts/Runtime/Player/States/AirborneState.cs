using UnityEngine;

namespace Peak.Player.States
{
    /// <summary>
    /// 공중 (Docs/202_gameplay.md 3장). Rigidbody 중력 + 약한 공중 제어 (지상 가속도 × airControl).
    /// 입력이 없으면 수평 관성을 그대로 둔다. 경사 ≥ walkSlope 인 면 위도 이 상태 → 마찰 0 이므로 미끄러져 내려간다.
    /// 등반면 위에서는 공중 제어의 "면 안쪽" 수평 성분을 버린다 — 남겨 두면 마찰 0 면에 밀어붙이는 힘이 위쪽으로 꺾여 급경사를 걸어 올라간다 (등반은 M1).
    /// </summary>
    public sealed class AirborneState : PlayerState
    {
        public AirborneState(PlayerController controller) : base(controller)
        {
        }

        public override void Enter()
        {
            Controller.Body.useGravity = true;
        }

        public override void FixedTick(float deltaTime)
        {
            Vector3 wish = Controller.GetWishDirection();
            if (wish.sqrMagnitude <= PlayerController.MinDirectionSqrMagnitude)
            {
                return;
            }

            var tuning = Controller.Tuning;
            var body = Controller.Body;
            // 공중에서는 소모 없음 — 달리기 판정(스태미나 있음)만 목표 속도에 쓴다 (202 5장)
            float speed = Controller.IsSprinting ? tuning.sprintSpeed : tuning.walkSpeed;

            Vector3 velocity = body.linearVelocity;
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 next = PlayerController.Accelerate(horizontal, wish * speed, tuning.groundAccelTime, tuning.airControl, deltaTime);
            Vector3 delta = next - horizontal;

            var ground = Controller.Ground;
            if (ground.HasHit && !ground.IsWalkable)
            {
                var intoSurface = new Vector3(-ground.Normal.x, 0f, -ground.Normal.z);
                if (intoSurface.sqrMagnitude > PlayerController.MinDirectionSqrMagnitude)
                {
                    intoSurface.Normalize();
                    float push = Vector3.Dot(delta, intoSurface);
                    if (push > 0f)
                    {
                        delta -= intoSurface * push;
                    }
                }
            }

            body.linearVelocity = velocity + delta;
        }
    }
}
