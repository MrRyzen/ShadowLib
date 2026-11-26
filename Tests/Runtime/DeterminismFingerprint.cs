using System;
using System.Collections.Generic;
using ShadowLib.Procedural;
using ShadowLib.RNG;
using ShadowLib.RNG.Distributions;
using ShadowLib.RNG.Noise;
using ShadowLib.RNG.Sources;
using ShadowLib.RNG.Utilities;
using ShadowLib.Spatial;
using ShadowLib.Spatial.Pathfinding;

namespace ShadowLib.DeterminismCheck
{
    /// <summary>
    /// Hashes long runs of draws and generated content. Run on two runtimes and compare the numbers:
    /// equal hashes mean bit-identical output. Kept free of Unity and NUnit so the same file compiles anywhere.
    /// </summary>
    internal static class DeterminismFingerprint
    {
        internal const int Draws = 1_000_000;
        private const ulong RootSeed = 0xC0FFEE1234ABCDEFUL;

        internal static ulong Start => 14695981039346656037UL;

        internal static ulong Mix(ulong hash, ulong value)
        {
            hash = (hash ^ (value & 0xFFFFFFFFUL)) * 1099511628211UL;
            return (hash ^ (value >> 32)) * 1099511628211UL;
        }

        private static ulong Bits(float value) => (uint)BitConverter.SingleToInt32Bits(value);

        private static ulong Bits(double value) => (ulong)BitConverter.DoubleToInt64Bits(value);

        private static ulong Text(ulong hash, string text)
        {
            foreach (char c in text)
                hash = Mix(hash, c);
            return Mix(hash, 0xFFFF);
        }

