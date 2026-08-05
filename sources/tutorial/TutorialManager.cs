using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using User;

public class TutorialManager : ManagerBase<TutorialManager>
{
    private List<TutorialGroupData> _listTutorialGroup = new();
    private TutorialManager_GroupConditionChecker _groupConditionChecker = new();
    [ReadOnlyProperty][SerializeField] private int _currTutorialGroup = 0;
    public int CurrTutorialGroup => _currTutorialGroup;
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isDialogueInProgress = false;
    public bool IsDialogueInProgress => _isDialogueInProgress;

    public void Initialize()
    {
        _listTutorialGroup.Clear();

        foreach (var group in TableManager.Instance.GetTutorialGroupDataList())
        {
            if (UserData.Tutorial.IsDoneTutorial(group.group))
                continue;

            _listTutorialGroup.Add(group);
        }
    }

    public void StartCheck()
    {
        if (_currTutorialGroup != 0)
            return;

        // 기존 검사가 진행 중이면 취소
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();

        CheckAndStartWithDelay(_cancellationTokenSource.Token).Forget();
    }

    private async UniTask CheckAndStartWithDelay(CancellationToken cancellationToken)
    {
        try
        {
            // 한 프레임 대기하여 동일 프레임 내 여러 요청을 통합
            await UniTask.NextFrame(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            await CheckAndStart(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 취소된 경우 정상적으로 종료
        }
    }

    private async UniTask CheckAndStart(CancellationToken cancellationToken)
    {
        if (_listTutorialGroup.Count == 0)
            return;

        await WaitForSceneCondition(cancellationToken);

        if (cancellationToken.IsCancellationRequested)
            return;

        // 프롤로그 재생 중이면 대기
        await WaitForPrologueComplete(cancellationToken);

        if (cancellationToken.IsCancellationRequested)
            return;

        // 대화 진행 중이면 대기
        await WaitForDialogueComplete(cancellationToken);

        if (cancellationToken.IsCancellationRequested)
            return;

        // StageId 조건이 현재 스테이지보다 낮은 튜토리얼 그룹 완료 처리
        CheckAndCompleteOutdatedTutorials();

        if (_listTutorialGroup.Count == 0)
            return;

        TutorialGroupData targetData = null;
        foreach (var group in _listTutorialGroup)
        {
            if (_groupConditionChecker.CheckCondition(group))
            {
                targetData = group;
                break;
            }
        }

        if (targetData == null)
            return;

        await WaitForAllPopupClose(cancellationToken);

        if (cancellationToken.IsCancellationRequested)
            return;

        TutorialProcess(targetData).Forget();
    }

    private async UniTask TutorialProcess(TutorialGroupData group)
    {
        if (!_groupConditionChecker.CheckCondition(group))
        {
            Util.Debug.LogError($"TutorialManager : TutorialProcess - 튜토리얼 그룹 조건 충족 안됨: {group.group}");
            StartCheck();
            return;
        }

        UIManager.Instance.CloseAllPopup();
        UserData.BlockSave();

        _currTutorialGroup = group.group;
        EventManager.OnTutorialStart?.Invoke();

        // 튜토리얼 시작 이벤트 전송 (AppsFlyer, ByteBrew, Firebase)
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsInitialized)
        {
            NetworkManager.Instance.SendTutorialStart(group.group);
        }

        var listData = TableManager.Instance.GetTutorialDataList(group.group);
        TutorialPanel.Instance.gameObject.SetActive(true);
        bool isBreak = false;
        foreach (var data in listData)
        {
            if (isBreak)
                break;

            TutorialPanel.Instance.SetCursorDirection(data.direction);

            // Util.Debug.Log($"TutorialManager : 단계 {data.idx} \n type({data.type}), target({data.target})");
            switch (data.type)
            {
                case eTutorialType.ButtonClick:
                    {
                        var targetObj = TutorialUISystem.GetTutorialObject(data.target);
                        if (targetObj == null)
                        {
                            Util.Debug.LogError($"TutorialManager : ButtonClick - 오브젝트 없음 {data.target}");
                            isBreak = true;
                            break;
                        }

                        await TutorialPanel.Instance.ButtonClickProcess(targetObj.gameObject);
                        break;
                    }
                case eTutorialType.Dialogue:
                    {
                        int groupId = int.Parse(data.target);
                        await TutorialPanel.Instance.DialogueProcess(groupId);
                        break;
                    }
                case eTutorialType.WaitForSecond:
                    {
                        await TutorialPanel.Instance.WaitForSecondProcess(float.Parse(data.target));
                        break;
                    }
                case eTutorialType.WaitForPopup:
                    {
                        await WaitForOpenPopup(data.target);
                        await UniTask.WaitForSeconds(1.0f);
                        break;
                    }
                case eTutorialType.UnlockBoxClick:
                    {
                        var boxes = FindObjectsByType<UnlockBox>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                        UnlockBox targetBox = null;
                        foreach (var box in boxes)
                        {
                            if (box.description.Contains(data.target))
                            {
                                targetBox = box;
                                break;
                            }
                        }

                        if (targetBox == null)
                        {
                            Util.Debug.LogError($"TutorialManager : UnlockBoxClick - 오브젝트 없음 {data.target}");
                            isBreak = true;
                            break;
                        }

                        await TutorialPanel.Instance.UnlockBoxClickProcess(targetBox);
                        break;
                    }
                case eTutorialType.WaitForTab:
                    {
                        var tabIdx = Enum.Parse<eTabId>(data.target);
                        while (UIManager.Instance.CurrentTabIdx != tabIdx)
                            await UniTask.WaitForSeconds(0.1f);
                        await UniTask.WaitForSeconds(0.1f);
                        break;
                    }
                case eTutorialType.FocusOnScroll:
                    {
                        var targetObj = TutorialUISystem.GetTutorialObject(data.target);
                        if (targetObj == null)
                        {
                            Util.Debug.LogError($"TutorialManager : FocusOnScroll - 오브젝트 없음 {data.target}");
                            isBreak = true;
                            break;
                        }

                        if (!targetObj.gameObject.activeInHierarchy)
                        {
                            Util.Debug.LogWarning($"TutorialManager : FocusOnScroll - 오브젝트 비활성화 {data.target}, 튜토리얼 단계 스킵");
                            isBreak = true;
                            continue;
                        }

                        await TutorialPanel.Instance.FocusOnScrollProcess(targetObj);
                        await UniTask.WaitForSeconds(0.1f);
                        break;
                    }
                case eTutorialType.WaitForClosePopup:
                    {
                        await TutorialPanel.Instance.WaitForClosePopupProcess(data.target);
                        await WaitForClosePopup(data.target);
                        await UniTask.WaitForSeconds(0.2f);
                        break;
                    }
                case eTutorialType.ObjectClick:
                    {
                        var targetObj = TutorialUISystem.GetTutorialObject(data.target);
                        if (targetObj == null)
                        {
                            Util.Debug.LogError($"TutorialManager : ObjectClick - 오브젝트 없음 {data.target}");
                            isBreak = true;
                            break;
                        }

                        if (!targetObj.gameObject.activeInHierarchy)
                        {
                            Util.Debug.LogWarning($"TutorialManager : ObjectClick - 오브젝트 비활성화 {data.target}, 튜토리얼 단계 스킵");
                            isBreak = true;
                            continue;
                        }

                        await TutorialPanel.Instance.ObjectClickProcess(targetObj);
                        await UniTask.WaitForSeconds(0.1f);
                        break;
                    }
                case eTutorialType.FocusObject:
                    {
                        var targetObj = TutorialUISystem.GetTutorialObject(data.target);
                        if (targetObj == null)
                        {
                            Util.Debug.LogError($"TutorialManager : FocusOnObject - 오브젝트 없음 {data.target}");
                            isBreak = true;
                            break;
                        }

                        if (!targetObj.gameObject.activeInHierarchy)
                        {
                            Util.Debug.LogWarning($"TutorialManager : FocusObject - 오브젝트 비활성화 {data.target}, 튜토리얼 단계 스킵");
                            isBreak = true;
                            continue;
                        }

                        await TutorialPanel.Instance.FocusObjectProcess(targetObj);
                        await UniTask.WaitForSeconds(0.1f);
                        break;
                    }
            }
        }

        // Util.Debug.Log($"TutorialManager : 튜토리얼 완료 group({group.group})");

        TutorialPanel.Instance.gameObject.SetActive(false);
        UserData.UnblockSave();

        OnTutorialDone(group);
    }

    private void OnTutorialDone(TutorialGroupData group)
    {
        UserData.Tutorial.AddDoneTutorial(group.group);
        _listTutorialGroup.Remove(group);

        _currTutorialGroup = 0;
        EventManager.OnTutorialDone?.Invoke();

        // 튜토리얼 종료 이벤트 전송 (AppsFlyer, ByteBrew, Firebase)
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsInitialized)
        {
            NetworkManager.Instance.SendTutorialEnd(group.group);
        }

        StartCheck();
    }

