using System;
using System.Collections;
using Peak.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Peak.Core
{
    /// <summary>
    /// Game·Sandbox 씬에 배치. 플레이 시작 시 <c>NetworkManager</c> 가 없으면 Boot 씬을 애디티브 로드하고 로컬 호스트로 시작한다
    /// (Docs/205_network.md 6장, Docs/301_decisions.md D13). 결과: 어느 씬에서 플레이를 눌러도 1인 호스트로 바로 동작.
    /// Boot → Lobby → Game 정상 경로에서는 NetworkManager 가 이미 있으므로 아무것도 하지 않는다.
    /// Multiplayer Play Mode 가상 플레이어 태그가 <see cref="MppmClientTag"/> 이면 호스트 대신 로컬 클라이언트로 붙는다 (확인은 M0-2).
    /// </summary>
    public sealed class Bootstrapper : MonoBehaviour
    {
        /// <summary>Multiplayer Play Mode 에서 클라이언트 역할을 뜻하는 가상 플레이어 태그.</summary>
        public const string MppmClientTag = "Client";

        /// <summary>Boot 로드 후 GameManager 를 기다리는 최대 시간(초).</summary>
        private const float BootTimeoutSeconds = 5f;

        private void Awake()
        {
            if (NetworkManager.Singleton != null)
            {
                return;
            }
            StartCoroutine(BootRoutine());
        }

        private IEnumerator BootRoutine()
        {
            Log.Info(LogCategory.Core, $"Bootstrapper: NetworkManager 없음 → Boot 씬 로드 ({SceneManager.GetActiveScene().name})");
            yield return SceneManager.LoadSceneAsync(SceneNames.Boot, LoadSceneMode.Additive);

            float elapsed = 0f;
            while (GameManager.Instance == null && elapsed < BootTimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (GameManager.Instance == null)
            {
                Log.Error(LogCategory.Core, "Bootstrapper: Boot 씬에 GameManager 가 없다 (Peak > Setup > Rebuild M0 Scenes)");
                yield break;
            }

            if (IsMppmClient())
            {
                Log.Info(LogCategory.Net, $"Bootstrapper: 가상 플레이어 태그 {MppmClientTag} → 로컬 클라이언트로 접속");
                NetService.StartClientLocal();
                yield break;
            }

            if (NetService.StartHostLocal())
            {
                GameManager.Instance.BeginDirectPlay();
            }
        }

        private static bool IsMppmClient()
        {
#if PEAK_HAS_MPPM
            var tags = Unity.Multiplayer.Playmode.CurrentPlayer.ReadOnlyTags();
            return Array.IndexOf(tags, MppmClientTag) >= 0;
#else
            return false;
#endif
        }
    }
}
