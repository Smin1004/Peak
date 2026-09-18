using UnityEngine;

namespace Peak.Visual
{
    /// <summary>
    /// 로직 → 비주얼 통지 창구 (Docs/102_required_assets.md 2장 1번). 프리팹 <c>Visual</c> 자식 루트에 붙은 구현체가 받는다.
    /// Root 쪽 스크립트는 <c>GetComponentInChildren&lt;IVisualState&gt;()</c> 로만 찾고, <c>Visual</c> 내부 파츠를 모른다.
    /// M0-2 는 색만. 이동 상태 표현(등반 = 벽에 붙음, 기절 = 눕힘 …)은 M1 이 메서드를 추가한다.
    /// </summary>
    public interface IVisualState
    {
        /// <summary>파츠 전체의 기본 색. 색은 <see cref="VisualTheme"/> 에서 가져온 값만 넘긴다.</summary>
        void SetTint(Color color);
    }
}
