using Peak.Core;
using UnityEngine;

namespace Peak.Stamina
{
    /// <summary>
    /// 스태미나 (Docs/202_gameplay.md 5장, Docs/100_game_design.md 4.1). 순수 로직 — MonoBehaviour 아님, EditMode 로 시험한다.
    /// usable = max(0, maxStamina − 상태이상 합), stamina 는 [0, usable] 로 클램프.
    /// 소모는 스태미나 먼저, 모자라면 보너스. 보너스는 재생 없음. 회복은 마지막 소모 뒤 regenDelay 가 지나고 canRegen 일 때 regenRate/초.
    /// </summary>
    public sealed class StaminaSystem
    {
        private readonly GameTuning _tuning;

        /// <summary>마지막 소모 이후 흐른 시간 (회복 지연 판정).</summary>
        private float _sinceSpend;

        public StaminaSystem(GameTuning tuning)
        {
            _tuning = tuning;
            Reset();
        }

        public float Stamina { get; private set; }

        /// <summary>보너스 스태미나 (예비 바). 음식·캠프파이어로 얻고 재생 없음.</summary>
        public float Bonus { get; private set; }

        /// <summary>실제로 쓸 수 있는 최대치 = max(0, maxStamina − 상태이상 합).</summary>
        public float Usable(float effectSum) => Mathf.Max(0f, _tuning.maxStamina - effectSum);

        /// <summary>이산 소모 (점프). 스태미나 + 보너스 ≥ amount 일 때만 스태미나 먼저·보너스 나중으로 빼고 true. 부족하면 아무것도 빼지 않는다.</summary>
        public bool TrySpend(float amount)
        {
            if (amount <= 0f)
            {
                return true;
            }
            if (Stamina + Bonus < amount)
            {
                return false;
            }
            Take(amount);
            return true;
        }

        /// <summary>연속 소모 (등반·달리기). 있는 만큼 스태미나 → 보너스 순으로 빼고, 실제로 뺀 양을 돌려준다. 둘 다 0 이면 멈춘다.</summary>
        public float Drain(float amount)
        {
            if (amount <= 0f)
            {
                return 0f;
            }
            return Take(Mathf.Min(amount, Stamina + Bonus));
        }

        /// <summary>
        /// 매 물리 스텝: stamina 를 usable 로 클램프하고, canRegen 이면 회복 지연을 넘긴 시간만큼 regenRate 로 회복한다.
        /// 지연 타이머는 canRegen 과 무관하게 흐른다 (지연 = "마지막 소모 뒤" 시간).
        /// </summary>
        public void Tick(bool canRegen, float deltaTime, float effectSum)
        {
            float usable = Usable(effectSum);
            float before = _sinceSpend;
            _sinceSpend += deltaTime;
            if (canRegen && _sinceSpend > _tuning.regenDelay)
            {
                float regenTime = _sinceSpend - Mathf.Max(before, _tuning.regenDelay);
                Stamina += _tuning.regenRate * regenTime;
            }
            Stamina = Mathf.Clamp(Stamina, 0f, usable);
        }

        public void AddBonus(float amount)
        {
            Bonus = Mathf.Max(0f, Bonus + amount);
        }

        /// <summary>
        /// 값 직접 지정 (기상 때 stamina = 0, 디버그). 다음 Tick 에서 usable 로 클램프된다.
        /// 방금 소모한 것처럼 회복 지연을 다시 시작한다 — 0 으로 지정한 직후 바로 차오르지 않게 (기상은 0 부터, 디버그 시험은 0 상태 유지).
        /// </summary>
        public void Set(float stamina, float bonus)
        {
            Stamina = Mathf.Max(0f, stamina);
            Bonus = Mathf.Max(0f, bonus);
            _sinceSpend = 0f;
        }

        /// <summary>스태미나 maxStamina, 보너스 0, 회복 지연 없음.</summary>
        public void Reset()
        {
            Stamina = _tuning.maxStamina;
            Bonus = 0f;
            _sinceSpend = _tuning.regenDelay;
        }

        private float Take(float amount)
        {
            float fromStamina = Mathf.Min(amount, Stamina);
            Stamina -= fromStamina;
            float fromBonus = Mathf.Min(amount - fromStamina, Bonus);
            Bonus -= fromBonus;
            _sinceSpend = 0f;
            return fromStamina + fromBonus;
        }
    }
}
