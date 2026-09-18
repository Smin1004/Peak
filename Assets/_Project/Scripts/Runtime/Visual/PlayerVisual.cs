using UnityEngine;

namespace Peak.Visual
{
    /// <summary>
    /// 플레이어 프리팹 <c>Visual</c> 오브젝트에 붙는 <see cref="IVisualState"/> 구현 (Docs/102_required_assets.md 2·3장).
    /// 자기 자식 Renderer(캡슐·코)에 MaterialPropertyBlock 으로 색을 넣는다. 머티리얼 에셋은 건드리지 않는다 (102 2장 3번).
    /// 모델로 교체할 때는 이 컴포넌트만 바꾸면 된다 — 로직은 인터페이스만 본다.
    /// </summary>
    public sealed class PlayerVisual : MonoBehaviour, IVisualState
    {
        /// <summary>URP Lit 의 기본 색 프로퍼티.</summary>
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Built-in 계열 셰이더 호환용 기본 색 프로퍼티.</summary>
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _block;

        public void SetTint(Color color)
        {
            _block ??= new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}
