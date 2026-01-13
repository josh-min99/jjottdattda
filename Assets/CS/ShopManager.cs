using UnityEngine;
using Photon.Pun;
using TMPro;
using UnityEngine.UI;

public class ShopManager : MonoBehaviour
{
    // 싱글톤 인스턴스: UI 버튼 등 외부에서 접근하기 위함
    public static ShopManager Instance;

    [Header("UI References")]
    // 상점 UI 패널 (활성화/비활성화 대상)
    public GameObject uiShopPanel;

    [Header("Upgrade Costs")]
    // 각 업그레이드 및 아이템의 가격 정보
    public int costInfectionUpgrade = 100;
    public int costFatalityUpgrade = 100;
    public int costSpreadUpgrade = 100;
    public int costVaccineRecoveryUpgrade = 100;
    public int costVaccineSupplyUpgrade = 100;    

    [Header("Item Costs")]
    public int costRandomItem = 300;
    public int costBomb = 400;              // 
    public int costBioWeapon = 500;         //  (바이러스 증폭 생화학 무기)
    public int costGeneScissors = 750;          //  (유전자 가위)
    public int costGovtSecretDocument = 100;      // 정부 기밀 문서

    [Header("Cost Texts (Optional)")]
    // 가격 변경 시 UI에 반영하기 위한 텍스트 컴포넌트    
    public Text costInfectionText;
    public Text costFatalityText;
    public Text costRandomUpgradeText;
    public Text costVaccineUpgradeText;
    public Text costVaccineSupplyText;
    public Text costGovtSecretDocumentText;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 게임 시작 시 상점 패널이 화면을 가리지 않도록 숨김 처리
        if (uiShopPanel != null) 
            uiShopPanel.SetActive(false);

