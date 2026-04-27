namespace ShadowLib.RNG.Noise;

using System;
using System.Runtime.CompilerServices;
using ShadowLib.RNG.Sources;

/// <summary>
/// Optimized simplex noise supporting 2D and 3D sampling.
/// Permutation table is shuffled via an <see cref="IRandom"/> instance,
/// making it fully deterministic and seedable.
/// </summary>
public sealed class SimplexNoise
{
    // Permutation table (doubled to avoid index wrapping)
    private readonly byte[] _perm = new byte[512];
    private readonly byte[] _permMod12 = new byte[512];

    // Skew / unskew constants – 2D
    private const float F2 = 0.3660254037844386f;  // (sqrt(3) - 1) / 2
    private const float G2 = 0.21132486540518713f;  // (3 - sqrt(3)) / 6

    // Skew / unskew constants – 3D
    private const float F3 = 1f / 3f;
    private const float G3 = 1f / 6f;

    // 2D gradients (12 directions, unit-length on axes/diagonals)
    private static readonly float[] Grad2X = { 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0 };
    private static readonly float[] Grad2Y = { 0, 0, 0, 0, 1, 1, -1, -1, 1, -1, 1, -1 };

    // 3D gradients (midpoints of edges of a cube – 12 directions)
    private static readonly float[] Grad3X = { 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0 };
    private static readonly float[] Grad3Y = { 1, 1, -1, -1, 0, 0, 0, 0, 1, -1, 1, -1 };
    private static readonly float[] Grad3Z = { 0, 0, 0, 0, 1, 1, -1, -1, 1, 1, -1, -1 };

    /// <summary>
    /// Create simplex noise with a specific RNG instance.
    /// The RNG is consumed only during construction (Fisher-Yates shuffle of the permutation table).
    /// </summary>
    public SimplexNoise(IRandom rng)
    {
        if (rng == null) throw new ArgumentNullException(nameof(rng));
        InitPermutation(rng);
    }

    private void InitPermutation(IRandom rng)
    {
        // Identity permutation
        for (int i = 0; i < 256; i++)
            _perm[i] = (byte)i;

        // Fisher-Yates shuffle using the provided IRandom
        for (int i = 255; i > 0; i--)
        {
            // Use NextUInt to avoid the Range method's max>min check overhead
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            byte tmp = _perm[i];
            _perm[i] = _perm[j];
            _perm[j] = tmp;
        }

        // Double the table & pre-compute mod12
        for (int i = 0; i < 256; i++)
        {
            _perm[256 + i] = _perm[i];
            _permMod12[i] = (byte)(_perm[i] % 12);
            _permMod12[256 + i] = _permMod12[i];
        }
    }

    /// <summary>
    /// Sample 2D simplex noise at (x, y).
    /// Returns a value in approximately [-1, 1].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Sample(float x, float y)
    {
        float n0, n1, n2;

        // Skew input space to determine which simplex cell we're in
        float s = (x + y) * F2;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);

        float t = (i + j) * G2;
        float x0 = x - (i - t);  // Unskewed distances from cell origin
        float y0 = y - (j - t);

