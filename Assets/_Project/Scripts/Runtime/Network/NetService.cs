using Peak.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.SceneManagement;

namespace Peak.Network
{
    /// <summary>
    /// 네트워크 진입점 래퍼. 씬·게임플레이 코드는 <c>Unity.Netcode</c> 타입을 직접 만지지 않고 이 클래스를 거친다 (Docs/205_network.md 5장).
    /// M0: 로컬 Unity Transport 로 호스트/클라이언트만. Relay·Sessions 는 M4.
    /// </summary>
    public static class NetService
    {
        /// <summary>로컬 개발용 접속 주소·포트 (Docs/205_network.md 6장, 인프라 상수).</summary>
        public const string LocalAddress = "127.0.0.1";
        public const ushort LocalPort = 7777;

        /// <summary>M4 에서 Relay 로 전환할 때 쓰는 개발 모드 토글 (205 6장). M0~M3 은 항상 false.</summary>
        public static bool UseRelay = false;

        private static NetworkManager Manager => NetworkManager.Singleton;

        public static bool IsRunning => Manager != null && Manager.IsListening;
        public static bool IsHost => IsRunning && Manager.IsHost;
        public static bool IsServer => IsRunning && Manager.IsServer;
        public static bool IsClient => IsRunning && Manager.IsClient;
        public static ulong LocalClientId => Manager != null ? Manager.LocalClientId : 0UL;
        public static bool IsSceneManagementEnabled => Manager != null && Manager.NetworkConfig.EnableSceneManagement && Manager.SceneManager != null;

        /// <summary>디버그 오버레이용 역할 문자열: Host / Client / None.</summary>
        public static string RoleName => IsHost ? "Host" : IsClient ? "Client" : "None";

        /// <summary>로컬 Unity Transport 로 호스트 시작. 이미 떠 있으면 현재 역할이 호스트인지 돌려준다.</summary>
        public static bool StartHostLocal()
        {
            var manager = Manager;
            if (manager == null)
            {
                Log.Error(LogCategory.Net, "StartHostLocal: NetworkManager 가 없다 (Boot 씬 미로드)");
                return false;
            }
            if (manager.IsListening)
            {
                return manager.IsHost;
            }

            ConfigureLocalTransport(manager);
            bool ok = manager.StartHost();
            Log.Info(LogCategory.Net, ok ? $"호스트 시작 ({LocalAddress}:{LocalPort}, clientId={manager.LocalClientId})" : "호스트 시작 실패");
            if (ok)
            {
                ConfigureClientSynchronization(manager);
            }
            return ok;
        }

        /// <summary>127.0.0.1 로컬 호스트에 클라이언트로 접속 (Multiplayer Play Mode 용).</summary>
        public static bool StartClientLocal()
        {
            var manager = Manager;
            if (manager == null)
            {
                Log.Error(LogCategory.Net, "StartClientLocal: NetworkManager 가 없다 (Boot 씬 미로드)");
                return false;
            }
            if (manager.IsListening)
            {
                return manager.IsClient;
            }

            ConfigureLocalTransport(manager);
            bool ok = manager.StartClient();
            Log.Info(LogCategory.Net, ok ? $"클라이언트 접속 시도 ({LocalAddress}:{LocalPort})" : "클라이언트 시작 실패");
            return ok;
        }

        public static void Shutdown()
        {
            if (IsRunning)
            {
                Manager.Shutdown();
                Log.Info(LogCategory.Net, "네트워크 종료");
            }
        }

        /// <summary>
        /// 접속 클라이언트의 초기 씬 동기화를 Additive 로 (서버가 정하는 값, 클라이언트는 따라감).
        /// 기본값 Single 이면 클라이언트는 서버의 활성 씬만 "이미 로드됨"으로 인정하고 나머지(Boot)를 다시 로드한다 →
        /// Bootstrapper 가 Boot 를 이미 얹은 클라이언트에서 Boot 가 중복 로드된다 (M0-2 8장 조사, NGO DefaultSceneManagerHandler.ClientShouldPassThrough).
        /// Additive 는 클라이언트에 이미 로드된 같은 씬을 재사용한다. 우리 씬 구조(Boot + 애디티브 콘텐츠 씬, 201 2장)와도 맞다.
        /// </summary>
        private static void ConfigureClientSynchronization(NetworkManager manager)
        {
            if (manager.NetworkConfig.EnableSceneManagement && manager.SceneManager != null)
            {
                manager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
            }
        }

        private static void ConfigureLocalTransport(NetworkManager manager)
        {
            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Log.Error(LogCategory.Net, "NetworkManager 에 UnityTransport 가 없다");
                return;
            }
            transport.SetConnectionData(LocalAddress, LocalPort, LocalAddress);
        }
    }
}
