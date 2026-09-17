using System;
using UnityEngine;

namespace Peak.Core
{
    /// <summary>로그 카테고리. 네트워크·생성 카테고리는 필수 (Docs/201_common.md 6장 9번).</summary>
    public enum LogCategory
    {
        Net,
        Gen,
        Player,
        Items,
        UI,
        Core,
    }

    /// <summary>
    /// <c>Debug.Log</c> 래퍼. 우리 코드는 <c>Debug.Log</c> 를 직접 쓰지 않는다 (Docs/201_common.md 6장 9번).
    /// 카테고리별 on/off 는 에디터 메뉴 Peak > Log > (카테고리) 로 토글한다 (Peak.Editor.LogCategoryMenu).
    /// Error 는 토글과 무관하게 항상 출력한다 — 꺼진 카테고리 때문에 오류를 놓치지 않기 위해.
    /// </summary>
    public static class Log
    {
        private static readonly bool[] s_Enabled = CreateAllEnabled();

        private static bool[] CreateAllEnabled()
        {
            var arr = new bool[Enum.GetValues(typeof(LogCategory)).Length];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = true;
            }
            return arr;
        }

        public static bool IsEnabled(LogCategory category) => s_Enabled[(int)category];

        public static void SetEnabled(LogCategory category, bool enabled) => s_Enabled[(int)category] = enabled;

        public static void Info(LogCategory category, string message)
        {
            if (!IsEnabled(category))
            {
                return;
            }
            Debug.Log(Format(category, message));
        }

        public static void Warn(LogCategory category, string message)
        {
            if (!IsEnabled(category))
            {
                return;
            }
            Debug.LogWarning(Format(category, message));
        }

        public static void Error(LogCategory category, string message)
        {
            Debug.LogError(Format(category, message));
        }

        private static string Format(LogCategory category, string message) => $"[{category}] {message}";
    }
}
