using UnityEngine;
using Photon.Pun;
using TMPro;
using UnityEngine.UI;

public class AdminManager : MonoBehaviourPun
{
    // 싱글톤 인스턴스: 외부에서 AdminManager에 쉽게 접근하기 위함
    public static AdminManager Instance;

    [Header("Admin UI")]
    // 관리자 UI 패널 (활성화/비활성화 대상)
    public GameObject adminPanel;
    // 명령어 입력을 받는 텍스트 필드
    public InputField inputCmd;

    void Awake()
    {
        // 스크립트 로드 시 싱글톤 인스턴스 초기화
        Instance = this;
    }

    void Start()
    {
        // 게임 시작 시 관리자 패널을 기본적으로 숨김 처리
        if (adminPanel != null)
            adminPanel.SetActive(false);
    }

    void Update()
    {
        // '/' 키를 누르면 관리자 패널을 열거나 닫음
        if (Input.GetKeyDown(KeyCode.Slash))
        {
            ToggleAdminPanel();
        }
    }

    // 관리자 패널의 활성화 상태를 토글하는 함수
    void ToggleAdminPanel()
    {
        if (adminPanel == null) return;

        // 현재 패널의 활성화 상태를 확인
        bool isActive = adminPanel.activeSelf;
        
        // 상태를 반전시켜 패널을 켜거나 끔
        adminPanel.SetActive(!isActive);

        // 패널을 켜는 상황(!isActive가 true였으므로 현재는 켜진 상태)일 때 처리
        if (!isActive) 
        {
            if (inputCmd != null) 
            {
                // 입력 필드 내용을 비움
                inputCmd.text = ""; 
                // 사용자가 바로 타이핑할 수 있도록 포커스 설정
                inputCmd.ActivateInputField(); 
            }
        }
    }

    // 입력 필드에서 엔터 키를 누르거나 전송 버튼 클릭 시 호출되는 함수
    public void OnSubmitCommand()
    {
        if (inputCmd == null) return;

        // 입력된 텍스트 가져오기
        string cmd = inputCmd.text;
        
        // '/' 키로 창을 열 때 입력 필드에 들어갈 수 있는 '/' 문자를 제거하고 앞뒤 공백 삭제
        cmd = cmd.Replace("/", "").Trim(); 

        // 내용이 비어있다면 처리하지 않음
        if (string.IsNullOrEmpty(cmd)) return;

        // 명령어 처리 로직 실행
        ProcessCommand(cmd);

        // 명령어 실행 후 다음 입력을 위해 필드 초기화 및 포커스 재설정
        inputCmd.text = ""; 
        inputCmd.ActivateInputField(); 
    }

    // 실제 명령어를 파싱하고 기능을 수행하는 핵심 로직
    void ProcessCommand(string cmd)
    {
        // 디버깅을 위해 입력된 명령어를 콘솔에 출력
        Debug.Log($"[Admin Command] {cmd}");

        // 공백을 기준으로 명령어를 분리 (예: "give money 1000" -> ["give", "money", "1000"])
        string[] parts = cmd.Split(' ');
        if (parts.Length < 2) return;

        // 첫 번째 단어는 수행할 동작(Action)
        string action = parts[0]; 

        // 1. 자원 및 아이템 지급 명령어 (give)
        if (action == "give")
        {
            string target = parts[1]; // 대상 (money, item 등)
            
            // 돈 지급 로직
            if (target == "money")
            {
                // 세 번째 인자로 금액이 들어왔는지 확인 및 정수 변환
                if (parts.Length > 2 && int.TryParse(parts[2], out int amount))
                {
                    if (GamePlayer.LocalPlayer != null)
                        GamePlayer.LocalPlayer.AddMoney(amount);
                }
            }
            // 아이템 지급 로직
            else if (target == "item")
            {
                if (parts.Length > 2)
                {
                    string itemName = parts[2];
                    ItemManager.ItemType type = ItemManager.ItemType.None;
                    
                    // 아이템 이름에 따라 타입 매핑
                    if (itemName == "bomb") type = ItemManager.ItemType.Bomb;
                    else if (itemName == "bio") type = ItemManager.ItemType.BioWeapon;
                    else if (itemName == "scissors") type = ItemManager.ItemType.GeneScissors;
                    else if (itemName == "document") type = ItemManager.ItemType.GovtSecretDocument;
                    
                    // 유효한 아이템 타입이고 매니저가 존재하면 아이템 획득 처리
                    if (type != ItemManager.ItemType.None && ItemManager.Instance != null)
                        ItemManager.Instance.AcquireItem(type);
                }
            }
        }
        // 2. 플레이어 스탯 설정 명령어 (set)
        else if (action == "set")
        {
            // "set stat 감염율 치사율 전파력" 형태인지 확인
            if (parts[1] == "stat" && parts.Length >= 5)
            {
                // 각 수치를 정수로 파싱 시도
                if (int.TryParse(parts[2], out int inf) && 
                    int.TryParse(parts[3], out int fat) && 
                    int.TryParse(parts[4], out int spread))
                {
                    if (GamePlayer.LocalPlayer != null)
                    {
                        // 로컬 플레이어의 스탯 강제 변경
                        GamePlayer.LocalPlayer .infectionRate = inf;
                        GamePlayer.LocalPlayer.fatalityRate = fat;
                        GamePlayer.LocalPlayer.spreadPower = spread;
                        GamePlayer.LocalPlayer.RecalculateActionPointsNow();
                        Debug.Log("관리자 권한으로 스탯 변경됨.");
                    }
                }
            }
        }
        // 3. 문제 해결 진행도 업데이트 명령어 (problem)
        else if (action == "problem")
        {
            // "problem 팀번호 문제수" 형태인지 확인
            if (parts.Length >= 3)
            {
                if (int.TryParse(parts[1], out int team) && 
                    int.TryParse(parts[2], out int problemCount))
                {
                    // 마스터 클라이언트만 네트워크 RPC를 호출할 수 있도록 제한
                    if (GameNetworkManager.Instance != null && PhotonNetwork.IsMasterClient)
                    {
                        // 모든 클라이언트(RpcTarget.All)에 생존자 문제 해결 현황 전파
                        GameNetworkManager.Instance.photonView.RPC("RPC_ProcessSurvivorProblems", 
                            RpcTarget.All, team, problemCount);
                        Debug.Log($"[조교 입력] 팀 {team}이 {problemCount}문제 해결");
                    }
                }
            }
        }
    }
}
