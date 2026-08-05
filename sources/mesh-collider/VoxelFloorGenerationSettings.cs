using UnityEditor;
using UnityEngine;

public enum VoxelFloorColliderMode
{
    None,
    SingleMeshCollider,
    BoxColliderPerBlock,
    MergedBoxColliders
}

public struct VoxelFloorGenerationSettings
{
    // cliff_1 (Assets/GameResource/Material/Environment_Mat/Floor/cliff_1.mat)
    public const string DefaultMaterialGuid = "f3cf47696e1e75d408fda59a33150a48";

    public float BlockSize;
    public float GridTolerance;
    /// <summary>
    /// Y 오프셋 그룹 간격(월드 단위). IslandBlockPlacementWindow YNudgeStep(0.2)과 맞춘다.
    /// 같은 잔여 Y(예: 0.2, 2.2, 4.2)끼리 한 격자 레이어로 묶어 메시를 병합한다.
    /// </summary>
    public float YSubGridStep;
    public bool IncludeOffGridBlocks;
    public bool PreserveTopMaterialPerBlock;
    public int TopMaterialSlot;
    public Material TopMaterial;
    public Material SideMaterial;
    public Material BottomMaterial;
    public VoxelFloorColliderMode ColliderMode;
    public bool MeshColliderConvex;
    /// <summary>생성 프리팹 루트(및 OffGrid 포함)에 적용할 레이어. 기본값 0 = Default.</summary>
    public int Layer;
    /// <summary>노출 모서리에 라운드 베벨(다단 호)을 넣는다. 콜라이더는 박스 그대로.</summary>
    public bool EnableChamfer;
    /// <summary>월드 단위 베벨 반경. BlockSize/2 미만으로 클램프된다.</summary>
    public float ChamferSize;
    /// <summary>모서리 호를 나눌 세그먼트 수. 클수록 더 완만하고 정점↑.</summary>
    public int ChamferSegments;

    public static VoxelFloorGenerationSettings Default
    {
        get
        {
            var defaultMaterial = LoadMaterialByGuid(DefaultMaterialGuid);
            return new VoxelFloorGenerationSettings
            {
                BlockSize = 2f,
                GridTolerance = 0.05f,
                YSubGridStep = 0.2f,
                IncludeOffGridBlocks = true,
                PreserveTopMaterialPerBlock = true,
                TopMaterialSlot = 3,
                TopMaterial = defaultMaterial,
                SideMaterial = defaultMaterial,
                BottomMaterial = defaultMaterial,
                ColliderMode = VoxelFloorColliderMode.BoxColliderPerBlock,
                MeshColliderConvex = false,
                Layer = 0,
                EnableChamfer = false,
                ChamferSize = 0.15f,
                ChamferSegments = 4
            };
        }
    }

    /// <summary>
    /// ScriptableObject 에셋이 없을 때 쓰는 섬 Floor bake fallback.
    /// 실제 bake는 <see cref="VoxelFloorGenerationSettingsAsset.ResolveForIslandBake"/>를 우선한다.
    /// </summary>
    public static VoxelFloorGenerationSettings CreateIslandFloorFallback()
    {
        var settings = Default;
        var floorWalkable = LayerMask.NameToLayer("Floor_Walkable");
        settings.Layer = floorWalkable >= 0 ? floorWalkable : 10;
        settings.ColliderMode = VoxelFloorColliderMode.MergedBoxColliders;
        // 실험안 C: rim chamfer 기본 강화 (SO 없을 때 fallback)
        settings.EnableChamfer = true;
        settings.ChamferSize = 0.35f;
        settings.ChamferSegments = 4;
        return settings;
    }

    /// <summary>섬 Floor bake용. SO 에셋 → 없으면 fallback.</summary>
    public static VoxelFloorGenerationSettings ForIslandFloor =>
        VoxelFloorGenerationSettingsAsset.ResolveForIslandBake();

    public static Material LoadMaterialByGuid(string guid)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
    }
}
