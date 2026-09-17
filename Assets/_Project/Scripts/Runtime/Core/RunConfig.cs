using System;
using Unity.Netcode;

namespace Peak.Core
{
    /// <summary>
    /// 한 판의 설정 — 호스트가 정하고 전원에게 동기화한다 (Docs/201_common.md 5장, Docs/205_network.md 4장).
    /// 이 값만 있으면 모든 클라이언트에서 같은 산이 나와야 한다.
    /// </summary>
    [Serializable]
    public struct RunConfig : INetworkSerializable, IEquatable<RunConfig>
    {
        /// <summary>로비 시드 입력 기본값이자 회귀 기준 시드 (Docs/201_common.md 7장 "시드 고정(예: 1)").</summary>
        public const int DefaultSeed = 1;

        /// <summary>0 = 기본 난이도. T2 Ascent 는 Docs/101_extra_design.md — M6 전까지 항상 0.</summary>
        public const int DefaultDifficulty = 0;

        public int Seed;

        /// <summary>BiomeDef 인덱스 순서 (T0: [Shore, Peak]). BiomeDef 는 M2 에서 정의되므로 M0 에서는 빈 배열.</summary>
        public int[] BiomeSequence;

        /// <summary>안개 ON/OFF. M3 까지 기본 OFF (Docs/300_roadmap.md M3 "안개 구현하되 기본 OFF").</summary>
        public bool FogEnabled;

        public int Difficulty;

        /// <summary>M0 기본 설정: 시드만 지정, 나머지는 기본값.</summary>
        public static RunConfig CreateDefault(int seed)
        {
            return new RunConfig
            {
                Seed = seed,
                BiomeSequence = Array.Empty<int>(),
                FogEnabled = false,
                Difficulty = DefaultDifficulty,
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Seed);

            // NGO 배열 직렬화는 null 을 허용하지 않는다 — 쓰기 전에 빈 배열로 정규화
            if (serializer.IsWriter && BiomeSequence == null)
            {
                BiomeSequence = Array.Empty<int>();
            }
            serializer.SerializeValue(ref BiomeSequence);

            serializer.SerializeValue(ref FogEnabled);
            serializer.SerializeValue(ref Difficulty);
        }

        public bool Equals(RunConfig other)
        {
            return Seed == other.Seed
                && FogEnabled == other.FogEnabled
                && Difficulty == other.Difficulty
                && SequenceEquals(BiomeSequence, other.BiomeSequence);
        }

        public override bool Equals(object obj) => obj is RunConfig other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Seed);
            hash.Add(FogEnabled);
            hash.Add(Difficulty);
            if (BiomeSequence != null)
            {
                for (int i = 0; i < BiomeSequence.Length; i++)
                {
                    hash.Add(BiomeSequence[i]);
                }
            }
            return hash.ToHashCode();
        }

        public override string ToString()
        {
            var seq = BiomeSequence == null ? "null" : string.Join(",", BiomeSequence);
            return $"RunConfig(Seed={Seed}, Biomes=[{seq}], Fog={FogEnabled}, Difficulty={Difficulty})";
        }

        private static bool SequenceEquals(int[] a, int[] b)
        {
            int lenA = a?.Length ?? 0;
            int lenB = b?.Length ?? 0;
            if (lenA != lenB)
            {
                return false;
            }
            for (int i = 0; i < lenA; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
