using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VirusSelectUI : MonoBehaviour
{
    [Header("Panel")]
    // 바이러스 선택 팝업창의 최상위 부모 오브젝트 (활성화/비활성화용)
    public GameObject panelRoot; 

    [Header("Buttons")]
    // 각 바이러스 유형별 선택 버튼
    public Button btnRush;    // 돌진형
    public Button btnAmbush;  // 잠복형
    public Button btnGamble;  // 도박형
    public Button btnMutant;  // 변이형
    public Button btnBasic; // 기본형

    [Header("Count Texts")]
    // 각 바이러스의 사용 횟수를 표시할 텍스트 (예: "1/2")
    public TextMeshProUGUI textCountRush;
    public TextMeshProUGUI textCountAmbush;
    public TextMeshProUGUI textCountGamble;
    public TextMeshProUGUI textCountMutant;

    void Start()
    {
        // 각 버튼 클릭 시 해당 바이러스 타입을 인자로 넘겨주는 람다식 연결
        btnRush.onClick.AddListener(() => OnSelectVirus(GamePlayer.VirusType.Rush));
        btnAmbush.onClick.AddListener(() => OnSelectVirus(GamePlayer.VirusType.Ambush));
        btnGamble.onClick.AddListener(() => OnSelectVirus(GamePlayer.VirusType.Gamble));
        btnMutant.onClick.AddListener(() => OnSelectVirus(GamePlayer.VirusType.Mutant));
        btnBasic.onClick.AddListener(() => OnSelectVirus(GamePlayer.VirusType.None));

        // 게임 시작 시에는 팝업창을 닫아둠
        ClosePanel();
    }

    void Update()
    {
        if (panelRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            ClosePanel();
    }

    // UI 패널 열기 (외부 버튼 등에서 호출)
    public void OpenPanel()
    {
        panelRoot.SetActive(true);
        // 패널이 열릴 때마다 최신 사용 횟수 정보를 갱신
        UpdateUsageTexts();
    }

    // UI 패널 닫기
    public void ClosePanel()
    {
        panelRoot.SetActive(false);
    }

    // 바이러스 버튼 클릭 시 실행되는 내부 로직
    void OnSelectVirus(GamePlayer.VirusType type)
    {
        // 로컬 플레이어 인스턴스가 없으면 중단
        if (GamePlayer.LocalPlayer == null) return;

        // 플레이어 스크립트에 바이러스 교체 요청
        // TrySetVirus가 true를 반환하면 교체 성공이므로 창을 닫음
        if (GamePlayer.LocalPlayer.TrySetVirus(type))
        {
            ClosePanel();
        }
    }

    // 각 바이러스의 남은 사용 횟수를 UI에 표시하고 버튼 활성 상태 관리
    void UpdateUsageTexts()
    {
        if (GamePlayer.LocalPlayer == null) return;

        // 플레이어의 바이러스 사용 기록 딕셔너리 가져오기
        var counts = GamePlayer.LocalPlayer.virusUsageCounts;

        int maxRush = GamePlayer.LocalPlayer.GetVirusMaxUses(GamePlayer.VirusType.Rush);
        int maxAmbush = GamePlayer.LocalPlayer.GetVirusMaxUses(GamePlayer.VirusType.Ambush);
        int maxGamble = GamePlayer.LocalPlayer.GetVirusMaxUses(GamePlayer.VirusType.Gamble);
        int maxMutant = GamePlayer.LocalPlayer.GetVirusMaxUses(GamePlayer.VirusType.Mutant);

        // 텍스트 갱신 (현재 사용 횟수 / 최대 횟수)
        textCountRush.text = $"돌진형\n({counts[GamePlayer.VirusType.Rush]}/{maxRush})";
        textCountAmbush.text = $"잠복형\n({counts[GamePlayer.VirusType.Ambush]}/{maxAmbush})";
        textCountGamble.text = $"도박형\n({counts[GamePlayer.VirusType.Gamble]}/{maxGamble})";
        textCountMutant.text = $"변이형\n({counts[GamePlayer.VirusType.Mutant]}/{maxMutant})";

        // 사용 횟수 초과 시 버튼 비활성화
        btnRush.interactable = counts[GamePlayer.VirusType.Rush] < maxRush;
        btnAmbush.interactable = counts[GamePlayer.VirusType.Ambush] < maxAmbush;
        btnGamble.interactable = counts[GamePlayer.VirusType.Gamble] < maxGamble;
        btnMutant.interactable = counts[GamePlayer.VirusType.Mutant] < maxMutant;
    }
}
