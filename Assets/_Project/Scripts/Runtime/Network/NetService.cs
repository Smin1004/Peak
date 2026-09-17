using Peak.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

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
