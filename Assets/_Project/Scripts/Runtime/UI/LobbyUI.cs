using Peak.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Peak.UI
{
    /// <summary>
    /// Lobby 씬 UI (Docs/204_ui.md 1장). M0~M3 은 [싱글 시작] 하나로 방 만들기를 대체한다.
    /// 시드 입력 → <see cref="GameManager.StartSingle"/>. 버튼 연결은 코드에서 (씬에 직렬화된 UnityEvent 없음 → 에디터 스크립트가 멱등).
    /// </summary>
    public sealed class LobbyUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField seedInput;
        [SerializeField] private Button startSingleButton;
        [SerializeField] private Button quitButton;

        private void Awake()
        {
            if (seedInput == null || startSingleButton == null || quitButton == null)
            {
                Log.Error(LogCategory.UI, "LobbyUI: 참조가 비어 있다 (Peak > Setup > Rebuild M0 Scenes)");
                enabled = false;
                return;
            }

            if (string.IsNullOrEmpty(seedInput.text))
            {
                seedInput.text = RunConfig.DefaultSeed.ToString();
            }
            startSingleButton.onClick.AddListener(OnStartSingle);
            quitButton.onClick.AddListener(OnQuit);
        }

        private void OnDestroy()
        {
            if (startSingleButton != null)
            {
                startSingleButton.onClick.RemoveListener(OnStartSingle);
            }
            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuit);
            }
        }

        private void OnStartSingle()
        {
            if (GameManager.Instance == null)
            {
                Log.Error(LogCategory.UI, "LobbyUI: GameManager 가 없다 (Boot 씬 미로드)");
                return;
            }

            int seed = int.TryParse(seedInput.text, out var parsed) ? parsed : RunConfig.DefaultSeed;
            seedInput.text = seed.ToString();
            startSingleButton.interactable = false;
            Log.Info(LogCategory.UI, $"[싱글 시작] seed={seed}");
            GameManager.Instance.StartSingle(seed);
        }

        private static void OnQuit()
        {
            Log.Info(LogCategory.UI, "[종료]");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
