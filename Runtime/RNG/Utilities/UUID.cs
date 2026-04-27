namespace ShadowLib.RNG.Utilities
{
    using System;
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
            // Convert inputs to raw bytes (no strings, no encoding ambiguity)
            byte[] seedBytes = BitConverter.GetBytes(seed);
            byte[] contextBytes = BitConverter.GetBytes(context);

            byte[] nameBytes = new byte[seedBytes.Length + contextBytes.Length];
            Buffer.BlockCopy(seedBytes, 0, nameBytes, 0, seedBytes.Length);
            Buffer.BlockCopy(contextBytes, 0, nameBytes, seedBytes.Length, contextBytes.Length);

            // Namespace UUID → network byte order
            byte[] namespaceBytes = ShadowLibNamespace.ToByteArray();
            SwapGuidByteOrder(namespaceBytes);

            // Hash(namespace + name)
            using var sha1 = SHA1.Create();
            byte[] hashInput = new byte[namespaceBytes.Length + nameBytes.Length];
            Buffer.BlockCopy(namespaceBytes, 0, hashInput, 0, namespaceBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, hashInput, namespaceBytes.Length, nameBytes.Length);

            byte[] hash = sha1.ComputeHash(hashInput);

            // Build UUID from first 16 bytes
            byte[] guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);

            // Set version (5)
            guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
            // Set variant (RFC 4122)
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

            // Convert back to .NET Guid byte order
            SwapGuidByteOrder(guidBytes);

            return new Guid(guidBytes);
        }

        /// <summary>
        /// Swaps byte order between RFC 4122 network order and .NET Guid order.
        /// </summary>
        private static void SwapGuidByteOrder(byte[] guid)
        {
            void Swap(int a, int b) => (guid[a], guid[b]) = (guid[b], guid[a]);

            Swap(0, 3);
            Swap(1, 2);
            Swap(4, 5);
            Swap(6, 7);
        }
    }
}
