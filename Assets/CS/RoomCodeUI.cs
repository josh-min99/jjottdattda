using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 방 코드 입력 UI 컨트롤러
/// Unity 에디터에서:
/// 1. Canvas 생성
/// 2. InputField (TMP) 추가 → roomCodeInput에 연결
/// 3. Button 추가 → OnConnectButtonClick() 연결
/// 4. 이 스크립트를 Canvas에 추가
/// </summary>
public class RoomCodeUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField roomCodeInput; // 방 코드 입력 필드
    public Button connectButton;         // 연결 버튼
    public TextMeshProUGUI statusText;   // 상태 텍스트 (선택사항)
    public GameObject roomCodePanel;     // 방 코드 입력 패널 (선택사항)

    void Start()
    {
        // 버튼 클릭 이벤트 연결
        if (connectButton != null)
        {
            connectButton.onClick.AddListener(OnConnectButtonClick);
        }

        // InputField에 Enter 키 입력 시 연결
        if (roomCodeInput != null)
        {
            roomCodeInput.onSubmit.AddListener(OnInputSubmit);
        }

        UpdateStatusText("방 코드를 입력하세요 (4자리 이상)");
    }

    // 연결 버튼 클릭 시
    public void OnConnectButtonClick()
    {
        string code = roomCodeInput.text.Trim();

        if (string.IsNullOrEmpty(code) || code.Length < 4)
        {
            UpdateStatusText("❌ 방 코드는 최소 4자리여야 합니다!");
            return;
        }

        UpdateStatusText($"🔌 방 '{code}'에 연결 중...");

        // GameNetworkManager에 방 코드 전달하여 연결 시작
        if (GameNetworkManager.Instance != null)
        {
            GameNetworkManager.Instance.ConnectWithRoomCode(code);

            // UI 패널 숨기기 (선택사항)
            if (roomCodePanel != null)
            {
                roomCodePanel.SetActive(false);
            }
        }
        else
        {
            UpdateStatusText("❌ GameNetworkManager를 찾을 수 없습니다!");
        }
    }

    // InputField에서 Enter 키 입력 시
    void OnInputSubmit(string code)
    {
        OnConnectButtonClick();
    }

    // 상태 텍스트 업데이트
    void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log(message);
    }
}
