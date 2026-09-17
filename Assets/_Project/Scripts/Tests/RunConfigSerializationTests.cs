using NUnit.Framework;
using Peak.Core;
using Unity.Collections;
using Unity.Netcode;

namespace Peak.Tests
{
    /// <summary>RunConfig 네트워크 직렬화 왕복 (Docs/201_common.md 5장: INetworkSerializable 필수).</summary>
    public class RunConfigSerializationTests
    {
        private const int BufferSize = 1024;

        [Test]
        public void RunConfig_RoundTrip_PreservesAllFields()
        {
            var source = new RunConfig
            {
                Seed = 123456,
                BiomeSequence = new[] { 0, 1, 2, 1 },
                FogEnabled = true,
                Difficulty = 2,
            };

            var result = RoundTrip(source);

            Assert.AreEqual(source.Seed, result.Seed);
            CollectionAssert.AreEqual(source.BiomeSequence, result.BiomeSequence);
            Assert.AreEqual(source.FogEnabled, result.FogEnabled);
            Assert.AreEqual(source.Difficulty, result.Difficulty);
            Assert.IsTrue(source.Equals(result), "IEquatable 비교가 왕복 후에도 같아야 한다");
        }

        [Test]
        public void RunConfig_RoundTrip_DefaultHasEmptyBiomeSequence()
        {
            var source = RunConfig.CreateDefault(RunConfig.DefaultSeed);

            var result = RoundTrip(source);

            Assert.AreEqual(RunConfig.DefaultSeed, result.Seed);
            Assert.IsNotNull(result.BiomeSequence);
            Assert.AreEqual(0, result.BiomeSequence.Length);
            Assert.IsFalse(result.FogEnabled);
            Assert.AreEqual(RunConfig.DefaultDifficulty, result.Difficulty);
        }

        [Test]
        public void RunConfig_RoundTrip_NullBiomeSequenceBecomesEmpty()
        {
            var source = new RunConfig { Seed = 7, BiomeSequence = null, FogEnabled = false, Difficulty = 0 };

            var result = RoundTrip(source);

            Assert.IsNotNull(result.BiomeSequence);
            Assert.AreEqual(0, result.BiomeSequence.Length);
            Assert.AreEqual(7, result.Seed);
        }

        private static RunConfig RoundTrip(RunConfig source)
        {
            using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
            writer.WriteNetworkSerializable(source);

            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out RunConfig result);
            return result;
        }
    }
}
