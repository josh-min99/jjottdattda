using UnityEngine;
using Photon.Pun;
using TMPro;

public class ItemManager : MonoBehaviour
{
    public static ItemManager Instance;

    // ✅ 아이템 4개만
    public enum ItemType { None = 0, GeneScissors = 1, Bomb = 2, BioWeapon = 3, GovtSecretDocument = 4 }

    [Header("Selected (Runtime)")]
    public ItemType currentItem = ItemType.None;   // ✅ 이거 없어서 오류났을 확률 99%

    [Header("Counts (Runtime)")]
    public int countGeneScissors = 0;
    public int countBomb = 0;
    public int countBioWeapon = 0;
    
    public int countGovtSecretDocument = 0;
[Header("UI - Panel Root")]
    public GameObject itemPanel; // ✅ 아이템 패널 (켜지고 꺼지는 루트)

    [Header("UI - Count Texts")]
    public TextMeshProUGUI txtScissorsCount;
    public TextMeshProUGUI txtBombCount;
    public TextMeshProUGUI txtBioCount;

    public TextMeshProUGUI txtGovtSecretDocumentCount;
[Header("UI - Hint Text (Optional)")]
    public TextMeshProUGUI txtHint; // 없으면 비워도 됨

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 시작은 닫혀있게
        if (itemPanel != null) itemPanel.SetActive(false);

        RefreshUI();
        SetHint("");
    }

    // =========================
    // ✅ UI: 패널 토글 버튼에 연결
    // =========================
    public void ToggleItemPanel()
    {
        Debug.Log("[UI] ToggleItemPanel CALLED");

        if (itemPanel == null)
        {
            Debug.LogError("[UI] itemPanel is NULL");
            return;
        }

        bool next = !itemPanel.activeSelf;
        itemPanel.SetActive(next);

        if (next)
        {
            RefreshUI();
            SetHint("아이템을 선택하세요");
        }
        else
        {
            CancelSelection();
        }
    }

    public void CloseItemPanel()
    {
        if (itemPanel == null) return;
        itemPanel.SetActive(false);
        CancelSelection();
    }

    // =========================
    // ✅ UI: 아이템 버튼 3개 OnClick 연결
    // =========================
    public void OnClick_SelectBomb() => SelectItem(ItemType.Bomb);
    public void OnClick_SelectBioWeapon() => SelectItem(ItemType.BioWeapon);
    public void OnClick_SelectGeneScissors() => SelectItem(ItemType.GeneScissors);

    public void OnClick_SelectGovtSecretDocument() => SelectItem(ItemType.GovtSecretDocument);
void SetHint(string msg)
    {
        if (txtHint != null) txtHint.text = msg;
    }

    public void CancelSelection()
    {
        currentItem = ItemType.None;
        SetHint("아이템을 선택하세요");
    }

    // =========================
    // ✅ 상점에서 구매 시 호출: "수량만 증가" (자동 장착 X)
    // ShopManager에서 ItemManager.Instance.AcquireItem(ItemType.Bomb); 이런 식으로 호출
    // =========================
    public void AcquireItem(ItemType type, int amount = 1)
    {
        if (amount < 1) amount = 1;

        switch (type)
        {
            case ItemType.GeneScissors: countGeneScissors += amount; break;
            case ItemType.Bomb: countBomb += amount; break;
            case ItemType.BioWeapon: countBioWeapon += amount; break;
            case ItemType.GovtSecretDocument: countGovtSecretDocument += amount; break;
            default: return;
        }

        RefreshUI();
        SetHint($"{type} 구매 완료! 아이템 버튼을 눌러 선택하세요.");
        Debug.Log($"[Item] Acquired {type} +{amount}");
    }

    // =========================
    // ✅ 선택 로직
    // =========================
    public void SelectItem(ItemType type)
    {
        if (!HasItem(type))
        {
            SetHint("아이템이 부족합니다!");
            return;
        }

        // 같은 거 다시 누르면 취소
        if (currentItem == type)
        {
            CancelSelection();
            return;
        }

        currentItem = type;
        SetHint($"{type} 선택됨 - 타일을 클릭하세요.");
        Debug.Log($"[Item] Selected: {type}");
    }

    // =========================
    // ✅ 타일 클릭 시(GameNetworkManager.DetectClick)에서 호출
    // "요청"만 보내고, 소모는 승인 후(마스터가 성공 판단 후) 처리
    // =========================
    public bool TryUseItemOnTile(HexTile targetTile)
    {
        if (targetTile == null) return false;
        if (currentItem == ItemType.None) return false;

        if (!HasItem(currentItem))
        {
            SetHint("아이템이 부족합니다!");
            CancelSelection();
            return false;
        }

        if (GameNetworkManager.Instance == null)
        {
            Debug.LogError("[Item] GameNetworkManager.Instance is null");
            return false;
        }

        if (PhotonNetwork.LocalPlayer == null)
        {
            Debug.LogError("[Item] PhotonNetwork.LocalPlayer is null");
            return false;
        }

        // ✅ 마스터에게 사용 요청
        GameNetworkManager.Instance.photonView.RPC(
            "RPC_UseItem",
            RpcTarget.MasterClient,
            targetTile.tileID,
            PhotonNetwork.LocalPlayer.ActorNumber,
            (int)currentItem
        );

        Debug.Log($"[Item] Request sent: {currentItem} -> tile {targetTile.tileID}");

        // ✅ 여기서 CancelSelection() 하지 마!
        // 요청 중임을 안내만
        SetHint($"{currentItem} 요청 보냄... 승인 대기중");
        return true;
    }

    bool HasItem(ItemType type)
    {
        switch (type)
        {
            case ItemType.GeneScissors: return countGeneScissors > 0;
            case ItemType.Bomb: return countBomb > 0;
            case ItemType.BioWeapon: return countBioWeapon > 0;
            case ItemType.GovtSecretDocument: return countGovtSecretDocument > 0;
            default: return false;
        }
    }

    // =========================
    // ✅ 여기! ConsumeLocal이 없어서 너 오류났던 거임
    // =========================
    void ConsumeLocal(ItemType type)
    {
        switch (type)
        {
            case ItemType.GeneScissors: if (countGeneScissors > 0) countGeneScissors--; break;
            case ItemType.Bomb: if (countBomb > 0) countBomb--; break;
            case ItemType.BioWeapon: if (countBioWeapon > 0) countBioWeapon--; break;
            case ItemType.GovtSecretDocument: if (countGovtSecretDocument > 0) countGovtSecretDocument--; break;
        }

        RefreshUI();
    }

    void RefreshUI()
    {
        if (txtScissorsCount != null) txtScissorsCount.text = countGeneScissors.ToString();
        if (txtBombCount != null) txtBombCount.text = countBomb.ToString();
        if (txtBioCount != null) txtBioCount.text = countBioWeapon.ToString();
        if (txtGovtSecretDocumentCount != null) txtGovtSecretDocumentCount.text = countGovtSecretDocument.ToString();
    }

    // =========================
    // ✅ GameNetworkManager가 RPC로 알려줄 때 호출되는 "로컬 처리"
    // (RPC 자체는 GameNetworkManager에 있어야 함)
    // =========================
    public void OnItemFailed(string reason)
    {
        Debug.LogWarning($"[Item] Failed: {reason}");
        SetHint($"실패: {reason}");
        currentItem = ItemType.None;
    }

    public void OnItemConsumedApproved(ItemType type)
    {
        ConsumeLocal(type);
        SetHint($"{type} 사용 완료!");
        currentItem = ItemType.None;
        Debug.Log($"[Item] Consumed approved: {type}");
    }
}
