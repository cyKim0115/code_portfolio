using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

public class PoolManager : ManagerBase<PoolManager>
{
    private readonly Dictionary<string, Queue<GameObject>> _poolDictionary = new();
    private readonly Dictionary<GameObject, string> _activeInstances = new();
    private Transform _cachedPoolParent;
    private readonly Dictionary<string, Transform> _cachedKeyParents = new();

    public bool HasInstance(string key)
    {
        return _poolDictionary.ContainsKey(key) && _poolDictionary[key].Count > 0;
    }

    /// <summary>
    /// 특정 키를 받아 해당하는 게임 오브젝트의 인스턴스를 생성하여 반환
    /// </summary>
    public GameObject GetInstance(string key, Transform parent = null)
    {
        // 부모 설정 (parent가 null이면 Pool/key 생성)
        Transform targetParent = parent ?? GetOrCreatePoolParent(key);

        if (!_poolDictionary.ContainsKey(key))
        {
            _poolDictionary[key] = new Queue<GameObject>();
        }

        GameObject instance;
        if (_poolDictionary[key].Count > 0)
        {
            // 풀에서 유효한 객체를 찾을 때까지 반복 (파괴된 객체 제거)
            instance = null;
            while (_poolDictionary[key].Count > 0)
            {
                instance = _poolDictionary[key].Dequeue();
                if (instance != null)
                {
                    // 유효한 객체를 찾았으면 활성화하고 루프 종료
                    instance.SetActive(true);
                    break;
                }
                // null인 객체는 무시하고 다음 객체 확인
            }
            
            // 풀에서 유효한 객체를 찾지 못했으면 새로 생성
            if (instance == null)
            {
                GameObject prefab = ResourceManager.Instance.GetPrefabInstance(key, targetParent);
                if (prefab == null)
                {
                    Debug.LogError($"[PoolManager] Failed to load prefab for key: {key}");
                    return null;
                }
                instance = prefab;
            }
        }
        else
        {
            GameObject prefab = ResourceManager.Instance.GetPrefabInstance(key, targetParent);
            if (prefab == null)
            {
                Debug.LogError($"[PoolManager] Failed to load prefab for key: {key}");
                return null;
            }
            instance = prefab;
        }

        instance.transform.SetParent(targetParent);

        _activeInstances[instance] = key;
        return instance;
    }

    /// <summary>
    /// 특정 키를 받아 비동기적으로 게임 오브젝트의 인스턴스를 생성하여 반환 (UniTask 사용)
    /// </summary>
    public async UniTask<GameObject> GetInstanceAsync(string key, Transform parent = null)
    {
        // 부모 설정 (parent가 null이면 Pool/key 생성)
        Transform targetParent = parent ?? GetOrCreatePoolParent(key);

        if (!_poolDictionary.ContainsKey(key))
        {
            _poolDictionary[key] = new Queue<GameObject>();
        }

        GameObject instance;
        if (_poolDictionary[key].Count > 0)
        {
            // 풀에서 유효한 객체를 찾을 때까지 반복 (파괴된 객체 제거)
            instance = null;
            while (_poolDictionary[key].Count > 0)
            {
                instance = _poolDictionary[key].Dequeue();
                if (instance != null)
                {
                    // 유효한 객체를 찾았으면 활성화하고 루프 종료
                    instance.SetActive(true);
                    break;
                }
                // null인 객체는 무시하고 다음 객체 확인
            }
            
            // 풀에서 유효한 객체를 찾지 못했으면 새로 생성
            if (instance == null)
            {
                GameObject prefab = await ResourceManager.Instance.GetPrefabInstanceAsync(key, targetParent);
                if (prefab == null)
                {
                    Debug.LogError($"[PoolManager] Failed to load prefab asynchronously for key: {key}");
                    return null;
                }
                prefab.name = key;
                instance = prefab;
            }
        }
        else
        {
            GameObject prefab = await ResourceManager.Instance.GetPrefabInstanceAsync(key, targetParent);
            if (prefab == null)
            {
                Debug.LogError($"[PoolManager] Failed to load prefab asynchronously for key: {key}");
                return null;
            }

            prefab.name = key;
            instance = prefab;
        }

        instance.transform.SetParent(targetParent);

        _activeInstances[instance] = key;
        return instance;
    }

    private async UniTask CreatePoolItems(string key)
    {
        Transform parent = GetOrCreatePoolParent(key);

        for (int i = 0; i < 10; i++)
        {
            GameObject prefab = await ResourceManager.Instance.GetPrefabInstanceAsync(key);
            prefab.name = key;
            prefab.transform.SetParent(parent);
            ReturnToPool(prefab);
        }
    }

