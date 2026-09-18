namespace Peak.Player.States
{
    /// <summary>상태이상 합 ≥ 100, 입력 무시 (202 3장). 자리만 — 구현은 M3.</summary>
    public sealed class UnconsciousState : PlayerState
    {
        public UnconsciousState(PlayerController controller) : base(controller)
        {
        }

        // M1
    }
}
