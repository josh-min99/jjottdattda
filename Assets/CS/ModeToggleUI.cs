using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ModeToggleUI : MonoBehaviour
{
    [Header("UI References")]
    // 모드 전환 버튼 컴포넌트
    public Button toggleButton;
    // 버튼 내부의 텍스트 (모드 상태 표시용)
    public TextMeshProUGUI buttonText;

    void Start()
    {
        // 버튼 클릭 이벤트에 함수 연결
        toggleButton.onClick.AddListener(OnToggle);
        
        // 게임 시작 시 현재 상태에 맞춰 UI 초기화
        UpdateUI();
    }

    // 버튼 클릭 시 호출되는 함수
    void OnToggle()
    {
        if (GameNetworkManager.Instance == null) return;

        // 현재 모드 상태를 반전 (공격 <-> 백신)
        GameNetworkManager.Instance.isAttackMode = !GameNetworkManager.Instance.isAttackMode;
        
        // 변경된 상태를 UI에 반영
        UpdateUI();
    }

    // 현재 모드에 따라 버튼의 텍스트와 색상을 변경하는 함수
    void UpdateUI()
    {
        if (GameNetworkManager.Instance == null) return;

        if (GameNetworkManager.Instance.isAttackMode)
        {
            // 공격 모드일 때: 붉은색 계열, 칼 아이콘 텍스트
            buttonText.text = "현재: 공격 모드";
            toggleButton.image.color = new Color(1f, 0.6f, 0.6f); 
        }
        else
        {
            // 백신 모드일 때: 푸른색 계열, 주사기 아이콘 텍스트
            buttonText.text = "현재: 백신 모드";
            toggleButton.image.color = new Color(0.6f, 0.8f, 1f); 
        }
    }
}
