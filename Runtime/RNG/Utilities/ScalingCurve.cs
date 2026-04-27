using System;

namespace ShadowLib.RNG.Utilities;

/// <summary>
/// A zero-alloc (on evaluate) curve for scaling values based on level/progression.
/// </summary>
public readonly struct Curve
{
    private enum CurveType : byte
    {
        Linear,
        Exponential,
        Logarithmic,
        Stepped
    }

    private readonly CurveType _type;
    private readonly float _start;
    private readonly float _rate;
    private readonly float _cap;
    private readonly bool _hasCap;
    private readonly (int levelThreshold, float value)[] _steps;

    private Curve(CurveType type, float start, float rate, float cap, bool hasCap)
    {
        _type = type;
        _start = start;
        _rate = rate;
        _cap = cap;
        _hasCap = hasCap;
        _steps = Array.Empty<(int, float)>();
    }

    private Curve((int levelThreshold, float value)[] steps)
    {
        _type = CurveType.Stepped;
        _start = 0f;
        _rate = 0f;
        _cap = 0f;
        _hasCap = false;
        _steps = steps;
    }

    internal static Curve CreateLinear(float start, float rate, float cap, bool hasCap)
        => new(CurveType.Linear, start, rate, cap, hasCap);

    internal static Curve CreateExponential(float start, float rate, float cap, bool hasCap)
        => new(CurveType.Exponential, start, rate, cap, hasCap);

    internal static Curve CreateLogarithmic(float start, float rate, float cap, bool hasCap)
        => new(CurveType.Logarithmic, start, rate, cap, hasCap);

    internal static Curve CreateStepped((int levelThreshold, float value)[] steps)
        => new(steps);

    /// <summary>
    /// Evaluates the curve at the given level.
    /// </summary>
    public float Evaluate(int level)
    {
        float raw = _type switch
        {
            CurveType.Linear => _start + _rate * level,
            CurveType.Exponential => _start * MathF.Pow(1f + _rate, level),
            CurveType.Logarithmic => _start + _rate * MathF.Log(level + 1),
            CurveType.Stepped => EvaluateStepped(level),
            _ => throw new InvalidOperationException($"Unknown curve type: {_type}")
        };

        return _hasCap ? Math.Min(raw, _cap) : raw;
    }

    private float EvaluateStepped(int level)
    {
        // Steps are expected sorted by levelThreshold ascending.
        // Returns the value of the first step whose threshold the level hasn't reached.
        var steps = _steps;
        for (int i = 0; i < steps.Length; i++)
        {
            if (level < steps[i].levelThreshold)
                return steps[i].value;
        }

        // Past all thresholds — return last step's value.
        return steps.Length > 0 ? steps[^1].value : 0f;
    }
}

/// <summary>
/// Factory methods for creating scaling curves.
/// </summary>
public static class ScalingCurve
{
    /// <summary>Creates a linear curve: start + rate * level.</summary>
    public static Curve Linear(float start, float rate)
        => Curve.CreateLinear(start, rate, 0f, false);

    /// <summary>Creates a linear curve clamped to a maximum value.</summary>
    public static Curve LinearClamped(float start, float rate, float cap)
        => Curve.CreateLinear(start, rate, cap, true);

    /// <summary>Creates an exponential curve: start * (1 + rate) ^ level.</summary>
    public static Curve Exponential(float start, float rate)
        => Curve.CreateExponential(start, rate, 0f, false);

    /// <summary>Creates an exponential curve clamped to a maximum value.</summary>
    public static Curve Exponential(float start, float rate, float cap)
        => Curve.CreateExponential(start, rate, cap, true);

    /// <summary>Creates a logarithmic curve: start + rate * ln(level + 1).</summary>
    public static Curve Logarithmic(float start, float rate)
        => Curve.CreateLogarithmic(start, rate, 0f, false);

    /// <summary>Creates a logarithmic curve clamped to a maximum value.</summary>
    public static Curve Logarithmic(float start, float rate, float cap)
        => Curve.CreateLogarithmic(start, rate, cap, true);

    /// <summary>Creates a stepped curve that returns discrete values based on level thresholds.</summary>
    public static Curve Stepped(params (int levelThreshold, float value)[] steps)
        => Curve.CreateStepped(steps);
}
