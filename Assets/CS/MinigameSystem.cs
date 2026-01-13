using UnityEngine;

public class MinigameSystem : MonoBehaviour
{
    // 이 스크립트가 붙은 오브젝트가 Panel_Minigame "안"에 있다면,
    // 최상위 패널을 끄기 위해 부모를 꺼줌
    private GameObject panelRoot;

    private void Awake()
    {
        // Panel_Minigame이 최상위라고 가정 (필요하면 여기만 바꾸면 됨)
        panelRoot = transform.root.gameObject;
        // 만약 Canvas가 root면 아래로 바꿔:
        // panelRoot = transform.parent.gameObject;  // 스크립트를 Panel_Minigame에 붙일 때
    }

    private void OnEnable()
    {
        // 여기서는 Pause 요청하지 말자 (아래 2번에서 이유 설명)
        // GameNetworkManager.Instance?.RequestPauseTime(true);
    }

    private void OnDisable()
    {
        // 여기서도 Pause 해제 요청하지 말자
        // GameNetworkManager.Instance?.RequestPauseTime(false);
    }

    public void WinnerBtnClick()
    {
        gameObject.SetActive(false); // Panel_Minigame OFF
        GameNetworkManager.Instance?.NotifyMinigameFinished();
    }

    public void LoseBtnClick()
    {
        gameObject.SetActive(false); // Panel_Minigame OFF
        GameNetworkManager.Instance?.NotifyMinigameFinished();
    }

    private void CloseMinigamePanel()
    {
        // 최상위 패널을 끄기
        if (panelRoot != null) panelRoot.SetActive(false);
        else gameObject.SetActive(false);
    }
}
