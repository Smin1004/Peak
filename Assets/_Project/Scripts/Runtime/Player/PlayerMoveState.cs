namespace Peak.Player
{
    /// <summary>
    /// 플레이어 이동 상태 (Docs/202_gameplay.md 3장). 전이는 <see cref="PlayerController"/> 한 곳에서만 한다.
    /// 전이 우선순위: Dead > Unconscious > Mantling > Climbing > Hanging > Carrying > Airborne > Grounded.
    /// M0-2 는 Grounded·Airborne 만 구현. 나머지는 M1(등반) · M3(기절·운반) · 206(로프·피톤).
    /// 값은 205 3장의 <c>NetworkVariable&lt;byte&gt;</c> 복제에 그대로 쓸 수 있게 byte 범위 안에서 순서를 고정한다 — 중간 삽입 금지, 끝에 추가.
    /// </summary>
    public enum PlayerMoveState : byte
    {
        Grounded,
        Airborne,
        Climbing,
        Mantling,
        Hanging,
        Carrying,
        Unconscious,
        Dead,
    }
}
