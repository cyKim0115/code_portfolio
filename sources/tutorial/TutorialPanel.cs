using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

public class TutorialPanel : MonoBehaviour
{
    public static TutorialPanel Instance;

    [SerializeField] private GameObject _goBackground;
    [SerializeField] private RectTransform _rtPanel;
    public RectTransform RtPanel => _rtPanel;
    [SerializeField] private RectTransform _rtCursor;
    [SerializeField] private GameObject _goHoleScreen;
    [SerializeField] private RectTransform _rtHoleScreen;
    [SerializeField] private DialoguePanel _tutorialDialoguePanel;

    public UnityAction OnClickHoleButtonAction;
    public UnityAction OnClickTouchBlockPanelAction;

    public async UniTask ButtonClickProcess(GameObject target)
    {
        AllOff();
        _goBackground.SetActive(true);

        var prevInfo = TutorialButtonInfo.GenerateInfo(target);
        await prevInfo.TutorialProcess();

        _goBackground.SetActive(false);
        SetCursorActive(false);
    }

    public async UniTask UnlockBoxClickProcess(UnlockBox unlockBox)
    {
        AllOff();
        _goHoleScreen.SetActive(true);

        var unlockBoxInfo = TutorialUnlockBoxInfo.GenerateInfo(unlockBox);
        await unlockBoxInfo.TutorialProcess();

        _goHoleScreen.SetActive(false);
        _goBackground.SetActive(false);
        SetCursorActive(false);
    }

    public async UniTask FocusOnScrollProcess(TutorialObject target)
    {
        AllOff();

        var focusScrollInfo = TutorialFocusScrollInfo.GenerateInfo(target);
        await focusScrollInfo.TutorialProcess();
    }

    public async UniTask FocusObjectProcess(TutorialObject target)
    {
        AllOff();

        var focusObjectInfo = TutorialFocusObjectInfo.GenerateInfo(target);
        await focusObjectInfo.TutorialProcess();
    }

    public async UniTask ObjectClickProcess(TutorialObject target)
    {
        AllOff();
        _goHoleScreen.SetActive(true);

        var objectClickInfo = TutorialObjectClickInfo.GenerateInfo(target);
        await objectClickInfo.TutorialProcess();

        _goHoleScreen.SetActive(false);
        _goBackground.SetActive(false);
        SetCursorActive(false);
    }

    public async UniTask DialogueProcess(int groupId)
    {
        AllOff();
        _tutorialDialoguePanel.gameObject.SetActive(true);

        await _tutorialDialoguePanel.DialogueProcess(groupId);
    }

    public async UniTask WaitForSecondProcess(float second)
    {
        AllOff();

        await UniTask.WaitForSeconds(second);
    }

    public async UniTask WaitForClosePopupProcess(string popupName)
    {
        AllOff();

        bool isClose = false;
        OnClickTouchBlockPanelAction += () => { isClose = true; };
        await UniTask.WaitUntil(() => isClose);
        OnClickTouchBlockPanelAction -= () => { isClose = true; };

        UIManager.Instance.GetPopup(popupName).CloseWithDelay();
    }

    public void SetCursorDirection(string direction)
    {
        switch (direction)
        {
            case "up":
                _rtCursor.rotation = Quaternion.Euler(0, 0, 0);
                break;
            case "down":
                _rtCursor.rotation = Quaternion.Euler(0, 0, 180);
                break;
            case "left":
                _rtCursor.rotation = Quaternion.Euler(0, 0, 90);
                break;
            case "right":
                _rtCursor.rotation = Quaternion.Euler(0, 0, 270);
                break;
            default:
                _rtCursor.rotation = Quaternion.Euler(0, 0, 0);
                break;
        }
    }

    public void SetCursorPosition(Vector3 position)
    {
        SetCursorActive(true);
        _rtCursor.position = position;
    }

    public void SetCursorActive(bool active)
    {
        _rtCursor.gameObject.SetActive(active);
    }

    public void SetHoleScreenPosition(Vector3 position)
    {
        _rtHoleScreen.position = position;
    }

    public void OnClickHoleButton()
    {
        OnClickHoleButtonAction?.Invoke();
    }

    public void OnClickTouchBlockPanel()
    {
        OnClickTouchBlockPanelAction?.Invoke();
    }

    private void AllOff()
    {
        _goBackground.SetActive(false);
        _goHoleScreen.SetActive(false);
        _tutorialDialoguePanel.gameObject.SetActive(false);
        SetCursorActive(false);
    }
}