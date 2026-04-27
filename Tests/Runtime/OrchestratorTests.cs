using NUnit.Framework;
using ShadowLib.RNG;
using ShadowLib.RNG.Utilities;

namespace ShadowLib.Tests;

[TestFixture]
public class OrchestratorTests
{
    [Test]
    public void SameContext_ReturnsCachedRng()
    {
        Orchestrator.Initialize(Seeding.GenerateSeed64("orchestrator-test-1"));
        var a = Orchestrator.CreateRNG("loot");
        var b = Orchestrator.CreateRNG("loot");
        Assert.AreSame(a, b);
    }

    [Test]
    public void SameRootAndContext_ProducesIdenticalSequence()
    {
        ulong root = Seeding.GenerateSeed64("orchestrator-test-2");

        Orchestrator.Initialize(root);
        var rngA = Orchestrator.CreateRNG("worldgen");
        int[] sequenceA = { rngA.NextInt(), rngA.NextInt(), rngA.NextInt() };

        Orchestrator.Initialize(root);
        var rngB = Orchestrator.CreateRNG("worldgen");
        int[] sequenceB = { rngB.NextInt(), rngB.NextInt(), rngB.NextInt() };

        CollectionAssert.AreEqual(sequenceA, sequenceB);
    }

    [Test]
    public void DifferentContexts_ProduceDifferentStreams()
    {
        Orchestrator.Initialize(Seeding.GenerateSeed64("orchestrator-test-3"));
        var loot = Orchestrator.CreateRNG("loot");
        var ai = Orchestrator.CreateRNG("ai");
        Assert.AreNotEqual(loot.NextInt(), ai.NextInt());
    }
}
