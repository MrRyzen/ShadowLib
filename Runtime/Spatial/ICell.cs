namespace ShadowLib.Spatial;

/// <summary>
/// Defines a cell in a 2D spatial grid that can hold an occupant of type <typeparamref name="TOccupant"/>.
/// </summary>
/// <typeparam name="TOccupant">The type of the occupant data.</typeparam>
public interface ICell<TOccupant>
{
    /// <summary>
    /// Gets the X coordinate of this cell within the grid.
    /// </summary>
    int X { get; }

    /// <summary>
    /// Gets the Y coordinate of this cell within the grid.
    /// </summary>
    int Y { get; }

    /// <summary>
    /// Gets or sets the occupant of the cell.
    /// This can be any data relevant to the occupant, such as an entity reference or item information.
    /// </summary>
    TOccupant Occupant { get; set; }

    /// <summary>
    /// Gets a value indicating whether this cell currently holds an occupant.
    /// </summary>
    bool IsOccupied { get; }

    /// <summary>
    /// Initializes the cell's grid coordinates. Called once by the owning grid
    /// immediately after the cell is created by the factory delegate.
    /// </summary>
    /// <param name="x">The X coordinate within the grid.</param>
    /// <param name="y">The Y coordinate within the grid.</param>
    void Initialize(int x, int y);

    /// <summary>
    /// Determines whether this cell's occupant equals <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The item to check against the current occupant.</param>
    /// <returns><see langword="true"/> if the cell contains <paramref name="item"/>; otherwise <see langword="false"/>.</returns>
    bool Contains(TOccupant item);

    /// <summary>
    /// Removes the current occupant and marks the cell as unoccupied.
    /// </summary>
    void Clear();

    /// <summary>
    /// Sets <paramref name="item"/> as the occupant and marks the cell as occupied.
    /// </summary>
    /// <param name="item">The item to place in this cell.</param>
    void Occupy(TOccupant item);
}
