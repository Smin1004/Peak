using Peak.Core;
using UnityEditor;

namespace Peak.Editor
{
    /// <summary>
    /// Peak > Log > (카테고리) 토글 메뉴. 상태는 EditorPrefs 에 저장되고 도메인 리로드(플레이 진입 포함) 때마다 <see cref="Log"/> 에 다시 적용된다.
    /// Docs/201_common.md 6장 9번 "카테고리별 on/off".
    /// </summary>
    [InitializeOnLoad]
    public static class LogCategoryMenu
    {
        private const string MenuRoot = "Peak/Log/";
        private const string PrefPrefix = "Peak.Log.";

        static LogCategoryMenu()
        {
            foreach (LogCategory category in System.Enum.GetValues(typeof(LogCategory)))
            {
                Log.SetEnabled(category, EditorPrefs.GetBool(PrefPrefix + category, true));
            }
        }

        private static void Toggle(LogCategory category)
        {
            bool next = !Log.IsEnabled(category);
            Log.SetEnabled(category, next);
            EditorPrefs.SetBool(PrefPrefix + category, next);
        }

        private static bool Validate(LogCategory category)
        {
            Menu.SetChecked(MenuRoot + category, Log.IsEnabled(category));
            return true;
        }

        [MenuItem(MenuRoot + "Net")] private static void ToggleNet() => Toggle(LogCategory.Net);
        [MenuItem(MenuRoot + "Net", true)] private static bool ValidateNet() => Validate(LogCategory.Net);

        [MenuItem(MenuRoot + "Gen")] private static void ToggleGen() => Toggle(LogCategory.Gen);
        [MenuItem(MenuRoot + "Gen", true)] private static bool ValidateGen() => Validate(LogCategory.Gen);

        [MenuItem(MenuRoot + "Player")] private static void TogglePlayer() => Toggle(LogCategory.Player);
        [MenuItem(MenuRoot + "Player", true)] private static bool ValidatePlayer() => Validate(LogCategory.Player);

        [MenuItem(MenuRoot + "Items")] private static void ToggleItems() => Toggle(LogCategory.Items);
        [MenuItem(MenuRoot + "Items", true)] private static bool ValidateItems() => Validate(LogCategory.Items);

        [MenuItem(MenuRoot + "UI")] private static void ToggleUI() => Toggle(LogCategory.UI);
        [MenuItem(MenuRoot + "UI", true)] private static bool ValidateUI() => Validate(LogCategory.UI);

        [MenuItem(MenuRoot + "Core")] private static void ToggleCore() => Toggle(LogCategory.Core);
        [MenuItem(MenuRoot + "Core", true)] private static bool ValidateCore() => Validate(LogCategory.Core);
    }
}
