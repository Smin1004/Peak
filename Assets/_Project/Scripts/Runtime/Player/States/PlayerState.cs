namespace Peak.Player.States
{
    /// <summary>
    /// 이동 상태 기반 클래스 (Docs/202_gameplay.md 3장). 상태는 자기 동작만 하고 **전이를 결정하지 않는다** — 전이는 <see cref="PlayerController"/> 한 곳.
    /// 호출은 오너 인스턴스에서만: <see cref="Tick"/> = Update, <see cref="FixedTick"/> = FixedUpdate (물리 제어는 여기).
    /// </summary>
    public abstract class PlayerState
    {
        protected readonly PlayerController Controller;

        protected PlayerState(PlayerController controller)
        {
            Controller = controller;
        }

        public virtual void Enter()
        {
        }

        public virtual void Tick(float deltaTime)
        {
        }

        public virtual void FixedTick(float deltaTime)
        {
        }

        public virtual void Exit()
        {
        }
    }
}
