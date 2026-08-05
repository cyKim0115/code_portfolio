using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class IslandFloorMeshBakeUtility
{
    private const string FloorParentChildName = "Floor Parent";
    private const string FloorChildName = "Floor";
    private const string IndependentedFolderName = "Independented";
    private const string GeneratedFolderName = "Generated";
    private const string UndoGroupNameBake = "Island Floor Mesh Bake";
    private const string UndoGroupNameToggle = "Island Floor Prefab Toggle";

    enum FloorIssue
    {
        None,
        Absent,
        MissingPrefab,
        Empty
    }

    readonly struct FloorBakeTarget
    {
        public FloorBakeTarget(GameObject floor, string levelName, string assetLevelKey, FloorIssue issue)
        {
            Floor = floor;
            LevelName = levelName;
            AssetLevelKey = assetLevelKey;
            Issue = issue;
        }

        public GameObject Floor { get; }
        public string LevelName { get; }
        public string AssetLevelKey { get; }
        public FloorIssue Issue { get; }
        public bool IsBakeable => Issue == FloorIssue.None && Floor != null;
    }

    public static void Bake(IslandBase island)
    {
        if (island == null)
        {
            Debug.LogError("[IslandFloorMeshBakeUtility] island가 null입니다.");
            return;
        }

        if (!TryResolveBodyPrefabPath(island, out var bodyPrefabPath, out var bodyName))
        {
            Debug.LogError($"[IslandFloorMeshBakeUtility] 본체 프리팹 경로를 찾지 못했습니다. ({island.name})");
            return;
        }

        var floorParent = ResolveFloorParent(island);
        if (floorParent == null)
        {
            Debug.LogError($"[IslandFloorMeshBakeUtility] '{FloorParentChildName}'를 찾지 못했습니다. ({island.name})");
            return;
        }

        var bodyDirectory = Path.GetDirectoryName(bodyPrefabPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(bodyDirectory))
        {
            Debug.LogError($"[IslandFloorMeshBakeUtility] 본체 프리팹 디렉터리를 해석하지 못했습니다. ({bodyPrefabPath})");
            return;
        }

        var bodyAssetFolder = $"{bodyDirectory}/{bodyName}";
        var independentedFolder = $"{bodyAssetFolder}/{IndependentedFolderName}";
        var generatedFolder = $"{bodyAssetFolder}/{GeneratedFolderName}";

        var initialTargets = CollectFloorTargets(floorParent, logIssues: false);
        var needsIndependentedRestore = HasAnyGeneratedFloor(initialTargets, generatedFolder, bodyName);
        if (needsIndependentedRestore && !AreAllBakeAssetsReady(island))
        {
            Debug.LogError(
                $"[IslandFloorMeshBakeUtility] 이미 Generated Floor인데 Independented 백업이 없어 재베이크할 수 없습니다. " +
                $"Independented/Generated 프리팹을 확인하세요. ({island.name})");
            return;
        }

        var precheckTargets = CollectFloorTargets(floorParent, logIssues: true);
        if (!TryValidateBakeTargets(island, precheckTargets, out var bakeTargets))
            return;

        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoGroupNameBake);

        var bakedCount = 0;
        var failedCount = 0;
        var settings = VoxelFloorGenerationSettingsAsset.ResolveForIslandBake();

        try
        {
            if (needsIndependentedRestore)
            {
                Debug.Log($"[IslandFloorMeshBakeUtility] Generated Floor 감지 → Independented로 복원 후 재베이크합니다. ({island.name})");
                if (!SwitchAllFloors(island, toIndependented: true, manageUndoGroup: false))
                    return;

                floorParent = ResolveFloorParent(island);
                if (floorParent == null)
                {
                    Debug.LogError($"[IslandFloorMeshBakeUtility] 복원 후 '{FloorParentChildName}'를 찾지 못했습니다. ({island.name})");
                    return;
                }
            }

            // 에셋 폴더를 지우기 전에 씬 Floor를 베이크 에셋에서 분리한다.
            // (삭제된 프리팹을 참조한 채 Undo하면 Missing Prefab이 되므로, 언팩 클론으로 교체해 Undo 시에도 메시가 남게 한다.)
            if (!DetachBakeFloorsFromAssetFolders(
                    floorParent, bakeTargets, independentedFolder, generatedFolder))
            {
                Debug.LogError($"[IslandFloorMeshBakeUtility] Floor 에셋 분리 실패로 베이크를 중단합니다. ({island.name})");
                return;
            }

            // 본체 프리팹 에셋이 Independented/Generated를 중첩 참조하면 폴더 삭제 시 임포트가 깨지므로 먼저 분리한다.
            DetachBakeFloorsInBodyPrefabAsset(
                bodyPrefabPath, bakeTargets, independentedFolder, generatedFolder);

            ClearAndRecreateBakeFolders(independentedFolder, generatedFolder);

            for (var i = 0; i < bakeTargets.Count; i++)
            {
                var target = bakeTargets[i];
                EditorUtility.DisplayProgressBar(
                    UndoGroupNameBake,
                    $"층계 '{target.LevelName}' Floor 병합 ({i + 1}/{bakeTargets.Count})",
                    (float)i / bakeTargets.Count);

                var level = FindLevelTransform(floorParent, target.LevelName, i < floorParent.childCount ? i : -1);
                var liveFloor = level != null ? FindFloorChild(level, out _) : null;
                if (liveFloor == null)
                {
                    Debug.LogError(
                        $"[IslandFloorMeshBakeUtility] 층계 '{target.LevelName}' Floor를 베이크 중 찾지 못했습니다. " +
                        $"이전 층까지 부분 적용됐을 수 있습니다. ({island.name})");
                    failedCount++;
                    break;
                }

                if (!BakeFloorLevel(
                        liveFloor.gameObject,
                        target.LevelName,
                        target.AssetLevelKey,
                        bodyName,
                        independentedFolder,
                        generatedFolder,
                        settings))
                {
                    failedCount++;
                    Debug.LogError(
                        $"[IslandFloorMeshBakeUtility] 층계 '{target.LevelName}' 베이크 실패. " +
                        $"이후 층은 중단합니다. 부분 적용 상태일 수 있으니 Independented로 전환 후 재시도하세요. ({island.name})");
                    break;
                }

                bakedCount++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Undo.CollapseUndoOperations(undoGroup);
        }

        if (failedCount > 0)
        {
            Debug.LogError(
                $"[IslandFloorMeshBakeUtility] Floor 메쉬 병합 실패. baked({bakedCount}/{bakeTargets.Count}) failed({failedCount}) ({island.name})");
            return;
        }

        if (bakedCount == 0)
        {
            Debug.LogWarning(
                $"[IslandFloorMeshBakeUtility] 처리된 Floor가 없습니다. targets({bakeTargets.Count}) ({island.name})");
            return;
        }

        EditorUtility.SetDirty(island);
        Debug.Log(
            $"[IslandFloorMeshBakeUtility] Floor 메쉬 병합 완료. baked({bakedCount}/{bakeTargets.Count}) levels({floorParent.childCount}) body({bodyName})");
    }

    /// <summary>
    /// Floor Parent 각 층계에 대해 Independented/Generated 프리팹이 모두 있으면 true.
    /// Missing/Empty Floor 층은 제외하고, 베이크 가능한 층이 하나 이상 있어야 한다.
    /// </summary>
    public static bool AreAllBakeAssetsReady(IslandBase island)
    {
        if (island == null)
            return false;

        if (!TryResolveBodyPrefabPath(island, out var bodyPrefabPath, out var bodyName))
            return false;

        var floorParent = ResolveFloorParent(island);
        if (floorParent == null || floorParent.childCount == 0)
            return false;

        var targets = CollectFloorTargets(floorParent, logIssues: false);
        var bakeTargets = targets.FindAll(t => t.IsBakeable);
        if (bakeTargets.Count == 0)
            return false;

        if (targets.Exists(t => t.Issue == FloorIssue.MissingPrefab || t.Issue == FloorIssue.Empty))
            return false;

        var bodyDirectory = Path.GetDirectoryName(bodyPrefabPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(bodyDirectory))
            return false;

        var bodyAssetFolder = $"{bodyDirectory}/{bodyName}";
        var independentedFolder = $"{bodyAssetFolder}/{IndependentedFolderName}";
        var generatedFolder = $"{bodyAssetFolder}/{GeneratedFolderName}";

        foreach (var target in bakeTargets)
        {
            var independentedPath = GetIndependentedPrefabPath(independentedFolder, bodyName, target.AssetLevelKey);
            var generatedPath = GetGeneratedPrefabPath(generatedFolder, bodyName, target.AssetLevelKey);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(independentedPath) == null)
                return false;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(generatedPath) == null)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 현재 Floor 중 Generated(이미 병합된) 프리팹 인스턴스가 하나라도 있으면 true.
    /// </summary>
    public static bool AreAnyFloorsGenerated(IslandBase island)
    {
        if (island == null)
            return false;

        if (!TryResolveBodyPrefabPath(island, out var bodyPrefabPath, out var bodyName))
            return false;

        var floorParent = ResolveFloorParent(island);
        if (floorParent == null)
            return false;

        var targets = CollectFloorTargets(floorParent, logIssues: false);
        var bakeTargets = targets.FindAll(t => t.IsBakeable);
        if (bakeTargets.Count == 0)
            return false;

        var bodyDirectory = Path.GetDirectoryName(bodyPrefabPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(bodyDirectory))
            return false;

        var generatedFolder = $"{bodyDirectory}/{bodyName}/{GeneratedFolderName}";
        return HasAnyGeneratedFloor(bakeTargets, generatedFolder, bodyName);
    }

    /// <summary>
    /// Missing Prefab / 빈 Floor 등 베이크 불가 상태가 있으면 true.
    /// </summary>
    public static bool HasBlockingFloorIssues(IslandBase island, out string summary)
    {
        summary = null;
        if (island == null)
            return false;

        var floorParent = ResolveFloorParent(island);
        if (floorParent == null)
            return false;

        var targets = CollectFloorTargets(floorParent, logIssues: false);
        var broken = targets.FindAll(t => t.Issue == FloorIssue.MissingPrefab || t.Issue == FloorIssue.Empty);
        if (broken.Count == 0)
            return false;

        var sb = new StringBuilder();
        sb.Append("베이크 불가 Floor: ");
        for (var i = 0; i < broken.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append('\'').Append(broken[i].LevelName).Append("'(").Append(DescribeIssue(broken[i].Issue)).Append(')');
        }

        summary = sb.ToString();
        return true;
    }

    /// <summary>
    /// 첫 Floor가 Generated면 Independented로, 아니면 Generated로 모든 Floor를 전환한다.
    /// </summary>
    public static void Toggle(IslandBase island)
    {
        if (island == null)
        {
            Debug.LogError("[IslandFloorMeshBakeUtility] island가 null입니다.");
            return;
        }

        if (!AreAllBakeAssetsReady(island))
        {
            Debug.LogWarning($"[IslandFloorMeshBakeUtility] 전환에 필요한 프리팹이 없습니다. ({island.name})");
            return;
        }

        if (!TryResolveBodyPrefabPath(island, out var bodyPrefabPath, out var bodyName))
        {
            Debug.LogError($"[IslandFloorMeshBakeUtility] 본체 프리팹 경로를 찾지 못했습니다. ({island.name})");
            return;
        }

        var floorParent = ResolveFloorParent(island);
        var targets = CollectFloorTargets(floorParent, logIssues: true).FindAll(t => t.IsBakeable);
        if (targets.Count == 0)
            return;

        var bodyDirectory = Path.GetDirectoryName(bodyPrefabPath)?.Replace('\\', '/');
        var generatedFolder = $"{bodyDirectory}/{bodyName}/{GeneratedFolderName}";
        var toIndependented = IsGeneratedFloorInstance(
            targets[0].Floor, generatedFolder, bodyName, targets[0].AssetLevelKey);

        SwitchAllFloors(island, toIndependented);
    }

    static bool SwitchAllFloors(IslandBase island, bool toIndependented, bool manageUndoGroup = true)
    {
        if (!TryResolveBodyPrefabPath(island, out var bodyPrefabPath, out var bodyName))
        {
            Debug.LogError($"[IslandFloorMeshBakeUtility] 본체 프리팹 경로를 찾지 못했습니다. ({island.name})");
            return false;
        }

        var floorParent = ResolveFloorParent(island);
        if (floorParent == null)
            return false;

        var targets = CollectFloorTargets(floorParent, logIssues: true).FindAll(t => t.IsBakeable);
        if (targets.Count == 0)
        {
            Debug.LogWarning($"[IslandFloorMeshBakeUtility] 전환할 Floor가 없습니다. ({island.name})");
            return false;
        }

        var bodyDirectory = Path.GetDirectoryName(bodyPrefabPath)?.Replace('\\', '/');
        var bodyAssetFolder = $"{bodyDirectory}/{bodyName}";
        var independentedFolder = $"{bodyAssetFolder}/{IndependentedFolderName}";
        var generatedFolder = $"{bodyAssetFolder}/{GeneratedFolderName}";

        var undoGroup = 0;
        if (manageUndoGroup)
        {
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoGroupNameToggle);
        }

        var swapCount = 0;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                EditorUtility.DisplayProgressBar(
                    UndoGroupNameToggle,
                    $"층계 '{target.LevelName}' Floor 전환 ({i + 1}/{targets.Count})",
                    (float)i / targets.Count);

                var level = FindLevelTransform(floorParent, target.LevelName, i < floorParent.childCount ? i : -1);
                var liveFloor = level != null ? FindFloorChild(level, out _) : null;
                if (liveFloor == null)
                    continue;

                var prefabPath = toIndependented
                    ? GetIndependentedPrefabPath(independentedFolder, bodyName, target.AssetLevelKey)
                    : GetGeneratedPrefabPath(generatedFolder, bodyName, target.AssetLevelKey);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"[IslandFloorMeshBakeUtility] 전환 대상 프리팹 없음: {prefabPath}");
                    continue;
                }

                if (!ReplaceFloorWithPrefab(liveFloor.gameObject, prefab, manageUndoGroup ? UndoGroupNameToggle : UndoGroupNameBake))
                    continue;

                swapCount++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (manageUndoGroup)
                Undo.CollapseUndoOperations(undoGroup);
        }

        EditorUtility.SetDirty(island);
        var modeLabel = toIndependented ? IndependentedFolderName : GeneratedFolderName;
        Debug.Log($"[IslandFloorMeshBakeUtility] Floor 전환 완료 → {modeLabel} count({swapCount}/{targets.Count}) body({bodyName})");
        return swapCount > 0;
    }

    static bool TryValidateBakeTargets(IslandBase island, List<FloorBakeTarget> targets, out List<FloorBakeTarget> bakeTargets)
    {
        bakeTargets = new List<FloorBakeTarget>();
        if (targets.Count == 0)
        {
            Debug.LogWarning($"[IslandFloorMeshBakeUtility] 처리할 Floor가 없습니다. ({island.name})");
            return false;
        }

        var blocked = new List<FloorBakeTarget>();
        var skippedAbsent = 0;

        foreach (var target in targets)
        {
            if (target.Issue == FloorIssue.Absent)
            {
                skippedAbsent++;
                continue;
            }

            if (target.Issue == FloorIssue.MissingPrefab || target.Issue == FloorIssue.Empty)
            {
                blocked.Add(target);
                continue;
            }

            if (target.IsBakeable)
                bakeTargets.Add(target);
        }

        if (blocked.Count > 0)
        {
            foreach (var target in blocked)
            {
                Debug.LogError(
                    $"[IslandFloorMeshBakeUtility] 층계 '{target.LevelName}' Floor {DescribeIssue(target.Issue)}. " +
                    $"Missing Prefab이거나 메시 블록이 없으면 병합할 수 없습니다. ({island.name})");
            }

            Debug.LogError(
                $"[IslandFloorMeshBakeUtility] 문제 Floor {blocked.Count}개로 베이크를 중단합니다. " +
                $"일부 층만 병합되지 않도록 전체 중단합니다. ({island.name})");
            return false;
        }

        if (bakeTargets.Count == 0)
        {
            Debug.LogWarning(
                $"[IslandFloorMeshBakeUtility] 베이크 가능한 Floor가 없습니다. " +
                $"Floor 없는 층계({skippedAbsent}) ({island.name})");
            return false;
        }

        if (skippedAbsent > 0)
        {
            Debug.Log(
                $"[IslandFloorMeshBakeUtility] Floor 없는 층계 {skippedAbsent}개는 건너뜁니다. " +
                $"베이크 대상 {bakeTargets.Count}개 ({island.name})");
        }

        return true;
    }

    static bool BakeFloorLevel(
        GameObject floorGo,
        string levelName,
        string assetLevelKey,
        string bodyName,
        string independentedFolder,
        string generatedFolder,
        VoxelFloorGenerationSettings settings)
    {
        if (floorGo == null || !IsFloorObjectName(floorGo.name))
        {
            Debug.LogError($"[IslandFloorMeshBakeUtility] Floor 대상이 아닙니다. level({levelName}) name({floorGo?.name})");
            return false;
        }

        if (!HasMeshContent(floorGo.transform))
        {
            Debug.LogError(
                $"[IslandFloorMeshBakeUtility] 층계 '{levelName}' Floor에 MeshRenderer가 없습니다. path({GetTransformPath(floorGo.transform)})");
            return false;
        }

        var independentedPath = GetIndependentedPrefabPath(independentedFolder, bodyName, assetLevelKey);
        var generatedPath = GetGeneratedPrefabPath(generatedFolder, bodyName, assetLevelKey);

        EnsureFolder(independentedPath);

        if (!TrySaveIsolatedFloorPrefab(floorGo, independentedPath, out var independentedGuid))
            return false;

        var generatedPrefab = VoxelFloorMeshGenerator.GenerateFromPrefab(independentedGuid, generatedPath, settings);
        if (generatedPrefab == null)
        {
            Debug.LogError($"[IslandFloorMeshBakeUtility] Generated 프리팹 생성 실패: level({levelName}) path({generatedPath})");
            return false;
        }

        if (!ReplaceFloorWithPrefab(floorGo, generatedPrefab, UndoGroupNameBake))
            return false;

        Debug.Log($"[IslandFloorMeshBakeUtility] 층계 '{levelName}' Floor 병합 완료 → {generatedPath}");
        return true;
    }

    /// <summary>
    /// Independented/Generated 폴더 삭제 전에 씬 Floor를 해당 에셋에서 분리한다.
    /// 완전 언팩 클론으로 교체해 Undo 시에도 삭제된 에셋 GUID에 의존하지 않는다.
    /// </summary>
    static bool DetachBakeFloorsFromAssetFolders(
        Transform floorParent,
        List<FloorBakeTarget> bakeTargets,
        string independentedFolder,
        string generatedFolder)
    {
        for (var i = 0; i < bakeTargets.Count; i++)
        {
            var target = bakeTargets[i];
            var level = FindLevelTransform(floorParent, target.LevelName, i < floorParent.childCount ? i : -1);
            var liveFloor = level != null ? FindFloorChild(level, out _) : null;
            if (liveFloor == null)
            {
                Debug.LogError($"[IslandFloorMeshBakeUtility] 분리 대상 Floor 없음: 층계 '{target.LevelName}'");
                return false;
            }

            if (!TryGetReferencedBakeAssetPath(
                    liveFloor.gameObject, independentedFolder, generatedFolder, out var bakeAssetPath))
                continue;

            Debug.Log(
                $"[IslandFloorMeshBakeUtility] 층계 '{target.LevelName}' Floor를 베이크 에셋에서 분리합니다. ({bakeAssetPath})");

            if (!ReplaceFloorWithUnpackedClone(liveFloor.gameObject, UndoGroupNameBake))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 본체 프리팹 에셋 안의 Floor가 베이크 폴더를 참조하면 언팩 클론으로 바꿔 저장한다.
    /// 폴더 DeleteAsset 전에 호출해야 Island_*.prefab 임포트가 깨지지 않는다. (에셋 편집이라 Undo 없음)
    /// </summary>
    static void DetachBakeFloorsInBodyPrefabAsset(
        string bodyPrefabPath,
        List<FloorBakeTarget> bakeTargets,
        string independentedFolder,
        string generatedFolder)
    {
        if (string.IsNullOrEmpty(bodyPrefabPath) || bakeTargets == null || bakeTargets.Count == 0)
            return;

        var root = PrefabUtility.LoadPrefabContents(bodyPrefabPath);
        try
        {
            var island = root.GetComponent<IslandBase>() ?? root.GetComponentInChildren<IslandBase>(true);
            if (island == null)
                return;

            var floorParent = ResolveFloorParent(island);
            if (floorParent == null)
                return;

            var changed = false;
            for (var i = 0; i < bakeTargets.Count; i++)
            {
                var target = bakeTargets[i];
                var level = FindLevelTransform(floorParent, target.LevelName, -1);
                var floor = level != null ? FindFloorChild(level, out _) : null;
                if (floor == null)
                    continue;

                if (!TryGetReferencedBakeAssetPath(
                        floor.gameObject, independentedFolder, generatedFolder, out var bakeAssetPath))
                    continue;

                if (!ReplaceFloorWithUnpackedCloneNoUndo(floor.gameObject))
                {
                    Debug.LogError(
                        $"[IslandFloorMeshBakeUtility] 본체 프리팹 Floor 분리 실패: 층계 '{target.LevelName}' ({bakeAssetPath})");
                    continue;
                }

                changed = true;
                Debug.Log(
                    $"[IslandFloorMeshBakeUtility] 본체 프리팹 층계 '{target.LevelName}' Floor 베이크 에셋 분리 저장. ({bakeAssetPath})");
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, bodyPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static bool ReplaceFloorWithUnpackedCloneNoUndo(GameObject floorGo)
    {
        var parent = floorGo.transform.parent;
        var siblingIndex = floorGo.transform.GetSiblingIndex();
        var localPosition = floorGo.transform.localPosition;
        var localRotation = floorGo.transform.localRotation;
        var localScale = floorGo.transform.localScale;

        var clone = Object.Instantiate(floorGo);
        clone.name = FloorChildName;
        clone.transform.SetParent(null, true);
        UnpackPrefabInstancesCompletely(clone);

        if (!HasMeshContent(clone.transform))
        {
            Object.DestroyImmediate(clone);
            return false;
        }

        Object.DestroyImmediate(floorGo);

        clone.transform.SetParent(parent, false);
        clone.transform.localPosition = localPosition;
        clone.transform.localRotation = localRotation;
        clone.transform.localScale = localScale;
        clone.transform.SetSiblingIndex(siblingIndex);
        return true;
    }

    static bool TryGetReferencedBakeAssetPath(
        GameObject floorGo,
        string independentedFolder,
        string generatedFolder,
        out string bakeAssetPath)
    {
        bakeAssetPath = null;
        if (floorGo == null)
            return false;

        // Floor 자체가 Independented/Generated 인스턴스 루트인 경우
        if (PrefabUtility.IsAnyPrefabInstanceRoot(floorGo))
        {
            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(floorGo);
            if (IsUnderBakeFolder(path, independentedFolder, generatedFolder))
            {
                bakeAssetPath = path;
                return true;
            }
        }

        // 중첩 인스턴스(Floor 아래 베이크 프리팹)까지 검사
        var transforms = floorGo.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < transforms.Length; i++)
        {
            var go = transforms[i].gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
                continue;

            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (!IsUnderBakeFolder(path, independentedFolder, generatedFolder))
                continue;

            bakeAssetPath = path;
            return true;
        }

        return false;
    }

    static bool IsUnderBakeFolder(string assetPath, string independentedFolder, string generatedFolder)
    {
        return IsUnderFolder(assetPath, independentedFolder) || IsUnderFolder(assetPath, generatedFolder);
    }

    static bool IsUnderFolder(string assetPath, string folder)
    {
        if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(folder))
            return false;

        var normalizedPath = assetPath.Replace('\\', '/');
        var normalizedFolder = folder.Replace('\\', '/').TrimEnd('/');
        return normalizedPath.StartsWith(normalizedFolder + "/", System.StringComparison.Ordinal)
               || string.Equals(normalizedPath, normalizedFolder, System.StringComparison.Ordinal);
    }

    static bool ReplaceFloorWithUnpackedClone(GameObject floorGo, string undoName)
    {
        var parent = floorGo.transform.parent;
        var siblingIndex = floorGo.transform.GetSiblingIndex();
        var localPosition = floorGo.transform.localPosition;
        var localRotation = floorGo.transform.localRotation;
        var localScale = floorGo.transform.localScale;

        var clone = Object.Instantiate(floorGo);
        clone.name = FloorChildName;
        clone.transform.SetParent(null, true);
        UnpackPrefabInstancesCompletely(clone);

        if (!HasMeshContent(clone.transform))
        {
            Object.DestroyImmediate(clone);
            Debug.LogError(
                $"[IslandFloorMeshBakeUtility] 언팩 클론에 MeshRenderer가 없습니다: {GetTransformPath(floorGo.transform)}");
            return false;
        }

        Undo.DestroyObjectImmediate(floorGo);

        clone.transform.SetParent(parent, false);
        clone.transform.localPosition = localPosition;
        clone.transform.localRotation = localRotation;
        clone.transform.localScale = localScale;
        clone.transform.SetSiblingIndex(siblingIndex);
        Undo.RegisterCreatedObjectUndo(clone, undoName);
        return true;
    }

    /// <summary>
    /// Independented/Generated 폴더를 통째로 지우고 빈 폴더로 다시 만든다.
    /// AssetDatabase 삭제는 Undo되지 않으므로, 호출 전에 씬 Floor 분리를 마쳐야 한다.
    /// </summary>
    static void ClearAndRecreateBakeFolders(string independentedFolder, string generatedFolder)
    {
        var deletedIndependented = DeleteAssetFolderIfExists(independentedFolder);
        var deletedGenerated = DeleteAssetFolderIfExists(generatedFolder);

        EnsureFolder(independentedFolder);
        EnsureFolder(generatedFolder);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[IslandFloorMeshBakeUtility] 베이크 폴더 초기화. " +
            $"Independented deleted={deletedIndependented}, Generated deleted={deletedGenerated}");
    }

    static bool DeleteAssetFolderIfExists(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
            return false;

        if (AssetDatabase.DeleteAsset(folder))
            return true;

        Debug.LogWarning($"[IslandFloorMeshBakeUtility] 폴더 삭제 실패: {folder}");
        return false;
    }

    static bool ReplaceFloorWithPrefab(GameObject floorGo, GameObject prefabAsset, string undoName)
    {
        var parent = floorGo.transform.parent;
        var siblingIndex = floorGo.transform.GetSiblingIndex();
        var localPosition = floorGo.transform.localPosition;
        var localRotation = floorGo.transform.localRotation;
        var localScale = floorGo.transform.localScale;

        Undo.DestroyObjectImmediate(floorGo);

        var instance = PrefabUtility.InstantiatePrefab(prefabAsset, parent) as GameObject;
        if (instance == null)
        {
            Debug.LogError($"[IslandFloorMeshBakeUtility] 프리팹 인스턴스 생성 실패: {prefabAsset.name}");
            return false;
        }

        Undo.RegisterCreatedObjectUndo(instance, undoName);

        instance.name = FloorChildName;
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = localRotation;
        instance.transform.localScale = localScale;
        instance.transform.SetSiblingIndex(siblingIndex);
        return true;
    }

    static bool HasAnyGeneratedFloor(
        List<FloorBakeTarget> targets,
        string generatedFolder,
        string bodyName)
    {
        foreach (var target in targets)
        {
            if (!target.IsBakeable)
                continue;

            if (IsGeneratedFloorInstance(target.Floor, generatedFolder, bodyName, target.AssetLevelKey))
                return true;
        }

        return false;
    }

    static bool IsGeneratedFloorInstance(
        GameObject floorGo,
        string generatedFolder,
        string bodyName,
        string assetLevelKey)
    {
        if (floorGo == null)
            return false;

        var expectedGeneratedPath = GetGeneratedPrefabPath(generatedFolder, bodyName, assetLevelKey);
        var instancePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(floorGo);
        if (string.IsNullOrEmpty(instancePath))
            return false;

        return string.Equals(instancePath.Replace('\\', '/'), expectedGeneratedPath.Replace('\\', '/'));
    }

    static List<FloorBakeTarget> CollectFloorTargets(Transform floorParent, bool logIssues)
    {
        var targets = new List<FloorBakeTarget>(floorParent.childCount);
        var usedAssetKeys = new HashSet<string>();

        for (var levelIndex = 0; levelIndex < floorParent.childCount; levelIndex++)
        {
            var level = floorParent.GetChild(levelIndex);
            var floor = FindFloorChild(level, out var issue);

            var levelName = level.name;
            var assetLevelKey = levelName;
            if (!usedAssetKeys.Add(assetLevelKey))
            {
                assetLevelKey = $"{levelName}_{levelIndex}";
                usedAssetKeys.Add(assetLevelKey);
                if (logIssues)
                {
                    Debug.LogWarning(
                        $"[IslandFloorMeshBakeUtility] 층계 이름 중복 '{levelName}' → asset key '{assetLevelKey}' 사용");
                }
            }

            if (floor == null)
            {
                if (logIssues)
                {
                    Debug.Log(
                        $"[IslandFloorMeshBakeUtility] 층계 '{levelName}'(index {levelIndex})에 Floor가 없어 건너뜁니다.");
                }

                targets.Add(new FloorBakeTarget(null, levelName, assetLevelKey, FloorIssue.Absent));
                continue;
            }

            if (issue == FloorIssue.None && !HasMeshContent(floor))
                issue = FloorIssue.Empty;

            if (logIssues && issue != FloorIssue.None)
            {
                Debug.LogWarning(
                    $"[IslandFloorMeshBakeUtility] 층계 '{levelName}' Floor {DescribeIssue(issue)}: {GetTransformPath(floor)}");
            }

            targets.Add(new FloorBakeTarget(floor.gameObject, levelName, assetLevelKey, issue));
        }

        return targets;
    }

    static Transform FindFloorChild(Transform level, out FloorIssue issue)
    {
        issue = FloorIssue.Absent;
        Transform exact = null;
        Transform brokenNamed = null;

        for (var i = 0; i < level.childCount; i++)
        {
            var child = level.GetChild(i);
            if (child.name == FloorChildName)
            {
                exact = child;
                break;
            }

            if (IsBrokenFloorPlaceholder(child))
                brokenNamed = child;
        }

        if (exact != null)
        {
            issue = ClassifyFloorIssue(exact);
            return exact;
        }

        if (brokenNamed != null)
        {
            issue = FloorIssue.MissingPrefab;
            return brokenNamed;
        }

        issue = FloorIssue.Absent;
        return null;
    }

    static FloorIssue ClassifyFloorIssue(Transform floor)
    {
        if (IsBrokenFloorPlaceholder(floor) || PrefabUtility.IsPrefabAssetMissing(floor.gameObject))
            return FloorIssue.MissingPrefab;

        // Nested Missing Prefab: 이름은 Floor지만 소스 프리팹 GUID가 로드되지 않는 경우
        if (PrefabUtility.IsPartOfPrefabInstance(floor.gameObject))
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(floor.gameObject);
            if (source == null && floor.childCount == 0 && !HasMeshContent(floor))
                return FloorIssue.MissingPrefab;
        }

        if (!HasMeshContent(floor))
            return FloorIssue.Empty;

        return FloorIssue.None;
    }

    static bool IsBrokenFloorPlaceholder(Transform child)
    {
        if (child == null)
            return false;

        if (!child.name.StartsWith(FloorChildName, System.StringComparison.Ordinal))
            return false;

        if (child.name.IndexOf("Missing Prefab", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return PrefabUtility.IsPrefabAssetMissing(child.gameObject);
    }

    static bool IsFloorObjectName(string name)
    {
        return name == FloorChildName
               || name.StartsWith(FloorChildName + " ", System.StringComparison.Ordinal)
               || name.StartsWith(FloorChildName + "(", System.StringComparison.Ordinal);
    }

    static bool HasMeshContent(Transform floor)
    {
        if (floor == null)
            return false;

        return floor.GetComponentsInChildren<MeshRenderer>(true).Length > 0;
    }

    static string DescribeIssue(FloorIssue issue)
    {
        switch (issue)
        {
            case FloorIssue.Absent:
                return "없음";
            case FloorIssue.MissingPrefab:
                return "Missing Prefab";
            case FloorIssue.Empty:
                return "비어 있음(메시 없음)";
            default:
                return "정상";
        }
    }

    static Transform FindLevelTransform(Transform floorParent, string levelName, int preferredIndex)
    {
        if (preferredIndex >= 0 && preferredIndex < floorParent.childCount)
        {
            var byIndex = floorParent.GetChild(preferredIndex);
            if (byIndex.name == levelName)
                return byIndex;
        }

        return FindDirectChild(floorParent, levelName);
    }

    static string GetIndependentedPrefabPath(string independentedFolder, string bodyName, string assetLevelKey)
    {
        return $"{independentedFolder}/{bodyName}_{assetLevelKey}_Floor.prefab";
    }

    static string GetGeneratedPrefabPath(string generatedFolder, string bodyName, string assetLevelKey)
    {
        return $"{generatedFolder}/{bodyName}_{assetLevelKey}_GenerateFloor.prefab";
    }

    static bool TrySaveIsolatedFloorPrefab(GameObject floorGo, string independentedPath, out string independentedGuid)
    {
        independentedGuid = null;

        var isolatedFloor = Object.Instantiate(floorGo);
        isolatedFloor.name = FloorChildName;
        isolatedFloor.transform.SetParent(null, true);

        try
        {
            // Nested Prefab 링크가 깨져 있으면 부분 저장될 수 있어 완전 언팩 후 저장한다.
            UnpackPrefabInstancesCompletely(isolatedFloor);

            var savedIndependented = PrefabUtility.SaveAsPrefabAsset(isolatedFloor, independentedPath);
            if (savedIndependented == null)
            {
                Debug.LogError($"[IslandFloorMeshBakeUtility] Independented 프리팹 저장 실패: {independentedPath}");
                return false;
            }

            independentedGuid = AssetDatabase.AssetPathToGUID(independentedPath);
            if (string.IsNullOrEmpty(independentedGuid))
            {
                Debug.LogError($"[IslandFloorMeshBakeUtility] Independented guid를 찾지 못했습니다: {independentedPath}");
                return false;
            }

            var savedContents = PrefabUtility.LoadPrefabContents(independentedPath);
            try
            {
                if (!HasMeshContent(savedContents.transform))
                {
                    Debug.LogError(
                        $"[IslandFloorMeshBakeUtility] Independented 저장 결과가 비어 있습니다: {independentedPath}");
                    return false;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(savedContents);
            }

            return true;
        }
        finally
        {
            Object.DestroyImmediate(isolatedFloor);
        }
    }

    static void UnpackPrefabInstancesCompletely(GameObject root)
    {
        if (root == null)
            return;

        // 자식부터 언팩해야 중첩 인스턴스가 남기지 않는다.
        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (var i = transforms.Length - 1; i >= 0; i--)
        {
            var go = transforms[i].gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
                continue;

            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }
    }

    static bool TryResolveBodyPrefabPath(IslandBase island, out string prefabPath, out string bodyName)
    {
        prefabPath = null;
        bodyName = null;

        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && island.gameObject.scene == stage.scene)
            prefabPath = stage.assetPath;
        else
            prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(island.gameObject);

        if (string.IsNullOrEmpty(prefabPath))
            return false;

        bodyName = Path.GetFileNameWithoutExtension(prefabPath);
        return !string.IsNullOrEmpty(bodyName);
    }

    static Transform ResolveFloorParent(IslandBase island)
    {
        if (island.FloorParent != null)
            return island.FloorParent;

        return island.transform.Find(FloorParentChildName);
    }

    static Transform FindDirectChild(Transform parent, string childName)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == childName)
                return child;
        }

        return null;
    }

    static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "(null)";

        var path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    static void EnsureFolder(string assetPathOrDirectory)
    {
        var directory = assetPathOrDirectory.EndsWith(".prefab") || assetPathOrDirectory.EndsWith(".asset")
            ? Path.GetDirectoryName(assetPathOrDirectory)?.Replace('\\', '/')
            : assetPathOrDirectory.Replace('\\', '/');

        if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory))
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
