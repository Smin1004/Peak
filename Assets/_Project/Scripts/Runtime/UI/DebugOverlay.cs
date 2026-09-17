using System;
using System.Collections.Generic;
using System.Text;
using Peak.Core;
using Peak.Network;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Peak.UI
{
    /// <summary>
    /// F3 디버그 오버레이 (Docs/204_ui.md 4장). Boot 씬 <c>[Boot]/DebugOverlay</c>, 별도 Canvas(SortOrder 100), 좌상단 TMP 텍스트.
    /// 기본 줄: FPS, 역할(Host/Client/None), LocalClientId, MatchPhase, RunConfig.Seed, 로드된 씬 목록.
    /// 다른 시스템은 <see cref="Register"/> / <see cref="Unregister"/> 로 줄을 추가한다 (M0-2 플레이어, M1 스태미나, M2 생성 해시 …).
    /// 토글 키는 액션 맵을 거치지 않고 Keyboard 디바이스를 직접 읽는다 (디버그 전용, 202 1장 매핑과 무관).
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        private const Key ToggleKey = Key.F3;
        private const float RefreshIntervalSeconds = 0.1f;
        private const float FpsSmoothing = 0.1f;

        private static readonly List<(string key, Func<string> line)> s_Lines = new List<(string, Func<string>)>();

        [SerializeField] private Canvas canvas;
        [SerializeField] private TMP_Text text;
        [Tooltip("플레이 시작 시 오버레이 표시 여부. 개발 중에는 켜 둔다")]
        [SerializeField] private bool startVisible = true;

        private readonly StringBuilder _builder = new StringBuilder();
        private float _fps;
        private float _refreshTimer;

        /// <summary>줄 추가. 같은 key 가 있으면 교체. <paramref name="line"/> 은 매 갱신마다 호출된다.</summary>
        public static void Register(string key, Func<string> line)
        {
            if (string.IsNullOrEmpty(key) || line == null)
            {
                return;
            }
            Unregister(key);
            s_Lines.Add((key, line));
        }

        public static void Unregister(string key)
        {
            s_Lines.RemoveAll(entry => entry.key == key);
        }

        public bool IsVisible => canvas != null && canvas.enabled;

        private void Awake()
        {
            if (canvas == null || text == null)
            {
                Log.Error(LogCategory.UI, "DebugOverlay: Canvas/Text 참조가 비어 있다 (Peak > Setup > Rebuild M0 Scenes)");
                enabled = false;
                return;
            }
            canvas.enabled = startVisible;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[ToggleKey].wasPressedThisFrame)
            {
                canvas.enabled = !canvas.enabled;
            }

            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                _fps = Mathf.Lerp(_fps, 1f / dt, FpsSmoothing);
            }

            if (!canvas.enabled)
            {
                return;
            }

            _refreshTimer += dt;
            if (_refreshTimer < RefreshIntervalSeconds)
            {
                return;
            }
            _refreshTimer = 0f;
            text.text = BuildText();
        }

        private string BuildText()
        {
            var sb = _builder;
            sb.Clear();

            sb.Append("FPS ").Append(Mathf.RoundToInt(_fps)).AppendLine();
            sb.Append("Role ").Append(NetService.RoleName).AppendLine();
            sb.Append("ClientId ").Append(NetService.IsRunning ? NetService.LocalClientId.ToString() : "-").AppendLine();

            var game = GameManager.Instance;
            sb.Append("Phase ").Append(game != null ? game.CurrentPhase.ToString() : "-").AppendLine();
            sb.Append("Seed ").Append(game != null ? game.CurrentConfig.Seed.ToString() : "-").AppendLine();

            sb.Append("Scenes ");
            var active = SceneManager.GetActiveScene();
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(scene.name);
                if (scene == active)
                {
                    sb.Append("*");
                }
            }
            sb.AppendLine();

            for (int i = 0; i < s_Lines.Count; i++)
            {
                var (key, line) = s_Lines[i];
                try
                {
                    sb.AppendLine(line());
                }
                catch (Exception e)
                {
                    sb.Append(key).Append(" <").Append(e.GetType().Name).Append(">").AppendLine();
                }
            }

            return sb.ToString();
        }
    }
}
