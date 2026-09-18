using Peak.Core;
using Peak.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace Peak.UI
{
    /// <summary>
    /// 게임 HUD 루트 (Docs/204_ui.md 2.2·3장). <c>Prefabs/UI/HudRoot.prefab</c> 의 Canvas 루트에 붙는다.
    /// 오너 로컬 플레이어만 <see cref="Peak.Player.PlayerLocalHud"/> 가 생성·파괴한다. M0-3 은 화면 중앙 조준점만.
    /// M1 이후 스태미나 바 등 HUD 요소는 이 프리팹에 추가 (204 3장) — 빌더(Peak > Setup > Rebuild Player Prefab)가 자식을 만들고 여기에 참조·표시 메서드를 더한다.
    /// 색은 프리팹에 굽지 않고 <see cref="ApplyTheme"/> 에서 <see cref="VisualTheme"/> 로 넣는다.
    /// </summary>
    public sealed class HudRoot : MonoBehaviour
    {
        [Tooltip("화면 중앙 조준점 (자식 Crosshair). 커서 해제(Esc) 중 숨김 — 204 2.2")]
        [SerializeField] private Image crosshair;

        public void ApplyTheme(VisualTheme theme)
        {
            if (theme == null)
            {
                Log.Error(LogCategory.UI, "HudRoot.ApplyTheme: VisualTheme 이 없다 (Boot 미로드)");
                return;
            }
            if (crosshair == null)
            {
                Log.Error(LogCategory.UI, "HudRoot: crosshair 참조가 비어 있다 (Peak > Setup > Rebuild Player Prefab)");
                return;
            }
            crosshair.color = theme.crosshairColor;
        }

        public void SetCrosshairVisible(bool visible)
        {
            if (crosshair != null && crosshair.gameObject.activeSelf != visible)
            {
                crosshair.gameObject.SetActive(visible);
            }
        }
    }
}
