namespace ShadowLib.RNG.Utilities
{
    using System;
    using System.Buffers.Binary;
    using System.Security.Cryptography;

    /// <summary>
    /// Utility class for generating random and deterministic UUIDs.
    /// Deterministic UUIDs follow RFC 4122 UUIDv5 (SHA-1, name-based).
    /// </summary>
    public static class UUID
    {
        /// <summary>
        /// ShadowLib-specific namespace UUID.
        /// This MUST remain constant forever once shipped.
        /// </summary>
        private static readonly Guid ShadowLibNamespace =
            Guid.Parse("a3c3c1c9-8f8a-4f89-9c1e-1c9c3c7b5e42");

        /// <summary>
        /// Generates a random UUID (version 4).
        /// </summary>
        public static Guid GenerateRandomUUID()
        {
            return Guid.NewGuid();
        }

        /// <summary>
        /// Generates a deterministic UUID (version 5) from a seed and context.
        /// </summary>
        /// <param name="seed">Derived seed (e.g. lootSeed).</param>
        /// <param name="context">Context value (e.g. RNG.NextULong()).</param>
        public static Guid GenerateDeterministicUUID(ulong seed, ulong context)
        {
            // Hash input = namespace (network order) + seed + context, both little-endian on every platform.
            Span<byte> input = stackalloc byte[32];
            NamespaceNetworkOrder.CopyTo(input);
            BinaryPrimitives.WriteUInt64LittleEndian(input.Slice(16, 8), seed);
            BinaryPrimitives.WriteUInt64LittleEndian(input.Slice(24, 8), context);

            Span<byte> hash = stackalloc byte[20];
            (_sha1 ??= SHA1.Create()).TryComputeHash(input, hash, out _);

            Span<byte> guidBytes = hash.Slice(0, 16);
            // Set version (5)
            guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
            // Set variant (RFC 4122)
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

            SwapGuidByteOrder(guidBytes);

            return new Guid(guidBytes);
        }

        [ThreadStatic]
        private static SHA1? _sha1;

        private static readonly byte[] NamespaceNetworkOrder = ToNetworkOrder(ShadowLibNamespace);

        private static byte[] ToNetworkOrder(Guid guid)
        {
            byte[] bytes = guid.ToByteArray();
            SwapGuidByteOrder(bytes);
            return bytes;
        }

        /// <summary>
        /// Swaps byte order between RFC 4122 network order and .NET Guid order.
        /// </summary>
        private static void SwapGuidByteOrder(Span<byte> guid)
        {
            (guid[0], guid[3]) = (guid[3], guid[0]);
            (guid[1], guid[2]) = (guid[2], guid[1]);
            (guid[4], guid[5]) = (guid[5], guid[4]);
            (guid[6], guid[7]) = (guid[7], guid[6]);
        }
    }
}
