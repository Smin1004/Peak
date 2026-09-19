namespace Peak.Stamina
{
    /// <summary>
    /// 상태이상 종류 (Docs/201_common.md 5장 공유 계약, Docs/100_game_design.md 4.2).
    /// M1-3 은 허기·부상만 정의 에셋을 둔다. T2 종류(포자·졸음·가시·저주·석화)는 101 — 끝에 추가한다.
    /// </summary>
    public enum StatusKind
    {
        Hunger,
        Injury,
        Weight,
        Cold,
        Poison,
        Heat,
    }
}
