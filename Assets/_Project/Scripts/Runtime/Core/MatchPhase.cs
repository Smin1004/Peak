namespace Peak.Core
{
    /// <summary>
    /// 매치 흐름 상태 (Docs/201_common.md 5장, Docs/205_network.md 2장).
    /// 전환은 호스트만. <see cref="GameManager"/> 가 NetworkVariable 로 보관한다.
    /// </summary>
    public enum MatchPhase
    {
        Lobby,
        Generating,
        Playing,
        Result,
    }
}
