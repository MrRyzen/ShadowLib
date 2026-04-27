using System.Linq;
using UnityEngine;
using ShadowLib.RNG;
using ShadowLib.RNG.Distributions;
using ShadowLib.RNG.Utilities;
using ShadowLib.Spatial;
using ShadowLib.Procedural;

namespace ShadowLib.Samples
{
    /// <summary>
    /// Minimal end-to-end demo: deterministic seed -> orchestrator -> tiered loot table -> grid placement.
    /// Drop on any GameObject and press Play; output goes to the Console.
    /// </summary>
    public class ShadowLibBasicUsage : MonoBehaviour
    {
        [Tooltip("String seed; same value = identical world every run.")]
        public string seedString = "shadowlib-demo";

        [Tooltip("Width and height of the demo grid.")]
        public int gridWidth = 12;
        public int gridHeight = 8;

        private void Start()
        {
            Orchestrator.Initialize(Seeding.GenerateSeed64(seedString));
            var rng = Orchestrator.CreateRNG("loot");

            var loot = new TieredTable<LootItem>();
            loot.AddTier(1, new (LootItem, float)[]
            {
                (new LootItem("Common Sword",  10, 1, 2), 20f),
                (new LootItem("Common Vest",   15, 2, 2), 20f),
                (new LootItem("Common Potion",  5, 1, 1), 25f),
            });
            loot.AddTier(2, new (LootItem, float)[]
            {
                (new LootItem("Rare Cleaver",  30, 1, 3),  5f),
                (new LootItem("Rare Plate",    35, 3, 3), 10f),
            });

            var drops = loot.Sample(rng, 1, 3).Concat(loot.Sample(rng, 2, 1)).ToArray();
            foreach (var item in drops)
                Debug.Log($"[ShadowLib] Rolled {item.Name} ({item.Width}x{item.Height})");

            var grid = new Grid2D<DemoCell, LootItem>(
                width: gridWidth,
                height: gridHeight,
                cellSize: 1f,
                cellFactory: (x, y) => new DemoCell()
            );
            var packer = new BinPacker<LootItem>(grid);
            packer.AutoSort(drops.Select(d => (d, Polyomino.FromRect(d.Width, d.Height))).ToArray());

            Debug.Log($"[ShadowLib] Packed {grid.GetOccupiedCells().Count} cells across {drops.Length} items.");
        }
    }

    public readonly struct LootItem : System.IEquatable<LootItem>
    {
        public readonly string Name;
        public readonly int Value;
        public readonly int Width;
        public readonly int Height;

        public LootItem(string name, int value, int width, int height)
        {
            Name = name; Value = value; Width = width; Height = height;
        }

        public bool Equals(LootItem other) => Name == other.Name && Value == other.Value;
        public override bool Equals(object obj) => obj is LootItem other && Equals(other);
        public override int GetHashCode() => System.HashCode.Combine(Name, Value);
    }

    public class DemoCell : ICell<LootItem>
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public LootItem Occupant { get; set; }
        public bool IsOccupied { get; private set; }

        public void Initialize(int x, int y) { X = x; Y = y; }
        public bool Contains(LootItem item) => IsOccupied && Occupant.Equals(item);
        public void Clear() { Occupant = default; IsOccupied = false; }
        public void Occupy(LootItem item) { Occupant = item; IsOccupied = true; }
    }
}