        // Determine which simplex (triangle) we are in
        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; }  // Lower triangle
        else { i1 = 0; j1 = 1; }            // Upper triangle

        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2;
        float y2 = y0 - 1f + 2f * G2;

        // Hash coordinates to gradient indices
        int ii = i & 255;
        int jj = j & 255;

        // Corner contributions
        float t0 = 0.5f - x0 * x0 - y0 * y0;
        if (t0 < 0f)
        {
            n0 = 0f;
        }
        else
        {
            t0 *= t0;
            int gi0 = _permMod12[ii + _perm[jj]];
            n0 = t0 * t0 * (Grad2X[gi0] * x0 + Grad2Y[gi0] * y0);
        }

        float t1 = 0.5f - x1 * x1 - y1 * y1;
        if (t1 < 0f)
        {
            n1 = 0f;
        }
        else
        {
            t1 *= t1;
            int gi1 = _permMod12[ii + i1 + _perm[jj + j1]];
            n1 = t1 * t1 * (Grad2X[gi1] * x1 + Grad2Y[gi1] * y1);
        }

        float t2 = 0.5f - x2 * x2 - y2 * y2;
        if (t2 < 0f)
        {
            n2 = 0f;
        }
        else
        {
            t2 *= t2;
            int gi2 = _permMod12[ii + 1 + _perm[jj + 1]];
            n2 = t2 * t2 * (Grad2X[gi2] * x2 + Grad2Y[gi2] * y2);
        }

        // Scale to [-1, 1]
        return 70f * (n0 + n1 + n2);
    }

    /// <summary>
    /// Sample 3D simplex noise at (x, y, z).
    /// Returns a value in approximately [-1, 1].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Sample(float x, float y, float z)
    {
        float n0, n1, n2, n3;

        // Skew input space
        float s = (x + y + z) * F3;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);
        int k = FastFloor(z + s);

        float t = (i + j + k) * G3;
        float x0 = x - (i - t);
        float y0 = y - (j - t);
        float z0 = z - (k - t);

        // Determine which simplex (tetrahedron) we are in
        int i1, j1, k1, i2, j2, k2;

        if (x0 >= y0)
        {
            if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }      // XYZ
            else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }  // XZY
            else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }                 // ZXY
        }
        else
        {
            if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }        // ZYX
            else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }   // YZX
            else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }                 // YXZ
        }

        float x1 = x0 - i1 + G3;
        float y1 = y0 - j1 + G3;
        float z1 = z0 - k1 + G3;
        float x2 = x0 - i2 + 2f * G3;
        float y2 = y0 - j2 + 2f * G3;
        float z2 = z0 - k2 + 2f * G3;
        float x3 = x0 - 1f + 3f * G3;
        float y3 = y0 - 1f + 3f * G3;
        float z3 = z0 - 1f + 3f * G3;

        int ii = i & 255;
        int jj = j & 255;
        int kk = k & 255;

        // Corner 0
        float t0 = 0.6f - x0 * x0 - y0 * y0 - z0 * z0;
        if (t0 < 0f)
        {
            n0 = 0f;
        }
        else
        {
            t0 *= t0;
            int gi0 = _permMod12[ii + _perm[jj + _perm[kk]]];
            n0 = t0 * t0 * (Grad3X[gi0] * x0 + Grad3Y[gi0] * y0 + Grad3Z[gi0] * z0);
        }

        // Corner 1
        float t1 = 0.6f - x1 * x1 - y1 * y1 - z1 * z1;
        if (t1 < 0f)
        {
            n1 = 0f;
        }
        else
        {
            t1 *= t1;
            int gi1 = _permMod12[ii + i1 + _perm[jj + j1 + _perm[kk + k1]]];
            n1 = t1 * t1 * (Grad3X[gi1] * x1 + Grad3Y[gi1] * y1 + Grad3Z[gi1] * z1);
        }

        // Corner 2
        float t2 = 0.6f - x2 * x2 - y2 * y2 - z2 * z2;
        if (t2 < 0f)
        {
            n2 = 0f;
        }
        else
        {
            t2 *= t2;
            int gi2 = _permMod12[ii + i2 + _perm[jj + j2 + _perm[kk + k2]]];
            n2 = t2 * t2 * (Grad3X[gi2] * x2 + Grad3Y[gi2] * y2 + Grad3Z[gi2] * z2);
        }

        // Corner 3
        float t3 = 0.6f - x3 * x3 - y3 * y3 - z3 * z3;
        if (t3 < 0f)
        {
            n3 = 0f;
        }
        else
        {
            t3 *= t3;
            int gi3 = _permMod12[ii + 1 + _perm[jj + 1 + _perm[kk + 1]]];
            n3 = t3 * t3 * (Grad3X[gi3] * x3 + Grad3Y[gi3] * y3 + Grad3Z[gi3] * z3);
        }

        // Scale to [-1, 1]
        return 32f * (n0 + n1 + n2 + n3);
    }

    /// <summary>
    /// Sample fractal Brownian motion (fBm) by layering octaves of 2D simplex noise.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="octaves">Number of octaves (layers). More octaves = more detail.</param>
    /// <param name="persistence">Amplitude multiplier per octave. Typically 0.5.</param>
    /// <param name="lacunarity">Frequency multiplier per octave. Typically 2.0.</param>
    /// <returns>Noise value, roughly in [-1, 1] but may exceed slightly with many octaves.</returns>
    public float Fractal(float x, float y, int octaves, float persistence = 0.5f, float lacunarity = 2f)
    {
        float sum = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxAmplitude = 0f;

        for (int o = 0; o < octaves; o++)
        {
            sum += amplitude * Sample(x * frequency, y * frequency);
            maxAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return sum / maxAmplitude;
    }

    /// <summary>
    /// Sample fractal Brownian motion (fBm) by layering octaves of 3D simplex noise.
    /// </summary>
    public float Fractal(float x, float y, float z, int octaves, float persistence = 0.5f, float lacunarity = 2f)
    {
        float sum = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxAmplitude = 0f;

        for (int o = 0; o < octaves; o++)
        {
            sum += amplitude * Sample(x * frequency, y * frequency, z * frequency);
            maxAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return sum / maxAmplitude;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FastFloor(float x)
    {
        int xi = (int)x;
        return x < xi ? xi - 1 : xi;
    }
}
