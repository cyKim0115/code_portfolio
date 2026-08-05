using UnityEngine;

public abstract class ManagerBase<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance
    {
        get
        {

            if (_instance == null)
            {
#if UNITY_EDITOR
                if (!GameStateUtil.IsSafeToAccess)
                {
                    return null;
                }
#endif
                var managersObj = GameObject.Find("Managers");

                if (managersObj == null)
                {
                    managersObj = new GameObject("Managers");
                    DontDestroyOnLoad(managersObj);
                }

                _instance = managersObj.AddComponent<T>();
            }

            return _instance;
        }
    }

    private static T _instance;
}