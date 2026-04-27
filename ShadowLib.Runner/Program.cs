namespace ShadowLib.Runner;

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
        Orchestrator.Initialize(rootSeed);
        var rng = Orchestrator.CreateRNG("loot-sample");

        Item[] drops = new Item[6];
        var tier1 = loot.Sample(rng, 1, 2);
        var tier2 = loot.Sample(rng, 2, 2);
        var tier3 = loot.Sample(rng, 3, 2);

        drops = tier1.Concat(tier2).Concat(tier3).ToArray();

        foreach (var item in drops)
        {
            Console.WriteLine(item);
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