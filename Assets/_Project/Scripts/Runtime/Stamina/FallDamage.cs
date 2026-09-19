using Peak.Core;
using UnityEngine;

namespace Peak.Stamina
{
    /// <summary>낙하 피해 공식 (Docs/202_gameplay.md 7장, Docs/100_game_design.md 4.5). 순수 함수.</summary>
    public static class FallDamage
    {
        /// <summary>
        /// 착지 수직 속도 → 부상량. v &lt; fallSafeSpeed → 0, 아니면 lerp(fallMinInjury, fallMaxInjury, inverseLerp(fallSafeSpeed, fallMaxSpeed, v)).
        /// fallMaxSpeed 이상은 fallMaxInjury. 탈진 낙하면 × exhaustedFallMultiplier (종류별 상한은 스택이 자른다).
        /// </summary>
        public static float ComputeInjury(float landingSpeed, bool exhausted, GameTuning tuning)
        {
            if (landingSpeed < tuning.fallSafeSpeed)
            {
                return 0f;
            }
            float t = Mathf.InverseLerp(tuning.fallSafeSpeed, tuning.fallMaxSpeed, landingSpeed);
            float injury = Mathf.Lerp(tuning.fallMinInjury, tuning.fallMaxInjury, t);
            return exhausted ? injury * tuning.exhaustedFallMultiplier : injury;
        }
    }
}
