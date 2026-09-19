namespace Peak.Stamina
{
    /// <summary>
    /// 생체 단계 (Docs/201_common.md 5장). Alive ↔ Unconscious 판정은 <see cref="PlayerVitals"/> (202 5장).
    /// 기절·사망 상태 기계 전이와 Dead 는 M3.
    /// </summary>
    public enum PlayerPhase
    {
        Alive,
        Unconscious,
        Dead,
    }
}
