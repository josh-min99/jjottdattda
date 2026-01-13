using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime; // optional (Player 타입 쓸 때)

public class StatLobbyUI : MonoBehaviour
{
    public static StatLobbyUI Instance;

    private bool startRequested = false;   // 내가 '시작 누름' 상태
    private bool readySent = false;        // Ready RPC를 이미 보냈는지
    private float roomCheckTimer = 0f;     // 가벼운 폴링

    [Header("UI References")]
    // 스탯 설정을 진행하는 로비 UI의 최상위 패널 (게임 시작 시 비활성화됨)
    public GameObject lobbyPanel; 
    // "남은 포인트: XX"를 표시할 텍스트
    public TextMeshProUGUI textRemainingPoints;

    // 게임시작화면의 텍스트
    [Header("Text")]
    public TextMeshProUGUI infectionText;
    public TextMeshProUGUI fatalityText;
    public TextMeshProUGUI spreadText;
    public Text virusTypeText;

    [Header("Stat: Infection Rate (치명률: 감염 확률)")]
    // 치명률(Infection) 관련 UI (텍스트 및 증감 버튼)
    public TextMeshProUGUI textInfection;
    public Button btnInfectionPlus;
    public Button btnInfectionMinus;

    [Header("Stat: Fatality Rate (치사율: 사망자 수)")]
    // 치사율(Fatality) 관련 UI
    public TextMeshProUGUI textFatality;
    public Button btnFatalityPlus;
    public Button btnFatalityMinus;

    [Header("Stat: Spread Power (전염률: 행동력/확산)")]
    // 전염률(Spread) 관련 UI
    public TextMeshProUGUI textSpread;
    public Button btnSpreadPlus;
    public Button btnSpreadMinus;

    [Header("Confirm")]
    // 설정 완료 후 게임 진입 버튼
    public Button btnGameStart;

    [Header("World References")]
    public GameObject tilesRootObj; // 타일 전체 부모 오브젝트 (TilesRoot)

    // 내부 데이터
    // 플레이어가 분배할 수 있는 총 스탯 포인트 (합계가 정확히 150이어야 함)
    private int totalPoints = 120; 
    private int currentPoints = 0; // (현재 로직에서는 GetRemainingPoints()로 계산하므로 직접 쓰이지는 않음)
    
    // 각 스탯의 초기값 (기본 50씩 할당)
    private int valInfection = 40; // 치명률 (감염 성공 확률)
    private int valFatality = 40; // 치사율 (사망자 발생 수)
    private int valSpread = 40; // 전염률 (행동력 결정 요인)

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 각 버튼에 스탯 변경 함수(람다식) 연결
        // ref 키워드를 사용해 실제 변수 값을 직접 수정하도록 설정
        btnInfectionPlus.onClick.AddListener(() => ModifyStat(ref valInfection, 5));
        btnInfectionMinus.onClick.AddListener(() => ModifyStat(ref valInfection, -5));

        btnFatalityPlus.onClick.AddListener(() => ModifyStat(ref valFatality, 5));
        btnFatalityMinus.onClick.AddListener(() => ModifyStat(ref valFatality, -5));

        btnSpreadPlus.onClick.AddListener(() => ModifyStat(ref valSpread, 5));
        btnSpreadMinus.onClick.AddListener(() => ModifyStat(ref valSpread, -5));

        // 게임 시작 버튼 연결
        btnGameStart.onClick.AddListener(OnGameStart);

        // 초기 UI 상태 갱신
        UpdateUI();

