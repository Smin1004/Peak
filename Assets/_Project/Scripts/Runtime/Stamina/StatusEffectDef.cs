using UnityEngine;

namespace Peak.Stamina
{
    /// <summary>
    /// 상태이상 한 종류의 정의 (Docs/201_common.md 4장 에셋 표, Docs/202_gameplay.md 6장, Docs/100_game_design.md 4.2).
    /// 에셋: <c>Data/StatusEffectDef/</c>. 색은 이 에셋이 진실 — 스태미나 바 조각 색 (M1-4). 표시 순서 = <see cref="order"/> 오름차순.
    /// </summary>
    [CreateAssetMenu(fileName = "StatusEffectDef", menuName = "Peak/Status Effect Def")]
    public sealed class StatusEffectDef : ScriptableObject
    {
        [Tooltip("종류. 스택은 종류별 1항목")]
        public StatusKind kind;

        [Tooltip("표시 이름 (한국어)")]
        public string displayName;

        [Tooltip("스태미나 바 조각 색 (100 4.2)")]
        public Color color = Color.white;

        [Tooltip("바에서 왼쪽부터 쌓이는 순서. 작을수록 왼쪽")]
        public int order;

        [Tooltip("종류별 상한 (202 6장: 100). 넘치는 만큼은 버린다")]
        public float maxAmount = 100f;

        [Tooltip("새 누적이 없는 채로 이 시간(초)이 지나면 자연 회복 시작")]
        public float decayDelay;

        [Tooltip("자연 회복 속도 /s. 0 = 자연 회복 없음 (허기·부상·무게)")]
        public float decayRate;
    }
}
