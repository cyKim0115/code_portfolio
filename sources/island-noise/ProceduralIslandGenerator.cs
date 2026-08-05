using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Perlin 마스크+높이로 단층 복셀 섬 블록을 배치한다.
/// 이동 가능 육지(0.2 계단), 못 올라가는 언덕(Δ≥0.4), 해변을 구분한다.
/// </summary>
public static class ProceduralIslandGenerator
{
    public enum CellKind : byte
    {
        Empty = 0,
        Walkable = 1,
        UnclimbableHill = 2,
        Beach = 3,
    }

    public struct GenerationResult
    {
        public Transform Parent;
        public int WalkableCount;
        public int UnclimbableHillCount;
        public int BeachCount;
        public int RadiusBlocks;
        public int GridSize;
        public float MinLandY;
        public float MaxLandY;
    }

    public static GenerationResult Generate(float approximateDiameterWorld, int seed, ProceduralIslandBlockCatalog catalog)
    {
        var settings = ProceduralIslandGenerationSettings.Default;
        settings.ApproximateDiameterWorld = approximateDiameterWorld;
        settings.Seed = seed;
        settings.BlockCatalog = catalog;
        return Generate(null, settings);
    }

    public static GenerationResult Generate(Transform targetParent, ProceduralIslandGenerationSettings settings)
    {
        if (settings.BlockSize <= 0f || settings.YStep <= 0f)
        {
            Debug.LogError("[ProceduralIslandGenerator] BlockSize and YStep must be > 0.");
            return default;
        }

        if (settings.ApproximateDiameterWorld < settings.BlockSize)
        {
            Debug.LogWarning(
                $"[ProceduralIslandGenerator] ApproximateDiameterWorld({settings.ApproximateDiameterWorld}) < BlockSize. Clamping.");
            settings.ApproximateDiameterWorld = settings.BlockSize;
        }

        var catalog = settings.BlockCatalog;
        if (catalog == null)
        {
            Debug.LogError("[ProceduralIslandGenerator] BlockCatalog is required.");
            return default;
        }

        if (!catalog.HasAnyWalkable)
        {
            Debug.LogError("[ProceduralIslandGenerator] WalkableTopMaterials 리스트가 비어 있습니다.");
            return default;
        }

        if (!catalog.HasAnyUnclimbableHill)
            Debug.LogWarning("[ProceduralIslandGenerator] UnclimbableHillTopMaterials가 비어 있으면 해당 셀은 Walkable 머티리얼로 대체합니다.");

        var floorPrefab = catalog.ResolveFloorBlockPrefab();
        if (floorPrefab == null)
        {
            Debug.LogError("[ProceduralIslandGenerator] FloorBlock 프리팹을 찾을 수 없습니다.");
            return default;
        }

        var sandPrefab = catalog.ResolveSandBlockPrefab();
        if (sandPrefab == null)
            Debug.LogWarning("[ProceduralIslandGenerator] SandBlock 프리팹을 찾을 수 없으면 해변 셀을 건너뜁니다.");

        var parent = ResolveParent(targetParent, settings.RecordUndo);
        if (settings.ClearTargetChildren)
            ClearChildren(parent, settings.RecordUndo);

        var radiusBlocks = Mathf.Max(1, Mathf.CeilToInt((settings.ApproximateDiameterWorld * 0.5f) / settings.BlockSize));
        var halfExtent = radiusBlocks + 1;
        var gridSize = halfExtent * 2 + 1;

        BuildGrids(
            settings,
            radiusBlocks,
            halfExtent,
            gridSize,
            out var kinds,
            out var heights);

        ResolveHeights(kinds, heights, gridSize, settings);
        AnchorBaseLandHeight(kinds, heights, settings);

        var rng = new System.Random(settings.Seed);
        var walkableCount = 0;
        var unclimbableCount = 0;
        var beachCount = 0;
        var minLandY = float.MaxValue;
        var maxLandY = float.MinValue;

        var recordUndo = settings.RecordUndo;
        var undoGroup = 0;
        if (recordUndo)
        {
            Undo.SetCurrentGroupName("Procedural Island Generate");
            undoGroup = Undo.GetCurrentGroup();
        }

        for (var z = 0; z < gridSize; z++)
        {
            for (var x = 0; x < gridSize; x++)
            {
                var index = z * gridSize + x;
                var kind = kinds[index];
                if (kind == CellKind.Empty)
                    continue;

                Material topMaterial = null;
                string namePrefix;
                GameObject prefab;
                switch (kind)
                {
                    case CellKind.Beach:
                        if (sandPrefab == null)
                            continue;
                        prefab = sandPrefab;
                        topMaterial = catalog.HasAnyBeachMaterial ? catalog.PickBeach(rng) : null;
                        namePrefix = "Beach";
                        beachCount++;
                        break;
                    case CellKind.UnclimbableHill:
                        prefab = floorPrefab;
                        topMaterial = catalog.HasAnyUnclimbableHill
                            ? catalog.PickUnclimbableHill(rng)
                            : catalog.PickWalkable(rng);
                        namePrefix = "CliffHill";
                        unclimbableCount++;
                        break;
                    default:
                        prefab = floorPrefab;
                        topMaterial = catalog.PickWalkable(rng);
                        namePrefix = "Walkable";
                        walkableCount++;
                        break;
                }

                if (kind != CellKind.Beach && topMaterial == null)
                    continue;

                var gx = x - halfExtent;
                var gz = z - halfExtent;
                var y = heights[index];
                if (kind != CellKind.Beach)
                {
                    minLandY = Mathf.Min(minLandY, y);
                    maxLandY = Mathf.Max(maxLandY, y);
                }

                var worldPos = new Vector3(gx * settings.BlockSize, y, gz * settings.BlockSize);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                if (recordUndo)
                    Undo.RegisterCreatedObjectUndo(instance, "Procedural Island Block");
                instance.transform.localPosition = worldPos;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one * IslandFloorBlockUtility.DefaultWorldScale;
                instance.name = $"{namePrefix}_{gx}_{gz}_Y{y:0.##}";
                if (topMaterial != null)
                    IslandFloorBlockUtility.SetTopMaterial(instance.transform, topMaterial, recordUndo: recordUndo);
            }
        }

        if (recordUndo)
            Undo.CollapseUndoOperations(undoGroup);
        Selection.activeTransform = parent;
        if (recordUndo)
            EditorGUIUtility.PingObject(parent.gameObject);

        // Live Mode(RecordUndo=false)는 Undo가 씬 dirty를 안 남기므로 명시적으로 표시
        EnsureHierarchyDirty(parent);

        if (walkableCount + unclimbableCount == 0)
        {
            minLandY = 0f;
            maxLandY = 0f;
        }

        var result = new GenerationResult
        {
            Parent = parent,
            WalkableCount = walkableCount,
            UnclimbableHillCount = unclimbableCount,
            BeachCount = beachCount,
            RadiusBlocks = radiusBlocks,
            GridSize = gridSize,
            MinLandY = minLandY,
            MaxLandY = maxLandY,
        };

        Debug.Log(
            $"[ProceduralIslandGenerator] Done. seed={settings.Seed} diameter≈{settings.ApproximateDiameterWorld} " +
            $"radiusBlocks={radiusBlocks} walkable={walkableCount} cliffHill={unclimbableCount} beach={beachCount} " +
            $"landY=[{minLandY:0.##}..{maxLandY:0.##}] parent={parent.name}");

        return result;
    }

