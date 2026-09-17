using System;
using System.Collections;
using System.Collections.Generic;
using Peak.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Peak.Core
{
    /// <summary>
    /// Boot 위에 콘텐츠 씬(Lobby/Game/Sandbox)을 애디티브로 얹고 이전 콘텐츠 씬을 내린다 (Docs/201_common.md 2장).
    /// 호스트가 시작된 뒤에는 NGO <c>NetworkSceneManager</c> 를 쓴다 (클라이언트 동기화, Docs/205_network.md 2장).
    /// 호스트 전(Boot 단독 실행 → Lobby)에는 일반 <c>SceneManager</c>.
    /// </summary>
    public sealed class SceneFlow : MonoBehaviour
    {
        public static SceneFlow Instance { get; private set; }

        /// <summary>현재 얹혀 있는 콘텐츠 씬. Boot 는 포함하지 않는다.</summary>
        private Scene _content;

        /// <summary>
        /// 현재 콘텐츠 씬을 NGO 로 얹었는지. 호스트 시작 전에 일반 SceneManager 로 얹은 씬(Boot 단독 실행 → Lobby, Bootstrapper 가 연 씬)은
        /// NGO 가 핸들을 추적하지 않아 NGO 로 내리면 "Failed to remove scene handles" 에러가 난다 → 같은 방식으로 내린다.
        /// </summary>
        private bool _contentLoadedByNgo;

        public bool IsBusy { get; private set; }
        public string CurrentSceneName => _content.IsValid() ? _content.name : "-";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Bootstrapper 경로: 에디터가 이미 열어 둔 활성 씬을 현재 콘텐츠 씬으로 삼는다.</summary>
        public void AdoptActiveScene()
        {
            _content = SceneManager.GetActiveScene();
            _contentLoadedByNgo = false;
        }

        /// <summary>대상 씬을 얹고 활성화한 뒤 이전 콘텐츠 씬을 내린다. 완료 시 <paramref name="onDone"/>.</summary>
        public void Transition(string sceneName, Action onDone)
        {
            if (IsBusy)
            {
                Log.Warn(LogCategory.Core, $"SceneFlow: 전이 중에 {sceneName} 요청 — 무시");
                return;
            }
            StartCoroutine(TransitionRoutine(sceneName, onDone));
        }

        private IEnumerator TransitionRoutine(string sceneName, Action onDone)
        {
            IsBusy = true;
            var previous = _content;
            bool previousLoadedByNgo = _contentLoadedByNgo;
            string previousName = previous.IsValid() ? previous.name : "-";

            bool loadedByNgo = UseNetworkSceneManager;
            yield return LoadAdditive(sceneName);

            var loaded = SceneManager.GetSceneByName(sceneName);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(loaded);
                _content = loaded;
                _contentLoadedByNgo = loadedByNgo;
            }
            else
            {
                Log.Error(LogCategory.Core, $"SceneFlow: {sceneName} 로드 실패 (Build Settings 확인)");
            }

            if (previous.IsValid() && previous.isLoaded && previous != loaded)
            {
                yield return Unload(previous, previousLoadedByNgo);
            }

            Log.Info(LogCategory.Core, $"SceneFlow: {previousName} → {sceneName} (NGO 로드={loadedByNgo})");
            IsBusy = false;
            onDone?.Invoke();
        }

        private static bool UseNetworkSceneManager => NetService.IsHost && NetService.IsSceneManagementEnabled;

        private IEnumerator LoadAdditive(string sceneName)
        {
            if (!UseNetworkSceneManager)
            {
                yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                yield break;
            }

            var netScenes = NetworkManager.Singleton.SceneManager;
            bool completed = false;
            void OnCompleted(string name, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
            {
                if (name == sceneName)
                {
                    completed = true;
                }
            }
            netScenes.OnLoadEventCompleted += OnCompleted;

            SceneEventProgressStatus status;
            while ((status = netScenes.LoadScene(sceneName, LoadSceneMode.Additive)) == SceneEventProgressStatus.SceneEventInProgress)
            {
                yield return null;
            }

            if (status != SceneEventProgressStatus.Started)
            {
                netScenes.OnLoadEventCompleted -= OnCompleted;
                Log.Error(LogCategory.Net, $"NetworkSceneManager.LoadScene({sceneName}) 실패: {status} — 일반 SceneManager 로 대체 (클라이언트 동기화 안 됨)");
                yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                yield break;
            }

            yield return new WaitUntil(() => completed);
            netScenes.OnLoadEventCompleted -= OnCompleted;
        }

        private IEnumerator Unload(Scene scene, bool loadedByNgo)
        {
            string sceneName = scene.name;
            if (!loadedByNgo || !UseNetworkSceneManager)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
                yield break;
            }

            var netScenes = NetworkManager.Singleton.SceneManager;
            bool completed = false;
            void OnCompleted(string name, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
            {
                if (name == sceneName)
                {
                    completed = true;
                }
            }
            netScenes.OnUnloadEventCompleted += OnCompleted;

            SceneEventProgressStatus status;
            while ((status = netScenes.UnloadScene(scene)) == SceneEventProgressStatus.SceneEventInProgress)
            {
                yield return null;
            }

            if (status != SceneEventProgressStatus.Started)
            {
                netScenes.OnUnloadEventCompleted -= OnCompleted;
                Log.Warn(LogCategory.Net, $"NetworkSceneManager.UnloadScene({sceneName}) 불가({status}) — 일반 SceneManager 로 언로드 (클라이언트 동기화 안 됨)");
                yield return SceneManager.UnloadSceneAsync(scene);
                yield break;
            }

            yield return new WaitUntil(() => completed);
            netScenes.OnUnloadEventCompleted -= OnCompleted;
        }
    }
}
