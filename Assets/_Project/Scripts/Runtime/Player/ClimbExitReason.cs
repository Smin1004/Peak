namespace Peak.Player
{
    /// <summary>
    /// Climbing 을 떠난 사유 (Docs/202_gameplay.md 4장 보강 규칙). <see cref="PlayerController.LastClimbExitReason"/> 로 노출.
    /// 7장 탈진 낙하 배율이 <see cref="Exhausted"/> 를 본다 (M1-3).
    /// </summary>
    public enum ClimbExitReason : byte
    {
        /// <summary>아직 이탈한 적 없음, 또는 아래로 내려와 걷는 면에 선 경우 (낙하 아님).</summary>
        None,

        /// <summary>Climb 을 뗌 (커서 해제 포함) → Airborne.</summary>
        Released,

        /// <summary>표면 재탐지 실패 → Airborne.</summary>
        LostSurface,

        /// <summary>모서리에 올라섬 → Mantling.</summary>
        Mantled,

        /// <summary>스태미나 0 → Airborne (M1-3).</summary>
        Exhausted,
    }
}
