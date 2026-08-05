using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;
using User;

public static class EventManager
{
    private static Dictionary<string, CancellationTokenSource> _dicEvent = new();

    #region Public Events
    // Customer & Character Events
    public static UnityAction<CustomerInfo> OnCustomerAdd;
    public static UnityAction<CharacterSaveInfo> OnCharacterAdd;

    // Stage Events
    public static UnityAction OnStageDispose;
    public static UnityAction OnStageInit;
    public static UnityAction OnStageIdChanged;

    // Ad Events
    public static UnityAction OnAdRefresh;

    // Relic Events
    public static event Action OnRelicChanged;
    public static UnityAction OnRelicRedDotChanged;

    // Battle Equipment Events
    public static UnityAction OnBE_SkinChanged;
    public static UnityAction OnBE_EquipmentChanged;
    public static UnityAction OnBE_RedDotChanged;

    // Carriage Events
    public static UnityAction OnCarriageEquipped;
    public static UnityAction OnCarriageLevelChanged;

    // Pet Events
    public static UnityAction OnPetEquipped;
    public static UnityAction OnPetChanged;
    public static UnityAction<PetInfo> OnPetSelect_PetPopup;
    public static UnityAction<PetInfo> OnPetSelect_PetSynthesizePopup;
    public static UnityAction OnPetRedDotChanged;

    // Attendance Events
    public static UnityAction OnAttendanceChecked;

    // Roulette Events
    public static UnityAction OnRoulette;

    // Timer Events
    public static UnityAction<string> OnTimerExpired;

    // Peaceful Equipment Events
    public static UnityAction OnPE_EquippedChanged;
    public static UnityAction OnPE_EquipmentAdded;
    public static UnityAction OnPE_Resorting;
    public static UnityAction OnPE_RecipeChanged;
    public static UnityAction OnPE_RedDotChanged;

    // Guide Mission Events
    public static UnityAction OnChangedGuideMission;
    public static UnityAction OnChangedGuideMissionCounter;

    // User Info Events
    public static UnityAction OnLevelChanged;
    public static UnityAction OnExpChanged;

    // Crafting Events
    public static UnityAction<CraftingId> OnCraftingUnlocked;
    public static UnityAction<CraftingId> OnCraftingStepUp;
    public static UnityAction<CraftingId> OnCraftingLevelUp;
    public static UnityAction OnCraftingReset;

    // Event Crafting Events
    public static UnityAction<CraftingId> OnEventCraftingUnlocked;
    public static UnityAction<CraftingId> OnEventCraftingStepUp;
    public static UnityAction<CraftingId> OnEventCraftingLevelUp;
    public static UnityAction OnEventCraftingReset;

    // Event NPC Events
    public static UnityAction OnEventManagerLevelChanged;
    public static UnityAction OnEventManagerCardCountChanged;
    public static UnityAction OnEventStoreLevelChanged;
    public static UnityAction OnEventStoreCardCountChanged;

    // Movement Events
    public static UnityAction OnMoveSpeedChanged;

    // Hunt Events
    public static event Action OnSkinExpChanged;
    public static UnityAction OnBladeUpgrade;
    public static UnityAction OnGripUpgrade;
    public static event Action OnChangedLootItem;

    // Boss Events
    public static UnityAction<int> OnBossStageChanged;

    // Gacha Events
    public static UnityAction OnGachaInfoChanged;
    public static UnityAction OnGachaAdInfoChanged;

    // Daily Events
    public static UnityAction OnDailyReset;

    // Currency Events
    public static UnityAction OnCurrencyChanged;
    public static UnityAction<CurrencyId, double> OnCurrencyAmountRefresh;

    // Directing Events
    public static UnityAction OnDirectingChanged;

    // Upgrade Events
    public static UnityAction OnUpgrade;

    // Auto Hunt Events
    public static UnityAction OnAutoHuntConsumeChanged;
    public static UnityAction OnAutoHuntMinimumQualityChanged;

    // UIManager Events
    public static UnityAction<eTabId> OnTabChanged;
    public static UnityAction<string> OnPopupOpened;
    public static UnityAction<string> OnPopupClosed;

    // Treasure Vault Events
    public static UnityAction OnTreasureVaultLevelChanged;

