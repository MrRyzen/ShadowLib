using System;

namespace ShadowLib.RNG.Modifiers;

/// <summary>
/// Defines the type of operation to perform when applying a weight modifier.
/// </summary>
public enum ModifierOperation
{
    /// <summary>
    /// Adds the value to the existing weight. For example, if the current weight is 10 and the modifier value is 5, the new weight will be 15.
    /// </summary>
    Add,
    /// <summary>
    /// Multiplies the existing weight by the value. For example, if the current weight is 10 and the modifier value is 2, the new weight will be 20.
    /// </summary>
    Multiply,
    /// <summary>
    /// Sets the weight to the specified value, replacing the existing weight.
    /// </summary>
    Set
}

/// <summary>
/// Represents a single weight modifier with condition and operation.
/// </summary>
public struct WeightModifier<T> : IComparable<WeightModifier<T>>
{
    /// <summary>
    /// A unique identifier for this modifier. This can be used to reference the modifier when applying or removing it from a distribution.
    /// </summary>
    public string Id { get; set; }
    /// <summary>
    /// A condition that must be met for this modifier to be applied. This can be an expression that references values in the context. For example, "PlayerLevel > 10" or "Environment == 'Dungeon'".
    /// </summary>
    public string Condition { get; set; }
    /// <summary>
    /// The target item that this modifier applies to. This could be an identifier for a specific item, category, or any other relevant target depending on the use case.
    /// </summary>
    public T Target { get; set; }
    /// <summary>
    /// The type of operation to perform when applying this modifier (Add, Multiply, Set).
    /// </summary>
    public ModifierOperation Operation { get; set; }
    /// <summary>
    /// The value to use in the operation when applying this modifier. The interpretation of this value depends on the specified operation. For Add, it will be added to the existing weight; for Multiply, it will be multiplied with the existing weight; for Set, it will replace the existing weight.
    /// </summary>
    public float Value { get; set; }
    /// <summary>
    /// The stage at which this modifier should be applied. This allows for ordering modifiers when multiple modifiers are applied to the same distribution. Modifiers with lower stage values will be applied before those with higher stage values. For example, you might want to apply certain global modifiers at stage 0, and then more specific item modifiers at stage 1, and finally temporary buffs/debuffs at stage 2. The default value is 999, which means it will be applied after any modifiers with a specified stage less than 999.
    /// </summary>
    public int Stage { get; set; } = 999; // Default to last stage if not specified

    /// <summary>
    /// Initializes a new instance of the WeightModifier struct with the specified parameters.
    /// </summary> <param name="id">A unique identifier for this modifier.</param>
    /// <param name="condition">A condition that must be met for this modifier to be applied.</param>
    /// <param name="target">The target item that this modifier applies to.</param>
    /// <param name="operation">The type of operation to perform when applying this modifier (Add, Multiply, Set).</param>
    /// <param name="value">The value to use in the operation when applying this modifier.</param>
    /// <param name="stage">The stage at which this modifier should be applied. Modifiers with lower stage values will be applied before those with higher stage values. The default value is 999, which means it will be applied after any modifiers with a specified stage less than 999.</param>
    /// <remarks>
    /// This constructor allows you to create a new WeightModifier with all necessary information. The Id should be unique to identify this modifier. The Condition can be an expression that references values in the context, and it will be evaluated to determine if the modifier should be applied. The Target specifies what this modifier applies to, and the Operation and Value define how the modifier affects the weight. The Stage allows you to control the order of application when multiple modifiers are present.
    /// </remarks>
    public WeightModifier(string id, string condition, T target, ModifierOperation operation, float value, int stage = 999)
    {
        Id = id;
        Condition = condition;
        Target = target;
        Operation = operation;
        Value = value;
        Stage = stage;
    }

    /// <summary>
    /// Compares this WeightModifier to another based on the Stage and Id. This is used for sorting modifiers to ensure they are applied in the correct order. Modifiers with lower stage values will be applied before those with higher stage values. If two modifiers have the same stage, they will be sorted by their Id to ensure a consistent order.
    /// </summary>
    /// <param name="other">The other WeightModifier to compare to.</param>
    /// <returns>A value indicating the relative order of the WeightModifiers being compared.</returns>
    public readonly int CompareTo(WeightModifier<T> other)
    {
        // Sort by stage first, then by Id to ensure consistent ordering
        int stageComparison = Stage.CompareTo(other.Stage);
        if (stageComparison != 0)
            return stageComparison;
        return string.Compare(Id, other.Id, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return $"WeightModifier(Id={Id}, Condition={Condition}, Target={Target}, Operation={Operation}, Value={Value}, Stage={Stage})";
    }
}