        internal static List<KeyValuePair<string, ulong>> Compute()
        {
            var rows = new List<KeyValuePair<string, ulong>>();
            void Add(string key, ulong hash) => rows.Add(new KeyValuePair<string, ulong>(key, hash));

            ulong hash;

            var system = new System.Random(12345);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, (ulong)system.Next());
            Add("System.Random(12345).Next()", hash);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, Bits(system.NextDouble()));
            Add("System.Random(12345).NextDouble()", hash);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, (ulong)system.Next(0, 100));
            Add("System.Random(12345).Next(0, 100)", hash);

            var rng = new Xoshiro128StarStar(12345UL);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, rng.NextUInt());
            Add("ShadowLib NextUInt", hash);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, rng.NextULong());
            Add("ShadowLib NextULong", hash);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, Bits(rng.NextFloat()));
            Add("ShadowLib NextFloat", hash);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, Bits(rng.NextDouble()));
            Add("ShadowLib NextDouble", hash);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, (ulong)(long)rng.Range(-1000, 1000 + i % 977));
            Add("ShadowLib Range(int)", hash);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, Bits(rng.Range(i % 91 * 0.5f, 1000f + i % 97 * 2f)));
            Add("ShadowLib Range(float)", hash);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, Bits(rng.Range(i % 91 * 0.5, 1000.0 + i % 97 * 2.0)));
            Add("ShadowLib Range(double)", hash);
            hash = Start;
            for (int i = 0; i < Draws; i++) hash = Mix(hash, rng.Range(5UL, 5UL + (ulong)(i % 1000 + 1) * 7919UL));
            Add("ShadowLib Range(ulong)", hash);

            hash = Start;
            foreach (string name in new[] { "world-1", "loot/chest/3", "x", "Städte-世界-🎲", "a very long context string that goes on and on / 0123456789" })
            {
                hash = Mix(hash, Seeding.GenerateSeed64(name));
                hash = Mix(hash, Seeding.GenerateSeed(name));
                hash = Mix(hash, Seeding.DeriveSeed64(RootSeed, name));
                hash = Mix(hash, Seeding.DeriveSeed(0xABCD1234u, name));
            }
            for (ulong index = 0; index < 1000; index++)
                hash = Mix(hash, Seeding.DeriveSeed64(RootSeed, index));
            Add("Seeding: string and index seed derivation", hash);

            hash = Start;
            for (ulong context = 0; context < 200; context++)
                hash = Text(hash, UUID.GenerateDeterministicUUID(RootSeed, context).ToString());
            Add("UUID.GenerateDeterministicUUID", hash);

            var orchestrator = new Orchestrator(RootSeed);
            hash = Start;
            foreach (string context in new[] { "loot", "terrain", "npc/names", "weather" })
            {
                var stream = orchestrator.CreateRNG(context);
                for (int i = 0; i < 2000; i++) hash = Mix(hash, stream.NextUInt());
                var derived = orchestrator.Derive("chunk/" + context);
                for (int i = 0; i < 2000; i++) hash = Mix(hash, derived.NextUInt());
                for (int n = 0; n < 5; n++)
                {
                    var next = orchestrator.Next(context);
                    for (int i = 0; i < 200; i++) hash = Mix(hash, next.NextUInt());
                }
                hash = Mix(hash, orchestrator.Fork(context).CreateRNG("child").NextULong());
            }
            Add("Orchestrator: CreateRNG / Derive / Next / Fork streams", hash);
            hash = Start;
            foreach (byte b in orchestrator.Save()) hash = Mix(hash, b);
            Add("Orchestrator.Save() bytes", hash);

            var tableRng = orchestrator.CreateRNG("tables");
            var dynamicTable = new DynamicWeightTable<int>();
            var items = new int[24];
            var weights = new float[24];
            for (int i = 0; i < 24; i++)
            {
                items[i] = i;
                weights[i] = 0.1f + i * 0.37f;
                dynamicTable.Add(i, weights[i]);
            }
            hash = Start;
            for (int i = 0; i < 100_000; i++) hash = Mix(hash, (ulong)dynamicTable.Sample(tableRng));
            Add("DynamicWeightTable: plain picks", hash);

            float pressure = 0f;
            for (int i = 0; i < 24; i += 3)
                dynamicTable.SetInfluence(i, 0.01f, 9.5f, () => pressure);
            dynamicTable.PreventRepeat = true;
            dynamicTable.SetStock(5, 300);
            hash = Start;
            for (int i = 0; i < 100_000; i++)
            {
                pressure = (i % 201 - 100) / 100f;
                hash = Mix(hash, (ulong)dynamicTable.Sample(tableRng));
            }
            Add("DynamicWeightTable: influence + stock + repeat prevention", hash);

            var aliasTable = new AliasTable<int>(items, weights);
            hash = Start;
            for (int i = 0; i < 100_000; i++) hash = Mix(hash, (ulong)aliasTable.Sample(tableRng));
            Add("AliasTable picks", hash);

            var bag = new RandomBag<int>(items, tableRng);
            hash = Start;
            for (int i = 0; i < 5000; i++) hash = Mix(hash, (ulong)bag.Sample());
            var shuffled = new int[5000];
            for (int i = 0; i < shuffled.Length; i++) shuffled[i] = i;
            RandomUtils.Shuffle(shuffled, tableRng);
            foreach (int value in shuffled) hash = Mix(hash, (ulong)value);
            Add("RandomBag draws and RandomUtils.Shuffle", hash);

            var chain = new MarkovChain<string>();
            string[] states = { "Sunny", "Cloudy", "Rainy", "Storm" };
            for (int from = 0; from < 4; from++)
                for (int to = 0; to < 4; to++)
                    chain.AddTransition(states[from], states[to], 0.15f + ((from * 7 + to * 3) % 5) * 0.21f);
            chain.Build();
            var walkBuffer = new string[2000];
            int walked = chain.Walk("Sunny", tableRng, walkBuffer);
            hash = Start;
            for (int i = 0; i < walked; i++) hash = Text(hash, walkBuffer[i]);
            var words = new MarkovWordGenerator(order: 2);
            words.Train(new[] { "Novartis", "Raytheon", "Blackstone", "Palantir", "Salesforce", "Lockheed", "Theranos", "Northrop", "Medtronic", "Crowdstrike" });
            words.Build();
            for (int i = 0; i < 300; i++) hash = Text(hash, words.Generate(tableRng, minLength: 4, maxLength: 10));
            Add("MarkovChain walk and MarkovWordGenerator names", hash);

            var noise = new SimplexNoise(orchestrator.CreateRNG("noise"));
            hash = Start;
            for (int i = 0; i < 100_000; i++) hash = Mix(hash, Bits(noise.Sample(i * 0.015625f - 400f, i % 331 * 0.0625f)));
            Add("SimplexNoise 2D samples (float maths)", hash);
            hash = Start;
            for (int i = 0; i < 100_000; i++) hash = Mix(hash, Bits(noise.Sample(i * 0.015625f - 400f, i % 331 * 0.0625f, i % 53 * 0.25f)));
            Add("SimplexNoise 3D samples (float maths)", hash);
            hash = Start;
            for (int i = 0; i < 50_000; i++) hash = Mix(hash, Bits(noise.Fractal(i * 0.015625f - 300f, i % 331 * 0.0625f, octaves: 5)));
            Add("SimplexNoise fractal (float maths)", hash);

            hash = Start;
            var linear = ScalingCurve.Linear(10f, 2.5f);
            var stepped = ScalingCurve.Stepped((1, 1f), (10, 2.5f), (25, 7f));
            for (int level = 0; level < 500; level++)
            {
                hash = Mix(hash, Bits(linear.Evaluate(level)));
                hash = Mix(hash, Bits(stepped.Evaluate(level)));
            }
            Add("ScalingCurve linear and stepped", hash);
            hash = Start;
            var exponential = ScalingCurve.Exponential(100f, 0.137f);
            var logarithmic = ScalingCurve.Logarithmic(5f, 3.3f);
            for (int level = 0; level < 500; level++)
            {
                hash = Mix(hash, Bits(exponential.Evaluate(level)));
                hash = Mix(hash, Bits(logarithmic.Evaluate(level)));
            }
            Add("ScalingCurve exponential and logarithmic (MathF.Pow / Log)", hash);

            var shapeRng = orchestrator.CreateRNG("shapes");
            hash = Start;
            for (int i = 0; i < 400; i++)
            {
                var flat = Polyomino.Generate(shapeRng, 1 + i % 40, i % 11 / 10f, 9, 7);
                foreach (var (col, row) in flat.Offsets) hash = Mix(hash, (ulong)(col * 31 + row));
            }
            Add("Polyomino.Generate", hash);
            hash = Start;
            for (int i = 0; i < 400; i++)
            {
                var solid = Polycube.Generate(shapeRng, 1 + i % 40, i % 11 / 10f, 6, 5, 4);
                foreach (var (col, row, layer) in solid.Offsets) hash = Mix(hash, (ulong)(col * 961 + row * 31 + layer));
            }
            Add("Polycube.Generate", hash);

            var walkRng = orchestrator.CreateRNG("walks");
            hash = Start;
            for (int i = 0; i < 300; i++)
            {
                var bias = new DirectionBias(0.1f + i % 7 * 0.13f, i % 3 * 0.21f, 0.05f + i % 5 * 0.17f, i % 4 * 0.11f);
                foreach (var (x, y) in RandomWalk.Generate(walkRng, 200, 23, 17, (i % 23, i % 17), bias)) hash = Mix(hash, (ulong)(x * 31 + y));
            }
            Add("RandomWalk.Generate", hash);

            var caveRng = orchestrator.CreateRNG("cave");
            var cave = new FieldGrid<bool>(96, 64, 1f);
            CellularAutomata.Randomize(cave, caveRng, 0.44f);
            new CellularAutomata(5, 4).Step(cave, 5);
            hash = Start;
            var floor = new List<(int X, int Y)>();
            var caveCells = cave.AsReadOnlySpan();
            for (int i = 0; i < caveCells.Length; i++)
            {
                hash = Mix(hash, caveCells[i] ? 1UL : 0UL);
                if (!caveCells[i]) floor.Add((i % 96, i / 96));
            }
            Add("CellularAutomata cave", hash);
            hash = Start;
            for (int i = 0; i < 40; i++)
            {
                var placed = SpacedPlacement.Place(caveRng, floor, 12, 3f + i % 9, (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y));
                foreach (var (x, y) in placed) hash = Mix(hash, (ulong)(x * 131 + y));
            }
            Add("SpacedPlacement on the cave floor", hash);

            var costs = new byte[96 * 64];
            for (int i = 0; i < costs.Length; i++) costs[i] = caveCells[i] ? (byte)0 : (byte)(1 + caveRng.Range(0, 4));
            var scratch = new int[GridAStar.ScratchLength(costs.Length)];
            var path = new int[costs.Length];
            ulong aStarHash = Start, jumpHash = Start;
            for (int i = 0; i < 300; i++)
            {
                var (startX, startY) = floor[caveRng.Range(0, floor.Count)];
                var (goalX, goalY) = floor[caveRng.Range(0, floor.Count)];
                int start = startY * 96 + startX, goal = goalY * 96 + goalX;
                var status = GridAStar.FindPath(costs, 96, 64, start, goal, i % 2 == 0, scratch, path, out int length, out int cost);
                aStarHash = Mix(Mix(aStarHash, (ulong)status), (ulong)cost);
                for (int step = 0; step < length && status == PathStatus.Found; step++) aStarHash = Mix(aStarHash, (ulong)path[step]);
                status = GridJumpPointSearch.FindPath(costs, 96, 64, start, goal, i % 2 == 0, scratch, path, out length, out cost);
                jumpHash = Mix(Mix(jumpHash, (ulong)status), (ulong)cost);
                for (int step = 0; step < length && status == PathStatus.Found; step++) jumpHash = Mix(jumpHash, (ulong)path[step]);
            }
            Add("GridAStar paths on the cave", aStarHash);
            Add("GridJumpPointSearch paths on the cave", jumpHash);
            var distances = new int[costs.Length];
            var directions = new byte[costs.Length];
            var (flowX, flowY) = floor[floor.Count / 2];
            GridFlowField.Build(costs, 96, 64, new[] { flowY * 96 + flowX }, true, new int[GridFlowField.ScratchLength(costs.Length)], distances, directions);
            hash = Start;
            for (int i = 0; i < costs.Length; i++) hash = Mix(Mix(hash, (ulong)distances[i]), directions[i]);
            Add("GridFlowField on the cave", hash);

            return rows;
        }
    }
}