    public void AllTutorialSetDone()
    {
        foreach (var group in _listTutorialGroup)
        {
            UserData.Tutorial.AddDoneTutorial(group.group);
        }

        _listTutorialGroup.Clear();

        _currTutorialGroup = 0;
        EventManager.OnTutorialDone?.Invoke();
    }

    private async UniTask WaitForOpenPopup(string popupName)
    {
        await UniTask.WaitUntil(() => UIManager.Instance.GetOpenedPopupListName().Contains(popupName));
    }

    private async UniTask WaitForClosePopup(string popupName)
    {
        await UniTask.WaitUntil(() => !UIManager.Instance.GetOpenedPopupListName().Contains(popupName));
    }

    private async UniTask WaitForAllPopupClose(CancellationToken cancellationToken)
    {
        await UniTask.WaitUntil(() => !UIManager.Instance.AnyPopupActive(), cancellationToken: cancellationToken);
        UIManager.Instance.CloseAllPopup();
    }

    private async UniTask WaitForSceneCondition(CancellationToken cancellationToken)
    {
        while (GameManager.Instance.CurrType == StageType.Stage && StageManager.Instance.CurrStage == null)
            await UniTask.WaitForSeconds(0.1f, cancellationToken: cancellationToken);

        while (GameManager.Instance.CurrType == StageType.Island && IslandManager.Instance.CurrIsland == null)
            await UniTask.WaitForSeconds(0.1f, cancellationToken: cancellationToken);

        while (GameManager.Instance.CurrType == StageType.Event && EventStageManager.Instance.CurrEventStage == null)
            await UniTask.WaitForSeconds(0.1f, cancellationToken: cancellationToken);

        while (MainSceneUI.Instance.IsSceneTransitionShowing())
            await UniTask.WaitForSeconds(0.1f, cancellationToken: cancellationToken);
    }

