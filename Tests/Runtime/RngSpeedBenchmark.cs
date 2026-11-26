using System;
using System.Diagnostics;
using NUnit.Framework;
using ShadowLib.RNG.Distributions;
using ShadowLib.RNG.Sources;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class RngSpeedBenchmark
    {
        private const int Picks = 5_000_000;
        private const int Draws = 20_000_000;
        private const int Rounds = 4;

        private static long _sink;

        private static double BestNanoseconds(int calls, Func<long> body)
        {
            double best = double.MaxValue;
            for (int round = 0; round < Rounds; round++)
            {
                var watch = Stopwatch.StartNew();
                _sink += body();
                watch.Stop();
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds * 1_000_000.0 / calls);
            }

            return best;
        }

        private static DynamicWeightTable<int> Table(int count)
        {
            var table = new DynamicWeightTable<int>();
            for (int i = 0; i < count; i++)
                table.Add(i, i % 10 + 1);
            return table;
        }

        [Test, Explicit("Prints timings; asserts nothing.")]
        public void PrintTimings()
        {
            var rng = new Xoshiro128StarStar(777UL);
            IRandom viaInterface = rng;
            var core = new Xoshiro128(777UL);

            var plain10 = Table(10);
            var plain100 = Table(100);
            var influenced = Table(10);
            float pressure = 0.25f;
            influenced.SetInfluence(3, 0.1f, 20f, () => pressure);
            var repeating = Table(10);
            repeating.PreventRepeat = true;
            var stocked = Table(10);
            stocked.SetStock(4, int.MaxValue);
            var items = new int[10];
            var weights = new float[10];
            for (int i = 0; i < 10; i++)
            {
                items[i] = i;
                weights[i] = i + 1;
            }
            var alias = new AliasTable<int>(items, weights);

            void Print(string label, double nanoseconds) => TestContext.Out.WriteLine($"{label,-58} {nanoseconds,8:F2} ns");

            Print("DynamicWeightTable.Sample, 10 plain items", BestNanoseconds(Picks, () => { long s = 0; for (int i = 0; i < Picks; i++) s += plain10.Sample(rng); return s; }));
            Print("DynamicWeightTable.Sample, 100 plain items", BestNanoseconds(Picks / 5, () => { long s = 0; for (int i = 0; i < Picks / 5; i++) s += plain100.Sample(rng); return s; }));
            Print("DynamicWeightTable.Sample, 10 items, one influenced", BestNanoseconds(Picks, () => { long s = 0; for (int i = 0; i < Picks; i++) s += influenced.Sample(rng); return s; }));
            Print("DynamicWeightTable.Sample, 10 items, PreventRepeat", BestNanoseconds(Picks, () => { long s = 0; for (int i = 0; i < Picks; i++) s += repeating.Sample(rng); return s; }));
            Print("DynamicWeightTable.Sample, 10 items, one stocked", BestNanoseconds(Picks, () => { long s = 0; for (int i = 0; i < Picks; i++) s += stocked.Sample(rng); return s; }));
            Print("AliasTable.Sample, 10 items", BestNanoseconds(Picks, () => { long s = 0; for (int i = 0; i < Picks; i++) s += alias.Sample(rng); return s; }));
            Print("Hand-written cumulative loop, 10 items", BestNanoseconds(Picks, () =>
            {
                long s = 0;
                for (int i = 0; i < Picks; i++)
                {
                    float target = rng.NextFloat() * 55f;
                    float running = 0f;
                    int picked = 9;
                    for (int k = 0; k < 10; k++)
                    {
                        running += weights[k];
                        if (running >= target) { picked = k; break; }
                    }
                    s += picked;
                }
                return s;
            }));

            Print("NextUInt (class)", BestNanoseconds(Draws, () => { long s = 0; for (int i = 0; i < Draws; i++) s += rng.NextUInt(); return s; }));
            Print("NextFloat (class)", BestNanoseconds(Draws, () => { long s = 0; for (int i = 0; i < Draws; i++) s += (long)(rng.NextFloat() * 1000f); return s; }));
            Print("NextFloat (through IRandom)", BestNanoseconds(Draws, () => { long s = 0; for (int i = 0; i < Draws; i++) s += (long)(viaInterface.NextFloat() * 1000f); return s; }));
            Print("NextFloat (struct)", BestNanoseconds(Draws, () => { long s = 0; for (int i = 0; i < Draws; i++) s += (long)(core.NextFloat() * 1000f); return s; }));
            Print("NextDouble (class)", BestNanoseconds(Draws, () => { long s = 0; for (int i = 0; i < Draws; i++) s += (long)(rng.NextDouble() * 1000.0); return s; }));
            Print("Range(float) (class)", BestNanoseconds(Draws, () => { long s = 0; for (int i = 0; i < Draws; i++) s += (long)rng.Range(0.5f, 1000f); return s; }));
            Print("Range(int) (class)", BestNanoseconds(Draws, () => { long s = 0; for (int i = 0; i < Draws; i++) s += rng.Range(0, 100); return s; }));
        }
    }
}
