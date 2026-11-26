namespace ShadowLib.Runner
{
    using ShadowLib.RNG;
    using ShadowLib.RNG.Distributions;
    using ShadowLib.RNG.Utilities;
    using ShadowLib.Spatial;
    using ShadowLib.Procedural;

    public class Program
    {
        public static void Main()
        {
            var linearCurve = ScalingCurve.Linear(0, 1);
            var expCurve = ScalingCurve.Exponential(1, 2);
            var logCurve = ScalingCurve.Logarithmic(1, 0.5f);
            var steppedCurve = ScalingCurve.Stepped(new (int, float)[]
            {
                (1, 10),
                (5, 20),
                (10, 30)
            });

            for (int level = 1; level <= 10; level++)
            {
                Console.WriteLine($"Level {level}: Linear={linearCurve.Evaluate(level)}, Exponential={expCurve.Evaluate(level)}, Logarithmic={logCurve.Evaluate(level)}, Stepped={steppedCurve.Evaluate(level)}");
            }

            TieredTable<Item> loot = new TieredTable<Item>();

            loot.AddTier(1, [
                (new Item("Common Sword", 10, 1, 2), 20f), 
                (new Item("Common Vest", 15, 2, 2), 20f), 
                (new Item("Common Potion", 5, 1, 1), 25f)
            ]);
            loot.AddTier(2, [
                (new Item("Uncommon Blade", 20, 1, 3), 15f), 
                (new Item("Uncommon Chainmail", 25, 3, 3), 15f), 
                (new Item("Uncommon Elixir", 12, 1, 1), 12f)
            ]);
            loot.AddTier(3, [
                (new Item("Rare Void Cleaver", 30, 1, 4), 5f), 
                (new Item("Rare Shadow Plate", 35, 4, 4), 10f)
            ]);
            Console.WriteLine("\nSampling from Tiered Table:");
            var rootSeed = Seeding.GenerateSeed64("test-seed");
            var orchestrator = new Orchestrator(rootSeed);
            var rng = orchestrator.CreateRNG("loot-sample");

            Item[] drops = new Item[6];
            var tier1 = loot.Sample(rng, 1, 2);
            var tier2 = loot.Sample(rng, 2, 2);
            var tier3 = loot.Sample(rng, 3, 2);

            drops = tier1.Concat(tier2).Concat(tier3).ToArray();

            foreach (var item in drops)
            {
                Console.WriteLine(item);
            }

            Console.WriteLine("\nThrowaway streams:");
            Console.WriteLine($"  Derive(\"chunk/3/4\") twice: {orchestrator.Derive("chunk/3/4").Range(0, 1000)}, {orchestrator.Derive("chunk/3/4").Range(0, 1000)} (same stream every call)");
            Console.WriteLine($"  Next(\"chest\") twice:      {orchestrator.Next("chest").Range(0, 1000)}, {orchestrator.Next("chest").Range(0, 1000)} (next in a replayable series)");

            byte[] save = orchestrator.Save();
            int[] beforeLoad = [rng.Range(0, 1000), rng.Range(0, 1000), rng.Range(0, 1000)];
            orchestrator.Load(save);
            int[] afterLoad = [rng.Range(0, 1000), rng.Range(0, 1000), rng.Range(0, 1000)];
            Console.WriteLine($"\nSave/Load ({save.Length} bytes):");
            Console.WriteLine($"  after save: {string.Join(", ", beforeLoad)}");
            Console.WriteLine($"  after load: {string.Join(", ", afterLoad)} (match: {beforeLoad.SequenceEqual(afterLoad)})");

            var hero = new Hero();
            var drain = new Punishment("Drain", 25);
            var wheel = new EffectWheel<Punishment, Hero>();
            wheel.Add(drain, 20f);
            wheel.Add(new Punishment("Lucky Escape", 0), 15f);
            wheel.Add(new Punishment("Scratch", 5), 15f);
            wheel.SetInfluence(drain, min: 5f, max: 60f, () => hero.Corruption);
            wheel.PreventRepeat = true;

            Console.WriteLine("\nEffect wheel (Drain odds follow corruption):");
            foreach (float corruption in new[] { -1f, 0f, 1f })
            {
                hero.Corruption = corruption;
                Console.WriteLine($"  corruption {corruption,2}: Drain {wheel.GetProbability(drain):P0}");
            }

            var wheelRng = orchestrator.CreateRNG("effect-wheel");
            for (int i = 0; i < 5; i++)
            {
                Punishment applied = wheel.SampleAndApply(wheelRng, hero);
                Console.WriteLine($"  spin {i + 1}: {applied.Name} (health {hero.Health})");
            }

            var upgrades = new DynamicWeightTable<string>();
            foreach (string upgrade in new[] { "Haste", "Armor", "Regen", "Crit", "Reach" })
                upgrades.Add(upgrade, 1f);
            var offer = new string[3];
            int offered = upgrades.SampleDistinct(wheelRng, offer);
            Console.WriteLine($"  upgrade offer: {string.Join(", ", offer[..offered])}");

            var shop = new DynamicWeightTable<string>();
            shop.Add("Unique Relic", 1f);
            shop.SetStock("Unique Relic", 1);
            bool first = shop.TrySample(wheelRng, out string? relic);
            bool second = shop.TrySample(wheelRng, out _);
            Console.WriteLine($"  stocked item: first pick {first} ({relic}), second pick {second} (sold out)");

            var pack = new SpatialGraph3D<string>(width: 7, height: 4, layers: 3);
            pack.AddPocket(Polycube.FromBox(4, 4, 3), 0, 0, 0);
            pack.AddPocket(Polycube.FromBox(2, 2, 1), 5, 0, 0);
            pack.Place(0, 0, 0, "Ammo Crate", Polycube.FromBox(2, 2, 2));
            pack.Place(2, 0, 0, "Rifle", Polycube.FromBox(1, 4, 1));
            string[] carried = ["Ammo Crate", "Rifle"];
            var pocketIds = new int[pack.Volume];
            Console.WriteLine($"\n3D backpack: {pack.AliveCount} living cells in {pack.FindPockets(pocketIds)} pocket(s), holding {string.Join(" and ", carried)}");

            var packRng = orchestrator.CreateRNG("backpack");
            int dropChance = 25;
            for (int hit = 1; hit <= 12 && pack.TryKillRandom(packRng, out var dead); hit++)
            {
                string outcome = "it was empty";
                if (dead.WasOccupied && packRng.Range(0, 100) < dropChance)
                {
                    pack.Free(dead.Occupant);
                    outcome = $"{dead.Occupant} falls out";
                }
                else if (dead.WasOccupied)
                {
                    dropChance += 25;
                    outcome = $"{dead.Occupant} hangs on (drop chance now {dropChance}%)";
                }
                Console.WriteLine($"  hit {hit}: cell ({dead.X}, {dead.Y}, {dead.Layer}) dies, {outcome}");
            }
            Console.WriteLine($"  after damage: {pack.AliveCount} living cells in {pack.FindPockets(pocketIds)} pocket(s), still holding {string.Join(" and ", carried.Where(pack.Contains).DefaultIfEmpty("nothing"))}");

            var shapeRng = orchestrator.CreateRNG("shapes");
            foreach (float compactness in new[] { 0.1f, 0.9f })
            {
                var pouch = Polyomino.Generate(shapeRng, cellCount: 10, compactness, maxWidth: 6, maxHeight: 4);
                Console.WriteLine($"\nGenerated polyomino, compactness {compactness} ({pouch.Width}x{pouch.Height}):");
                var rows = new char[pouch.Height][];
                for (int row = 0; row < pouch.Height; row++)
                    rows[row] = new string('.', pouch.Width).ToCharArray();
                foreach (var (col, row) in pouch.Offsets)
                    rows[row][col] = '#';
                foreach (char[] row in rows)
                    Console.WriteLine($"  {new string(row)}");
            }

            var lump = Polycube.Generate(shapeRng, cellCount: 6, compactness: 0.7f, maxWidth: 2, maxHeight: 2, maxDepth: 3);
            var crate = new SpatialGraph3D<string>(width: 2, height: 2, layers: 3);
            crate.AddPocket(Polycube.FromBox(2, 2, 3), 0, 0, 0);
            crate.Place(0, 0, 0, "Lump", lump);
            Console.WriteLine($"Generated polycube: {lump.CellCount} cells, {lump.Width}x{lump.Height}x{lump.Depth}, fits its 2x2x3 crate: {crate.Contains("Lump")}");

            var weather = new MarkovChain<string>();
            weather.AddTransition("Sunny", "Sunny", 0.7f);
            weather.AddTransition("Sunny", "Cloudy", 0.3f);
            weather.AddTransition("Cloudy", "Sunny", 0.4f);
            weather.AddTransition("Cloudy", "Rainy", 0.4f);
            weather.AddTransition("Cloudy", "Cloudy", 0.2f);
            weather.AddTransition("Rainy", "Cloudy", 0.6f);
            weather.AddTransition("Rainy", "Rainy", 0.4f);
            weather.Build();

            var weatherRng = orchestrator.CreateRNG("weather");
            var forecast = new string[7];
            int days = weather.Walk("Sunny", weatherRng, forecast);
            Console.WriteLine($"\nMarkov weather forecast: {string.Join(" -> ", forecast[..days])}");

            var companyGen = new MarkovWordGenerator(order: 2);
            companyGen.Train([
                "Novartis", "Raytheon", "Blackstone", "Palantir", "Salesforce",
                "Lockheed", "Theranos", "Northrop", "Medtronic", "Crowdstrike"
            ]);
            companyGen.Build();

            string[] suffixes = ["Inc.", "Corp.", "LLC", "Group", "Holdings", "Industries", "Systems", "Technologies"];
            var nameRng = orchestrator.CreateRNG("company-names");
            Console.WriteLine("\nMarkov-generated companies:");
            for (int i = 0; i < 5; i++)
            {
                string word = companyGen.Generate(nameRng, minLength: 4, maxLength: 10);
                string company = char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
                Console.WriteLine($"  {company} {suffixes[nameRng.Range(0, suffixes.Length)]}");
            }

            Grid2D<Cell<Item>, Item> grid = new(
                width: rng.Range(5, 50),
                height: rng.Range(5, 50),
                cellSize: 1f,
                cellFactory: (int x, int y) => new Cell<Item>()
            );

            Console.WriteLine($"\nCreated grid of size {grid.Width}x{grid.Height}.");
            // Place some items in the grid using BinPacker
            var packer = new BinPacker<Item>(grid);

            var placed = packer.AutoSort(drops.Select(d => (d, Polyomino.FromRect(d.Width, d.Height))).ToArray());

            // Build a label map: each unique item gets a letter
            var occupants = grid.GetOccupiedCells();
            var labelMap = new Dictionary<Item, char>();
            char label = 'A';
            foreach (var kvp in occupants)
            {
                Console.WriteLine($"  Cell ({kvp.Key}): {kvp.Value}");
                labelMap[kvp.Key] = label++;
            }

            // Print legend
            foreach (var kvp in labelMap)
            {
                Console.WriteLine($"  [{kvp.Value}] {kvp.Key}");
            }
            Console.WriteLine();

            // Print grid top border
            Console.Write("  +");
            for (int x = 0; x < grid.Width; x++) Console.Write("--+");
            Console.WriteLine();

            for (int y = 0; y < grid.Height; y++)
            {
                Console.Write("  |");
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = grid[x, y];
                    if (cell.IsOccupied && labelMap.TryGetValue(cell.Occupant, out char c))
                        Console.Write($" {c}|");
                    else
                        Console.Write("  |");
                }
                Console.WriteLine();

                Console.Write("  +");
                for (int x = 0; x < grid.Width; x++) Console.Write("--+");
                Console.WriteLine();
            }
        }
    }

    public struct Item : IEquatable<Item>
    {
        public string Name { get; }
        public int Value { get; }

        public int Width { get; } = 1;
        public int Height { get; } = 1;

        public Item() {}

        public Item(string name, int value, int width = 1, int height = 1)
        {
            Name = name;
            Value = value;
            Width = width;
            Height = height;
        }

        public bool Equals(Item other) => Name == other.Name && Value == other.Value;

        public override string ToString() => $"{Name} (Value: {Value})";
    }

    public sealed class Hero
    {
        public int Health = 100;
        public float Corruption;
    }

    public sealed class Punishment : IEffect<Hero>, IEquatable<Punishment>
    {
        public string Name { get; }
        public int Damage { get; }

        public Punishment(string name, int damage)
        {
            Name = name;
            Damage = damage;
        }

        public void Apply(Hero target) => target.Health -= Damage;

        public bool Equals(Punishment? other) => other != null && other.Name == Name;

        public override bool Equals(object? obj) => Equals(obj as Punishment);

        public override int GetHashCode() => Name.GetHashCode();
    }

    public class Cell<TOccupant> : ICell<TOccupant>
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public TOccupant Occupant { get; set; }
        public bool IsOccupied { get; private set; }

        public Cell()
        {
            Occupant = default(TOccupant);
        }

        public void Initialize(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Contains(TOccupant item) => EqualityComparer<TOccupant>.Default.Equals(Occupant, item);

        public void Clear()
        {
            Occupant = default(TOccupant);
            IsOccupied = false;
        }

        public void Occupy(TOccupant item)
        {
            Occupant = item;
            IsOccupied = true;
        }
    }
}
