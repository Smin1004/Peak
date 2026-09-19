using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Peak.Stamina;
using UnityEngine;
using UnityEngine.TestTools;

namespace Peak.Tests
{
    /// <summary>StatusEffectStack 규칙 (Docs/202_gameplay.md 6장). 정의는 테스트 안에서 만든다.</summary>
    public class StatusEffectStackTests
    {
        private const float Tolerance = 1e-4f;
        private const float KindCap = 100f;
        private const float TotalCap = 200f;
        private const float ColdDecayDelay = 1f;
        private const float ColdDecayRate = 10f;

        private readonly List<StatusEffectDef> _defs = new List<StatusEffectDef>();

        [SetUp]
        public void SetUp()
        {
            // 일부러 order 와 다른 순서로 넘긴다 — 스택이 order 로 정렬해야 한다
            _defs.Add(CreateDef(StatusKind.Cold, 2, ColdDecayDelay, ColdDecayRate));
            _defs.Add(CreateDef(StatusKind.Hunger, 0, 0f, 0f));
            _defs.Add(CreateDef(StatusKind.Injury, 1, 0f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var def in _defs)
            {
                Object.DestroyImmediate(def);
            }
            _defs.Clear();
        }

        private static StatusEffectDef CreateDef(StatusKind kind, int order, float decayDelay, float decayRate)
        {
            var def = ScriptableObject.CreateInstance<StatusEffectDef>();
            def.kind = kind;
            def.displayName = kind.ToString();
            def.order = order;
            def.maxAmount = KindCap;
            def.decayDelay = decayDelay;
            def.decayRate = decayRate;
            return def;
        }

        [Test]
        public void Entries_AreSortedByOrder()
        {
            var stack = new StatusEffectStack(_defs, TotalCap);

            Assert.AreEqual(3, stack.Count);
            Assert.AreEqual(StatusKind.Hunger, stack.GetDefinition(0).kind);
            Assert.AreEqual(StatusKind.Injury, stack.GetDefinition(1).kind);
            Assert.AreEqual(StatusKind.Cold, stack.GetDefinition(2).kind);
        }

        [Test]
        public void Add_ClampsAtKindCap()
        {
            var stack = new StatusEffectStack(_defs, TotalCap);

            float added = stack.Add(StatusKind.Injury, 150f);

            Assert.AreEqual(KindCap, added, Tolerance);
            Assert.AreEqual(KindCap, stack.Get(StatusKind.Injury), Tolerance);
        }

        [Test]
        public void Add_ClampsAtTotalCap()
        {
            var stack = new StatusEffectStack(_defs, TotalCap);
            stack.Add(StatusKind.Hunger, 90f);
            stack.Add(StatusKind.Injury, 90f);

            float added = stack.Add(StatusKind.Cold, 50f);

            Assert.AreEqual(20f, added, Tolerance);
            Assert.AreEqual(TotalCap, stack.Sum, Tolerance);
        }

        [Test]
        public void Set_ReplacesValue_WithinCaps()
        {
            var stack = new StatusEffectStack(_defs, TotalCap);
            stack.Set(StatusKind.Hunger, 40f);
            Assert.AreEqual(40f, stack.Get(StatusKind.Hunger), Tolerance);

            stack.Set(StatusKind.Hunger, 10f);
            Assert.AreEqual(10f, stack.Get(StatusKind.Hunger), Tolerance);

            stack.Set(StatusKind.Hunger, 150f);
            Assert.AreEqual(KindCap, stack.Get(StatusKind.Hunger), Tolerance);

            stack.Set(StatusKind.Injury, KindCap);
            stack.Set(StatusKind.Cold, 50f);
            Assert.AreEqual(0f, stack.Get(StatusKind.Cold), Tolerance, "합계 상한 200 이 이미 찼다");
        }

        [Test]
        public void UndefinedKind_IsRejectedWithWarning()
        {
            var stack = new StatusEffectStack(_defs, TotalCap);
            LogAssert.Expect(LogType.Warning, new Regex("Poison 정의가 없어 거부"));

            float added = stack.Add(StatusKind.Poison, 10f);

            Assert.AreEqual(0f, added, Tolerance);
            Assert.AreEqual(0f, stack.Get(StatusKind.Poison), Tolerance);
            Assert.AreEqual(0f, stack.Sum, Tolerance);
        }

        [Test]
        public void Decay_WaitsDelay_ThenDecayRate()
        {
            var stack = new StatusEffectStack(_defs, TotalCap);
            stack.Add(StatusKind.Cold, 50f);

            stack.Tick(ColdDecayDelay * 0.5f);
            Assert.AreEqual(50f, stack.Get(StatusKind.Cold), Tolerance, "지연 안에서는 그대로");

            // 지연을 1초 넘긴 시점 → 1초 분량만 감소
            stack.Tick(ColdDecayDelay * 0.5f + 1f);
            Assert.AreEqual(50f - ColdDecayRate, stack.Get(StatusKind.Cold), Tolerance);
        }

        [Test]
        public void Decay_NoneWhenRateZero()
        {
            var stack = new StatusEffectStack(_defs, TotalCap);
            stack.Add(StatusKind.Injury, 30f);

            stack.Tick(100f);

            Assert.AreEqual(30f, stack.Get(StatusKind.Injury), Tolerance);
        }

        [Test]
        public void Remove_And_Clear()
        {
            var stack = new StatusEffectStack(_defs, TotalCap);
            stack.Add(StatusKind.Injury, 30f);

            Assert.AreEqual(30f, stack.Remove(StatusKind.Injury, 50f), Tolerance);
            Assert.AreEqual(0f, stack.Get(StatusKind.Injury), Tolerance);

            stack.Add(StatusKind.Hunger, 20f);
            stack.Clear();
            Assert.AreEqual(0f, stack.Sum, Tolerance);
        }
    }
}
