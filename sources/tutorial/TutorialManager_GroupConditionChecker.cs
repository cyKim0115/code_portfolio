using System;
using System.Collections.Generic;
using System.Linq;
using User;
using Util;

public class TutorialManager_GroupConditionChecker
{
    private Dictionary<int, Dictionary<eTutorialCondition, int>> _dicTutorialConditionValue = new();
    private Dictionary<int, Dictionary<eTutorialCondition, CraftingId>> _dicTutorialConditionId = new();

    public bool CheckCondition(TutorialGroupData group)
    {
        var dicCondition = TableManager.Instance.GetTutorialCondition(group.group);
        foreach (var condition in dicCondition)
        {
            switch (condition.Key)
            {
                case eTutorialCondition.StageType:
                    {
                        if (condition.Value != GameManager.Instance.CurrType.ToString())
                            return false;

                        break;
                    }
                case eTutorialCondition.StageId:
                    {
                        if (!TryGetTutorialConditionValue(group, condition.Key, out var targetId))
                        {
                            targetId = int.Parse(condition.Value);
                            AddTutorialConditionValue(group, condition.Key, targetId);
                        }

                        if (targetId > StageSystem.GetCurrStageData().ID)
                            return false;

                        break;
                    }
                case eTutorialCondition.CraftingAllLevel:
                    {
                        var gameMode = GameManager.Instance.CurrType;
                        var totalLevel = 0L;
                        totalLevel = gameMode == StageType.Stage
                                        ? UserData.Crafting.TotalLevel()
                                        : UserData.EventCrafting.TotalLevel();
                        if (!TryGetTutorialConditionValue(group, condition.Key, out var targetLevel))
                        {
                            targetLevel = int.Parse(condition.Value);
                            AddTutorialConditionValue(group, condition.Key, targetLevel);
                        }

                        if (totalLevel < targetLevel)
                            return false;

                        break;
                    }
                case eTutorialCondition.ClearTutorialGroup:
                    {
                        if (!TryGetTutorialConditionValue(group, condition.Key, out var targetLevel))
                        {
                            targetLevel = int.Parse(condition.Value);
                            AddTutorialConditionValue(group, condition.Key, targetLevel);
                        }

                        if (!UserData.Tutorial.IsDoneTutorial(targetLevel))
                            return false;

                        break;
                    }
                case eTutorialCondition.AnyCostume:
                    {
                        if (!UserData.PEquipment.HasAnyCostume())
                            return false;

                        break;
                    }
                case eTutorialCondition.TargetCraftingLevel:
                    {
                        var arrValue = condition.Value.Split(',');

                        if (!TryGetTutorialConditionId(group, condition.Key, out var craftingId))
                        {
                            if (!Enum.TryParse(arrValue[0], out craftingId))
                            {
                                Util.Debug.LogError($"TutorialManager_GroupConditionChecker : TryGetTutorialConditionValue - 파싱할 수 없는 값 id({condition.Value})");
                                return false;
                            }
                            AddTutorialConditionId(group, condition.Key, craftingId);
                        }

                        if (!TryGetTutorialConditionValue(group, condition.Key, out var targetLevel))
                        {
                            targetLevel = int.Parse(arrValue[1]);
                            AddTutorialConditionValue(group, condition.Key, targetLevel);
                        }

                        if (UserData.Crafting.GetLevel(craftingId) < targetLevel)
                            return false;

                        break;
                    }
                case eTutorialCondition.EnoughUpgradeCost:
                    {
                        var upgradeId = long.Parse(condition.Value);
                        var upgradeData = TableManager.Instance.GetUpgradeData(upgradeId);
                        if (upgradeData == null)
                            return false;

                        if (upgradeData.cost.GetUnitValue() > UserData.Currency.GetCurrencyAmount(CurrencyId.gold))
                            return false;
                        break;
                    }
                case eTutorialCondition.EnoughToGoNextStage:
                    {
                        var cost = StageSystem.GetGoNextStageReqCost();

                        if (UserData.Currency.GetCurrencyAmount(CurrencyId.gold) < cost)
                            return false;

                        break;
                    }
                case eTutorialCondition.UnlockNpc:
                    {
                        var characterId = condition.Value;
                        var listCharacter = UserData.Upgrade.GetCharacterList();
                        if (!listCharacter.Any(x => x.id == characterId))
                            return false;

                        if (GameManager.Instance.CurrType != StageType.Stage)
                            return false;

                        if (StageManager.Instance.CurrStage == null)
                            return false;

                        if (!StageManager.Instance.CurrStage.Character.IsAnyWorker(condition.Value))
                            return false;

                        break;
                    }
                case eTutorialCondition.EnoughUpgradeAuto:
                    {
                        if (GameManager.Instance.CurrType != StageType.Event)
                            return false;

                        var craftingIds = TableManager.Instance.GetEventCraftingIdList(EventScheduleSystem.CurrentEventGroupId);
                        var craftingId = craftingIds[int.Parse(condition.Value)];

                        if (EventCraftingSystem.IsAuto(craftingId))
                            return false;

                        var cost = EventCraftingSystem.GetAutoCost(craftingId);
                        if (UserData.Currency.GetCurrencyAmount(CurrencyId.event_gold) < cost)
                            return false;

                        break;
                    }
                case eTutorialCondition.ReachScoreStep:
                    {
                        var targetStep = int.Parse(condition.Value);
                        if (EventScoreSystem.CachedScoreRewardStep < targetStep)
                            return false;

                        break;
                    }
                case eTutorialCondition.UpgradableNpc:
                    {
                        var listNpcId = TableManager.Instance.GetEventNPCInfoDataAll()
                                            .Where(x => x.npc_type == EventNpcType.Store)
                                            .ToList();
                        var targetNpcData = listNpcId[int.Parse(condition.Value)];
                        if (targetNpcData == null)
                            return false;

                        if (!targetNpcData.CanLevelUp())
                            return false;

                        break;
                    }
                case eTutorialCondition.UpgradableStore:
                    {
                        var craftingIds = TableManager.Instance.GetEventCraftingIdList(EventScheduleSystem.CurrentEventGroupId);
                        var craftingId = craftingIds[int.Parse(condition.Value)];

                        if (!EventStoreSystem.CanLevelUp(craftingId))
                            return false;

                        break;
                    }
            }
        }

        return true;
    }

    private bool TryGetTutorialConditionValue(TutorialGroupData group, eTutorialCondition condition, out int value)
    {
        if (_dicTutorialConditionValue.TryGetValue(group.group, out var dicConditionValue))
        {
            if (dicConditionValue.TryGetValue(condition, out value))
                return true;
        }

        value = 0;
        return false;
    }

    private void AddTutorialConditionValue(TutorialGroupData group, eTutorialCondition condition, int value)
    {
        _dicTutorialConditionValue.TryAdd(group.group, new Dictionary<eTutorialCondition, int>());
        _dicTutorialConditionValue[group.group][condition] = value;
    }

    private bool TryGetTutorialConditionId(TutorialGroupData group, eTutorialCondition condition, out CraftingId id)
    {
        if (_dicTutorialConditionId.TryGetValue(group.group, out var dicConditionId))
        {
            if (dicConditionId.TryGetValue(condition, out id))
                return true;
        }

        id = CraftingId.None;
        return false;
    }

    private void AddTutorialConditionId(TutorialGroupData group, eTutorialCondition condition, CraftingId id)
    {
        _dicTutorialConditionId.TryAdd(group.group, new Dictionary<eTutorialCondition, CraftingId>());
        _dicTutorialConditionId[group.group][condition] = id;
    }
}