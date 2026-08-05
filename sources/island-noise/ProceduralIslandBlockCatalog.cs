using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 프로시저럴 섬 생성에 쓰는 FloorBlock/SandBlock top 머티리얼 카탈로그.
/// </summary>
[CreateAssetMenu(
    fileName = "ProceduralIslandBlockCatalog",
    menuName = "TeenipingTycoon/Island/Procedural Block Catalog")]
public class ProceduralIslandBlockCatalog : ScriptableObject
{
    [Header("FloorBlock")]
    [Tooltip("육지(Walkable/Cliff) 셀에 인스턴스화할 원본 프리팹. 비우면 IslandFloorBlockUtility 기본 경로를 씁니다.")]
    [SerializeField] private GameObject _floorBlockPrefab;

    [Header("SandBlock (해변)")]
    [Tooltip("해변 셀용. 콜라이더 없음. 비우면 IslandFloorBlockUtility 기본 경로를 씁니다.")]
    [SerializeField] private GameObject _sandBlockPrefab;

    [Header("이동 가능 땅 top 머티리얼 (평지·등반 가능 언덕)")]
    [Tooltip("grass_unlit* 등. 비우면 생성 실패.")]
    [SerializeField] private List<Material> _walkableTopMaterials = new List<Material>();

    [Header("못 올라가는 언덕 top 머티리얼")]
    [Tooltip("비우면 Walkable 머티리얼로 대체.")]
    [SerializeField] private List<Material> _unclimbableHillTopMaterials = new List<Material>();

    [Header("해변 top 머티리얼 (선택)")]
    [Tooltip("비우면 SandBlock 프리팹 기본 머티리얼을 그대로 씁니다.")]
    [SerializeField] private List<Material> _beachTopMaterials = new List<Material>();

    public GameObject FloorBlockPrefab => _floorBlockPrefab;
    public GameObject SandBlockPrefab => _sandBlockPrefab;
    public IReadOnlyList<Material> WalkableTopMaterials => _walkableTopMaterials;
    public IReadOnlyList<Material> UnclimbableHillTopMaterials => _unclimbableHillTopMaterials;
    public IReadOnlyList<Material> BeachTopMaterials => _beachTopMaterials;

    // 하위 호환 이름 (윈도우 요약 라벨 등)
    public IReadOnlyList<Material> WalkableBlocks => _walkableTopMaterials;
    public IReadOnlyList<Material> UnclimbableHillBlocks => _unclimbableHillTopMaterials;
    public IReadOnlyList<Material> BeachBlocks => _beachTopMaterials;

    public bool HasAnyWalkable => HasValidMaterial(_walkableTopMaterials);
    public bool HasAnyUnclimbableHill => HasValidMaterial(_unclimbableHillTopMaterials);
    public bool HasAnyBeachMaterial => HasValidMaterial(_beachTopMaterials);
    /// <summary>SandBlock 프리팹을 해석할 수 있으면 해변 배치 가능.</summary>
    public bool HasAnyBeach => ResolveSandBlockPrefab() != null;

    public Material PickWalkable(System.Random rng) => Pick(_walkableTopMaterials, rng);
    public Material PickUnclimbableHill(System.Random rng) => Pick(_unclimbableHillTopMaterials, rng);
    public Material PickBeach(System.Random rng) => Pick(_beachTopMaterials, rng);

    public GameObject ResolveFloorBlockPrefab()
    {
        if (_floorBlockPrefab != null)
            return _floorBlockPrefab;
        return IslandFloorBlockUtility.FloorBlockPrefab;
    }

    public GameObject ResolveSandBlockPrefab()
    {
        if (_sandBlockPrefab != null)
            return _sandBlockPrefab;
        return IslandFloorBlockUtility.SandBlockPrefab;
    }

    static bool HasValidMaterial(List<Material> list)
    {
        if (list == null)
            return false;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] != null)
                return true;
        }

        return false;
    }

    static Material Pick(List<Material> list, System.Random rng)
    {
        if (list == null || list.Count == 0 || rng == null)
            return null;

        for (var attempt = 0; attempt < list.Count * 2; attempt++)
        {
            var candidate = list[rng.Next(list.Count)];
            if (candidate != null)
                return candidate;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] != null)
                return list[i];
        }

        return null;
    }
}
