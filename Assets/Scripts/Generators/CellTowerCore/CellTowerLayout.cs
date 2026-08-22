using System;
using System.Collections.Generic;
using UnityEngine;

public enum CellTowerPlacementKind
{
    CentralPole,
    RadialSupport,
    HorizontalSupport,
    VerticalSupport,
    DiagonalSupport,
    Platform,
    Ladder,
    Antenna
}

public readonly struct CellTowerLayoutSettings
{
    public CellTowerLayoutSettings(
        int seed,
        int levelCount,
        int startingLevel,
        int sideCount,
        float radius,
        float levelHeight,
        bool skipRandomSection,
        float antennaDensity,
        int ladderStartingLevel = 0)
    {
        Seed = seed;
        LevelCount = levelCount;
        StartingLevel = startingLevel;
        SideCount = sideCount;
        Radius = radius;
        LevelHeight = levelHeight;
        SkipRandomSection = skipRandomSection;
        AntennaDensity = antennaDensity;
        LadderStartingLevel = ladderStartingLevel;
    }

    public int Seed { get; }
    public int LevelCount { get; }
    public int StartingLevel { get; }
    public int SideCount { get; }
    public float Radius { get; }
    public float LevelHeight { get; }
    public bool SkipRandomSection { get; }
    public float AntennaDensity { get; }
    public int LadderStartingLevel { get; }
}

