using UnityEngine;
using UnityEngine.SceneManagement;

namespace Peak.Core
{
    /// <summary>
    /// Boot 씬 루트 <c>[Boot]</c>. 자기 자신(과 자식 GameManager·SceneFlow·DebugOverlay·EventSystem)을 DontDestroyOnLoad 로 옮긴다.
    /// NetworkManager 는 NGO 제약(부모 금지) 때문에 별도 루트 오브젝트이며 스스로 DontDestroyOnLoad 한다.
    /// Boot 를 단독 실행했으면(다른 씬 없음) Lobby 로 넘어간다 (Docs/201_common.md 2장). Bootstrapper 경로에서는 아무것도 하지 않는다.
    /// </summary>
    public sealed class BootRoot : MonoBehaviour
    {
        private static BootRoot s_Instance;

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Log.Warn(LogCategory.Core, "Boot 가 두 번 로드됐다. 중복 [Boot] 제거");
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Boot 단독 실행: 로드된 씬이 Boot 하나뿐이면 Lobby 로. (DontDestroyOnLoad 씬은 sceneCount 에 포함되지 않는다)
            if (SceneManager.sceneCount == 1 && GameManager.Instance != null)
            {
                Log.Info(LogCategory.Core, "Boot 단독 실행 → Lobby");
                GameManager.Instance.EnterLobby();
            }
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }
    }
}