    static void BuildGrids(
        ProceduralIslandGenerationSettings settings,
        int radiusBlocks,
        int halfExtent,
        int gridSize,
        out CellKind[] kinds,
        out float[] heights)
    {
        kinds = new CellKind[gridSize * gridSize];
        heights = new float[gridSize * gridSize];

        var seedOffset = HashSeed(settings.Seed);
        var shapeOx = seedOffset.x;
        var shapeOz = seedOffset.y;
        var heightOx = seedOffset.x + 17.3f;
        var heightOz = seedOffset.y + 41.7f;
        var cliffOx = seedOffset.x + 53.7f;
        var cliffOz = seedOffset.y + 29.1f;
        var beachOx = seedOffset.x + 91.1f;
        var beachOz = seedOffset.y + 13.9f;

        // Pass 1: land mask + quantized walkable height
        for (var z = 0; z < gridSize; z++)
        {
            for (var x = 0; x < gridSize; x++)
            {
                var gx = x - halfExtent;
                var gz = z - halfExtent;
                var index = z * gridSize + x;

                var dist = Mathf.Sqrt(gx * gx + gz * gz);
                if (dist > radiusBlocks + 0.01f)
                {
                    kinds[index] = CellKind.Empty;
                    continue;
                }

                var falloff = 1f - Mathf.Clamp01(dist / Mathf.Max(1f, radiusBlocks));
                falloff = falloff * falloff * (3f - 2f * falloff);

                var nx = gx * settings.ShapeFrequency + shapeOx;
                var nz = gz * settings.ShapeFrequency + shapeOz;
                var shapeNoise = Mathf.PerlinNoise(nx, nz);
                const float edgeBias = 0.55f;
                var mask = shapeNoise - (1f - falloff) * edgeBias;

                if (mask < settings.LandThreshold)
                {
                    kinds[index] = CellKind.Empty;
                    continue;
                }

                kinds[index] = CellKind.Walkable;

                var hx = gx * settings.HeightFrequency + heightOx;
                var hz = gz * settings.HeightFrequency + heightOz;
                var heightNoise = Mathf.PerlinNoise(hx, hz);
                // 대부분 기준 평지(0). threshold 넘는 봉우리만 0.2 계단으로 올림.
                var hillAmount = 0f;
                if (heightNoise > settings.HillStartThreshold)
                {
                    hillAmount = Mathf.InverseLerp(settings.HillStartThreshold, 1f, heightNoise);
                    hillAmount = Mathf.Pow(Mathf.Clamp01(hillAmount), Mathf.Max(0.01f, settings.HillPower));
                    // 가장자리는 평지 유지, 중심부만 언덕
                    hillAmount *= falloff;
                }

                heights[index] = QuantizeHeight(hillAmount, settings);

                // cliff candidate flag stored later; keep Walkable for now
                var cx = gx * settings.CliffFrequency + cliffOx;
                var cz = gz * settings.CliffFrequency + cliffOz;
                var cliffNoise = Mathf.PerlinNoise(cx, cz);
                if (cliffNoise >= settings.CliffThreshold && falloff > 0.25f)
                    kinds[index] = CellKind.UnclimbableHill;
            }
        }

        // Pass 2: beach on empty cells adjacent to land
        var landSnapshot = (CellKind[])kinds.Clone();
        int[] neighbors = { -1, 0, 1, 0, 0, -1, 0, 1 };

        for (var z = 0; z < gridSize; z++)
        {
            for (var x = 0; x < gridSize; x++)
            {
                var index = z * gridSize + x;
                if (landSnapshot[index] != CellKind.Empty)
                    continue;

                var gx = x - halfExtent;
                var gz = z - halfExtent;
                var adjacentLand = false;
                for (var n = 0; n < 4; n++)
                {
                    var nx = x + neighbors[n * 2];
                    var nz = z + neighbors[n * 2 + 1];
                    if (nx < 0 || nz < 0 || nx >= gridSize || nz >= gridSize)
                        continue;
                    var neighborKind = landSnapshot[nz * gridSize + nx];
                    if (neighborKind == CellKind.Walkable || neighborKind == CellKind.UnclimbableHill)
                    {
                        adjacentLand = true;
                        break;
                    }
                }

                if (!adjacentLand)
                    continue;

                var dist = Mathf.Sqrt(gx * gx + gz * gz);
                if (dist > radiusBlocks + 1.01f)
                    continue;

                var bx = gx * settings.BeachFrequency + beachOx;
                var bz = gz * settings.BeachFrequency + beachOz;
                var beachNoise = Mathf.PerlinNoise(bx, bz);
                if (beachNoise >= settings.BeachThreshold)
                {
                    kinds[index] = CellKind.Beach;
                    heights[index] = settings.SandY;
                }
            }
        }
    }