    // Daily Mission Events
    public static UnityAction OnDailyMissionChanged;

    // Boost Events
    public static UnityAction OnBoostChanged;

    // Merchant Fox Events
    public static UnityAction<bool> OnMerchantFoxActiveChange;
    public static UnityAction OnMerchantFoxGoldChanged;
    #endregion

    // Box Storage
    public static UnityAction OnBoxItemChanged;
    public static UnityAction<string> OnBoxStorageTargetChanged;

    // Stage Ads Bonus Events
    public static UnityAction OnStageAdsBonusChanged;

    // Event Crafting Events
    public static UnityAction OnEventCraftingAutoChanged;
    public static UnityAction<List<RewardData>> OnEventCraftingStepUpReward;
    public static UnityAction<string> OnEventIapPackagePurchaseCompleted;

    // Event Gacha Events
    public static UnityAction OnEventGachaAnyChanged;

    // Event Guide Mission Events
    public static UnityAction<int> OnEventGuideMissionChanged;
    public static UnityAction<int> OnEventGuideMissionProgressChanged;
    public static UnityAction<EventGuideMissionData> OnEventGuideMissionRewardable;

    public static UnityAction OnEventScoreChanged;
    public static UnityAction OnEventScoreRewardStepChanged;

    // Event Schedule Events
    public static UnityAction<eEventScheduleState> OnEventScheduleTimeChanged;

    // Event Currency Merchant Events
    public static UnityAction OnEventCurrencyMerchantChanged;

    // Shop Events
    public static UnityAction<eShopTabId> OnShopTabChanged;
    public static UnityAction OnShopDailyRewardChanged;

    // Pass Events
    public static UnityAction OnAnyPassPurchaseChanged;
    public static UnityAction OnPassRewardedChanged;
    public static UnityAction OnVipPassMissionRewardable;
    public static UnityAction OnVipPassProgressChanged;

    // Tutorial Events
    public static UnityAction OnTutorialStart;
    public static UnityAction OnTutorialDone;

    // Piggy Bank Events
    public static UnityAction OnPiggyBankProgressChanged;
    public static UnityAction OnPiggyBankRewardClaimed;

    // Confirmed Gacha Events
    public static UnityAction OnConfirmedGachaAnyChanged;

    // Island Events
    public static UnityAction OnIslandOwnedItemChanged;
    public static UnityAction OnIslandMountedItemChanged;
    public static UnityAction OnIslandCraftingDecoChanged;
    public static UnityAction OnIslandQuestProgressChanged;

    public static void InvokeOnSkinExpChanged()
    {
        Invoke(nameof(OnSkinExpChanged), OnSkinExpChanged);
    }

    public static void InvokeOnChangedLootItem()
    {
        Invoke(nameof(OnChangedLootItem), OnChangedLootItem);
    }

    public static void InvokeOnRelicChanged()
    {
        Invoke(nameof(OnRelicChanged), OnRelicChanged);
    }

