using NUnit.Framework;
using Peak.Core;
using Peak.Stamina;
using UnityEngine;

namespace Peak.Tests
{
    /// <summary>낙하 피해 공식 (Docs/202_gameplay.md 7장). 튜닝은 GameTuning 기본값 (v_safe 8, v_max 25, 5 → 100, 탈진 ×1.5).</summary>
    public class FallDamageTests
    {
        private const float Tolerance = 1e-3f;

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
        public void BelowSafeSpeed_IsZero()
        {
            Assert.AreEqual(0f, FallDamage.ComputeInjury(7.99f, false, _tuning), Tolerance);
        }

        [Test]
        public void AtSafeSpeed_IsMinInjury()
        {
            Assert.AreEqual(5f, FallDamage.ComputeInjury(8f, false, _tuning), Tolerance);
        }

        [Test]
        public void AtMaxSpeed_IsMaxInjury()
        {
            Assert.AreEqual(100f, FallDamage.ComputeInjury(25f, false, _tuning), Tolerance);
        }

        [Test]
        public void AboveMaxSpeed_StaysMaxInjury()
        {
            Assert.AreEqual(100f, FallDamage.ComputeInjury(30f, false, _tuning), Tolerance);
        }

        [Test]
        public void Exhausted_MultipliesByOnePointFive()
        {
            float normal = FallDamage.ComputeInjury(16.5f, false, _tuning);
            float exhausted = FallDamage.ComputeInjury(16.5f, true, _tuning);

            Assert.AreEqual(52.5f, normal, Tolerance);
            Assert.AreEqual(normal * 1.5f, exhausted, Tolerance);
        }
    }
}