        // 초기 가격 정보를 UI에 표시
        UpdateCostUI();
    }

    // 상점 버튼 클릭 시 호출 (열려있으면 닫고, 닫혀있으면 염)
    public void OnClick_ToggleShop()
    {
        if (uiShopPanel != null)
        {
            bool isActive = uiShopPanel.activeSelf;
            uiShopPanel.SetActive(!isActive); 
        }
    }

    // 상점 패널 내의 닫기(X) 버튼 클릭 시 호출
    public void OnClick_CloseShop()
    {
        if (uiShopPanel != null)
            uiShopPanel.SetActive(false);
    }

    // 현재 가격 정보를 텍스트 UI에 갱신하는 함수
    void UpdateCostUI()
    {
        if (costInfectionText != null) costInfectionText.text = $"{costInfectionUpgrade} G";
        if (costFatalityText != null) costFatalityText.text = $"{costFatalityUpgrade} G";
        if (costRandomUpgradeText != null) costRandomUpgradeText.text = $"{costSpreadUpgrade} G";
        if (costVaccineUpgradeText != null) costVaccineUpgradeText.text = $"{costVaccineRecoveryUpgrade} G";
        if (costVaccineSupplyText != null) costVaccineSupplyText.text = $"{costVaccineSupplyUpgrade} G";
        if (costGovtSecretDocumentText != null) costGovtSecretDocumentText.text = $"{costGovtSecretDocument} G";
    }

    // [구매 로직] 치명률 업그레이드
    public void BuyUpgrade_Infection()
    {
        GamePlayer me = GamePlayer.LocalPlayer;
        if (me == null) return;

        // 돈이 충분한지 확인하고 지불
        if (me.TrySpendMoney(costInfectionUpgrade))
        {
            // 스탯 강화 적용
            me.infectionRate += 1f;
            Debug.Log($"전염률 강화 성공! 현재: {me.infectionRate}%");

            me.RecalculateActionPointsNow();
            
            // 다음 구매 가격 인상 (인플레이션)            
            UpdateCostUI();
            me.RecalculateActionPointsNow();
        }
    }

    // [구매 로직] 치사율 업그레이드
    public void BuyUpgrade_Fatality()
    {
        GamePlayer me = GamePlayer.LocalPlayer;
        if (me == null) return;

        if (me.TrySpendMoney(costFatalityUpgrade))
        {
            me.fatalityRate += 1f;
            Debug.Log($"치사율 강화 성공! 현재: {me.fatalityRate}%");
            
            // 가격 인상 및 UI 갱신            
            UpdateCostUI();
            if (GameNetworkManager.Instance != null)
            {
                GameNetworkManager.Instance.photonView.RPC(
                    "RPC_ReportPlayerStats",
                    RpcTarget.MasterClient,
                    PhotonNetwork.LocalPlayer.ActorNumber,
                    me.infectionRate,
                    me.fatalityRate,
                    me.spreadPower
                );
            }
 
        }
    }

    // [구매 로직] 전염률 업그레이드
    public void BuyUpgrade_Spread()
    {
        GamePlayer me = GamePlayer.LocalPlayer;
        if (me == null) return;

        if (me.TrySpendMoney(costSpreadUpgrade))
        {
            me.spreadPower += 1f;
            Debug.Log($"치사율 강화 성공! 현재: {me.spreadPower}%");
            me.RecalculateActionPointsNow();

            // 가격 인상 및 UI 갱신            
            UpdateCostUI();
        }
    }

    // [구매 로직] 랜덤 아이템 뽑기
    public void BuyItem_Random()
    {
        GamePlayer me = GamePlayer.LocalPlayer;
        if (me == null) return;

        if (me.TrySpendMoney(costRandomItem))
        {
            int randomIdx = Random.Range(1, 5); // 1~4 (Bomb, Bio, Scissors, Document)
            ItemManager.Instance.AcquireItem((ItemManager.ItemType)randomIdx);
            Debug.Log("랜덤 아이템 구매 완료!");
        }
    }

    // [구매 로직] 폭탄 구매 
    public void BuyItem_Bomb()
    {
        GamePlayer me = GamePlayer.LocalPlayer;
        if (me == null) return;

        if (me.TrySpendMoney(costBomb))
        {
            ItemManager.Instance.AcquireItem(ItemManager.ItemType.Bomb);
            Debug.Log("폭탄 구매 완료!");
        }
    }

    // [구매 로직] 바이러스 증폭 생화학 무기 구매
    public void BuyItem_BioWeapon()
    {
        GamePlayer me = GamePlayer.LocalPlayer;
        if (me == null) return;

        if (me.TrySpendMoney(costBioWeapon))
        {
            ItemManager.Instance.AcquireItem(ItemManager.ItemType.BioWeapon);
            Debug.Log("바이러스 증폭 생화학 무기 구매 완료!");
        }
    }

    // [구매 로직] 유전자 가위 구매
    public void BuyItem_GeneScissors()
    {
        GamePlayer me = GamePlayer.LocalPlayer;
        if (me == null) return;

        if (me.TrySpendMoney(costGeneScissors))
        {
            ItemManager.Instance.AcquireItem(ItemManager.ItemType.GeneScissors);
            Debug.Log("유전자 가위 구매 완료!");
        }
    }


    // [?? ??] ?? ?? ?? ??
    public void BuyItem_GovtSecretDocument()
    {
        GamePlayer me = GamePlayer.LocalPlayer;
        if (me == null) return;

        if (me.TrySpendMoney(costGovtSecretDocument))
        {
            ItemManager.Instance.AcquireItem(ItemManager.ItemType.GovtSecretDocument);
            Debug.Log("?? ?? ?? ?? ??!");
        }
    }

    public void BuyUpgrade_VaccineRecovery()
    {
        GamePlayer me = GamePlayer.LocalPlayer;

        Debug.Log($"[BuyUpgrade_VaccineRecovery] click. me={(me != null)} " +
              $"money={(me != null ? me.money : -1)} cost={costVaccineRecoveryUpgrade}");

        if (me == null) return;

        if (me.TrySpendMoney(costVaccineRecoveryUpgrade))
        {
            me.vaccineRecoveryRate += 5f;
            Debug.Log($"?? ??? ?? ??! ??: {me.vaccineRecoveryRate}%");

            me.PushVaccineUI();
            UpdateCostUI();
        }
        else
        {
            Debug.Log("[BuyUpgrade_VaccineRecovery] not enough money (TrySpendMoney failed)");
        }
    }

    public void BuyUpgrade_VaccineSupply()
    {
        GamePlayer me = GamePlayer.LocalPlayer;

        Debug.Log($"[BuyUpgrade_VaccineSupply] click. me={(me != null)} " +
              $"money={(me != null ? me.money : -1)} cost={costVaccineSupplyUpgrade}");

        if (me == null) return;

        if (me.TrySpendMoney(costVaccineSupplyUpgrade))
        {
            me.vaccineSupplyRate += 5f;
            Debug.Log($"?? ??? ?? ??! ??: {me.vaccineSupplyRate}%");

            me.RecalculateVaccineUses();
            me.PushVaccineUI();
            UpdateCostUI();
        }
        else
        {
            Debug.Log("[BuyUpgrade_VaccineSupply] not enough money (TrySpendMoney failed)");
        }
    }
}
