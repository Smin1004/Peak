using NUnit.Framework;
using Peak.Core;
using Peak.Stamina;
using UnityEngine;

namespace Peak.Tests
{
    /// <summary>StaminaSystem 규칙 (Docs/202_gameplay.md 5장). 튜닝은 GameTuning 기본값.</summary>
    public class StaminaSystemTests
    {
        private const float Tolerance = 1e-4f;

        private GameTuning _tuning;

        [SetUp]
        public void SetUp()
        {
            _tuning = ScriptableObject.CreateInstance<GameTuning>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_tuning);
        }

        [Test]
        public void Usable_IsMaxMinusEffects_ClampedAtZero()
        {
            var stamina = new StaminaSystem(_tuning);

            Assert.AreEqual(_tuning.maxStamina, stamina.Usable(0f), Tolerance);
            Assert.AreEqual(70f, stamina.Usable(30f), Tolerance);
            Assert.AreEqual(0f, stamina.Usable(150f), Tolerance);
        }

        [Test]
        public void Tick_ClampsStaminaToUsable()
        {
            var stamina = new StaminaSystem(_tuning);

            stamina.Tick(true, _tuning.regenDelay, 30f);

            Assert.AreEqual(70f, stamina.Stamina, Tolerance);
        }

        [Test]
        public void TrySpend_TakesStaminaFirstThenBonus()
        {
            var stamina = new StaminaSystem(_tuning);
            stamina.Set(10f, 20f);

            Assert.IsTrue(stamina.TrySpend(15f));

            Assert.AreEqual(0f, stamina.Stamina, Tolerance);
            Assert.AreEqual(15f, stamina.Bonus, Tolerance);
        }

        [Test]
        public void TrySpend_WhenShort_TakesNothing()
        {
            var stamina = new StaminaSystem(_tuning);
            stamina.Set(5f, 4f);

            Assert.IsFalse(stamina.TrySpend(10f));

            Assert.AreEqual(5f, stamina.Stamina, Tolerance);
            Assert.AreEqual(4f, stamina.Bonus, Tolerance);
        }

        [Test]
        public void Drain_StopsAtZero_AndReturnsActualAmount()
        {
            var stamina = new StaminaSystem(_tuning);
            stamina.Set(3f, 2f);

            Assert.AreEqual(5f, stamina.Drain(10f), Tolerance);
            Assert.AreEqual(0f, stamina.Stamina, Tolerance);
            Assert.AreEqual(0f, stamina.Bonus, Tolerance);
            Assert.AreEqual(0f, stamina.Drain(10f), Tolerance);
        }

        [Test]
        public void Regen_WaitsDelay_ThenRegenRate()
        {
            var stamina = new StaminaSystem(_tuning);
            stamina.Drain(_tuning.maxStamina);
            float halfDelay = _tuning.regenDelay * 0.5f;

            stamina.Tick(true, halfDelay, 0f);
            Assert.AreEqual(0f, stamina.Stamina, Tolerance, "회복 지연 안에서는 그대로");

            // 지연을 1초 넘긴 시점 → 1초 분량만 회복
            stamina.Tick(true, halfDelay + 1f, 0f);
            Assert.AreEqual(_tuning.regenRate, stamina.Stamina, Tolerance);
        }

        [Test]
        public void Regen_OnlyWhenCanRegen()
        {
            var stamina = new StaminaSystem(_tuning);
            stamina.Drain(_tuning.maxStamina);

            stamina.Tick(false, _tuning.regenDelay + 1f, 0f);

            Assert.AreEqual(0f, stamina.Stamina, Tolerance);
        }

        [Test]
        public void Spend_RestartsRegenDelay()
        {
            var stamina = new StaminaSystem(_tuning);
            stamina.Set(50f, 0f);
            stamina.Tick(true, _tuning.regenDelay + 1f, 0f);
            float afterRegen = stamina.Stamina;

            stamina.Drain(1f);
            stamina.Tick(true, _tuning.regenDelay * 0.5f, 0f);

            Assert.AreEqual(afterRegen - 1f, stamina.Stamina, Tolerance);
        }

        [Test]
        public void Set_RestartsRegenDelay()
        {
            var stamina = new StaminaSystem(_tuning);

            stamina.Set(0f, 0f);
            stamina.Tick(true, _tuning.regenDelay * 0.5f, 0f);

            Assert.AreEqual(0f, stamina.Stamina, Tolerance);
        }

        [Test]
        public void Bonus_IsNotRegenerated()
        {
            var stamina = new StaminaSystem(_tuning);
            stamina.Set(0f, 5f);
            stamina.Drain(5f);

            stamina.Tick(true, _tuning.regenDelay + 1f, 0f);

            Assert.AreEqual(0f, stamina.Bonus, Tolerance);
            Assert.Greater(stamina.Stamina, 0f);
        }
    }
}
