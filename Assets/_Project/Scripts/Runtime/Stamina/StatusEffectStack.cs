using System.Collections.Generic;
using Peak.Core;
using UnityEngine;

namespace Peak.Stamina
{
    /// <summary>
    /// 상태이상 스택 (Docs/202_gameplay.md 6장). 순수 로직 — MonoBehaviour 아님, EditMode 로 시험한다.
    /// 종류별 1항목. 정의가 있는 종류만 받고(없으면 거부 + Log.Warn), 종류별 상한(<see cref="StatusEffectDef.maxAmount"/>)과
    /// 합계 상한에서 넘치는 만큼은 버린다. 자연 회복은 정의대로: 마지막 누적 뒤 decayDelay 가 지나면 decayRate/초.
    /// 항목은 <see cref="StatusEffectDef.order"/> 순으로 정렬돼 있다 — 인덱스 = 스태미나 바 표시 순서 (M1-4).
    /// </summary>
    public sealed class StatusEffectStack
    {
        private sealed class Entry
        {
            public readonly StatusEffectDef Def;
            public float Amount;
            public float SinceAdded;

            public Entry(StatusEffectDef def)
            {
                Def = def;
            }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly float _totalCap;

        public StatusEffectStack(IReadOnlyList<StatusEffectDef> definitions, float totalCap)
        {
            _totalCap = totalCap;
            if (definitions != null)
            {
                foreach (var def in definitions)
                {
                    if (def == null)
                    {
                        continue;
                    }
                    if (Find(def.kind) != null)
                    {
                        Log.Warn(LogCategory.Player, $"StatusEffectStack: {def.kind} 정의가 둘 이상이다 — 첫 번째만 쓴다");
                        continue;
                    }
                    _entries.Add(new Entry(def));
                }
            }
            _entries.Sort((a, b) => a.Def.order.CompareTo(b.Def.order));
        }

        /// <summary>정의된 종류 수 = 표시 항목 수.</summary>
        public int Count => _entries.Count;

        /// <summary>합계 (0..합계 상한).</summary>
        public float Sum
        {
            get
            {
                float sum = 0f;
                foreach (var entry in _entries)
                {
                    sum += entry.Amount;
                }
                return sum;
            }
        }

        /// <summary>표시 순서 <paramref name="index"/> 번째 정의 (색·이름).</summary>
        public StatusEffectDef GetDefinition(int index) => _entries[index].Def;

        /// <summary>표시 순서 <paramref name="index"/> 번째 양.</summary>
        public float GetAmount(int index) => _entries[index].Amount;

        public bool IsDefined(StatusKind kind) => Find(kind) != null;

        public float Get(StatusKind kind)
        {
            var entry = Find(kind);
            return entry != null ? entry.Amount : 0f;
        }

        /// <summary>누적. 종류별 상한·합계 상한에서 넘치는 만큼은 버린다. 실제로 더해진 양을 돌려준다. 자연 회복 대기를 다시 시작한다.</summary>
        public float Add(StatusKind kind, float amount)
        {
            var entry = FindOrReject(kind);
            if (entry == null || amount <= 0f)
            {
                return 0f;
            }
            float room = Mathf.Min(entry.Def.maxAmount - entry.Amount, _totalCap - Sum);
            float added = Mathf.Clamp(amount, 0f, Mathf.Max(room, 0f));
            entry.Amount += added;
            entry.SinceAdded = 0f;
            return added;
        }

        /// <summary>해소 (음식·붕대 등). 실제로 뺀 양을 돌려준다.</summary>
        public float Remove(StatusKind kind, float amount)
        {
            var entry = FindOrReject(kind);
            if (entry == null || amount <= 0f)
            {
                return 0f;
            }
            float removed = Mathf.Min(amount, entry.Amount);
            entry.Amount -= removed;
            return removed;
        }

        /// <summary>값을 직접 지정 (무게처럼 다른 값에서 파생되는 종류용). 종류별 상한·합계 상한으로 자른다. 늘어나면 자연 회복 대기를 다시 시작한다.</summary>
        public void Set(StatusKind kind, float amount)
        {
            var entry = FindOrReject(kind);
            if (entry == null)
            {
                return;
            }
            float others = Sum - entry.Amount;
            float value = Mathf.Clamp(amount, 0f, Mathf.Min(entry.Def.maxAmount, Mathf.Max(_totalCap - others, 0f)));
            if (value > entry.Amount)
            {
                entry.SinceAdded = 0f;
            }
            entry.Amount = value;
        }

        /// <summary>자연 회복: 마지막 누적 뒤 decayDelay 를 넘긴 시간만큼 decayRate 로 줄인다.</summary>
        public void Tick(float deltaTime)
        {
            foreach (var entry in _entries)
            {
                float before = entry.SinceAdded;
                entry.SinceAdded += deltaTime;
                if (entry.Def.decayRate <= 0f || entry.Amount <= 0f || entry.SinceAdded <= entry.Def.decayDelay)
                {
                    continue;
                }
                float decayingTime = entry.SinceAdded - Mathf.Max(before, entry.Def.decayDelay);
                entry.Amount = Mathf.Max(0f, entry.Amount - entry.Def.decayRate * decayingTime);
            }
        }

        public void Clear()
        {
            foreach (var entry in _entries)
            {
                entry.Amount = 0f;
                entry.SinceAdded = 0f;
            }
        }

        private Entry Find(StatusKind kind)
        {
            foreach (var entry in _entries)
            {
                if (entry.Def.kind == kind)
                {
                    return entry;
                }
            }
            return null;
        }

        private Entry FindOrReject(StatusKind kind)
        {
            var entry = Find(kind);
            if (entry == null)
            {
                Log.Warn(LogCategory.Player, $"StatusEffectStack: {kind} 정의가 없어 거부한다 (Data/StatusEffectDef)");
            }
            return entry;
        }
    }
}