    /// <summary>
    /// 특정 키로 생성된 모든 인스턴스와 해당 풀을 삭제
    /// isForce가 true면 활성화된 오브젝트도 삭제, false면 활성화된 오브젝트가 있으면 로그만 출력.
    /// </summary>
    public void DestroyPool(string key, bool isForce)
    {
        if (!_poolDictionary.ContainsKey(key) && !_activeInstances.ContainsValue(key))
        {
            Debug.LogWarning($"[PoolManager] No instances found for key: {key}");
            return;
        }

        List<GameObject> toRemove = new List<GameObject>();

        // 활성화된 인스턴스 삭제 여부 확인
        foreach (var instance in _activeInstances)
        {
            if (instance.Value == key)
            {
                if (isForce)
                {
                    GameObject.Destroy(instance.Key);
                    toRemove.Add(instance.Key);
                }
                else
                {
                    Debug.LogWarning($"[PoolManager] Active instance exists for key: {key} - Skipping deletion");
                }
            }
        }

        // Dictionary에서 삭제
        foreach (var obj in toRemove)
        {
            _activeInstances.Remove(obj);
        }

        // 비활성화된 객체 삭제
        if (_poolDictionary.ContainsKey(key))
        {
            while (_poolDictionary[key].Count > 0)
            {
                GameObject obj = _poolDictionary[key].Dequeue();
                if (obj != null)
                {
                    GameObject.Destroy(obj);
                }
            }
            _poolDictionary.Remove(key);
        }

        // 하이어라키에서 부모도 삭제
        Transform parent = GetPoolParent(key);
        if (parent != null)
        {
            GameObject.Destroy(parent.gameObject);
        }

        Debug.Log($"[PoolManager] Pool for key {key} has been destroyed.");
    }

    /// <summary>
    /// 특정 게임 오브젝트를 다시 풀에 반환
    /// </summary>
    public void ReturnToPool(GameObject obj)
    {
        if (!GameStateUtil.IsSafeToAccess)
            return;

        if (obj == null)
            return;

        if (!_activeInstances.TryGetValue(obj, out var key))
        {
            Debug.LogWarning($"[PoolManager] Trying to return an object that was not pooled: {obj.name}");
            return;
        }

        // 부모 GameObject가 비활성화되는 동안 부모를 설정하려고 하면 에러 발생
        // 따라서 현재 GameObject의 부모가 비활성화 중이 아닐 때만 부모 설정
        Transform targetParent = GetOrCreatePoolParent(key);
        
        // 현재 GameObject의 부모가 활성화되어 있고, 타겟 부모도 활성화되어 있을 때만 부모 설정
        if (targetParent != null && targetParent.gameObject.activeInHierarchy)
        {
            // 현재 부모가 비활성화 중이 아닐 때만 부모 설정 시도
            Transform currentParent = obj.transform.parent;
            if (currentParent == null || currentParent.gameObject.activeInHierarchy)
            {
                obj.transform.SetParent(targetParent);
            }
            else
            {
                // 현재 부모가 비활성화 중이면, 부모 설정을 건너뛰고 나중에 설정
                // 일단 비활성화만 수행하고, 부모는 다음에 활성화될 때 설정
            }
        }

        // 객체가 파괴되지 않았을 때만 비활성화
        if (obj != null)
        {
            obj.SetActive(false);
        }

        if (!_poolDictionary.ContainsKey(key))
        {
            _poolDictionary[key] = new Queue<GameObject>();
        }

        _poolDictionary[key].Enqueue(obj);
        _activeInstances.Remove(obj);
    }

    /// <summary>
    /// 하이어라키에서 "Pool/key" 부모를 가져오거나 생성
    /// </summary>
    private Transform GetOrCreatePoolParent(string key)
    {
        // Pool 부모를 캐시하거나 찾기
        if (_cachedPoolParent == null || _cachedPoolParent.gameObject == null)
        {
            // 캐시가 없거나 유효하지 않으면 찾거나 생성
            GameObject poolObject = GameObject.Find("Pool");
            if (poolObject == null)
            {
                poolObject = new GameObject("Pool");
                // DontDestroyOnLoad로 설정하여 영구적으로 유지
                DontDestroyOnLoad(poolObject);
            }
            _cachedPoolParent = poolObject.transform;
        }

        // Key 부모를 캐시에서 찾거나 생성
        if (!_cachedKeyParents.TryGetValue(key, out Transform keyParent) || keyParent == null)
        {
            keyParent = _cachedPoolParent.Find(key);
            if (keyParent == null)
            {
                GameObject keyObject = new GameObject(key);
                keyParent = keyObject.transform;
                keyParent.SetParent(_cachedPoolParent);
            }
            _cachedKeyParents[key] = keyParent;
        }

        return keyParent;
    }

    /// <summary>
    /// 하이어라키에서 "Pool/key" 부모를 찾아 반환 (없으면 null)
    /// </summary>
    private Transform GetPoolParent(string key)
    {
        if (_cachedPoolParent == null)
        {
            GameObject poolObject = GameObject.Find("Pool");
            if (poolObject != null)
            {
                _cachedPoolParent = poolObject.transform;
            }
        }

        if (_cachedPoolParent == null)
            return null;

        if (_cachedKeyParents.TryGetValue(key, out Transform keyParent) && keyParent != null)
        {
            return keyParent;
        }

        return _cachedPoolParent.Find(key);
    }
}