    private async UniTask WaitForPrologueComplete(CancellationToken cancellationToken)
    {
        while (MainSceneUI.Instance != null && MainSceneUI.Instance.IsProloguePlaying())
            await UniTask.WaitForSeconds(0.1f, cancellationToken: cancellationToken);
    }

    private async UniTask WaitForDialogueComplete(CancellationToken cancellationToken)
    {
        while (_isDialogueInProgress)
            await UniTask.WaitForSeconds(0.1f, cancellationToken: cancellationToken);
    }

    public void StartDialogueOnceTime(int groupId)
    {
        var key = $"Dialogue_Once_{groupId}";
        if (PlayerPrefs.HasKey(key))
            return;

        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();

        StartDialogue(groupId).Forget();
    }

    public async UniTask<bool> StartDialogue(int groupId)
    {
        // 튜토리얼 진행 중이면 대화 시작 불가
        if (_currTutorialGroup != 0)
        {
            Util.Debug.LogWarning($"TutorialManager : StartDialogue - 튜토리얼 진행 중이어서 대화 시작 불가 (groupId: {groupId})");
            return false;
        }

        // 이미 대화 진행 중이면 시작 불가
        if (_isDialogueInProgress)
        {
            Util.Debug.LogWarning($"TutorialManager : StartDialogue - 이미 대화 진행 중 (groupId: {groupId})");
            return false;
        }

        _isDialogueInProgress = true;

        try
        {
            UIManager.Instance.CloseAllPopup();
            TutorialPanel.Instance.gameObject.SetActive(true);
            await TutorialPanel.Instance.DialogueProcess(groupId);
        }
        finally
        {
            _isDialogueInProgress = false;
            // 대화 종료 후 튜토리얼 체크 재개
            TutorialPanel.Instance.gameObject.SetActive(false);
            StartCheck();
        }

        return true;
    }

    private void Awake()
    {
        EventManager.OnCraftingLevelUp += (id) => { StartCheck(); };
        EventManager.OnCurrencyAmountRefresh += (id, amount) => { if (id == CurrencyId.gold) StartCheck(); };
        EventManager.OnPE_EquipmentAdded += () => { StartCheck(); };

        EventManager.OnEventCraftingLevelUp += (id) => { StartCheck(); };
    }

    public bool IsBlockedByTutorialProgress(int tutorialId)
    {
        return !UserData.Tutorial.IsDoneTutorial(tutorialId) && _currTutorialGroup != tutorialId;
    }