    private static void Invoke(string key,Action action)
    {
        if (_dicEvent.TryGetValue(key, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _dicEvent.Remove(key);
        }

        cts = new CancellationTokenSource();
        _dicEvent.Add(key, cts);

        InvokeAsync(action, cts.Token).Forget();
    }

    private static async UniTask InvokeAsync(Action action, CancellationToken token)
    {
        await UniTask.WaitForEndOfFrame(token);

        action?.Invoke();
    }

    #region Clear All Events
    
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    public static void ClearAllEvents()
    {
        // Customer & Character Events
        OnCustomerAdd = null;
        OnCharacterAdd = null;

        // Stage Events
        OnStageDispose = null;
        OnStageInit = null;
        OnStageIdChanged = null;

        // Ad Events
        OnAdRefresh = null;

        // Relic Events
        OnRelicChanged = null;
        OnRelicRedDotChanged = null;

        // Battle Equipment Events
        OnBE_SkinChanged = null;
        OnBE_EquipmentChanged = null;
        OnBE_RedDotChanged = null;

        // Carriage Events
        OnCarriageEquipped = null;
        OnCarriageLevelChanged = null;

        // Pet Events
        OnPetEquipped = null;
        OnPetChanged = null;
        OnPetSelect_PetPopup = null;
        OnPetSelect_PetSynthesizePopup = null;
        OnPetRedDotChanged = null;

        // Attendance Events
        OnAttendanceChecked = null;

        // Roulette Events
        OnRoulette = null;

        // Timer Events
        OnTimerExpired = null;

        // Peaceful Equipment Events
        OnPE_EquippedChanged = null;
        OnPE_EquipmentAdded = null;
        OnPE_Resorting = null;
        OnPE_RecipeChanged = null;
        OnPE_RedDotChanged = null;

        // Guide Mission Events
        OnChangedGuideMission = null;
        OnChangedGuideMissionCounter = null;

        // User Info Events
        OnLevelChanged = null;
        OnExpChanged = null;

        // Crafting Events
        OnCraftingUnlocked = null;
        OnCraftingStepUp = null;
        OnCraftingLevelUp = null;
        OnCraftingReset = null;

        // Event Crafting Events
        OnEventCraftingUnlocked = null;
        OnEventCraftingStepUp = null;
        OnEventCraftingLevelUp = null;
        OnEventCraftingReset = null;

        // Event NPC Events
        OnEventManagerLevelChanged = null;
        OnEventManagerCardCountChanged = null;
        OnEventStoreLevelChanged = null;
        OnEventStoreCardCountChanged = null;

        // Movement Events
        OnMoveSpeedChanged = null;

        // Hunt Events
        OnSkinExpChanged = null;
        OnBladeUpgrade = null;
        OnGripUpgrade = null;

        // Boss Events
        OnBossStageChanged = null;

        // Gacha Events
        OnGachaInfoChanged = null;
        OnGachaAdInfoChanged = null;

        // Daily Events
        OnDailyReset = null;

        // Currency Events
        OnCurrencyChanged = null;
        OnCurrencyAmountRefresh = null;

        // Box Storage Events
        OnBoxItemChanged = null;

        // Directing Events
        OnDirectingChanged = null;

        // Upgrade Events
        OnUpgrade = null;

        // Auto Hunt Events
        OnAutoHuntConsumeChanged = null;
        OnAutoHuntMinimumQualityChanged = null;

        // UIManager Events
        OnTabChanged = null;
        OnPopupOpened = null;
        OnPopupClosed = null;

        // Treasure Vault Events
        OnTreasureVaultLevelChanged = null;

        // Daily Mission Events
        OnDailyMissionChanged = null;

        // Boost Events
        OnBoostChanged = null;

        // Merchant Fox Events
        OnMerchantFoxActiveChange = null;
        OnMerchantFoxGoldChanged = null;

        // Box Storage Events
        OnBoxStorageTargetChanged = null;

        // Stage Ads Bonus Events
        OnStageAdsBonusChanged = null;

        // Event Crafting Events
        OnEventCraftingAutoChanged = null;
        OnEventCraftingStepUpReward = null;
        OnEventIapPackagePurchaseCompleted = null;
        
        // Event Gacha Events
        OnEventGachaAnyChanged = null;

        // Event Guide Mission Events
        OnEventGuideMissionChanged = null;
        OnEventGuideMissionProgressChanged = null;
        OnEventGuideMissionRewardable = null;

        // Event Score Events
        OnEventScoreChanged = null;
        OnEventScoreRewardStepChanged = null;

        // Event Schedule Events
        OnEventScheduleTimeChanged = null;

        // Event Currency Merchant Events
        OnEventCurrencyMerchantChanged = null;

        // Shop Events
        OnShopTabChanged = null;
        OnShopDailyRewardChanged = null;

        // Pass Events
        OnAnyPassPurchaseChanged = null;
        OnPassRewardedChanged = null;
        OnVipPassMissionRewardable = null;
        OnVipPassProgressChanged = null;

        // Tutorial Events
        OnTutorialStart = null;
        OnTutorialDone = null;

        // Piggy Bank Events
        OnPiggyBankProgressChanged = null;
        OnPiggyBankRewardClaimed = null;

        // Confirmed Gacha Events
        OnConfirmedGachaAnyChanged = null;

        // Island Events
        OnIslandOwnedItemChanged = null;
        OnIslandMountedItemChanged = null;
        OnIslandCraftingDecoChanged = null;
        OnIslandQuestProgressChanged = null;
    }
    #endregion
}