    static float QuantizeHeight(float normalized01, ProceduralIslandGenerationSettings settings)
    {
        var maxSteps = Mathf.Max(0, Mathf.RoundToInt((settings.MaxLandY - settings.BaseLandY) / settings.YStep));
        // Floor로 평지(0) 비중을 조금 더 확보
        var stepIndex = Mathf.Clamp(Mathf.FloorToInt(normalized01 * (maxSteps + 1)), 0, maxSteps);
        return settings.BaseLandY + stepIndex * settings.YStep;
    }

    /// <summary>
    /// 이동 가능 육지의 최저점이 BaseLandY(기본 0)가 되도록 전체 육지 높이를 내린다.
    /// 해변(SandY)은 그대로 둔다.
    /// </summary>
    static void AnchorBaseLandHeight(
        CellKind[] kinds,
        float[] heights,
        ProceduralIslandGenerationSettings settings)
    {
        var minLandY = float.MaxValue;
        for (var i = 0; i < kinds.Length; i++)
        {
            if (kinds[i] != CellKind.Walkable && kinds[i] != CellKind.UnclimbableHill)
                continue;
            minLandY = Mathf.Min(minLandY, heights[i]);
        }

        if (minLandY >= float.MaxValue * 0.5f)
            return;

        var delta = minLandY - settings.BaseLandY;
        if (Mathf.Abs(delta) < 0.001f)
            return;

        for (var i = 0; i < kinds.Length; i++)
        {
            if (kinds[i] != CellKind.Walkable && kinds[i] != CellKind.UnclimbableHill)
                continue;
            heights[i] -= delta;
            if (heights[i] > settings.MaxLandY)
                heights[i] = settings.MaxLandY;
            if (heights[i] < settings.BaseLandY)
                heights[i] = settings.BaseLandY;
        }
    }

