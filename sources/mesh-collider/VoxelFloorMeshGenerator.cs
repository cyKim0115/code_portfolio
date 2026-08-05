using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class VoxelFloorMeshGenerator
{
    const float RotationAlignThreshold = 1f;

    public sealed class PreviewInfo
    {
        public int GridBlockCount;
        public int OffGridBlockCount;
        public int ExposedFaceCount;
        public int CulledInnerFaceCount;
        public int TopMaterialVariantCount;
    }

    sealed class GridLayer
    {
        public Vector3 Phase;
        public readonly Dictionary<Vector3Int, CellTop> CellTops = new Dictionary<Vector3Int, CellTop>();
    }

    readonly struct CellTop
    {
        public CellTop(Material material, float localYRotationDegrees)
        {
            Material = material;
            LocalYRotationDegrees = localYRotationDegrees;
        }

        public Material Material { get; }
        /// <summary>FloorBlock local Y(도). 베이크 메시는축정렬, 윗면 UV만 이 각도로 회전.</summary>
        public float LocalYRotationDegrees { get; }
    }

    sealed class BlockClassification
    {
        public readonly List<GridLayer> Layers = new List<GridLayer>();
        public readonly List<GameObject> LooseBlocks = new List<GameObject>();

        public int GridBlockCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < Layers.Count; i++)
                    count += Layers[i].CellTops.Count;
                return count;
            }
        }
    }

    readonly struct BlockEntry
    {
        public BlockEntry(MeshRenderer renderer, Vector3 localCenter, Quaternion localRotation)
        {
            Renderer = renderer;
            LocalCenter = localCenter;
            LocalRotation = localRotation;
        }

        public MeshRenderer Renderer { get; }
        public Vector3 LocalCenter { get; }
        public Quaternion LocalRotation { get; }
    }

    static readonly Vector3Int[] FaceDirections =
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(0, -1, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1)
    };

    public static GameObject GenerateFromPrefab(string sourcePrefabGuid, string outputPrefabPath, VoxelFloorGenerationSettings settings)
    {
        var sourcePath = AssetDatabase.GUIDToAssetPath(sourcePrefabGuid);
        if (string.IsNullOrEmpty(sourcePath))
        {
            Debug.LogError($"[Voxel Floor Mesh] guid에 해당하는 프리팹을 찾지 못했습니다: {sourcePrefabGuid}");
            return null;
        }

        var contentsRoot = PrefabUtility.LoadPrefabContents(sourcePath);
        try
        {
            return Generate(new[] { contentsRoot }, outputPrefabPath, settings);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contentsRoot);
        }
    }

    public static GameObject Generate(IReadOnlyList<GameObject> sourceRoots, string outputPrefabPath, VoxelFloorGenerationSettings settings)
    {
        if (string.IsNullOrEmpty(outputPrefabPath) || settings.BlockSize <= 0f)
            return null;

        var roots = NormalizeRoots(sourceRoots);
        if (roots.Count == 0)
            return null;

        var classification = Classify(roots, settings, out var pivotRoot);
        var gridBlockCount = classification.GridBlockCount;
        var hasLoose = settings.IncludeOffGridBlocks && classification.LooseBlocks.Count > 0;

        if (gridBlockCount == 0 && !hasLoose)
            return null;

        var appliedColliderCount = 0;

        EnsureAssetFolder(outputPrefabPath);

        var tempRoot = new GameObject(System.IO.Path.GetFileNameWithoutExtension(outputPrefabPath));
        tempRoot.transform.SetPositionAndRotation(pivotRoot.transform.position, pivotRoot.transform.rotation);
        tempRoot.transform.localScale = Vector3.one;

        var savedMeshPaths = new List<string>();
        Mesh colliderMesh = null;

        if (gridBlockCount > 0)
        {
            // Y 오프셋 레이어마다 자식 MeshRenderer를 둬서 계단식 높이가 계층/뷰에서 구분되게 한다.
            var solidCenters = CollectSolidCentersBlocks(classification);
            for (var layerIndex = 0; layerIndex < classification.Layers.Count; layerIndex++)
            {
                var layer = classification.Layers[layerIndex];
                var layerMesh = BuildVoxelMeshForLayer(layer, settings, solidCenters, out var materials);
                if (layerMesh == null)
                    continue;

                var yOffset = layer.Phase.y * settings.BlockSize;
                var layerSuffix = $"Y{yOffset:0.##}".Replace('.', 'p');
                layerMesh.name = System.IO.Path.GetFileNameWithoutExtension(outputPrefabPath) + "_Mesh_" + layerSuffix;
                var layerMeshPath = GetMeshAssetPath(outputPrefabPath, layerSuffix);
                layerMesh = SaveOrReplaceMeshAsset(layerMesh, layerMeshPath);
                savedMeshPaths.Add(layerMeshPath);

                var layerGo = new GameObject($"YLayer_{yOffset:0.##}");
                layerGo.transform.SetParent(tempRoot.transform, false);
                var meshFilter = layerGo.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = layerMesh;
                var meshRenderer = layerGo.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterials = materials;

                if (colliderMesh == null)
                    colliderMesh = layerMesh;
            }

            // SingleMeshCollider용: 레이어를 합친 메시
            if (settings.ColliderMode == VoxelFloorColliderMode.SingleMeshCollider)
            {
                colliderMesh = BuildVoxelMesh(classification, settings, out _);
                if (colliderMesh != null)
                {
                    colliderMesh.name = System.IO.Path.GetFileNameWithoutExtension(outputPrefabPath) + "_Mesh_Combined";
                    var combinedPath = GetMeshAssetPath(outputPrefabPath, "Combined");
                    colliderMesh = SaveOrReplaceMeshAsset(colliderMesh, combinedPath);
                    savedMeshPaths.Add(combinedPath);
                }
            }

            ApplyColliders(tempRoot, colliderMesh, classification, settings, out appliedColliderCount);
        }

        if (hasLoose)
            IncludeOffGridBlocks(tempRoot, pivotRoot, classification.LooseBlocks);

        ApplyLayer(tempRoot, settings.Layer);

        var savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempRoot, outputPrefabPath);
        Object.DestroyImmediate(tempRoot);

        if (savedPrefab == null)
        {
            foreach (var path in savedMeshPaths)
                AssetDatabase.DeleteAsset(path);

            return null;
        }

        foreach (var path in savedMeshPaths)
        {
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        AssetDatabase.ImportAsset(
            outputPrefabPath,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();

        savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(outputPrefabPath);

        Debug.Log(
            $"[Voxel Floor Mesh] Saved prefab: {outputPrefabPath}, grid: {gridBlockCount}, y-layers: {classification.Layers.Count} [{FormatLayerSummary(classification, settings)}], colliders: {appliedColliderCount}, off-grid: {(hasLoose ? classification.LooseBlocks.Count : 0)}");
        return savedPrefab;
    }

    public static PreviewInfo BuildPreview(IReadOnlyList<GameObject> sourceRoots, VoxelFloorGenerationSettings settings)
    {
        var roots = NormalizeRoots(sourceRoots);
        if (roots.Count == 0 || settings.BlockSize <= 0f)
            return new PreviewInfo();

        var classification = Classify(roots, settings, out _);
        var exposed = 0;
        for (var i = 0; i < classification.Layers.Count; i++)
            exposed += CountExposedFaces(classification.Layers[i].CellTops.Keys.ToHashSet());

        return new PreviewInfo
        {
            GridBlockCount = classification.GridBlockCount,
            OffGridBlockCount = classification.LooseBlocks.Count,
            ExposedFaceCount = exposed,
            CulledInnerFaceCount = classification.GridBlockCount * 6 - exposed,
            TopMaterialVariantCount = CountTopMaterialVariants(classification)
        };
    }

    public static List<GameObject> NormalizeRoots(IReadOnlyList<GameObject> sources)
    {
        var validSources = sources.Where(go => go != null).Distinct().ToList();
        var result = new List<GameObject>();

        foreach (var source in validSources)
        {
            var isChildOfAnotherSelected = false;
            var parent = source.transform.parent;
            while (parent != null)
            {
                if (validSources.Contains(parent.gameObject))
                {
                    isChildOfAnotherSelected = true;
                    break;
                }

                parent = parent.parent;
            }

            if (!isChildOfAnotherSelected)
                result.Add(source);
        }

        return result;
    }

    static BlockClassification Classify(IReadOnlyList<GameObject> roots, VoxelFloorGenerationSettings settings, out GameObject pivotRoot)
    {
        var classification = new BlockClassification();
        pivotRoot = roots.Count > 0 ? roots[0] : null;

        if (pivotRoot == null || settings.BlockSize <= 0f)
            return classification;

        var pivotTransform = pivotRoot.transform;
        var scannedRenderers = new HashSet<MeshRenderer>();
        var entries = new List<BlockEntry>();

        foreach (var root in roots)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!scannedRenderers.Add(renderer))
                    continue;

                var localCenter = pivotTransform.InverseTransformPoint(renderer.transform.position);
                var localRotation = Quaternion.Inverse(pivotTransform.rotation) * renderer.transform.rotation;
                entries.Add(new BlockEntry(renderer, localCenter, localRotation));
            }
        }

        if (entries.Count == 0)
            return classification;

        var blockSize = settings.BlockSize;
        var phaseX = ComputeAxisPhase(entries.Select(e => e.LocalCenter.x / blockSize).ToList());
        var phaseZ = ComputeAxisPhase(entries.Select(e => e.LocalCenter.z / blockSize).ToList());
        var toleranceInBlocks = settings.GridTolerance / blockSize;
        var yStepInBlocks = ResolveYStepInBlocks(settings);

        var entriesByYPhase = new Dictionary<int, List<BlockEntry>>();
        foreach (var entry in entries)
        {
            var ratioY = entry.LocalCenter.y / blockSize;
            var phaseIndex = QuantizeYPhaseIndex(Frac(ratioY), yStepInBlocks);
            if (!entriesByYPhase.TryGetValue(phaseIndex, out var group))
            {
                group = new List<BlockEntry>();
                entriesByYPhase.Add(phaseIndex, group);
            }

            group.Add(entry);
        }

        foreach (var pair in entriesByYPhase.OrderBy(p => p.Key))
        {
            var phaseY = pair.Key * yStepInBlocks;
            if (phaseY >= 1f)
                phaseY = 0f;

            var layer = new GridLayer
            {
                Phase = new Vector3(phaseX, phaseY, phaseZ)
            };

            foreach (var entry in pair.Value)
            {
                var ratio = entry.LocalCenter / blockSize;
                if (!TryGetGridYRotation(entry.LocalRotation, out var localYRotation))
                {
                    classification.LooseBlocks.Add(entry.Renderer.gameObject);
                    continue;
                }

                var cell = new Vector3Int(
                    Mathf.RoundToInt(ratio.x - phaseX),
                    Mathf.RoundToInt(ratio.y - phaseY),
                    Mathf.RoundToInt(ratio.z - phaseZ));

                var alignedX = Mathf.Abs(ratio.x - (cell.x + phaseX)) <= toleranceInBlocks;
                var alignedY = Mathf.Abs(ratio.y - (cell.y + phaseY)) <= toleranceInBlocks;
                var alignedZ = Mathf.Abs(ratio.z - (cell.z + phaseZ)) <= toleranceInBlocks;

                if (alignedX && alignedY && alignedZ)
                {
                    layer.CellTops[cell] = new CellTop(
                        ResolveTopMaterial(entry.Renderer, settings),
                        localYRotation);
                }
                else
                {
                    classification.LooseBlocks.Add(entry.Renderer.gameObject);
                }
            }

            if (layer.CellTops.Count > 0)
                classification.Layers.Add(layer);
        }

        return classification;
    }

    /// <summary>
    /// 격자 병합 가능: up이 유지되고 Yaw가 0/90/180/270에 스냅되는 경우만.
    /// (길 페인트 E/W 끝단 Length_1 ±90° 포함)
    /// </summary>
    static bool TryGetGridYRotation(Quaternion localRotation, out float yDegrees)
    {
        yDegrees = 0f;
        var up = localRotation * Vector3.up;
        if (Vector3.Angle(up, Vector3.up) > RotationAlignThreshold)
            return false;

        var yaw = localRotation.eulerAngles.y;
        var snapped = Mathf.Round(yaw / 90f) * 90f;
        if (Mathf.Abs(Mathf.DeltaAngle(yaw, snapped)) > RotationAlignThreshold)
            return false;

        yDegrees = Mathf.DeltaAngle(0f, snapped);
        return true;
    }

    /// <summary>
    /// YSubGridStep을 블록 단위 잔여 위상 간격으로 변환. 0 이하면 단일 위상(블록 1칸)으로 취급.
    /// </summary>
    static float ResolveYStepInBlocks(VoxelFloorGenerationSettings settings)
    {
        if (settings.YSubGridStep <= 0f || settings.BlockSize <= 0f)
            return 1f;

        var step = settings.YSubGridStep / settings.BlockSize;
        return Mathf.Clamp(step, 0.0001f, 1f);
    }

    static int QuantizeYPhaseIndex(float frac, float yStepInBlocks)
    {
        var stepsPerBlock = Mathf.Max(1, Mathf.RoundToInt(1f / yStepInBlocks));
        var index = Mathf.RoundToInt(frac / yStepInBlocks);
        index %= stepsPerBlock;
        if (index < 0)
            index += stepsPerBlock;
        return index;
    }

    static Material ResolveTopMaterial(MeshRenderer renderer, VoxelFloorGenerationSettings settings)
    {
        if (settings.PreserveTopMaterialPerBlock)
        {
            var sharedMaterials = renderer.sharedMaterials;
            // Sand 블록은 전면 Sand이므로 슬롯0에서도 감지한다.
            for (var i = 0; i < sharedMaterials.Length; i++)
            {
                if (IslandFloorBlockUtility.IsSandMaterial(sharedMaterials[i]))
                    return IslandFloorBlockUtility.SandMaterial != null
                        ? IslandFloorBlockUtility.SandMaterial
                        : sharedMaterials[i];
            }

            if (settings.TopMaterialSlot >= 0 && settings.TopMaterialSlot < sharedMaterials.Length &&
                sharedMaterials[settings.TopMaterialSlot] != null)
                return sharedMaterials[settings.TopMaterialSlot];
        }

        return settings.TopMaterial;
    }

    /// <summary>
    /// FloorBlock과 동일: Sand 윗면이면 옆면도 Sand, 그 외는 SideMaterial(cliff).
    /// </summary>
    static Material ResolveSideMaterial(Material topMaterial, VoxelFloorGenerationSettings settings)
    {
        if (IslandFloorBlockUtility.IsSandMaterial(topMaterial))
            return IslandFloorBlockUtility.SandMaterial != null ? IslandFloorBlockUtility.SandMaterial : topMaterial;

        return settings.SideMaterial != null ? settings.SideMaterial : settings.TopMaterial;
    }

    /// <summary>
    /// FloorBlock과 동일: Sand 윗면이면 아랫면도 Sand, 그 외는 BottomMaterial(cliff).
    /// </summary>
    static Material ResolveBottomMaterial(Material topMaterial, VoxelFloorGenerationSettings settings)
    {
        if (IslandFloorBlockUtility.IsSandMaterial(topMaterial))
            return IslandFloorBlockUtility.SandMaterial != null ? IslandFloorBlockUtility.SandMaterial : topMaterial;

        return settings.BottomMaterial != null ? settings.BottomMaterial : settings.SideMaterial;
    }

    static float ComputeAxisPhase(List<float> ratios)
    {
        var fractions = ratios.Select(Frac).ToList();
        const float clusterTolerance = 0.1f;

        var bestPhase = fractions[0];
        var bestCount = -1;

        foreach (var candidate in fractions)
        {
            var count = fractions.Count(f => CircularDistance(f, candidate) <= clusterTolerance);
            if (count > bestCount)
            {
                bestCount = count;
                bestPhase = candidate;
            }
        }

        return bestPhase;
    }

    static float Frac(float value)
    {
        return value - Mathf.Floor(value);
    }

    static float CircularDistance(float a, float b)
    {
        var distance = Mathf.Abs(a - b);
        return Mathf.Min(distance, 1f - distance);
    }

    static int CountExposedFaces(HashSet<Vector3Int> gridCells)
    {
        var count = 0;
        foreach (var block in gridCells)
        {
            foreach (var direction in FaceDirections)
            {
                if (!gridCells.Contains(block + direction))
                    count++;
            }
        }

        return count;
    }

    static int CountTopMaterialVariants(BlockClassification classification)
    {
        return classification.Layers
            .SelectMany(layer => layer.CellTops.Values)
            .Select(cell => cell.Material)
            .Where(m => m != null)
            .Distinct()
            .Count();
    }

    static Mesh BuildVoxelMesh(BlockClassification classification, VoxelFloorGenerationSettings settings, out Material[] materials)
    {
        var blockSize = settings.BlockSize;
        var half = blockSize * 0.5f;
        var solidCenters = CollectSolidCentersBlocks(classification);

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        var topTrianglesByMaterial = new Dictionary<Material, List<int>>();
        var topMaterialOrder = new List<Material>();
        var sideTrianglesByMaterial = new Dictionary<Material, List<int>>();
        var sideMaterialOrder = new List<Material>();
        var bottomTrianglesByMaterial = new Dictionary<Material, List<int>>();
        var bottomMaterialOrder = new List<Material>();

        foreach (var layer in classification.Layers)
        {
            AppendLayerFaces(
                layer, settings, half, blockSize, solidCenters,
                vertices, normals, uvs,
                topTrianglesByMaterial, topMaterialOrder,
                sideTrianglesByMaterial, sideMaterialOrder,
                bottomTrianglesByMaterial, bottomMaterialOrder);
        }

        return FinalizeVoxelMesh(
            vertices, normals, uvs,
            topTrianglesByMaterial, topMaterialOrder,
            sideTrianglesByMaterial, sideMaterialOrder,
            bottomTrianglesByMaterial, bottomMaterialOrder,
            out materials);
    }

    static Mesh BuildVoxelMeshForLayer(
        GridLayer layer,
        VoxelFloorGenerationSettings settings,
        List<Vector3> solidCenters,
        out Material[] materials)
    {
        var blockSize = settings.BlockSize;
        var half = blockSize * 0.5f;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        var topTrianglesByMaterial = new Dictionary<Material, List<int>>();
        var topMaterialOrder = new List<Material>();
        var sideTrianglesByMaterial = new Dictionary<Material, List<int>>();
        var sideMaterialOrder = new List<Material>();
        var bottomTrianglesByMaterial = new Dictionary<Material, List<int>>();
        var bottomMaterialOrder = new List<Material>();

        AppendLayerFaces(
            layer, settings, half, blockSize, solidCenters,
            vertices, normals, uvs,
            topTrianglesByMaterial, topMaterialOrder,
            sideTrianglesByMaterial, sideMaterialOrder,
            bottomTrianglesByMaterial, bottomMaterialOrder);
        return FinalizeVoxelMesh(
            vertices, normals, uvs,
            topTrianglesByMaterial, topMaterialOrder,
            sideTrianglesByMaterial, sideMaterialOrder,
            bottomTrianglesByMaterial, bottomMaterialOrder,
            out materials);
    }

    static List<Vector3> CollectSolidCentersBlocks(BlockClassification classification)
    {
        var solids = new List<Vector3>();
        foreach (var layer in classification.Layers)
        {
            foreach (var cell in layer.CellTops.Keys)
            {
                solids.Add(new Vector3(
                    cell.x + layer.Phase.x,
                    cell.y + layer.Phase.y,
                    cell.z + layer.Phase.z));
            }
        }

        return solids;
    }

    const float SolidNeighborTol = 0.08f;

    static bool HasSideNeighbor(List<Vector3> solids, Vector3 centerBlocks, int dx, int dz)
    {
        var tx = centerBlocks.x + dx;
        var tz = centerBlocks.z + dz;
        for (var i = 0; i < solids.Count; i++)
        {
            var s = solids[i];
            if (Mathf.Abs(s.x - tx) > SolidNeighborTol || Mathf.Abs(s.z - tz) > SolidNeighborTol)
                continue;
            // 같은 높이 이웃만 옆면을 가린다.
            // 높이 단차가 있으면 위·아래 블록 모두 맞닿는 옆면을 유지한다.
            if (Mathf.Abs(s.y - centerBlocks.y) <= SolidNeighborTol)
                return true;
        }

        return false;
    }

    static bool HasVerticalNeighbor(List<Vector3> solids, Vector3 centerBlocks, int dy)
    {
        var ty = centerBlocks.y + dy;
        for (var i = 0; i < solids.Count; i++)
        {
            var s = solids[i];
            if (Mathf.Abs(s.x - centerBlocks.x) > SolidNeighborTol ||
                Mathf.Abs(s.z - centerBlocks.z) > SolidNeighborTol)
                continue;
            if (Mathf.Abs(s.y - ty) <= SolidNeighborTol)
                return true;
        }

        return false;
    }

    static void AppendLayerFaces(
        GridLayer layer,
        VoxelFloorGenerationSettings settings,
        float half,
        float blockSize,
        List<Vector3> solidCenters,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        Dictionary<Material, List<int>> topTrianglesByMaterial,
        List<Material> topMaterialOrder,
        Dictionary<Material, List<int>> sideTrianglesByMaterial,
        List<Material> sideMaterialOrder,
        Dictionary<Material, List<int>> bottomTrianglesByMaterial,
        List<Material> bottomMaterialOrder)
    {
        var phase = layer.Phase;
        var radius = ResolveChamferSize(settings, half);
        var segments = Mathf.Max(1, settings.ChamferSegments);

        foreach (var pair in layer.CellTops)
        {
            var cell = pair.Key;
            var cellTop = pair.Value;
            var cellTopMaterial = cellTop.Material != null ? cellTop.Material : settings.TopMaterial;
            var cellYRot = cellTop.LocalYRotationDegrees;
            var cellSideMaterial = ResolveSideMaterial(cellTopMaterial, settings);
            var cellBottomMaterial = ResolveBottomMaterial(cellTopMaterial, settings);
            var centerBlocks = new Vector3(cell.x + phase.x, cell.y + phase.y, cell.z + phase.z);
            var center = centerBlocks * blockSize;

            var expPosX = !HasSideNeighbor(solidCenters, centerBlocks, 1, 0);
            var expNegX = !HasSideNeighbor(solidCenters, centerBlocks, -1, 0);
            var expPosZ = !HasSideNeighbor(solidCenters, centerBlocks, 0, 1);
            var expNegZ = !HasSideNeighbor(solidCenters, centerBlocks, 0, -1);
            var expPosY = !HasVerticalNeighbor(solidCenters, centerBlocks, 1);
            var expNegY = !HasVerticalNeighbor(solidCenters, centerBlocks, -1);

            var sideTriangles = GetOrCreateMaterialTriangles(
                cellSideMaterial, sideTrianglesByMaterial, sideMaterialOrder);
            var bottomTriangles = GetOrCreateMaterialTriangles(
                cellBottomMaterial, bottomTrianglesByMaterial, bottomMaterialOrder);

            if (radius <= 0f)
            {
                var flatSideUvStart = vertices.Count;
                if (expPosX)
                    AddFace(center, Vector3.right, half, vertices, normals, uvs, sideTriangles);
                if (expNegX)
                    AddFace(center, Vector3.left, half, vertices, normals, uvs, sideTriangles);
                if (expPosZ)
                    AddFace(center, Vector3.forward, half, vertices, normals, uvs, sideTriangles);
                if (expNegZ)
                    AddFace(center, Vector3.back, half, vertices, normals, uvs, sideTriangles);
                if (expNegY)
                    AddFace(center, Vector3.down, half, vertices, normals, uvs, bottomTriangles);
                RewriteSideUvs(vertices, normals, uvs, flatSideUvStart, blockSize, center.y + half);

                if (expPosY)
                {
                    AddTopFace(
                        center, half, cellYRot, vertices, normals, uvs,
                        GetOrCreateMaterialTriangles(cellTopMaterial, topTrianglesByMaterial, topMaterialOrder));
                }

                continue;
            }

            AppendRoundedCell(
                center,
                centerBlocks,
                half,
                radius,
                segments,
                solidCenters,
                expPosX, expNegX, expPosY, expNegY, expPosZ, expNegZ,
                cellTopMaterial,
                cellYRot,
                vertices, normals, uvs,
                topTrianglesByMaterial, topMaterialOrder,
                sideTriangles, bottomTriangles);
        }
    }

    static float ResolveChamferSize(VoxelFloorGenerationSettings settings, float half)
    {
        if (!settings.EnableChamfer || settings.ChamferSize <= 0f || half <= 0f)
            return 0f;

        return Mathf.Min(settings.ChamferSize, half * 0.49f);
    }

    static float EdgeExtent(bool hasNeighbor, int sign, float half, float radius)
    {
        return hasNeighbor ? sign * half : sign * (half - radius);
    }

    static void AppendRoundedCell(
        Vector3 center,
        Vector3 centerBlocks,
        float half,
        float radius,
        int segments,
        List<Vector3> solidCenters,
        bool expPosX, bool expNegX, bool expPosY, bool expNegY, bool expPosZ, bool expNegZ,
        Material cellTopMaterial,
        float cellYRot,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        Dictionary<Material, List<int>> topTrianglesByMaterial,
        List<Material> topMaterialOrder,
        List<int> sideTriangles,
        List<int> bottomTriangles)
    {
        var topTriangles = GetOrCreateMaterialTriangles(cellTopMaterial, topTrianglesByMaterial, topMaterialOrder);
        var hasPosX = !expPosX;
        var hasNegX = !expNegX;
        var hasPosZ = !expPosZ;
        var hasNegZ = !expNegZ;
        var hasPosY = !expPosY;
        var hasNegY = !expNegY;

        if (expPosY)
        {
            var x0 = EdgeExtent(hasNegX, -1, half, radius);
            var x1 = EdgeExtent(hasPosX, 1, half, radius);
            var z0 = EdgeExtent(hasNegZ, -1, half, radius);
            var z1 = EdgeExtent(hasPosZ, 1, half, radius);
            var uvStart = vertices.Count;
            AddQuadFlat(
                center + new Vector3(x0, half, z0),
                center + new Vector3(x0, half, z1),
                center + new Vector3(x1, half, z1),
                center + new Vector3(x1, half, z0),
                Vector3.up, vertices, normals, uvs, topTriangles);
            RewriteTopUvs(vertices, uvs, uvStart, center, half, cellYRot);
        }

        var sideUvStart = vertices.Count;
        if (expNegY)
        {
            var x0 = EdgeExtent(hasNegX, -1, half, radius);
            var x1 = EdgeExtent(hasPosX, 1, half, radius);
            var z0 = EdgeExtent(hasNegZ, -1, half, radius);
            var z1 = EdgeExtent(hasPosZ, 1, half, radius);
            AddQuadFlat(
                center + new Vector3(x0, -half, z0),
                center + new Vector3(x1, -half, z0),
                center + new Vector3(x1, -half, z1),
                center + new Vector3(x0, -half, z1),
                Vector3.down, vertices, normals, uvs, bottomTriangles);
        }

        if (expPosX)
            AddRoundedSideFace(center, half, radius, 1, 0, hasNegY, hasPosY, hasNegX, hasPosX, hasNegZ, hasPosZ, vertices, normals, uvs, sideTriangles);
        if (expNegX)
            AddRoundedSideFace(center, half, radius, -1, 0, hasNegY, hasPosY, hasNegX, hasPosX, hasNegZ, hasPosZ, vertices, normals, uvs, sideTriangles);
        if (expPosZ)
            AddRoundedSideFace(center, half, radius, 0, 1, hasNegY, hasPosY, hasNegX, hasPosX, hasNegZ, hasPosZ, vertices, normals, uvs, sideTriangles);
        if (expNegZ)
            AddRoundedSideFace(center, half, radius, 0, -1, hasNegY, hasPosY, hasNegX, hasPosX, hasNegZ, hasPosZ, vertices, normals, uvs, sideTriangles);
        RewriteSideUvs(vertices, normals, uvs, sideUvStart, half * 2f, center.y + EdgeExtent(hasPosY, 1, half, radius));

        // Convex horizontal rims
        if (expPosY && expPosX)
            AddHorizontalRoundEdgeTop(center, half, radius, segments, 1, 0, hasNegZ, hasPosZ, cellYRot, vertices, normals, uvs, topTriangles);
        if (expPosY && expNegX)
            AddHorizontalRoundEdgeTop(center, half, radius, segments, -1, 0, hasNegZ, hasPosZ, cellYRot, vertices, normals, uvs, topTriangles);
        if (expPosY && expPosZ)
            AddHorizontalRoundEdgeTop(center, half, radius, segments, 0, 1, hasNegX, hasPosX, cellYRot, vertices, normals, uvs, topTriangles);
        if (expPosY && expNegZ)
            AddHorizontalRoundEdgeTop(center, half, radius, segments, 0, -1, hasNegX, hasPosX, cellYRot, vertices, normals, uvs, topTriangles);

        // 하단 림 아크·수직 라운드 엣지도 측면과 동일한 박스 프로젝션 UV로
        // 재기록해 세그먼트 쿼드마다 0..1 반복(과다 타일링)을 없앤다.
        var edgeUvStart = vertices.Count;
        if (expNegY && expPosX)
            AddHorizontalRoundEdge(center, half, radius, segments, 1, 0, false, hasNegZ, hasPosZ, vertices, normals, uvs, bottomTriangles);
        if (expNegY && expNegX)
            AddHorizontalRoundEdge(center, half, radius, segments, -1, 0, false, hasNegZ, hasPosZ, vertices, normals, uvs, bottomTriangles);
        if (expNegY && expPosZ)
            AddHorizontalRoundEdge(center, half, radius, segments, 0, 1, false, hasNegX, hasPosX, vertices, normals, uvs, bottomTriangles);
        if (expNegY && expNegZ)
            AddHorizontalRoundEdge(center, half, radius, segments, 0, -1, false, hasNegX, hasPosX, vertices, normals, uvs, bottomTriangles);

        // Convex vertical edges
        if (expPosX && expPosZ)
            AddVerticalRoundEdge(center, half, radius, segments, 1, 1, hasNegY, hasPosY, vertices, normals, uvs, sideTriangles);
        if (expPosX && expNegZ)
            AddVerticalRoundEdge(center, half, radius, segments, 1, -1, hasNegY, hasPosY, vertices, normals, uvs, sideTriangles);
        if (expNegX && expPosZ)
            AddVerticalRoundEdge(center, half, radius, segments, -1, 1, hasNegY, hasPosY, vertices, normals, uvs, sideTriangles);
        if (expNegX && expNegZ)
            AddVerticalRoundEdge(center, half, radius, segments, -1, -1, hasNegY, hasPosY, vertices, normals, uvs, sideTriangles);
        RewriteSideUvs(vertices, normals, uvs, edgeUvStart, half * 2f, center.y + EdgeExtent(hasPosY, 1, half, radius));

        // Convex trihedral corners — top uses planar UV
        TryAddRoundCornerTop(center, half, radius, segments, expPosX, expPosY, expPosZ, 1, 1, 1, cellYRot, topTriangles, vertices, normals, uvs);
        TryAddRoundCornerTop(center, half, radius, segments, expNegX, expPosY, expPosZ, -1, 1, 1, cellYRot, topTriangles, vertices, normals, uvs);
        TryAddRoundCornerTop(center, half, radius, segments, expPosX, expPosY, expNegZ, 1, 1, -1, cellYRot, topTriangles, vertices, normals, uvs);
        TryAddRoundCornerTop(center, half, radius, segments, expNegX, expPosY, expNegZ, -1, 1, -1, cellYRot, topTriangles, vertices, normals, uvs);
        var bottomCornerUvStart = vertices.Count;
        TryAddRoundCorner(center, half, radius, segments, expPosX, expNegY, expPosZ, 1, -1, 1, bottomTriangles, vertices, normals, uvs);
        TryAddRoundCorner(center, half, radius, segments, expNegX, expNegY, expPosZ, -1, -1, 1, bottomTriangles, vertices, normals, uvs);
        TryAddRoundCorner(center, half, radius, segments, expPosX, expNegY, expNegZ, 1, -1, -1, bottomTriangles, vertices, normals, uvs);
        TryAddRoundCorner(center, half, radius, segments, expNegX, expNegY, expNegZ, -1, -1, -1, bottomTriangles, vertices, normals, uvs);
        RewriteSideUvs(vertices, normals, uvs, bottomCornerUvStart, half * 2f, center.y + EdgeExtent(hasPosY, 1, half, radius));

        TryAddConcaveCornerPlug(center, centerBlocks, half, radius, solidCenters, 1, 1, sideTriangles, vertices, normals, uvs);
        TryAddConcaveCornerPlug(center, centerBlocks, half, radius, solidCenters, -1, 1, sideTriangles, vertices, normals, uvs);
        TryAddConcaveCornerPlug(center, centerBlocks, half, radius, solidCenters, 1, -1, sideTriangles, vertices, normals, uvs);
        TryAddConcaveCornerPlug(center, centerBlocks, half, radius, solidCenters, -1, -1, sideTriangles, vertices, normals, uvs);
    }

    static void AddRoundedSideFace(
        Vector3 center,
        float half,
        float radius,
        int dirX,
        int dirZ,
        bool hasNegY,
        bool hasPosY,
        bool hasNegX,
        bool hasPosX,
        bool hasNegZ,
        bool hasPosZ,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var y0 = EdgeExtent(hasNegY, -1, half, radius);
        var y1 = EdgeExtent(hasPosY, 1, half, radius);

        if (dirX != 0)
        {
            var x = dirX * half;
            var z0 = EdgeExtent(hasNegZ, -1, half, radius);
            var z1 = EdgeExtent(hasPosZ, 1, half, radius);
            var normal = new Vector3(dirX, 0f, 0f);
            if (dirX > 0)
            {
                AddQuadFlat(
                    center + new Vector3(x, y0, z0),
                    center + new Vector3(x, y1, z0),
                    center + new Vector3(x, y1, z1),
                    center + new Vector3(x, y0, z1),
                    normal, vertices, normals, uvs, triangles);
            }
            else
            {
                AddQuadFlat(
                    center + new Vector3(x, y0, z0),
                    center + new Vector3(x, y0, z1),
                    center + new Vector3(x, y1, z1),
                    center + new Vector3(x, y1, z0),
                    normal, vertices, normals, uvs, triangles);
            }

            return;
        }

        var z = dirZ * half;
        var x0 = EdgeExtent(hasNegX, -1, half, radius);
        var x1 = EdgeExtent(hasPosX, 1, half, radius);
        var normalZ = new Vector3(0f, 0f, dirZ);
        if (dirZ > 0)
        {
            AddQuadFlat(
                center + new Vector3(x0, y0, z),
                center + new Vector3(x0, y1, z),
                center + new Vector3(x1, y1, z),
                center + new Vector3(x1, y0, z),
                normalZ, vertices, normals, uvs, triangles);
        }
        else
        {
            AddQuadFlat(
                center + new Vector3(x0, y0, z),
                center + new Vector3(x1, y0, z),
                center + new Vector3(x1, y1, z),
                center + new Vector3(x0, y1, z),
                normalZ, vertices, normals, uvs, triangles);
        }
    }

    static void AddHorizontalRoundEdgeTop(
        Vector3 center,
        float half,
        float radius,
        int segments,
        int dirX,
        int dirZ,
        bool hasAlongNeg,
        bool hasAlongPos,
        float cellYRot,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var uvStart = vertices.Count;
        AddHorizontalRoundEdge(
            center, half, radius, segments, dirX, dirZ, true, hasAlongNeg, hasAlongPos,
            vertices, normals, uvs, triangles);
        RewriteTopUvs(vertices, uvs, uvStart, center, half, cellYRot);
    }

    static void TryAddRoundCornerTop(
        Vector3 center,
        float half,
        float radius,
        int segments,
        bool expX,
        bool expY,
        bool expZ,
        int sx,
        int sy,
        int sz,
        float cellYRot,
        List<int> triangles,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs)
    {
        var uvStart = vertices.Count;
        TryAddRoundCorner(
            center, half, radius, segments, expX, expY, expZ, sx, sy, sz,
            triangles, vertices, normals, uvs);
        RewriteTopUvs(vertices, uvs, uvStart, center, half, cellYRot);
    }

    static void AddHorizontalRoundEdge(
        Vector3 center,
        float half,
        float radius,
        int segments,
        int dirX,
        int dirZ,
        bool top,
        bool hasAlongNeg,
        bool hasAlongPos,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var along0 = EdgeExtent(hasAlongNeg, -1, half, radius);
        var along1 = EdgeExtent(hasAlongPos, 1, half, radius);
        var ySign = top ? 1f : -1f;
        var ox = dirX != 0 ? dirX * (half - radius) : 0f;
        var oz = dirZ != 0 ? dirZ * (half - radius) : 0f;
        var oy = ySign * (half - radius);

        for (var i = 0; i < segments; i++)
        {
            var a0 = (i / (float)segments) * Mathf.PI * 0.5f;
            var a1 = ((i + 1) / (float)segments) * Mathf.PI * 0.5f;
            var ca0 = Mathf.Cos(a0);
            var sa0 = Mathf.Sin(a0);
            var ca1 = Mathf.Cos(a1);
            var sa1 = Mathf.Sin(a1);

            Vector3 p00, p01, p11, p10, n0, n1;
            if (dirX != 0)
            {
                p00 = center + new Vector3(ox + dirX * radius * sa0, oy + ySign * radius * ca0, along0);
                p01 = center + new Vector3(ox + dirX * radius * sa0, oy + ySign * radius * ca0, along1);
                p11 = center + new Vector3(ox + dirX * radius * sa1, oy + ySign * radius * ca1, along1);
                p10 = center + new Vector3(ox + dirX * radius * sa1, oy + ySign * radius * ca1, along0);
                n0 = new Vector3(dirX * sa0, ySign * ca0, 0f).normalized;
                n1 = new Vector3(dirX * sa1, ySign * ca1, 0f).normalized;
            }
            else
            {
                p00 = center + new Vector3(along0, oy + ySign * radius * ca0, oz + dirZ * radius * sa0);
                p01 = center + new Vector3(along1, oy + ySign * radius * ca0, oz + dirZ * radius * sa0);
                p11 = center + new Vector3(along1, oy + ySign * radius * ca1, oz + dirZ * radius * sa1);
                p10 = center + new Vector3(along0, oy + ySign * radius * ca1, oz + dirZ * radius * sa1);
                n0 = new Vector3(0f, ySign * ca0, dirZ * sa0).normalized;
                n1 = new Vector3(0f, ySign * ca1, dirZ * sa1).normalized;
            }

            if (top)
                AddQuadSmooth(p00, p01, p11, p10, n0, n0, n1, n1, vertices, normals, uvs, triangles);
            else
                AddQuadSmooth(p00, p10, p11, p01, n0, n1, n1, n0, vertices, normals, uvs, triangles);
        }
    }

    static void AddVerticalRoundEdge(
        Vector3 center,
        float half,
        float radius,
        int segments,
        int dirX,
        int dirZ,
        bool hasNegY,
        bool hasPosY,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var y0 = EdgeExtent(hasNegY, -1, half, radius);
        var y1 = EdgeExtent(hasPosY, 1, half, radius);
        var ox = dirX * (half - radius);
        var oz = dirZ * (half - radius);

        for (var i = 0; i < segments; i++)
        {
            var a0 = (i / (float)segments) * Mathf.PI * 0.5f;
            var a1 = ((i + 1) / (float)segments) * Mathf.PI * 0.5f;
            var ca0 = Mathf.Cos(a0);
            var sa0 = Mathf.Sin(a0);
            var ca1 = Mathf.Cos(a1);
            var sa1 = Mathf.Sin(a1);

            var p00 = center + new Vector3(ox + dirX * radius * ca0, y0, oz + dirZ * radius * sa0);
            var p01 = center + new Vector3(ox + dirX * radius * ca0, y1, oz + dirZ * radius * sa0);
            var p11 = center + new Vector3(ox + dirX * radius * ca1, y1, oz + dirZ * radius * sa1);
            var p10 = center + new Vector3(ox + dirX * radius * ca1, y0, oz + dirZ * radius * sa1);
            var n0 = new Vector3(dirX * ca0, 0f, dirZ * sa0).normalized;
            var n1 = new Vector3(dirX * ca1, 0f, dirZ * sa1).normalized;
            AddQuadSmooth(p00, p01, p11, p10, n0, n0, n1, n1, vertices, normals, uvs, triangles);
        }
    }

    static void TryAddRoundCorner(
        Vector3 center,
        float half,
        float radius,
        int segments,
        bool expX,
        bool expY,
        bool expZ,
        int sx,
        int sy,
        int sz,
        List<int> triangles,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs)
    {
        if (!expX || !expY || !expZ)
            return;

        var ox = sx * (half - radius);
        var oy = sy * (half - radius);
        var oz = sz * (half - radius);

        for (var i = 0; i < segments; i++)
        {
            var u0 = (i / (float)segments) * Mathf.PI * 0.5f;
            var u1 = ((i + 1) / (float)segments) * Mathf.PI * 0.5f;
            for (var j = 0; j < segments; j++)
            {
                var v0 = (j / (float)segments) * Mathf.PI * 0.5f;
                var v1 = ((j + 1) / (float)segments) * Mathf.PI * 0.5f;

                var y0 = Mathf.Cos(u0);
                var r0 = Mathf.Sin(u0);
                var y1c = Mathf.Cos(u1);
                var r1 = Mathf.Sin(u1);
                var x00 = r0 * Mathf.Cos(v0);
                var z00 = r0 * Mathf.Sin(v0);
                var x01 = r0 * Mathf.Cos(v1);
                var z01 = r0 * Mathf.Sin(v1);
                var x10 = r1 * Mathf.Cos(v0);
                var z10 = r1 * Mathf.Sin(v0);
                var x11 = r1 * Mathf.Cos(v1);
                var z11 = r1 * Mathf.Sin(v1);

                var p00 = center + new Vector3(ox + sx * radius * x00, oy + sy * radius * y0, oz + sz * radius * z00);
                var p01 = center + new Vector3(ox + sx * radius * x01, oy + sy * radius * y0, oz + sz * radius * z01);
                var p11 = center + new Vector3(ox + sx * radius * x11, oy + sy * radius * y1c, oz + sz * radius * z11);
                var p10 = center + new Vector3(ox + sx * radius * x10, oy + sy * radius * y1c, oz + sz * radius * z10);
                var n00 = new Vector3(sx * x00, sy * y0, sz * z00).normalized;
                var n01 = new Vector3(sx * x01, sy * y0, sz * z01).normalized;
                var n11 = new Vector3(sx * x11, sy * y1c, sz * z11).normalized;
                var n10 = new Vector3(sx * x10, sy * y1c, sz * z10).normalized;
                AddQuadSmooth(p00, p01, p11, p10, n00, n01, n11, n10, vertices, normals, uvs, triangles);
            }
        }
    }

    /// <summary>
    /// ㄱ자(오목) 코너: 이웃 chamfer inset으로 두 절벽 면·림 호가 만나지 못해 생기는
    /// 세로 틈(호 단면 캡)을 L자 벽으로 메운다. 윗면은 셀 본체 top이 풀 코너까지 덮는다.
    /// </summary>
    static void TryAddConcaveCornerPlug(
        Vector3 center,
        Vector3 centerBlocks,
        float half,
        float radius,
        List<Vector3> solidCenters,
        int sx,
        int sz,
        List<int> sideTriangles,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs)
    {
        if (sideTriangles == null || radius <= 0f)
            return;

        var hasX = HasSideNeighbor(solidCenters, centerBlocks, sx, 0);
        var hasZ = HasSideNeighbor(solidCenters, centerBlocks, 0, sz);
        if (!hasX || !hasZ)
            return;
        if (HasSideNeighbor(solidCenters, centerBlocks, sx, sz))
            return;

        var y0 = center.y - half;
        var y1 = center.y + half;
        var cx = center.x + sx * half;
        var cz = center.z + sz * half;
        var r = Mathf.Min(radius, half * 0.98f);
        var plugUvStart = vertices.Count;

        // X 이웃의 chamfer 포켓(공기)은 x가 코너 평면보다 +sx 쪽 → 벽은 +sx를 바라봐야 보인다.
        var ax0 = new Vector3(cx, y0, cz);
        var ax1 = new Vector3(cx, y1, cz);
        var ax2 = new Vector3(cx, y1, cz - sz * r);
        var ax3 = new Vector3(cx, y0, cz - sz * r);
        AddQuadFlat(ax0, ax1, ax2, ax3, new Vector3(sx, 0f, 0f), vertices, normals, uvs, sideTriangles);

        // Z 이웃의 chamfer 포켓은 z가 코너 평면보다 +sz 쪽 → 벽은 +sz를 바라봐야 보인다.
        var az0 = new Vector3(cx, y0, cz);
        var az1 = new Vector3(cx, y1, cz);
        var az2 = new Vector3(cx - sx * r, y1, cz);
        var az3 = new Vector3(cx - sx * r, y0, cz);
        AddQuadFlat(az0, az3, az2, az1, new Vector3(0f, 0f, sz), vertices, normals, uvs, sideTriangles);

        // 플러그 벽 상단은 셀 top 평면과 만나므로 거기에 그래스 보더가 오도록 앵커한다.
        RewriteSideUvs(vertices, normals, uvs, plugUvStart, half * 2f, y1);
    }

    static void AddQuadFlat(
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 v3,
        Vector3 normal,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        AddQuadSmooth(v0, v1, v2, v3, normal, normal, normal, normal, vertices, normals, uvs, triangles);
    }

    static void AddQuadSmooth(
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 v3,
        Vector3 n0,
        Vector3 n1,
        Vector3 n2,
        Vector3 n3,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var baseIndex = vertices.Count;
        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v3);
        normals.Add(n0);
        normals.Add(n1);
        normals.Add(n2);
        normals.Add(n3);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));

        // 구면 패치 극점처럼 v0==v1로 첫 삼각형이 퇴화하면 외적이 0이 되어
        // 와인딩 판정이 임의로 고정된다. 두 삼각형 노멀 합으로 판정해 퇴화에 안전하게 한다.
        var triangleNormal = Vector3.Cross(v1 - v0, v2 - v0) + Vector3.Cross(v2 - v0, v3 - v0);
        var guide = n0 + n1 + n2 + n3;
        if (Vector3.Dot(triangleNormal, guide) >= 0f)
        {
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }
        else
        {
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }
    }

    static void AddTriangleFlat(
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 normal,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var baseIndex = vertices.Count;
        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 0f));

        var triangleNormal = Vector3.Cross(v1 - v0, v2 - v0);
        if (Vector3.Dot(triangleNormal, normal) >= 0f)
        {
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
        }
        else
        {
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
        }
    }

    static Mesh FinalizeVoxelMesh(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        Dictionary<Material, List<int>> topTrianglesByMaterial,
        List<Material> topMaterialOrder,
        Dictionary<Material, List<int>> sideTrianglesByMaterial,
        List<Material> sideMaterialOrder,
        Dictionary<Material, List<int>> bottomTrianglesByMaterial,
        List<Material> bottomMaterialOrder,
        out Material[] materials)
    {
        if (vertices.Count == 0)
        {
            materials = System.Array.Empty<Material>();
            return null;
        }

        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);

        var submeshTriangles = new List<List<int>>();
        var submeshMaterials = new List<Material>();

        AppendMaterialSubmeshes(topMaterialOrder, topTrianglesByMaterial, submeshTriangles, submeshMaterials);
        AppendMaterialSubmeshes(sideMaterialOrder, sideTrianglesByMaterial, submeshTriangles, submeshMaterials);
        AppendMaterialSubmeshes(bottomMaterialOrder, bottomTrianglesByMaterial, submeshTriangles, submeshMaterials);

        mesh.subMeshCount = submeshTriangles.Count;
        for (var i = 0; i < submeshTriangles.Count; i++)
            mesh.SetTriangles(submeshTriangles[i], i);

        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        materials = submeshMaterials.ToArray();
        return mesh;
    }

    static void AppendMaterialSubmeshes(
        List<Material> materialOrder,
        Dictionary<Material, List<int>> trianglesByMaterial,
        List<List<int>> submeshTriangles,
        List<Material> submeshMaterials)
    {
        foreach (var material in materialOrder)
        {
            if (!trianglesByMaterial.TryGetValue(material, out var triangles) || triangles.Count == 0)
                continue;

            submeshTriangles.Add(triangles);
            submeshMaterials.Add(material);
        }
    }

    static string FormatLayerSummary(BlockClassification classification, VoxelFloorGenerationSettings settings)
    {
        var parts = new List<string>(classification.Layers.Count);
        foreach (var layer in classification.Layers)
        {
            var yOffset = layer.Phase.y * settings.BlockSize;
            parts.Add($"{yOffset:0.##}x{layer.CellTops.Count}");
        }

        return string.Join(", ", parts);
    }

    static List<int> GetOrCreateMaterialTriangles(
        Material material,
        Dictionary<Material, List<int>> trianglesByMaterial,
        List<Material> materialOrder)
    {
        if (!trianglesByMaterial.TryGetValue(material, out var triangles))
        {
            triangles = new List<int>();
            trianglesByMaterial.Add(material, triangles);
            materialOrder.Add(material);
        }

        return triangles;
    }

    /// <summary>
    /// FloorBlock 윗면 UV와 동일: U=+X, V=+Z. localYRot는 길 페인트 E/W 끝단 회전 반영.
    /// </summary>
    static void AddTopFace(
        Vector3 cubeCenter,
        float half,
        float localYRotDeg,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var v0 = cubeCenter + new Vector3(-half, half, -half);
        var v1 = cubeCenter + new Vector3(-half, half, half);
        var v2 = cubeCenter + new Vector3(half, half, half);
        var v3 = cubeCenter + new Vector3(half, half, -half);

        var baseIndex = vertices.Count;
        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v3);
        normals.Add(Vector3.up);
        normals.Add(Vector3.up);
        normals.Add(Vector3.up);
        normals.Add(Vector3.up);
        uvs.Add(TopUvFromWorld(v0, cubeCenter, half, localYRotDeg));
        uvs.Add(TopUvFromWorld(v1, cubeCenter, half, localYRotDeg));
        uvs.Add(TopUvFromWorld(v2, cubeCenter, half, localYRotDeg));
        uvs.Add(TopUvFromWorld(v3, cubeCenter, half, localYRotDeg));

        triangles.Add(baseIndex + 0);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 0);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 3);
    }

    static Vector2 TopUvFromWorld(Vector3 worldVertex, Vector3 cellCenter, float half, float localYRotDeg)
    {
        var worldDx = worldVertex.x - cellCenter.x;
        var worldDz = worldVertex.z - cellCenter.z;
        // 블록 local Y 회전의 역변환 → FloorBlock 메시 로컬 좌표의 UV
        var theta = -localYRotDeg * Mathf.Deg2Rad;
        var cos = Mathf.Cos(theta);
        var sin = Mathf.Sin(theta);
        var localX = worldDx * cos + worldDz * sin;
        var localZ = -worldDx * sin + worldDz * cos;
        var inv = 1f / (2f * half);
        return new Vector2(localX * inv + 0.5f, localZ * inv + 0.5f);
    }

    static void RewriteTopUvs(
        List<Vector3> vertices,
        List<Vector2> uvs,
        int fromIndex,
        Vector3 cellCenter,
        float half,
        float localYRotDeg)
    {
        for (var i = fromIndex; i < vertices.Count; i++)
            uvs[i] = TopUvFromWorld(vertices[i], cellCenter, half, localYRotDeg);
    }

    /// <summary>
    /// 측면·하단 UV를 노멀 지배축 기준 박스 프로젝션으로 재기록.
    /// 세로면은 V=Y(정방향 보장), 블록 한 면이 UV 1타일이 되도록 1/blockSize 스케일.
    /// vTopAnchor(면 상단 Y)가 V=1이 되도록 정렬해, 절벽 텍스쳐 상단의
    /// 그래스 보더 행이 면 최상단에만 나타나게 한다.
    /// </summary>
    static void RewriteSideUvs(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        int fromIndex,
        float blockSize,
        float vTopAnchor)
    {
        var inv = 1f / blockSize;
        for (var i = fromIndex; i < vertices.Count; i++)
        {
            var p = vertices[i];
            var n = normals[i];
            var ax = Mathf.Abs(n.x);
            var ay = Mathf.Abs(n.y);
            var az = Mathf.Abs(n.z);
            Vector2 uv;
            if (ay >= ax && ay >= az)
                uv = new Vector2(p.x * inv, p.z * inv);
            else if (ax >= az)
                uv = new Vector2((n.x > 0f ? p.z : -p.z) * inv, (p.y - vTopAnchor) * inv + 1f);
            else
                uv = new Vector2((n.z > 0f ? -p.x : p.x) * inv, (p.y - vTopAnchor) * inv + 1f);

            uvs[i] = uv;
        }
    }

    static void AddFace(
        Vector3 cubeCenter,
        Vector3 normal,
        float half,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var up = Mathf.Abs(normal.y) > 0.5f ? Vector3.forward : Vector3.up;
        var right = Vector3.Cross(up, normal).normalized;
        up = Vector3.Cross(normal, right).normalized;

        var faceCenter = cubeCenter + normal * half;
        var v0 = faceCenter - right * half - up * half;
        var v1 = faceCenter - right * half + up * half;
        var v2 = faceCenter + right * half + up * half;
        var v3 = faceCenter + right * half - up * half;

        var baseIndex = vertices.Count;
        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v3);

        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));

        var triangleNormal = Vector3.Cross(v1 - v0, v2 - v0);
        if (Vector3.Dot(triangleNormal, normal) >= 0f)
        {
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }
        else
        {
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }
    }

    static void ApplyColliders(
        GameObject root,
        Mesh mesh,
        BlockClassification classification,
        VoxelFloorGenerationSettings settings,
        out int colliderCount)
    {
        colliderCount = 0;

        switch (settings.ColliderMode)
        {
            case VoxelFloorColliderMode.None:
                break;

            case VoxelFloorColliderMode.SingleMeshCollider:
                var meshCollider = root.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = mesh;
                meshCollider.convex = settings.MeshColliderConvex;
                colliderCount = 1;
                break;

            case VoxelFloorColliderMode.BoxColliderPerBlock:
                foreach (var layer in classification.Layers)
                {
                    var phase = layer.Phase;
                    foreach (var cell in layer.CellTops.Keys)
                    {
                        var boxCollider = root.AddComponent<BoxCollider>();
                        boxCollider.center = new Vector3(cell.x + phase.x, cell.y + phase.y, cell.z + phase.z) * settings.BlockSize;
                        boxCollider.size = Vector3.one * settings.BlockSize;
                        colliderCount++;
                    }
                }

                break;

            case VoxelFloorColliderMode.MergedBoxColliders:
                foreach (var layer in classification.Layers)
                {
                    colliderCount += AddMergedBoxColliders(
                        root,
                        layer.CellTops.Keys.ToHashSet(),
                        layer.Phase,
                        settings.BlockSize);
                }

                break;
        }
    }

    /// <summary>
    /// Same-Y grid cells를 greedy 사각형으로 묶어 BoxCollider를 추가한다.
    /// </summary>
    static int AddMergedBoxColliders(
        GameObject root,
        HashSet<Vector3Int> gridCells,
        Vector3 phase,
        float blockSize)
    {
        if (gridCells.Count == 0)
            return 0;

        var cellsByY = new Dictionary<int, HashSet<Vector2Int>>();
        foreach (var cell in gridCells)
        {
            if (!cellsByY.TryGetValue(cell.y, out var layer))
            {
                layer = new HashSet<Vector2Int>();
                cellsByY.Add(cell.y, layer);
            }

            layer.Add(new Vector2Int(cell.x, cell.z));
        }

        var colliderCount = 0;
        foreach (var pair in cellsByY)
        {
            var y = pair.Key;
            var remaining = pair.Value;

            while (remaining.Count > 0)
            {
                var seed = PickMinCell(remaining);
                var maxX = seed.x;
                while (remaining.Contains(new Vector2Int(maxX + 1, seed.y)))
                    maxX++;

                var maxZ = seed.y;
                while (CanExpandRow(remaining, seed.x, maxX, maxZ + 1))
                    maxZ++;

                for (var x = seed.x; x <= maxX; x++)
                {
                    for (var z = seed.y; z <= maxZ; z++)
                        remaining.Remove(new Vector2Int(x, z));
                }

                var width = maxX - seed.x + 1;
                var depth = maxZ - seed.y + 1;
                var boxCollider = root.AddComponent<BoxCollider>();
                boxCollider.center = new Vector3(
                    (seed.x + maxX) * 0.5f + phase.x,
                    y + phase.y,
                    (seed.y + maxZ) * 0.5f + phase.z) * blockSize;
                boxCollider.size = new Vector3(width, 1f, depth) * blockSize;
                colliderCount++;
            }
        }

        return colliderCount;
    }

    static Vector2Int PickMinCell(HashSet<Vector2Int> cells)
    {
        var best = default(Vector2Int);
        var hasBest = false;
        foreach (var cell in cells)
        {
            if (!hasBest ||
                cell.y < best.y ||
                (cell.y == best.y && cell.x < best.x))
            {
                best = cell;
                hasBest = true;
            }
        }

        return best;
    }

    static bool CanExpandRow(HashSet<Vector2Int> remaining, int minX, int maxX, int z)
    {
        for (var x = minX; x <= maxX; x++)
        {
            if (!remaining.Contains(new Vector2Int(x, z)))
                return false;
        }

        return true;
    }

    static void IncludeOffGridBlocks(GameObject root, GameObject pivotRoot, List<GameObject> looseBlocks)
    {
        var container = new GameObject("OffGrid");
        container.transform.SetParent(root.transform, false);

        var pivotTransform = pivotRoot.transform;

        foreach (var source in looseBlocks)
        {
            if (source == null)
                continue;

            var copy = Object.Instantiate(source);
            copy.name = source.name;
            copy.transform.SetParent(container.transform, false);
            copy.transform.localPosition = pivotTransform.InverseTransformPoint(source.transform.position);
            copy.transform.localRotation = Quaternion.Inverse(pivotTransform.rotation) * source.transform.rotation;
            copy.transform.localScale = source.transform.lossyScale;
        }
    }

    static void ApplyLayer(GameObject root, int layer)
    {
        if (root == null || layer < 0 || layer > 31)
            return;

        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = layer;
    }

    static string GetMeshAssetPath(string prefabPath, string suffix = null)
    {
        var directory = System.IO.Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
        var prefabNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
        if (string.IsNullOrEmpty(suffix))
            return $"{directory}/{prefabNameWithoutExtension}_Mesh.asset";

        return $"{directory}/{prefabNameWithoutExtension}_Mesh_{suffix}.asset";
    }

    /// <summary>
    /// 기존 경로에 CreateAsset만 하면 실패하거나 프리팹 MeshFilter 참조가 Missing으로 남는다.
    /// GUID를 유지한 채 메시 데이터를 교체하고, 재임포트 후 에셋 참조를 반환한다.
    /// </summary>
    static Mesh SaveOrReplaceMeshAsset(Mesh sourceMesh, string meshAssetPath)
    {
        EnsureAssetFolder(meshAssetPath);

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(sourceMesh, existing);
            existing.name = System.IO.Path.GetFileNameWithoutExtension(meshAssetPath);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(sourceMesh);
        }
        else
        {
            // 파일이 있지만 Mesh로 로드되지 않는 깨진 에셋이면 제거 후 재생성
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(meshAssetPath)))
                AssetDatabase.DeleteAsset(meshAssetPath);

            AssetDatabase.CreateAsset(sourceMesh, meshAssetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(
            meshAssetPath,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
    }

    static void EnsureAssetFolder(string assetPath)
    {
        var directory = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(directory))
            return;

        if (AssetDatabase.IsValidFolder(directory))
            return;

        var parts = directory.Split('/');
        var current = parts[0];

        for (var i = 1; i < parts.Length; i++)
        {
            var next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }
}
