using System.Collections.Generic;
using UnityEngine;

public static class TutorialUISystem
{
    private static Dictionary<string, TutorialObject> _dicTutorialObject = new();

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void ResetOnInitializeOnLoadMethod()
    {
        _dicTutorialObject.Clear();
    }
#endif


    public static void Initialize()
    {
    }

    public static TutorialObject GetTutorialObject(string key)
    {
        if (!_dicTutorialObject.TryGetValue(key, out var tutorialObject))
        {
            Debug.LogError($"TutorialUISystem : GetTutorialObject - 없는 key({key})");
            return null;
        }

        return tutorialObject;
    }

    public static void AddTutorialObject(string key, TutorialObject tutorialObject)
    {
        // if (key == "guide_mission_button")
        //     Util.Debug.Log($"TutorialUISystem : AddTutorialObject - {key}");

        _dicTutorialObject.TryAdd(key, tutorialObject);
    }

    public static void RemoveTutorialObject(string key)
    {
        // if (key == "guide_mission_button")
        //     Util.Debug.Log($"TutorialUISystem : RemoveTutorialObject - {key}");

        _dicTutorialObject.Remove(key);
    }
}
