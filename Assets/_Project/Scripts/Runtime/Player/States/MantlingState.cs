namespace Peak.Player.States
{
    /// <summary>Climbing 중 위쪽 모서리 감지 → 0.4초 고정 이동 (202 3·4장). 자리만 — 구현은 M1.</summary>
    public sealed class MantlingState : PlayerState
    {
        public MantlingState(PlayerController controller) : base(controller)
        {
        }

        // M1
    }
}
