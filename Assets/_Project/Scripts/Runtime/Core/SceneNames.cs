namespace Peak.Core
{
    /// <summary>
    /// 씬 이름 상수 (Docs/201_common.md 2장).
    /// 빌드 목록 순서: Boot(0) · Lobby(1) · Game(2). Sandbox 두 개는 빌드 제외 (에디터 전용).
    /// </summary>
    public static class SceneNames
    {
        public const string Boot = "Boot";
        public const string Lobby = "Lobby";
        public const string Game = "Game";
        public const string SandboxClimb = "Sandbox_Climb";
        public const string SandboxProcGen = "Sandbox_ProcGen";
    }

    /// <summary>
    /// 레이어 이름 상수. M0-1 에서 예약, 실제 사용은 Terrain·Player 부터 (Docs/Prompts/M0-1_skeleton.md 4장).
    /// </summary>
    public static class Layers
    {
        public const string Terrain = "Terrain";
        public const string Player = "Player";
        public const string Rope = "Rope";
        public const string Item = "Item";
        public const string Interactable = "Interactable";
    }
}
