using Peak.Visual;

namespace Peak.Core
{
    /// <summary>
    /// 튜닝·테마 에셋 접근점. Resources 를 쓰지 않고 Boot 씬의 <see cref="GameManager"/> 가 인스펙터 참조로 들고 있다가 바인딩한다.
    /// Boot 가 로드되기 전(에디터 EditMode 등)에는 null — 게임플레이 코드는 Boot 이후에만 돈다 (Docs/201_common.md 2장).
    /// </summary>
    public static class GameConfig
    {
        public static GameTuning Tuning { get; private set; }
        public static VisualTheme Theme { get; private set; }

        public static bool IsBound => Tuning != null && Theme != null;

        internal static void Bind(GameTuning tuning, VisualTheme theme)
        {
            Tuning = tuning;
            Theme = theme;
            if (!IsBound)
            {
                Log.Error(LogCategory.Core, "GameConfig: GameTuning / VisualTheme 참조가 비어 있다. Boot 씬의 GameManager 인스펙터를 확인 (Peak > Setup > Rebuild M0 Scenes)");
            }
        }

        internal static void Unbind()
        {
            Tuning = null;
            Theme = null;
        }
    }
}