    public void CompleteTutorialGroup(int tutorialGroupId)
    {
        // 리스트에서 해당 그룹 찾기
        TutorialGroupData targetGroup = null;
        foreach (var group in _listTutorialGroup)
        {
            if (group.group == tutorialGroupId)
            {
                targetGroup = group;
                break;
            }
        }

        // 리스트에 없거나 이미 완료된 튜토리얼이면 처리하지 않음
        if (targetGroup == null)
        {
            Util.Debug.LogWarning($"TutorialManager : CompleteTutorialGroup - 튜토리얼 그룹을 찾을 수 없음 (groupId: {tutorialGroupId})");
            return;
        }

        // 완료 처리
        UserData.Tutorial.AddDoneTutorial(tutorialGroupId);
        _listTutorialGroup.Remove(targetGroup);

        // 현재 진행 중인 튜토리얼이 해당 그룹이면 초기화
        if (_currTutorialGroup == tutorialGroupId)
        {
            _currTutorialGroup = 0;
        }

        // 이벤트 호출
        EventManager.OnTutorialDone?.Invoke();

        // 튜토리얼 종료 이벤트 전송 (AppsFlyer, ByteBrew, Firebase)
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsInitialized)
        {
            NetworkManager.Instance.SendTutorialEnd(tutorialGroupId);
        }

        // 다음 튜토리얼 체크
        StartCheck();
    }

    private void CheckAndCompleteOutdatedTutorials()
    {
        var currStageData = StageSystem.GetCurrStageData();
        if (currStageData == null)
            return;

        int currStageId = currStageData.ID;
        var groupsToRemove = new List<TutorialGroupData>();

        foreach (var group in _listTutorialGroup)
        {
            if (currStageId == 10103)
            {
                if (group.group == 19)
                {
                    if (UserData.Currency.GetCurrencyAmount(CurrencyId.hunt_sword) != 10
                    || UserData.Hunt.BossStage > 0)
                    {
                        UserData.Tutorial.AddDoneTutorial(group.group);
                        groupsToRemove.Add(group);
                    }

                    continue;
                }

                if (group.group == 66)
                {
                    if (UserData.Island.GetMountedCount("house_blueroof_cottage") == 1)
                    {
                        UserData.Tutorial.AddDoneTutorial(group.group);
                        groupsToRemove.Add(group);
                    }

                    continue;
                }
            }

            var dicCondition = TableManager.Instance.GetTutorialCondition(group.group);
            if (dicCondition == null)
                continue;

            // StageId 조건이 있는지 확인
            if (dicCondition.TryGetValue(eTutorialCondition.StageId, out var stageIdValue))
            {
                if (int.TryParse(stageIdValue, out var targetStageId))
                {
                    // StageId 조건 값이 현재 스테이지보다 낮으면 완료 처리
                    if (targetStageId < currStageId)
                    {
                        UserData.Tutorial.AddDoneTutorial(group.group);
                        groupsToRemove.Add(group);
                    }
                }
            }

            if (EventScheduleSystem.IsInEventSchedule)
            {
                if (dicCondition.TryGetValue(eTutorialCondition.EnoughUpgradeAuto, out var enoughUpgradeAutoValue))
                {
                    var craftingIds = TableManager.Instance.GetEventCraftingIdList(EventScheduleSystem.CurrentEventGroupId);
                    var craftingId = craftingIds[int.Parse(enoughUpgradeAutoValue)];

                    if (EventCraftingSystem.IsAuto(craftingId))
                    {
                        UserData.Tutorial.AddDoneTutorial(group.group);
                        groupsToRemove.Add(group);

                        continue;
                    }
                }

                if (dicCondition.TryGetValue(eTutorialCondition.UpgradableNpc, out var upgradableNpcValue))
                {
                    var listNpcId = TableManager.Instance.GetEventNPCInfoDataAll()
                                        .Where(x => x.npc_type == EventNpcType.Store)
                                        .ToList();
                    var targetNpcData = listNpcId[int.Parse(upgradableNpcValue)];

                    if (targetNpcData.GetLevel() >= 2)
                    {
                        UserData.Tutorial.AddDoneTutorial(group.group);
                        groupsToRemove.Add(group);

                        continue;
                    }
                }

                if (dicCondition.TryGetValue(eTutorialCondition.UpgradableStore, out var upgradableStoreValue))
                {
                    var craftingIds = TableManager.Instance.GetEventCraftingIdList(EventScheduleSystem.CurrentEventGroupId);
                    var craftingId = craftingIds[int.Parse(upgradableStoreValue)];

                    if (UserData.EventCrafting.GetStoreUpgradeLevel(craftingId) >= 2)
                    {
                        UserData.Tutorial.AddDoneTutorial(group.group);
                        groupsToRemove.Add(group);

                        continue;
                    }
                }
            }
        }

        // 완료 처리된 그룹들을 리스트에서 제거
        foreach (var group in groupsToRemove)
        {
            _listTutorialGroup.Remove(group);
        }
    }
}