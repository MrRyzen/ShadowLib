using System.IO;
using System.Text;
using NUnit.Framework;
using ShadowLib.DeterminismCheck;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class DeterminismFingerprintTests
    {
        [Test]
        public void Fingerprint_IsTheSameOnASecondRun()
        {
            CollectionAssert.AreEqual(DeterminismFingerprint.Compute(), DeterminismFingerprint.Compute());
        }

        [Test, Explicit("Writes the fingerprint to the path in SHADOWLIB_FINGERPRINT_OUT for comparison with another runtime.")]
        public void WriteFingerprint()
        {
            string? path = System.Environment.GetEnvironmentVariable("SHADOWLIB_FINGERPRINT_OUT");
            Assert.IsNotNull(path, "Set SHADOWLIB_FINGERPRINT_OUT to an output file path.");

            var text = new StringBuilder();
            foreach (var row in DeterminismFingerprint.Compute())
                text.Append(row.Key).Append('=').Append(row.Value.ToString("X16")).Append('\n');
            File.WriteAllText(path!, text.ToString());
        }
    }
}
