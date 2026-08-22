using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class CellTowerLayoutTests
{
    [Test]
    public void SameSeedAndSettingsProduceSameSignature()
    {
        CellTowerLayoutSettings settings = CreateSettings(seed: 12345, antennaDensity: 0.65f);

        ulong first = CellTowerLayout.ComputeSignature(CellTowerLayout.Build(settings));
        ulong second = CellTowerLayout.ComputeSignature(CellTowerLayout.Build(settings));

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void DifferentSeedsProduceDifferentSignature()
    {
        CellTowerLayoutSettings firstSettings = CreateSettings(seed: 12345, antennaDensity: 1f);
        CellTowerLayoutSettings secondSettings = CreateSettings(seed: 54321, antennaDensity: 1f);

        ulong first = CellTowerLayout.ComputeSignature(CellTowerLayout.Build(firstSettings));
        ulong second = CellTowerLayout.ComputeSignature(CellTowerLayout.Build(secondSettings));

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [TestCase(15, 5, 3)]
    [TestCase(15, 5, 4)]
    [TestCase(20, 10, 5)]
    public void ExtremeLayoutsContainExpectedStructuralCounts(
        int levelCount,
        int startingLevel,
        int sideCount)
    {
        var settings = new CellTowerLayoutSettings(
            100,
            levelCount,
            startingLevel,
            sideCount,
            2.2f,
            2.5f,
            false,
            1f);
        List<CellTowerPlacement> placements = CellTowerLayout.Build(settings);
        int activeLevels = levelCount - startingLevel;

        Assert.That(Count(placements, CellTowerPlacementKind.CentralPole), Is.EqualTo(levelCount));
        Assert.That(Count(placements, CellTowerPlacementKind.RadialSupport), Is.EqualTo(activeLevels * sideCount));
        Assert.That(Count(placements, CellTowerPlacementKind.HorizontalSupport), Is.EqualTo(activeLevels * sideCount));
        Assert.That(Count(placements, CellTowerPlacementKind.VerticalSupport), Is.EqualTo((activeLevels - 1) * sideCount));
        Assert.That(Count(placements, CellTowerPlacementKind.DiagonalSupport), Is.EqualTo((activeLevels - 1) * sideCount));
        Assert.That(Count(placements, CellTowerPlacementKind.Platform), Is.EqualTo(activeLevels));
        Assert.That(Count(placements, CellTowerPlacementKind.Ladder), Is.EqualTo(levelCount - 1));
        Assert.That(Count(placements, CellTowerPlacementKind.Antenna), Is.EqualTo(activeLevels * sideCount));
    }

    [Test]
    public void SkippedLevelOnlyRemovesPlatformAndAntennas()
    {
        CellTowerLayoutSettings fullSettings = CreateSettings(
            seed: 777,
            antennaDensity: 1f,
            skipRandomSection: false);
        CellTowerLayoutSettings skippedSettings = CreateSettings(
            seed: 777,
            antennaDensity: 1f,
            skipRandomSection: true);

        List<CellTowerPlacement> full = CellTowerLayout.Build(fullSettings);
        List<CellTowerPlacement> skipped = CellTowerLayout.Build(skippedSettings);
        int skippedLevel = CellTowerLayout.GetSkippedLevel(skippedSettings);

        Assert.That(skippedLevel, Is.InRange(skippedSettings.StartingLevel + 2, skippedSettings.LevelCount - 3));
        Assert.That(
            Count(skipped, CellTowerPlacementKind.Platform),
            Is.EqualTo(Count(full, CellTowerPlacementKind.Platform) - 1));
        Assert.That(
            Count(skipped, CellTowerPlacementKind.Antenna),
            Is.EqualTo(Count(full, CellTowerPlacementKind.Antenna) - skippedSettings.SideCount));
        Assert.That(
            CountStructure(skipped),
            Is.EqualTo(CountStructure(full)));
    }

    [Test]
    public void ZeroAntennaDensityProducesNoAntennas()
    {
        List<CellTowerPlacement> placements = CellTowerLayout.Build(
            CreateSettings(seed: 1, antennaDensity: 0f));

        Assert.That(Count(placements, CellTowerPlacementKind.Antenna), Is.Zero);
    }

    [Test]
    public void LadderSegmentsUseTowerCentralAxis()
    {
        List<CellTowerPlacement> placements = CellTowerLayout.Build(
            CreateSettings(seed: 1, antennaDensity: 0f));

        for (int index = 0; index < placements.Count; index++)
        {
            CellTowerPlacement placement = placements[index];
            if (placement.Kind != CellTowerPlacementKind.Ladder)
                continue;

            Assert.That(placement.Start.x, Is.Zero.Within(0.0001f));
            Assert.That(placement.Start.z, Is.Zero.Within(0.0001f));
            Assert.That(placement.End.x, Is.Zero.Within(0.0001f));
            Assert.That(placement.End.z, Is.Zero.Within(0.0001f));
            Assert.That(placement.Position.x, Is.Zero.Within(0.0001f));
            Assert.That(placement.Position.z, Is.Zero.Within(0.0001f));
        }
    }

    [Test]
    public void LadderStartsBeforeUpperStructure()
    {
        CellTowerLayoutSettings settings = CreateSettings(seed: 1, antennaDensity: 0f);
        List<CellTowerPlacement> placements = CellTowerLayout.Build(settings);
        int firstLadderLevel = int.MaxValue;
        int firstRadialLevel = int.MaxValue;

        for (int index = 0; index < placements.Count; index++)
        {
            CellTowerPlacement placement = placements[index];
            if (placement.Kind == CellTowerPlacementKind.Ladder)
                firstLadderLevel = Mathf.Min(firstLadderLevel, placement.Level);
            else if (placement.Kind == CellTowerPlacementKind.RadialSupport)
                firstRadialLevel = Mathf.Min(firstRadialLevel, placement.Level);
        }

        Assert.That(firstLadderLevel, Is.EqualTo(settings.LadderStartingLevel));
        Assert.That(firstLadderLevel, Is.LessThan(firstRadialLevel));
        Assert.That(firstRadialLevel, Is.EqualTo(settings.StartingLevel));
    }

    private static CellTowerLayoutSettings CreateSettings(
        int seed,
        float antennaDensity,
        bool skipRandomSection = false)
    {
        return new CellTowerLayoutSettings(
            seed,
            18,
            9,
            4,
            2.2f,
            2.5f,
            skipRandomSection,
            antennaDensity);
    }

    private static int Count(
        IReadOnlyList<CellTowerPlacement> placements,
        CellTowerPlacementKind kind)
    {
        int count = 0;
        for (int index = 0; index < placements.Count; index++)
        {
            if (placements[index].Kind == kind)
                count++;
        }

        return count;
    }

    private static int CountStructure(IReadOnlyList<CellTowerPlacement> placements)
    {
        return Count(placements, CellTowerPlacementKind.CentralPole)
            + Count(placements, CellTowerPlacementKind.RadialSupport)
            + Count(placements, CellTowerPlacementKind.HorizontalSupport)
            + Count(placements, CellTowerPlacementKind.VerticalSupport)
            + Count(placements, CellTowerPlacementKind.DiagonalSupport)
            + Count(placements, CellTowerPlacementKind.Ladder);
    }
}
