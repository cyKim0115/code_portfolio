using System.IO;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;
using User;

public class GameManager : ManagerBase<GameManager>
{
    public StageType CurrType => _currType;
    private StageType _currType = StageType.None;
    public eViewMode CurrViewMode => _currMode;
    private eViewMode _currMode;

    public UnityAction<StageType> OnStageTypeChanged;
    public UnityAction<eViewMode> OnViewModeChanged;

    public bool IsFirstInitialize => _isFirstInitialize;
    private bool _isFirstInitialize = false;

    private void Awake()
    {
        EventManager.OnTabChanged += RefreshViewMode;
    }

    public void SetStageType(StageType target, bool force = false)
    {
        if (_currType == target && !force)
            return;

        _currType = target;

        UIManager.Instance.SetTab(eTabId.None);
        RefreshViewMode(target);

        Initialize().Forget();
    }

    private void RefreshViewMode(StageType stageType)
    {
        switch (stageType)
        {
            case StageType.Stage:
                SetViewMode(eViewMode.Stage);
                break;
            case StageType.Island:
                SetViewMode(eViewMode.Island);
                break;
            case StageType.Event:
                SetViewMode(eViewMode.EventStage);
                break;
        }
    }

    private void RefreshViewMode(eTabId tabId)
    {
        switch (tabId)
        {
            case eTabId.Hunt:
                SetViewMode(eViewMode.Hunt);
                break;
            case eTabId.Inventory:
                SetViewMode(eViewMode.Inventory);
                break;
            case eTabId.Shop:
                SetViewMode(eViewMode.Shop);
                break;
            case eTabId.BoxStorage:
                SetViewMode(eViewMode.BoxStorage);
                break;
            case eTabId.EventManagerList:
                SetViewMode(eViewMode.EventManagerList);
                break;
            case eTabId.EventStoreList:
                SetViewMode(eViewMode.EventStoreList);
                break;
            case eTabId.EventShop:
                SetViewMode(eViewMode.EventShop);
                break;
            case eTabId.StageShop:
                SetViewMode(eViewMode.StageShop);
                break;
            default:
                RefreshViewMode(_currType);
                break;
        }
    }

    public void SetViewMode(eViewMode viewMode)
    {
        if (_currMode == viewMode)
            return;

        _currMode = viewMode;

        OnViewModeChanged?.Invoke(_currMode);

        SoundManager.Instance.RefreshBgm();
    }

    private async UniTask Initialize()
    {
        SettingETC();

        await Init();

        switch (_currType)
        {
            case StageType.Stage:
                {
                    MainSceneUI.Instance.ShowSceneTransition(_currType);

                    IslandManager.Instance.DeleteIsland();
                    EventStageManager.Instance.DeleteEventStage();
                    await StageManager.Instance.Init();
                    break;
                }
            case StageType.Event:
                {
                    MainSceneUI.Instance.ShowSceneTransition(_currType);

                    StageManager.Instance.DeleteStage();
                    IslandManager.Instance.DeleteIsland();
                    await EventStageManager.Instance.Init();
                    break;
                }
            case StageType.Island:
                {
                    MainSceneUI.Instance.ShowSceneTransition(_currType);

                    if (StageSystem.IsFirstStage)
                    {
                        SetStageType(StageType.Stage);
                        return;
                    }

                    StageManager.Instance.DeleteStage();
                    EventStageManager.Instance.DeleteEventStage();
                    await IslandManager.Instance.Init();

                    await HuntManager.Instance.Init();
                    break;
                }
            case StageType.Title:
                {
                    IslandManager.Instance.DeleteIsland();
                    EventStageManager.Instance.DeleteEventStage();
                    StageManager.Instance.DeleteStage();

                    DailyMissionSystem.AddValue(DailyMissionId.Login, 1);
                    PassVipMissionSystem.AddProgress(ePassVipMissionId.Login, 1);
                    break;
                }
            default:
                throw new InvalidDataException();
        }

        _isFirstInitialize = true;

        // 씬 전환 시 카메라 줌 크기 리셋
        CameraController.ResetMainCameraOrthographicSize();

        OnStageTypeChanged?.Invoke(_currType);
    }

    private void SettingETC()
    {
        Application.targetFrameRate = 60;
        Screen.fullScreen = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        PrimeTweenConfig.warnEndValueEqualsCurrent = false;
    }

    private static async UniTask Init()
    {
        if (!TimeManager.Instance.IsInitialized)
        {
            if (!await TimeManager.Instance.Initialize())
            {
                // Debug.LogError($"GameManager : 인터넷 시간 초기화 실패");
                // Debug.LogError($"에디터라서 그냥 Return 처리");

                return;
            }
        }

        if (!TableManager.Instance.IsInitialized)
        {
            // Debug.Log("GameManager : TableManager 초기화 시작");
            TableManager.Instance.Init();
            // Debug.Log("GameManager : TableManager 초기화 완료");
        }

        // Debug.Log("GameManager : UserData 초기화 시작");
        await UserData.Initialize();
        // Debug.Log("GameManager : UserData 초기화 완료");

        if (!NetworkManager.Instance.IsInitialized)
        {
            // Debug.Log("GameManager : NetworkManager 초기화 시작");
            await NetworkManager.Instance.Initialize();
            // Debug.Log("GameManager : NetworkManager 초기화 완료");
        }

        if (!IAPManager.Instance.IsInitialized)
        {
            // Debug.Log("GameManager : IAPManager 초기화 시작");
            await IAPManager.Instance.InitializePurchasing();
            // Debug.Log("GameManager : IAPManager 초기화 완료");
        }

        // Debug.Log("GameManager : LocalNotificationManager 초기화 시작");
        LocalNotificationManager.Instance.Initialize();
        // Debug.Log("GameManager : LocalNotificationManager 초기화 완료");

        // Debug.Log("GameManager : InitSystem 초기화 시작");
        await InitSystem();
        // Debug.Log("GameManager : InitSystem 초기화 완료");

        AdManager.Instance.Initialize();
        // Debug.Log("GameManager : AdManager 초기화 완료");
        TutorialManager.Instance.Initialize();
        // Debug.Log("GameManager : TutorialManager 초기화 완료");
    }

    private static async UniTask InitSystem()
    {
        await UnityRemoteConfigSystem.Initialize();

        // Remote Config 초기화 후 버전 체크 수행
        await VersionCheckUtil.CheckVersionAsync();

        StageSystem.Initialize();
        CharacterSystem.Initialize();
        CraftingSystem.Initialize();
        CustomerSystem.Initialize();
        PeacefulEquipmentSystem.Initialize();
        AttendanceSystem.Initialize();
        PetSystem.Initialize();
        RelicSystem.Initialize();
        BattleSystem.Initialize();
        HuntSystem.Initialize();
        DailyMissionSystem.Initialize();
        RouletteSystem.Initialize();
        CurrencyEffectSystem.Initialize();
        BoostSystem.Initialize();
        StageOfflineSystem.Initialize();
        EventOfflineSystem.Initialize();
        MerchantFoxSystem.Initialize();
        BoxStorageSystem.Initialize();
        StageAdsBonusSystem.Initialize();
        EventScheduleSystem.Initialize();
        ShopSystem.Initialize();
        TutorialUISystem.Initialize();
        SoundSystem.Initialize();
        PiggyBankSystem.Initialize();
        PassSystem.Initialize();
        PassVipMissionSystem.Initialize();
        EventCurrencyMerchantSystem.Initialize();
        IapInvitationSystem.Initialize();
        CouponSystem.Initialize();
        IslandSystem.Initialize();

        await UniTask.WaitForEndOfFrame();
    }
}