    static void ResolveHeights(
        CellKind[] kinds,
        float[] heights,
        int gridSize,
        ProceduralIslandGenerationSettings settings)
    {
        // 절벽 적용 ↔ walkable 스무딩을 교차 반복해 강등 후 Δ 조건을 안정화한다.
        for (var iter = 0; iter < 10; iter++)
        {
            var beforeKinds = (CellKind[])kinds.Clone();
            var beforeHeights = (float[])heights.Clone();

            EnforceWalkableClimbSteps(kinds, heights, gridSize, settings);
            ApplyUnclimbableHills(kinds, heights, gridSize, settings);
            ClampLandHeights(kinds, heights, settings.MaxLandY);

            var changed = false;
            for (var i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] != beforeKinds[i] || !Mathf.Approximately(heights[i], beforeHeights[i]))
                {
                    changed = true;
                    break;
                }
            }

            if (!changed)
                break;
        }

        EnforceWalkableClimbSteps(kinds, heights, gridSize, settings);
        ClampLandHeights(kinds, heights, settings.MaxLandY);
    }

    static void ClampLandHeights(CellKind[] kinds, float[] heights, float maxLandY)
    {
        for (var i = 0; i < heights.Length; i++)
        {
            if (kinds[i] == CellKind.Empty || kinds[i] == CellKind.Beach)
                continue;
            if (heights[i] > maxLandY)
                heights[i] = maxLandY;
        }
    }

    /// <summary>
    /// 이동 가능 셀끼리 ΔY ≤ MaxClimbStep이 되도록 높은 쪽을 한 계단씩 깎는다.
    /// UnclimbableHill은 아직 높이지 않은 후보로만 두고, 이후 Apply에서 들어 올린다.
    /// </summary>
    static void EnforceWalkableClimbSteps(
        CellKind[] kinds,
        float[] heights,
        int gridSize,
        ProceduralIslandGenerationSettings settings)
    {
        var maxStep = settings.MaxClimbStep;
        var yStep = settings.YStep;
        var changed = true;
        var guard = 0;
        var maxGuard = gridSize * gridSize * 4;

        while (changed && guard++ < maxGuard)
        {
            changed = false;
            for (var z = 0; z < gridSize; z++)
            {
                for (var x = 0; x < gridSize; x++)
                {
                    var index = z * gridSize + x;
                    if (kinds[index] != CellKind.Walkable)
                        continue;

                    if (!TryGetLowestLandNeighbor(kinds, heights, gridSize, x, z, includeUnclimbableAsNeighbor: false, out var lowest))
                        continue;

                    if (heights[index] - lowest <= maxStep + 0.001f)
                        continue;

                    heights[index] = QuantizeDown(lowest + maxStep, settings.BaseLandY, yStep);
                    if (heights[index] > settings.MaxLandY)
                        heights[index] = settings.MaxLandY;
                    changed = true;
                }
            }
        }
    }

    /// <summary>
    /// 못 올라가는 언덕: 인접 Walkable 대비 최소 CliffMinDelta 이상 높게 맞추고 카탈로그용 kind 유지.
    /// </summary>
    static void ApplyUnclimbableHills(
        CellKind[] kinds,
        float[] heights,
        int gridSize,
        ProceduralIslandGenerationSettings settings)
    {
        var yStep = settings.YStep;
        var cliffMin = settings.CliffMinDelta;
        var maxY = settings.MaxLandY;

        for (var z = 0; z < gridSize; z++)
        {
            for (var x = 0; x < gridSize; x++)
            {
                var index = z * gridSize + x;
                if (kinds[index] != CellKind.UnclimbableHill)
                    continue;

                if (!TryGetHighestWalkableNeighbor(kinds, heights, gridSize, x, z, out var highestWalkable))
                {
                    // 고립되면 최소 cliff 높이만 보장
                    heights[index] = Mathf.Min(maxY, settings.BaseLandY + cliffMin);
                    heights[index] = QuantizeUp(heights[index], settings.BaseLandY, yStep);
                    continue;
                }

                var target = highestWalkable + cliffMin;
                if (target > maxY + 0.001f)
                {
                    // MaxLandY로는 절벽 높이를 못 내면 이동 가능으로 강등
                    kinds[index] = CellKind.Walkable;
                    if (TryGetLowestWalkableNeighbor(kinds, heights, gridSize, x, z, out var lowestWalkable))
                        heights[index] = QuantizeDown(lowestWalkable + settings.MaxClimbStep, settings.BaseLandY, yStep);
                    else
                        heights[index] = highestWalkable;
                    continue;
                }

                heights[index] = Mathf.Min(maxY, QuantizeUp(target, settings.BaseLandY, yStep));

                // 양자화 후에도 절벽 조건 재확인
                if (heights[index] - highestWalkable < cliffMin - 0.001f)
                {
                    kinds[index] = CellKind.Walkable;
                    if (TryGetLowestWalkableNeighbor(kinds, heights, gridSize, x, z, out var lowestWalkable))
                        heights[index] = QuantizeDown(lowestWalkable + settings.MaxClimbStep, settings.BaseLandY, yStep);
                    else
                        heights[index] = highestWalkable;
                }
            }
        }
    }

    static bool TryGetLowestLandNeighbor(
        CellKind[] kinds,
        float[] heights,
        int gridSize,
        int x,
        int z,
        bool includeUnclimbableAsNeighbor,
        out float lowest)
    {
        lowest = float.MaxValue;
        var found = false;
        int[] neighbors = { -1, 0, 1, 0, 0, -1, 0, 1 };
        for (var n = 0; n < 4; n++)
        {
            var nx = x + neighbors[n * 2];
            var nz = z + neighbors[n * 2 + 1];
            if (nx < 0 || nz < 0 || nx >= gridSize || nz >= gridSize)
                continue;
            var ni = nz * gridSize + nx;
            var kind = kinds[ni];
            if (kind != CellKind.Walkable &&
                !(includeUnclimbableAsNeighbor && kind == CellKind.UnclimbableHill))
                continue;
            found = true;
            lowest = Mathf.Min(lowest, heights[ni]);
        }

        return found;
    }

    static bool TryGetHighestWalkableNeighbor(
        CellKind[] kinds,
        float[] heights,
        int gridSize,
        int x,
        int z,
        out float highest)
    {
        highest = float.MinValue;
        var found = false;
        int[] neighbors = { -1, 0, 1, 0, 0, -1, 0, 1 };
        for (var n = 0; n < 4; n++)
        {
            var nx = x + neighbors[n * 2];
            var nz = z + neighbors[n * 2 + 1];
            if (nx < 0 || nz < 0 || nx >= gridSize || nz >= gridSize)
                continue;
            var ni = nz * gridSize + nx;
            if (kinds[ni] != CellKind.Walkable)
                continue;
            found = true;
            highest = Mathf.Max(highest, heights[ni]);
        }

        return found;
    }

    static bool TryGetLowestWalkableNeighbor(
        CellKind[] kinds,
        float[] heights,
        int gridSize,
        int x,
        int z,
        out float lowest)
    {
        lowest = float.MaxValue;
        var found = false;
        int[] neighbors = { -1, 0, 1, 0, 0, -1, 0, 1 };
        for (var n = 0; n < 4; n++)
        {
            var nx = x + neighbors[n * 2];
            var nz = z + neighbors[n * 2 + 1];
            if (nx < 0 || nz < 0 || nx >= gridSize || nz >= gridSize)
                continue;
            var ni = nz * gridSize + nx;
            if (kinds[ni] != CellKind.Walkable)
                continue;
            found = true;
            lowest = Mathf.Min(lowest, heights[ni]);
        }

        return found;
    }

    static float QuantizeDown(float value, float baseY, float yStep)
    {
        var steps = Mathf.FloorToInt((value - baseY) / yStep + 0.001f);
        return baseY + Mathf.Max(0, steps) * yStep;
    }

    static float QuantizeUp(float value, float baseY, float yStep)
    {
        var steps = Mathf.CeilToInt((value - baseY) / yStep - 0.001f);
        return baseY + Mathf.Max(0, steps) * yStep;
    }

    static Transform ResolveParent(Transform targetParent, bool recordUndo)
    {
        if (targetParent != null)
            return targetParent;

        var existing = GameObject.Find("ProceduralIsland_Preview");
        if (existing != null)
            return existing.transform;

        var go = new GameObject("ProceduralIsland_Preview");
        if (recordUndo)
            Undo.RegisterCreatedObjectUndo(go, "Procedural Island Preview Root");
        return go.transform;
    }

    static void ClearChildren(Transform parent, bool recordUndo)
    {
        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            if (recordUndo)
                Undo.DestroyObjectImmediate(child);
            else
                Object.DestroyImmediate(child);
        }
    }

    /// <summary>
    /// 계층 변경 후 씬/프리팹이 dirty가 아니면 표시한다. (Live Mode Undo 생략 경로용)
    /// </summary>
    static void EnsureHierarchyDirty(Transform parent)
    {
        if (parent == null)
            return;

        EditorUtility.SetDirty(parent.gameObject);

        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
        {
            if (!stage.scene.isDirty)
                EditorSceneManager.MarkSceneDirty(stage.scene);
            return;
        }

        var scene = parent.gameObject.scene;
        if (scene.IsValid() && !scene.isDirty)
            EditorSceneManager.MarkSceneDirty(scene);
    }

    static Vector2 HashSeed(int seed)
    {
        var x = (seed * 12.9898f) % 1000f;
        var y = (seed * 78.233f) % 1000f;
        if (x < 0f) x += 1000f;
        if (y < 0f) y += 1000f;
        return new Vector2(x + 0.1f, y + 0.1f);
    }
}