public readonly struct CellTowerPlacement
{
    public CellTowerPlacement(
        CellTowerPlacementKind kind,
        int level,
        int side,
        Vector3 start,
        Vector3 end,
        Vector3 position,
        Quaternion rotation,
        float uniformScale,
        float lateralScale,
        float randomValue)
    {
        Kind = kind;
        Level = level;
        Side = side;
        Start = start;
        End = end;
        Position = position;
        Rotation = rotation;
        UniformScale = uniformScale;
        LateralScale = lateralScale;
        RandomValue = randomValue;
    }

    public CellTowerPlacementKind Kind { get; }
    public int Level { get; }
    public int Side { get; }
    public Vector3 Start { get; }
    public Vector3 End { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public float UniformScale { get; }
    public float LateralScale { get; }
    public float RandomValue { get; }
}

public static class CellTowerLayout
{
    private const int PlatformCategory = 10;
    private const int AntennaPresenceCategory = 20;
    private const int AntennaVariantCategory = 21;
    private const int AntennaYawCategory = 22;
    private const int AntennaScaleCategory = 23;
    private const int SkippedLevelCategory = 30;

    public static List<CellTowerPlacement> Build(CellTowerLayoutSettings settings)
    {
        Validate(settings);

        var placements = new List<CellTowerPlacement>();
        int skippedLevel = GetSkippedLevel(settings);

        AddCentralPole(settings, placements);
        AddRingStructure(settings, placements);
        AddPlatformsAndAntennas(settings, skippedLevel, placements);
        AddLadder(settings, placements);

        return placements;
    }

    public static int GetSkippedLevel(CellTowerLayoutSettings settings)
    {
        if (!settings.SkipRandomSection)
            return -1;

        int minimum = settings.StartingLevel + 2;
        int maximum = settings.LevelCount - 3;
        if (minimum > maximum)
            return -1;

        return RangeInclusive(settings.Seed, SkippedLevelCategory, 0, 0, minimum, maximum);
    }

    public static int RangeInclusive(
        int seed,
        int category,
        int level,
        int side,
        int minimum,
        int maximum)
    {
        if (minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum cannot exceed maximum.");

        uint range = (uint)(maximum - minimum + 1);
        return minimum + (int)(Hash(seed, category, level, side) % range);
    }

    public static float Value01(int seed, int category, int level, int side)
    {
        return (Hash(seed, category, level, side) & 0x00FFFFFFu) / 16777216f;
    }

    public static ulong ComputeSignature(IReadOnlyList<CellTowerPlacement> placements)
    {
        const ulong offset = 14695981039346656037ul;
        const ulong prime = 1099511628211ul;
        ulong signature = offset;

        for (int index = 0; index < placements.Count; index++)
        {
            CellTowerPlacement placement = placements[index];
            Mix(ref signature, (int)placement.Kind, prime);
            Mix(ref signature, placement.Level, prime);
            Mix(ref signature, placement.Side, prime);
            Mix(ref signature, Quantize(placement.Start.x), prime);
            Mix(ref signature, Quantize(placement.Start.y), prime);
            Mix(ref signature, Quantize(placement.Start.z), prime);
            Mix(ref signature, Quantize(placement.End.x), prime);
            Mix(ref signature, Quantize(placement.End.y), prime);
            Mix(ref signature, Quantize(placement.End.z), prime);
            Mix(ref signature, Quantize(placement.Position.x), prime);
            Mix(ref signature, Quantize(placement.Position.y), prime);
            Mix(ref signature, Quantize(placement.Position.z), prime);
            Mix(ref signature, Quantize(placement.Rotation.x), prime);
            Mix(ref signature, Quantize(placement.Rotation.y), prime);
            Mix(ref signature, Quantize(placement.Rotation.z), prime);
            Mix(ref signature, Quantize(placement.Rotation.w), prime);
            Mix(ref signature, Quantize(placement.UniformScale), prime);
            Mix(ref signature, Quantize(placement.LateralScale), prime);
            Mix(ref signature, Quantize(placement.RandomValue), prime);
        }

        return signature;
    }

    private static void AddCentralPole(
        CellTowerLayoutSettings settings,
        ICollection<CellTowerPlacement> placements)
    {
        for (int level = 0; level < settings.LevelCount; level++)
        {
            Vector3 start = Vector3.up * (level * settings.LevelHeight);
            Vector3 end = Vector3.up * ((level + 1) * settings.LevelHeight);
            float lateralScale = level < 10 ? Mathf.Pow(0.95f, level + 1) : 0.57f;

            placements.Add(CreateBeam(
                CellTowerPlacementKind.CentralPole,
                level,
                -1,
                start,
                end,
                lateralScale));
        }
    }

    private static void AddRingStructure(
        CellTowerLayoutSettings settings,
        ICollection<CellTowerPlacement> placements)
    {
        int lastLevel = settings.LevelCount - 1;
        for (int level = settings.StartingLevel; level <= lastLevel; level++)
        {
            Vector3 center = Vector3.up * (level * settings.LevelHeight);
            for (int side = 0; side < settings.SideCount; side++)
            {
                int nextSide = (side + 1) % settings.SideCount;
                Vector3 current = GetRingPoint(settings, level, side);
                Vector3 next = GetRingPoint(settings, level, nextSide);

                placements.Add(CreateBeam(
                    CellTowerPlacementKind.RadialSupport,
                    level,
                    side,
                    center,
                    current));
                placements.Add(CreateBeam(
                    CellTowerPlacementKind.HorizontalSupport,
                    level,
                    side,
                    current,
                    next));

                if (level >= lastLevel)
                    continue;

                Vector3 aboveCurrent = GetRingPoint(settings, level + 1, side);
                Vector3 aboveNext = GetRingPoint(settings, level + 1, nextSide);
                placements.Add(CreateBeam(
                    CellTowerPlacementKind.VerticalSupport,
                    level,
                    side,
                    current,
                    aboveCurrent));

                bool reverseDiagonal = ((level - settings.StartingLevel + side) & 1) != 0;
                placements.Add(CreateBeam(
                    CellTowerPlacementKind.DiagonalSupport,
                    level,
                    side,
                    reverseDiagonal ? next : current,
                    reverseDiagonal ? aboveCurrent : aboveNext));
            }
        }
    }

    private static void AddPlatformsAndAntennas(
        CellTowerLayoutSettings settings,
        int skippedLevel,
        ICollection<CellTowerPlacement> placements)
    {
        for (int level = settings.StartingLevel; level < settings.LevelCount; level++)
        {
            if (level == skippedLevel)
                continue;

            float yaw = settings.SideCount switch
            {
                3 => 60f,
                4 => 75f,
                5 => 84f,
                _ => 0f
            };

            placements.Add(new CellTowerPlacement(
                CellTowerPlacementKind.Platform,
                level,
                -1,
                Vector3.zero,
                Vector3.zero,
                Vector3.up * (level * settings.LevelHeight),
                Quaternion.Euler(0f, yaw, 0f),
                1f,
                1f,
                Value01(settings.Seed, PlatformCategory, level, 0)));

            for (int side = 0; side < settings.SideCount; side++)
            {
                float presence = Value01(settings.Seed, AntennaPresenceCategory, level, side);
                if (presence >= settings.AntennaDensity)
                    continue;

                int nextSide = (side + 1) % settings.SideCount;
                Vector3 position = Vector3.Lerp(
                    GetRingPoint(settings, level, side),
                    GetRingPoint(settings, level, nextSide),
                    0.5f);
                Vector3 outward = Vector3.ProjectOnPlane(position, Vector3.up).normalized;
                float yawOffset = Mathf.Lerp(
                    -25f,
                    25f,
                    Value01(settings.Seed, AntennaYawCategory, level, side));
                Vector3 rotatedOutward = Quaternion.AngleAxis(yawOffset, Vector3.up) * outward;
                Quaternion rotation = Quaternion.LookRotation(Vector3.up, rotatedOutward);
                float scale = Mathf.Lerp(
                    1.2f,
                    1.5f,
                    Value01(settings.Seed, AntennaScaleCategory, level, side));

                placements.Add(new CellTowerPlacement(
                    CellTowerPlacementKind.Antenna,
                    level,
                    side,
                    Vector3.zero,
                    Vector3.zero,
                    position,
                    rotation,
                    scale,
                    1f,
                    Value01(settings.Seed, AntennaVariantCategory, level, side)));
            }
        }
    }

    private static void AddLadder(
        CellTowerLayoutSettings settings,
        ICollection<CellTowerPlacement> placements)
    {
        if (settings.LadderStartingLevel >= settings.LevelCount - 1)
            return;

        for (int level = settings.LadderStartingLevel; level < settings.LevelCount - 1; level++)
        {
            Vector3 start = Vector3.up * (level * settings.LevelHeight);
            Vector3 end = start + Vector3.up * settings.LevelHeight;
            placements.Add(CreateBeam(
                CellTowerPlacementKind.Ladder,
                level,
                0,
                start,
                end));
        }
    }

    private static CellTowerPlacement CreateBeam(
        CellTowerPlacementKind kind,
        int level,
        int side,
        Vector3 start,
        Vector3 end,
        float lateralScale = 1f)
    {
        return new CellTowerPlacement(
            kind,
            level,
            side,
            start,
            end,
            Vector3.Lerp(start, end, 0.5f),
            Quaternion.identity,
            1f,
            lateralScale,
            0f);
    }

    private static Vector3 GetRingPoint(CellTowerLayoutSettings settings, int level, int side)
    {
        float angle = side * (Mathf.PI * 2f / settings.SideCount);
        return new Vector3(
            Mathf.Cos(angle) * settings.Radius,
            level * settings.LevelHeight,
            Mathf.Sin(angle) * settings.Radius);
    }

    private static void Validate(CellTowerLayoutSettings settings)
    {
        if (settings.LevelCount < 2)
            throw new ArgumentOutOfRangeException(nameof(settings), "Level count must be at least two.");
        if (settings.StartingLevel < 0 || settings.StartingLevel >= settings.LevelCount)
            throw new ArgumentOutOfRangeException(nameof(settings), "Starting level must be inside the tower.");
        if (settings.LadderStartingLevel < 0 || settings.LadderStartingLevel >= settings.LevelCount - 1)
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Ladder starting level must leave room for at least one segment.");
        if (settings.SideCount < 3 || settings.SideCount > 5)
            throw new ArgumentOutOfRangeException(nameof(settings), "Side count must be between three and five.");
        if (settings.Radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Radius must be positive.");
        if (settings.LevelHeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Level height must be positive.");
        if (settings.AntennaDensity < 0f || settings.AntennaDensity > 1f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Antenna density must be between zero and one.");
    }

    private static uint Hash(int seed, int category, int level, int side)
    {
        uint value = unchecked((uint)seed) ^ 0x9E3779B9u;
        value = Mix(value ^ unchecked((uint)category) * 0x85EBCA6Bu);
        value = Mix(value ^ unchecked((uint)level) * 0xC2B2AE35u);
        value = Mix(value ^ unchecked((uint)side) * 0x27D4EB2Fu);
        return Mix(value);
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }

    private static int Quantize(float value)
    {
        return Mathf.RoundToInt(value * 10000f);
    }

    private static void Mix(ref ulong signature, int value, ulong prime)
    {
        signature ^= unchecked((uint)value);
        signature *= prime;
    }
}
