using Peak.Core;
using Peak.UI;
using UnityEngine;

namespace Peak.Player
{
    /// <summary>
    /// 오너 로컬 HUD (Docs/204_ui.md 3장: HUD 는 오너 로컬 플레이어만 생성). Player 루트에 붙는다. **오너 전용** —
    /// <see cref="PlayerController"/> 가 오너 스폰 때 카메라 다음에 <see cref="Activate"/>, 디스폰 때 <see cref="Deactivate"/> 를 부른다.
    /// HudRoot 프리팹을 인스턴스화하고 테마 색을 적용한다. 조준점은 커서가 잠겨 있을 때만 보인다 (204 2.2).
    /// </summary>
    [RequireComponent(typeof(PlayerCameraRig))]
    public sealed class PlayerLocalHud : MonoBehaviour
    {
        [Tooltip("Prefabs/UI/HudRoot.prefab (Peak > Setup > Rebuild Player Prefab 이 연결)")]
        [SerializeField] private GameObject hudPrefab;

        private PlayerCameraRig _cameraRig;
        private HudRoot _hud;

        private void Awake()
        {
            _cameraRig = GetComponent<PlayerCameraRig>();
        }

        public void Activate()
        {
            if (_hud != null)
            {
                return;
            }
            if (hudPrefab == null)
            {
                Log.Error(LogCategory.UI, "PlayerLocalHud: hudPrefab 참조가 비어 있다 (Peak > Setup > Rebuild Player Prefab)");
                return;
            }

            var instance = Instantiate(hudPrefab);
            instance.name = hudPrefab.name;
            _hud = instance.GetComponent<HudRoot>();
            if (_hud == null)
            {
                Log.Error(LogCategory.UI, "PlayerLocalHud: HudRoot 프리팹 루트에 HudRoot 컴포넌트가 없다");
                Destroy(instance);
                return;
            }
            _hud.ApplyTheme(GameConfig.Theme);
            _hud.SetCrosshairVisible(_cameraRig.IsCursorLocked);
        }

        public void Deactivate()
        {
            if (_hud != null)
            {
                Destroy(_hud.gameObject);
                _hud = null;
            }
        }

        private void OnDestroy()
        {
            Deactivate();
        }

        private void LateUpdate()
        {
            if (_hud != null)
            {
                _hud.SetCrosshairVisible(_cameraRig.IsCursorLocked);
            }
        }
    }
}
