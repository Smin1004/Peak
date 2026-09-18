using Peak.Core;
using UnityEngine;

namespace Peak.Visual
{
    /// <summary>
    /// 지형 메시의 걷는 면 / 등반면 색 (Docs/Prompts/M1-1_climb_course.md 4장, Docs/102_required_assets.md 2장 3번).
    /// 메시는 서브메시 2개로 나뉘어 있어야 한다: <see cref="WalkSubmesh"/> = 경사 &lt; walkSlope, <see cref="WallSubmesh"/> = 경사 ≥ walkSlope.
    /// 두 머티리얼 슬롯이 같은 머티리얼을 공유해도 슬롯(= 서브메시)별 MaterialPropertyBlock 으로 색을 넣는다. 머티리얼 에셋은 건드리지 않는다.
    /// <c>GameConfig</c> 에 의존하지 않는다 — Sandbox 직접 플레이 때 Boot 보다 먼저 켜지므로 테마를 직접 참조한다.
    /// M2 지형 메시가 같은 규칙(걷는 면 / 등반면 색)을 재사용할 수 있다: 같은 서브메시 규칙으로 메시를 만들고 이 컴포넌트를 붙인 뒤,
    /// 메시를 바꿀 때마다 <see cref="Apply"/> 를 부른다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public sealed class SlopeTint : MonoBehaviour
    {
        /// <summary>걷는 면 서브메시·머티리얼 슬롯 인덱스 (경사 &lt; walkSlope).</summary>
        public const int WalkSubmesh = 0;

        /// <summary>등반면 서브메시·머티리얼 슬롯 인덱스 (경사 ≥ walkSlope).</summary>
        public const int WallSubmesh = 1;

        /// <summary>URP Lit 의 기본 색 프로퍼티.</summary>
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Built-in 계열 셰이더 호환용 기본 색 프로퍼티.</summary>
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private VisualTheme theme;

        private MaterialPropertyBlock _block;

        private void OnEnable()
        {
            Apply();
        }

        /// <summary>테마 색을 슬롯별로 다시 넣는다. 에디터 Scene 뷰에서도 보인다 ([ExecuteAlways]).</summary>
        public void Apply()
        {
            if (theme == null)
            {
                // 에디터에서는 빌더가 AddComponent 직후(테마 연결 전) OnEnable 이 먼저 불린다 → 플레이 중에만 경고
                if (Application.isPlaying)
                {
                    Log.Warn(LogCategory.UI, $"{name}: SlopeTint 에 VisualTheme 이 없다");
                }
                return;
            }
            var target = GetComponent<Renderer>();
            SetSlotColor(target, WalkSubmesh, theme.terrainWalkColor);
            SetSlotColor(target, WallSubmesh, theme.terrainWallColor);
        }

        private void SetSlotColor(Renderer target, int slot, Color color)
        {
            if (slot >= target.sharedMaterials.Length)
            {
                return;
            }
            _block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(_block, slot);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            target.SetPropertyBlock(_block, slot);
        }
    }
}