        // 로비가 떠있는 동안 뒤 타일 클릭 방지
        if (tilesRootObj != null) tilesRootObj.SetActive(false);
    }

    void Update()
    {
        if (GamePlayer.LocalPlayer != null)
        {
            infectionText.text = $"치명율 : {GamePlayer.LocalPlayer.infectionRate}%";

            float displayFatality = GamePlayer.LocalPlayer.fatalityRate;
            var gnm = GameNetworkManager.Instance;
            if (gnm != null && gnm.bioWeaponBuffActiveLocal)
                displayFatality *= 1.1f;

            fatalityText.text = $"치사율 : {displayFatality:0.0}%";

            spreadText.text = $"전염력 : {GamePlayer.LocalPlayer.spreadPower}%";
            virusTypeText.text = $"현재 바이러스\n{GamePlayer.LocalPlayer.currentVirus}";
        }
        if (!startRequested || readySent) return;        

        roomCheckTimer -= Time.deltaTime;
        if (roomCheckTimer > 0f) return;

        roomCheckTimer = 0.2f; // 0.2초마다 체크(가벼움)
        TrySendReadyIfPossible();
    }

    // 스탯 증감 처리 로직
    // statValue: 변경할 스탯 변수의 참조 (ref)
    // amount: 변경할 양 (+5 또는 -5)
    void ModifyStat(ref int statValue, int amount)
    {
        // 1. 포인트를 더하려고 할 때, 남은 포인트가 5 미만이면 증가 불가
        if (amount > 0 && GetRemainingPoints() < 5) return;
        
        // 2. 포인트를 빼려고 할 때, 스탯이 10 이하라면 감소 불가 (최소 스탯 10 유지)
        if (amount < 0 && statValue <= 10) return;

        // 값 변경 및 UI 갱신
        statValue += amount;
        UpdateUI();
    }

    // 현재 남은 포인트 계산 함수
    int GetRemainingPoints()
    {
        return totalPoints - (valInfection + valFatality + valSpread);
    }

    // 스탯 변경 시 UI 텍스트 및 버튼 상태 업데이트
    void UpdateUI()
    {
        int remain = GetRemainingPoints();
        textRemainingPoints.text = $"남은 포인트: {remain}";

        textInfection.text = $"치명율(확률): {valInfection}%";
        textFatality.text = $"치사율(사망): {valFatality}%";
        textSpread.text = $"전염력(확산): {valSpread}";

        bool roomFull = Photon.Pun.PhotonNetwork.InRoom &&
                Photon.Pun.PhotonNetwork.CurrentRoom.PlayerCount >= 2;

        // 남은 포인트가 정확히 0이어야만 게임 시작 가능 (설정 완료)
        btnGameStart.interactable = (remain == 0);
    }

    // 게임 시작 버튼 클릭 시 호출
    void OnGameStart()
    {
        if (GamePlayer.LocalPlayer == null) return;

        // 150포인트 다 쓴 상태에서만 눌리게 돼 있으니 여기선 스탯 확정
        GamePlayer.LocalPlayer.SetInitialStats(valInfection, valFatality, valSpread);

        // 시작 누름 예약
        startRequested = true;

        // 로비 UI 닫기 (원하면 유지해도 됨)
        lobbyPanel.SetActive(false);

        // 타일은 아직 켜지지 않게 (게임 시작 확정 전까지는)
        // if (tilesRootObj != null) tilesRootObj.SetActive(true); // 여기서 켜지 마

        Debug.Log("[LOBBY] 시작 요청됨. 상대 입장 대기중...");

        // 혹시 이미 2명 차있으면 즉시 Ready 보내기
        TrySendReadyIfPossible();
    }

    void TrySendReadyIfPossible()
    {
        if (!startRequested || readySent) return;
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        // ✅ 2명 찼을 때만 Ready 전송
        if (PhotonNetwork.CurrentRoom.PlayerCount < 2) return;

        if (GameNetworkManager.Instance == null) return;

        readySent = true;

        // 이제 진짜 게임 시작 단계로 넘어가도 됨: 타일 ON
        if (tilesRootObj != null) tilesRootObj.SetActive(true);

        GameNetworkManager.Instance.photonView.RPC(
            "RPC_PlayerReady",
            RpcTarget.MasterClient,
            PhotonNetwork.LocalPlayer.ActorNumber
        );

        Debug.Log("[LOBBY] 2명 입장 확인. Ready 전송 완료!");
    }

    public void RefreshLobbyStatsText()
    {
        if (GamePlayer.LocalPlayer == null) return;

            infectionText.text = $"치명율 : {GamePlayer.LocalPlayer.infectionRate}%";

        float displayFatality = GamePlayer.LocalPlayer.fatalityRate;
        var gnm = GameNetworkManager.Instance;
        if (gnm != null && gnm.bioWeaponBuffActiveLocal)
            displayFatality *= 1.1f;
            fatalityText.text = $"치사율 : {displayFatality:0.0}%";

            spreadText.text = $"전염력 : {GamePlayer.LocalPlayer.spreadPower}%";
            virusTypeText.text = $"현재 바이러스\n{GamePlayer.LocalPlayer.currentVirus}";
    }

}
