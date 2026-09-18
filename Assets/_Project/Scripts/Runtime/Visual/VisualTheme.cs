using UnityEngine;

namespace Peak.Visual
{
    /// <summary>
    /// 색 팔레트. 색은 코드에 하드코딩하지 않고 여기서만 가져온다 (Docs/102_required_assets.md 1·2장, Docs/201_common.md 4장).
    /// 런타임 적용은 MaterialPropertyBlock 으로 (102 2장 3번). 상태이상 색은 M1 이 <c>StatusEffectDef.color</c> 로 추가한다.
    /// 접근: <see cref="Peak.Core.GameConfig.Theme"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "VisualTheme", menuName = "Peak/Visual Theme")]
    public sealed class VisualTheme : ScriptableObject
    {
        /// <summary>플레이어 4명 색. 인덱스 = 플레이어 인덱스 (102 3장).</summary>
        public const int PlayerCount = 4;

        [Header("플레이어 (102 3장)")]
        [Tooltip("플레이어별 색. 인덱스 = OwnerClientId % 4")]
        public Color[] playerColors = new Color[PlayerCount]
        {
            new Color(0.90f, 0.30f, 0.25f),
            new Color(0.25f, 0.55f, 0.95f),
            new Color(0.35f, 0.80f, 0.40f),
            new Color(0.95f, 0.80f, 0.25f),
        };

        [Header("지형 (임시 회색 2종 — 바이옴 정점 색은 M2, 102 3장)")]
        [Tooltip("걷기 가능 면 색 (경사 < walkSlope)")]
        public Color terrainWalkColor = new Color(0.62f, 0.62f, 0.62f);

        [Tooltip("등반면 색 (경사 ≥ walkSlope)")]
        public Color terrainWallColor = new Color(0.42f, 0.42f, 0.42f);

        [Header("UI (102 1장: 단색 패널)")]
        public Color uiBackground = new Color(0.09f, 0.10f, 0.12f);
        public Color uiText = new Color(0.92f, 0.92f, 0.92f);
        public Color uiAccent = new Color(0.98f, 0.76f, 0.20f);

        [Header("HUD (204 2.2)")]
        [Tooltip("화면 중앙 조준점 색 (반투명 흰색). HudRoot.ApplyTheme 이 적용 — M0-3 추가")]
        public Color crosshairColor = new Color(1f, 1f, 1f, 0.75f);
    }
}
