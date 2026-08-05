using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class VoxelFloorMeshGeneratorWindow : EditorWindow
{
    const float ExecuteButtonHeight = 28f;

    // TestFloor.prefab (Assets/GameResource/Prefab/Environment_Pre/Island/TestFloor.prefab)
    const string TestFloorPrefabGuid = "8c965a21e77927548b2df07cd7cc1dfe";

    readonly List<GameObject> sourceRoots = new List<GameObject>();
    readonly HashSet<GameObject> sourceRootSet = new HashSet<GameObject>();

    Vector2 sourceScroll;
    string prefabName = "VoxelFloor";
    float blockSize = 2f;
    float gridTolerance = 0.05f;
    float ySubGridStep = 0.2f;
    bool includeOffGridBlocks = true;

    bool preserveTopMaterialPerBlock = true;
    int topMaterialSlot = 3;

    VoxelFloorColliderMode colliderMode = VoxelFloorColliderMode.BoxColliderPerBlock;
    bool meshColliderConvex;
    int layer;

    bool enableChamfer;
    float chamferSize = 0.15f;
    int chamferSegments = 4;

    Material topMaterial;
    Material sideMaterial;
    Material bottomMaterial;

    [MenuItem("Tools/Voxel Floor/Open Generator Window")]
    public static void ShowWindow()
    {
        var window = GetWindow<VoxelFloorMeshGeneratorWindow>("Voxel Floor Mesh");
        window.titleContent = new GUIContent("Voxel Floor Mesh", EditorGUIUtility.IconContent("d_Prefab Icon").image);
        window.minSize = new Vector2(400f, 560f);
    }

    [MenuItem("Tools/Voxel Floor/Test Generate From TestFloor")]
    public static void TestGenerateFromTestFloor()
    {
        var settings = VoxelFloorGenerationSettings.Default;

        var sourcePath = AssetDatabase.GUIDToAssetPath(TestFloorPrefabGuid);
        if (string.IsNullOrEmpty(sourcePath))
        {
            Debug.LogError("[Voxel Floor Mesh] TestFloor 프리팹을 찾지 못했습니다.");
            return;
        }

        var folder = System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
        var outputPath = $"{folder}/Test Voxel Floor.prefab";

        var savedPrefab = VoxelFloorMeshGenerator.GenerateFromPrefab(TestFloorPrefabGuid, outputPath, settings);
        if (savedPrefab == null)
        {
            Debug.LogError("[Voxel Floor Mesh] Test 생성에 실패했습니다.");
            return;
        }

        Selection.activeObject = savedPrefab;
        EditorGUIUtility.PingObject(savedPrefab);
        Debug.Log($"[Voxel Floor Mesh] Test 생성 완료: {outputPath}");
    }

    void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 사용할 수 없습니다.", MessageType.Warning);
            return;
        }

        var contentRect = new Rect(0f, 0f, position.width, position.height - ExecuteButtonHeight);
        GUILayout.BeginArea(contentRect);
        DrawSourcePanel();
        EditorGUILayout.Space(6f);
        DrawPreviewPanel();
        EditorGUILayout.Space(6f);
        DrawOptionsPanel();
        GUILayout.EndArea();

        DrawExecuteButton();
    }

    void DrawSourcePanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Source Objects ({sourceRoots.Count})", EditorStyles.boldLabel);

                if (GUILayout.Button("From Selection", GUILayout.Width(110f)))
                    AddFromSelection();

                using (new EditorGUI.DisabledScope(sourceRoots.Count == 0))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(70f)))
                        ClearSources();
                }
            }

            var dropRect = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "블록 배치 루트를 여기에 드래그 앤 드롭", EditorStyles.helpBox);
            HandleSourceDragAndDrop(dropRect);

            sourceScroll = EditorGUILayout.BeginScrollView(sourceScroll, GUILayout.MinHeight(80f));
            for (var i = sourceRoots.Count - 1; i >= 0; i--)
            {
                var source = sourceRoots[i];
                if (source == null)
                {
                    RemoveSourceAt(i);
                    continue;
                }

                DrawSourceRow(source);
            }

            EditorGUILayout.EndScrollView();
        }
    }

    void DrawSourceRow(GameObject source)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.ObjectField(source, typeof(GameObject), true);

            if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                RemoveSource(source);
        }
    }

    void DrawPreviewPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            if (sourceRoots.Count == 0)
            {
                EditorGUILayout.HelpBox("블록이 배치된 루트 오브젝트를 추가하세요.", MessageType.Info);
                return;
            }

            if (blockSize <= 0f)
            {
                EditorGUILayout.HelpBox("Block Size는 0보다 커야 합니다.", MessageType.Warning);
                return;
            }

            var preview = VoxelFloorMeshGenerator.BuildPreview(sourceRoots, BuildSettings());

            EditorGUILayout.LabelField($"Grid Blocks: {preview.GridBlockCount}");
            EditorGUILayout.LabelField($"Off-Grid Blocks: {preview.OffGridBlockCount}");
            EditorGUILayout.LabelField($"Exposed Faces: {preview.ExposedFaceCount}");
            EditorGUILayout.LabelField($"Culled Inner Faces: {preview.CulledInnerFaceCount}");
            EditorGUILayout.LabelField($"Top Material Variants: {preview.TopMaterialVariantCount}");

            if (preview.GridBlockCount == 0 && preview.OffGridBlockCount == 0)
                EditorGUILayout.HelpBox("MeshRenderer를 가진 블록을 찾지 못했습니다.", MessageType.Warning);
        }
    }

    void DrawOptionsPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            prefabName = EditorGUILayout.TextField("Prefab Name", prefabName);

            using (new EditorGUILayout.HorizontalScope())
            {
                blockSize = EditorGUILayout.FloatField("Block Size", blockSize);
                if (GUILayout.Button("Detect", GUILayout.Width(70f)))
                    DetectBlockSize();
            }

            gridTolerance = EditorGUILayout.FloatField("Grid Tolerance", gridTolerance);
            ySubGridStep = EditorGUILayout.FloatField("Y Sub Grid Step", ySubGridStep);
            includeOffGridBlocks = EditorGUILayout.Toggle("Include Off-Grid Blocks", includeOffGridBlocks);

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Materials", EditorStyles.boldLabel);

            preserveTopMaterialPerBlock = EditorGUILayout.Toggle("Preserve Top Per Block", preserveTopMaterialPerBlock);

            using (new EditorGUI.DisabledScope(!preserveTopMaterialPerBlock))
            {
                topMaterialSlot = EditorGUILayout.IntField("Top Material Slot", topMaterialSlot);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                topMaterial = (Material)EditorGUILayout.ObjectField("Top (Fallback)", topMaterial, typeof(Material), false);
                if (GUILayout.Button("Auto", GUILayout.Width(70f)))
                    AutoAssignMaterials();
            }

            sideMaterial = (Material)EditorGUILayout.ObjectField("Side", sideMaterial, typeof(Material), false);
            bottomMaterial = (Material)EditorGUILayout.ObjectField("Bottom", bottomMaterial, typeof(Material), false);

            EditorGUILayout.Space(2f);
            colliderMode = (VoxelFloorColliderMode)EditorGUILayout.EnumPopup("Collider Mode", colliderMode);

            using (new EditorGUI.DisabledScope(colliderMode != VoxelFloorColliderMode.SingleMeshCollider))
            {
                meshColliderConvex = EditorGUILayout.Toggle("MeshCollider Convex", meshColliderConvex);
            }

            layer = EditorGUILayout.LayerField("Layer", layer);

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Chamfer", EditorStyles.boldLabel);
            enableChamfer = EditorGUILayout.Toggle("Enable Chamfer", enableChamfer);
            using (new EditorGUI.DisabledScope(!enableChamfer))
            {
                chamferSize = EditorGUILayout.FloatField("Chamfer Size", chamferSize);
                chamferSegments = EditorGUILayout.IntSlider("Chamfer Segments", chamferSegments, 1, 8);
            }

            if (enableChamfer)
            {
                EditorGUILayout.HelpBox(
                    "노출 모서리를 다단 호로 둥글게 깎습니다(단차·오목 코너 포함). 콜라이더(박스)는 직각 유지.",
                    MessageType.None);
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Island Bake Settings", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var islandSettings = VoxelFloorGenerationSettingsAsset.LoadIslandDefault();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(
                    islandSettings,
                    typeof(VoxelFloorGenerationSettingsAsset),
                    false);
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("Ping", GUILayout.Width(50f)))
                    VoxelFloorGenerationSettingsAsset.PingOrSelectIslandDefault();
            }

            EditorGUILayout.HelpBox(
                "섬 Floor 메쉬 병합은 위 ScriptableObject 옵션을 사용합니다. 이 창의 Options는 수동 Generate 전용입니다.",
                MessageType.None);
        }
    }

    void DrawExecuteButton()
    {
        var roots = VoxelFloorMeshGenerator.NormalizeRoots(sourceRoots);
        var canExecute = roots.Count > 0 && blockSize > 0f && !string.IsNullOrWhiteSpace(prefabName);
        var executeRect = new Rect(0f, position.height - ExecuteButtonHeight, position.width, ExecuteButtonHeight);

        using (new EditorGUI.DisabledScope(!canExecute))
        {
            if (GUI.Button(executeRect, "Generate Voxel Mesh Prefab"))
                OnExecute();
        }
    }

    VoxelFloorGenerationSettings BuildSettings()
    {
        return new VoxelFloorGenerationSettings
        {
            BlockSize = blockSize,
            GridTolerance = gridTolerance,
            YSubGridStep = ySubGridStep,
            IncludeOffGridBlocks = includeOffGridBlocks,
            PreserveTopMaterialPerBlock = preserveTopMaterialPerBlock,
            TopMaterialSlot = topMaterialSlot,
            TopMaterial = topMaterial,
            SideMaterial = sideMaterial,
            BottomMaterial = bottomMaterial,
            ColliderMode = colliderMode,
            MeshColliderConvex = meshColliderConvex,
            Layer = layer,
            EnableChamfer = enableChamfer,
            ChamferSize = chamferSize,
            ChamferSegments = chamferSegments
        };
    }

    void OnExecute()
    {
        var roots = VoxelFloorMeshGenerator.NormalizeRoots(sourceRoots.Where(go => go != null).ToList());
        if (roots.Count == 0)
            return;

        var defaultName = string.IsNullOrWhiteSpace(prefabName) ? "VoxelFloor" : prefabName.Trim();
        var prefabPath = EditorUtility.SaveFilePanelInProject(
            "Save Voxel Floor Prefab",
            defaultName,
            "prefab",
            "생성된 프리팹을 저장할 경로를 선택하세요.");

        if (string.IsNullOrEmpty(prefabPath))
            return;

        var savedPrefab = VoxelFloorMeshGenerator.Generate(roots, prefabPath, BuildSettings());
        if (savedPrefab == null)
        {
            EditorUtility.DisplayDialog("Voxel Floor Mesh", "포함할 블록을 찾지 못했거나 저장에 실패했습니다.", "OK");
            return;
        }

        Selection.activeObject = savedPrefab;
        EditorGUIUtility.PingObject(savedPrefab);
    }

    void HandleSourceDragAndDrop(Rect dropRect)
    {
        var evt = Event.current;
        if (!dropRect.Contains(evt.mousePosition))
            return;

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = HasSupportedDraggedSceneObject()
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            AddDraggedSceneObjects(DragAndDrop.objectReferences);
            evt.Use();
        }
    }

    static bool HasSupportedDraggedSceneObject()
    {
        foreach (var obj in DragAndDrop.objectReferences)
        {
            if (IsSceneGameObject(obj as GameObject))
                return true;
        }

        return false;
    }

    static bool IsSceneGameObject(GameObject go)
    {
        return go != null && !EditorUtility.IsPersistent(go);
    }

    void AddFromSelection()
    {
        foreach (var obj in Selection.gameObjects)
            AddSource(obj);
    }

    void AddDraggedSceneObjects(Object[] draggedObjects)
    {
        foreach (var obj in draggedObjects)
        {
            if (!IsSceneGameObject(obj as GameObject))
                continue;

            AddSource((GameObject)obj);
        }
    }

    void AddSource(GameObject source)
    {
        if (!sourceRootSet.Add(source))
            return;

        sourceRoots.Add(source);
    }

    void RemoveSource(GameObject source)
    {
        var index = sourceRoots.IndexOf(source);
        if (index < 0)
            return;

        RemoveSourceAt(index);
    }

    void RemoveSourceAt(int index)
    {
        sourceRootSet.Remove(sourceRoots[index]);
        sourceRoots.RemoveAt(index);
    }

    void ClearSources()
    {
        sourceRoots.Clear();
        sourceRootSet.Clear();
    }

    void DetectBlockSize()
    {
        var roots = VoxelFloorMeshGenerator.NormalizeRoots(sourceRoots);
        foreach (var root in roots)
        {
            foreach (var boxCollider in root.GetComponentsInChildren<BoxCollider>(true))
            {
                var lossyScale = boxCollider.transform.lossyScale;
                var scaledSize = Mathf.Abs(boxCollider.size.x * lossyScale.x);
                if (scaledSize > 0.0001f)
                {
                    blockSize = scaledSize;
                    return;
                }
            }
        }

        EditorUtility.DisplayDialog("Voxel Floor Mesh", "BoxCollider를 찾지 못해 Block Size를 감지할 수 없습니다.", "OK");
    }

    void AutoAssignMaterials()
    {
        var roots = VoxelFloorMeshGenerator.NormalizeRoots(sourceRoots);
        foreach (var root in roots)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var sharedMaterials = renderer.sharedMaterials;
                var firstMaterial = sharedMaterials.FirstOrDefault(m => m != null);
                if (firstMaterial == null)
                    continue;

                var topSlotMaterial = topMaterialSlot >= 0 && topMaterialSlot < sharedMaterials.Length
                    ? sharedMaterials[topMaterialSlot]
                    : null;

                topMaterial = topMaterial != null ? topMaterial : topSlotMaterial != null ? topSlotMaterial : firstMaterial;
                sideMaterial = sideMaterial != null ? sideMaterial : firstMaterial;
                bottomMaterial = bottomMaterial != null ? bottomMaterial : firstMaterial;
                return;
            }
        }
    }
}
