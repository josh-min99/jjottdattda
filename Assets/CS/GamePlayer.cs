using UnityEngine;
using Photon.Pun;
using TMPro;
using System.Collections.Generic;

public class GamePlayer : MonoBehaviourPunCallbacks
{
    // 로컬 ?�레?�어(???�신)???�게 ?�근?�기 ?�한 ?��???
    public static GamePlayer LocalPlayer; 

    // 게임 ???�용?�는 4가지 ?�수 바이?�스 ?�형
    public enum VirusType { None, Rush, Ambush, Gamble, Mutant }

    [Header("Player Info")]
    public int myTeam;       // 1: Red Team, 2: Blue Team
    public int money = 0;    // ?�재 보유 ?�금
    
    // ?�재 ?�착 중인 바이?�스 (?�략)
    public VirusType currentVirus = VirusType.None; 

    // ?�동??Action Point) ?�스??변??
    public int maxActionPoints = 0;     // ?�번 ?�에 ?�용?????�는 최�? ?�동??
    public int currentActionPoints = 0; // ?�재 ?��? ?�동??

    // 바이?�스 ?�용 ?�수 ?�한 관�?(�?바이?�스 ??최�? 2??
    public Dictionary<VirusType, int> virusUsageCounts = new Dictionary<VirusType, int>()
    {
        { VirusType.Rush, 0 },
        { VirusType.Ambush, 0 },
        { VirusType.Gamble, 0 },
        { VirusType.Mutant, 0 }
    };

    [Header("Current Virus Stats")]
    private float prevInfectionRate = 0f;
    private float prevSpreadPower = 0f;
    private float prevFatalityRate = 0f;
    private bool revertVirusStatsNextTurn = false;
    public float infectionRate = 50f; // 치명�? 감염?�률
    public float spreadPower = 50f;   // ?�염�? ?�동??
    public float fatalityRate = 50f;  // 치사?? ?�구감소??

    [Header("Vaccine Stats")]
    public float vaccineRate = 0;   // legacy: 공용 백신 ?�치
    public int vaccineRange = 0;      // 백신 ?�거�?(?�??�???
    public int maxVaccineUses = 0;     // ?�번 ?�에 ?�용?????�는 최�? 백신 ?�용 ?�수 (보급�?기반)
    public int currentVaccineUses = 0; // ?�재 ?�용??백신 ?�수

    [Header("Survivor System")]
    [Tooltip("?�류?�원????문제 ??(조교/관리자가 ?�력)")]
    public int solvedProblemsCount = 0;

    [Header("Mutant Buff System (5?�운??")]
    public bool hasMutantBuff = false;              // ?�연변??버프 ?�성???��?
    public bool usedGuaranteedInfection = false;   // 감염 ?�공�?100% ?�용 ?��? (1번만)
    public int mutantBuffRound = 0;                 // 버프가 ?�용???�운??(1?�운??지??

    [Header("Disaster Debuff System")]
    public bool hasBlackoutDebuff = false;          // ?�전 ?�태 ?�버?? 감염 가???�??-1 

    private bool revertBlackoutStatsNextTurn = false;
    private float prevBlackoutInfectionRate = 0f;
    private float prevBlackoutFatalityRate = 0f;

    public float vaccineRecoveryRate = 0f; // ?�복�??�공�? ?�시??
    public float vaccineSupplyRate = 0f; // 보급�??�용 가?�량 계산?? ?�시??

    void Awake()
    {
        // ?�셔?�리 ?�전 초기??(?�락 방�?)
        if (virusUsageCounts == null)
        {
            virusUsageCounts = new Dictionary<VirusType, int>
            {
                { VirusType.Rush, 0 },
                { VirusType.Ambush, 0 },
                { VirusType.Gamble, 0 },
                { VirusType.Mutant, 0 }
            };
        }
    }

    void Start()
    {
        // ?�재 바이?�스(초기�??�함) 마스?�에�?보고
        if (GameNetworkManager.Instance != null)
        {
            GameNetworkManager.Instance.photonView.RPC(
                "RPC_ReportCurrentVirus",
                RpcTarget.MasterClient,
                PhotonNetwork.LocalPlayer.ActorNumber,
                (int)currentVirus
            );
        }

        // 로컬 ?�레?�어 초기??
        if (photonView.IsMine)
        {
            LocalPlayer = this;

            // ?� 배정
            myTeam = (PhotonNetwork.LocalPlayer.ActorNumber == 1) ? 1 : 2;

            GameUIManager.Instance?.UpdateMyTeamText(myTeam);
            GameUIManager.Instance?.UpdateMoney(money);
            GameUIManager.Instance?.UpdateActionPoints(currentActionPoints, maxActionPoints);

            Debug.Log($"[GamePlayer] ?�성 ?�료. Team: {myTeam}");
        }

        // ===== 백신 초기�??�책 =====
        // ?�거리는 기본 3�??��? ?�한 �?
        vaccineRate = 0;
        vaccineRange = 3;

        // ?�청?�항: ?�작 UI???�복�?보급�?0?�로 보이�?
        vaccineRecoveryRate = 0f;
        vaccineSupplyRate = 0f;

        // ?�작???�용 불�? ?�태�????�작 ??계산?�서 지�?
        maxVaccineUses = 0;
        currentVaccineUses = 0;

        PushVaccineUI();

        // 첫 턴 전에도 팀 치사율이 반영되도록 마스터에 초기 스탯 보고
        if (photonView.IsMine && GameNetworkManager.Instance != null)
        {
            GameNetworkManager.Instance.photonView.RPC(
                "RPC_ReportPlayerStats",
                RpcTarget.MasterClient,
                PhotonNetwork.LocalPlayer.ActorNumber,
                infectionRate,
                fatalityRate,
                spreadPower
            );
        }
    }

    public void PushVaccineUI()
    {
        if (GameUIManager.Instance != null)
            GameUIManager.Instance.UpdateVaccineStats(
                vaccineRecoveryRate,
                vaccineSupplyRate,
                vaccineRange,
                currentVaccineUses,
                maxVaccineUses
            );
    }

    // ??[?�심] ???�작 ??GameNetworkManager???�해 ?�출??
    // ?�동?�을 ?�충?�하�? 바이?�스 ?�성(?�시�????�용??
    public void OnTurnStart(int turnNumber)
    {
        if (!photonView.IsMine) return;

        if (revertBlackoutStatsNextTurn)
        {
            infectionRate = prevBlackoutInfectionRate;
            fatalityRate = prevBlackoutFatalityRate;
            revertBlackoutStatsNextTurn = false;
            hasBlackoutDebuff = false;
        }


        if (revertVirusStatsNextTurn)
        {
            infectionRate = prevInfectionRate;
            spreadPower = prevSpreadPower;
            fatalityRate = prevFatalityRate;
            currentVirus = VirusType.None;
            revertVirusStatsNextTurn = false;
        }

        // 1. ?�동??AP) 계산 공식: ?�염�?/ 20 (최소 1 보장)
        // ?�염률이 ?�을?�록 ???�에 ??많�? ?�동???????�음
        maxActionPoints = Mathf.FloorToInt(spreadPower / 20f);
        if (maxActionPoints < 1) maxActionPoints = 1;

        // 2. 바이?�스�??�시�??�력 ?�용
        if (currentVirus == VirusType.Rush)
        {
            maxActionPoints += 1; // ?�진?? 기본 ?�동??+1
            Debug.Log("[?�진?? 보너?�로 ?�동?�이 +1 ?�었?�니??");
        }
        else if (currentVirus == VirusType.Gamble)
        {
            // ?�박?? ???�널???�거
        }
        else if (currentVirus == VirusType.Mutant)
        {
            // 변?�형: �????�탯???�덤?�게 변경됨
            RandomizeStatsForMutant();
        }

        // 3. ?�복(Ambush) ?�태?�?????�?�들???�시 보이�??�제
        //CheckAndClearAmbush();

        // 5. ?�동??충전 �?UI 반영
        currentActionPoints = maxActionPoints;

        // 6. ?�동??UI 갱신
        if (GameUIManager.Instance != null)
            GameUIManager.Instance.UpdateActionPoints(currentActionPoints, maxActionPoints);

        Debug.Log($"[???�작] ?�동?? {currentActionPoints} (?�염�?기반)");

        // 6. 백신 보급률에 ?�른 ?�용 가???�??개수 계산

        // max 계산
        RecalculateVaccineUses();

        // ?�번 ???�용?�수 리셋(?�기?�만!)
        if (GameNetworkManager.Instance != null)
        {
            GameNetworkManager.Instance.photonView.RPC(
                "RPC_ReportPlayerStats",
                RpcTarget.MasterClient,
                PhotonNetwork.LocalPlayer.ActorNumber,
                infectionRate,
                fatalityRate,
                spreadPower
            );
        }

        ResetVaccineUsesForNewTurn();
    }

    public void RecalculateVaccineUses()
    {
        // ?�시 �??�희가 �??�해????:
        // 보급�?0~19 => 0??
        // 20~39 => 1??
        // 40~59 => 2??...
        maxVaccineUses = Mathf.FloorToInt(vaccineSupplyRate / 10f);

        // 최소 1??강제 ?�거 (기획 반영)
        if (maxVaccineUses < 0) maxVaccineUses = 0;

        // ?�용 ?�수???�기??건드리�? ?�음 (리셋?� ???�작?�서�?)
        PushVaccineUI();
    }

    // 백신 ?�용 가???��? ?�인
    public bool CanUseVaccine()
    {
        return currentVaccineUses < maxVaccineUses;
    }

    // 백신 ?�용 ???�출
    public void OnVaccineUsed()
    {
        currentVaccineUses++;
        PushVaccineUI();
    }

    // ?�연변??버프: ?�기 ?�???�구 ?�동 ?�복
    void HealMyTiles()
    {
        if (!photonView.IsMine) return;
        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null) return;

        // 마스???�라?�언?�에�??�복 ?�청
        if (PhotonNetwork.IsMasterClient)
        {
            // 마스???�라?�언?�는 직접 처리
            foreach (var tile in MapGenerator.Instance.allTiles.Values)
            {
                if (tile.ownerTeam == myTeam && tile.population > 0)
                {
                    // ?�기 ?�???�구 ?�동 ?�복 (10명씩 ?�복)
                    int oldPop = tile.population;
                    int healedPop = tile.population + 10;
                    tile.population = healedPop;
                    tile.UpdateVisuals(tile.ownerTeam, tile.population);
                    Debug.Log($"[?�연변??버프] ?�??{tile.tileID} ?�구 ?�복: {oldPop} -> {tile.population}");
                }
            }
        }
        else
        {
            // ?�반 ?�라?�언?�는 RPC�??�청
            GameNetworkManager.Instance.photonView.RPC("RPC_HealMyTiles", RpcTarget.MasterClient, PhotonNetwork.LocalPlayer.ActorNumber);
        }
    }

    // ?�동???�용 ?�도 (공격/?�동 ???�션 ?�행 ???�출)
    // ?�공 ??true 반환 �?AP 감소, ?�패 ??false 반환
    public bool TryUseActionPoint()
    {
        if (currentActionPoints > 0)
        {
            currentActionPoints--;
            Debug.Log($"?�동???�모! ?��? AP: {currentActionPoints}");
            
            if (GameUIManager.Instance != null)
                GameUIManager.Instance.UpdateActionPoints(currentActionPoints, maxActionPoints);
            
            return true;
        }
        else
        {
            Debug.Log("?�동?�이 부족합?�다!");
            return false;
        }
    }

    // [?�박???�널?? ???�토 �?무작????곳의 ?�구 감소
    void ApplyGambleOverheat()
    {
        List<int> myTileIDs = new List<int>();
        if (MapGenerator.Instance != null)
        {
            // ???�유???�??ID ?�집
            foreach(var tile in MapGenerator.Instance.allTiles.Values)
            {
                if (tile.ownerTeam == myTeam) myTileIDs.Add(tile.tileID);
            }
        }

        if (myTileIDs.Count > 0)
        {
            int rndID = myTileIDs[Random.Range(0, myTileIDs.Count)];
            
            // EventManager RPC�??�해 ?�구 감소(-10) ?�용
            if (EventManager.Instance != null)
            {
                EventManager.Instance.photonView.RPC("RPC_ApplyTileEvent", RpcTarget.All, rndID, "Heal", -10);
            }
            Debug.Log("[?�박?? 과열! ???�??1�??�구 감소.");
        }
    }

    // [?�복 ?�제] ???�유 ?�??�??�겨�?Hidden) ?�태�??�제
    void CheckAndClearAmbush()
    {
        if (MapGenerator.Instance == null) return;

        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile.ownerTeam == myTeam && tile.isHidden)
            {
                // 모든 ?�라?�언?�에 ?��? ?�제 ?�기??
                if (GameNetworkManager.Instance != null)
                   GameNetworkManager.Instance.photonView.RPC("RPC_SetTileHidden", RpcTarget.All, tile.tileID, false);
            }
        }
    }

    // ?�금 추�? �?UI 갱신
    public void AddMoney(int amount)
    {
        if (!photonView.IsMine) return;

        money += amount;
        if (GameUIManager.Instance != null) 
            GameUIManager.Instance.UpdateMoney(money);
        
        Debug.Log($"?�금 ?�득: +{amount} (Total: {money})");
    }

    // ?�금 ?�비 ?�도 (부족하�?false 반환)
    public bool TrySpendMoney(int amount)
    {
        if (!photonView.IsMine) return false;

        if (money >= amount)
        {
            money -= amount;
            if (GameUIManager.Instance != null) 
                GameUIManager.Instance.UpdateMoney(money);

            Debug.Log($"[지�? {amount}???�용 (?��? ?? {money})");
            return true;
        }
        else
        {
            Debug.Log($"[?�액 부�? ?�요: {amount}, 보유: {money}");
            return false;
        }
    }

    // UI?�서 바이?�스 변�??�도 ???�출
    public bool TrySetVirus(VirusType type)
    {
        int maxUses = GetVirusMaxUses(type);
        if (virusUsageCounts.ContainsKey(type) && virusUsageCounts[type] >= maxUses)
        {
            Debug.Log($"[{type}] virus already used {maxUses} times.");
            return false;
        }

        if (!revertVirusStatsNextTurn)
        {
            prevInfectionRate = infectionRate;
            prevSpreadPower = spreadPower;
            prevFatalityRate = fatalityRate;
        }
        revertVirusStatsNextTurn = true;

        currentVirus = type;
        if (type == VirusType.Mutant) RandomizeStatsForMutant();
        if (type == VirusType.Gamble)
        {
            fatalityRate = prevFatalityRate * ((Random.value < 0.5f) ? 2.0f : 0.5f);
        }
        if (GameUIManager.Instance != null)
            GameUIManager.Instance.RefreshPlayerStatsUI();

        if (type == VirusType.Rush)
        {
            maxActionPoints += 1;
            currentActionPoints += 1;
            if (GameUIManager.Instance != null)
                GameUIManager.Instance.UpdateActionPoints(currentActionPoints, maxActionPoints);
        }

        if (GameNetworkManager.Instance != null)
        {
            GameNetworkManager.Instance.photonView.RPC(
                "RPC_ReportPlayerStats",
                RpcTarget.MasterClient,
                PhotonNetwork.LocalPlayer.ActorNumber,
                infectionRate,
                fatalityRate,
                spreadPower
            );
        }

        Debug.Log($"Virus set -> {type}");

        if (GameNetworkManager.Instance != null)
        {
            GameNetworkManager.Instance.photonView.RPC(
                "RPC_ReportCurrentVirus",
                RpcTarget.MasterClient,
                PhotonNetwork.LocalPlayer.ActorNumber,
                (int)currentVirus
            );
        }

        return true;
    }

    // 공격 ???�재 바이?�스 ?�용 가???��? ?�확??
    public bool CanUseCurrentVirus()
    {
        if (currentVirus == VirusType.None) return true; 
        
        if (virusUsageCounts.ContainsKey(currentVirus))
        {
            int maxUses = GetVirusMaxUses(currentVirus);
            return virusUsageCounts[currentVirus] < maxUses;
        }
        return true; 
    }

    // 공격 ?�공 ???�출?�어 ?�용 ?�수�?차감
    public void OnVirusUsed()
    {
        if (currentVirus == VirusType.None) return;

        if (virusUsageCounts.ContainsKey(currentVirus))
        {
            virusUsageCounts[currentVirus]++;
            int maxUses = GetVirusMaxUses(currentVirus);
            int left = maxUses - virusUsageCounts[currentVirus];
            Debug.Log($"[{currentVirus}] used. Remaining: {left}");

            if (left <= 0)
            {
                currentVirus = VirusType.None;
                Debug.Log("Virus uses exhausted -> revert to base virus.");
            }
        }
    }

    public int GetVirusMaxUses(VirusType type)
    {
        if (type == VirusType.Gamble || type == VirusType.Mutant)
            return 1;
        if (type == VirusType.None)
            return int.MaxValue;
        return 2;
    }

    public void RecalculateActionPointsNow()
    {
        maxActionPoints = Mathf.FloorToInt(spreadPower / 20f);
        if (maxActionPoints < 1) maxActionPoints = 1;

        if (currentVirus == VirusType.Rush)
            maxActionPoints += 1;

        currentActionPoints = maxActionPoints;
        if (GameUIManager.Instance != null)
            GameUIManager.Instance.UpdateActionPoints(currentActionPoints, maxActionPoints);
    }

    public void RandomizeStatsForMutant()
    {
        int inf = Random.Range(0, 101);
        int fat = Random.Range(0, 101);
        int spread = Random.Range(0, 101);

        infectionRate = inf;
        fatalityRate = fat;
        spreadPower = spread;

        RecalculateActionPointsNow();
        Debug.Log($"[Mutant] stats randomized -> inf:{infectionRate} / fat:{fatalityRate} / spread:{spreadPower}");
    }

    public void SetInitialStats(int inf, int fat, int spread)
    {
        infectionRate = inf;
        fatalityRate = fat;
        spreadPower = spread;
        Debug.Log($"Initial stats set. (inf:{infectionRate} / fat:{fatalityRate} / spread:{spreadPower})");

        RecalculateActionPointsNow();
    }

    public void ResetVaccineUsesForNewTurn()
    {
        currentVaccineUses = 0;
        PushVaccineUI();
    }

    
    public void ApplyBlackoutEffect(float infectionBonus, float fatalityMultiplier)
    {
        if (!photonView.IsMine) return;

        prevBlackoutInfectionRate = infectionRate;
        prevBlackoutFatalityRate = fatalityRate;
        revertBlackoutStatsNextTurn = true;

        infectionRate = Mathf.Max(0f, infectionRate + infectionBonus);
        fatalityRate = Mathf.Max(0f, fatalityRate * fatalityMultiplier);
        hasBlackoutDebuff = true;

        if (GameUIManager.Instance != null)
            GameUIManager.Instance.RefreshPlayerStatsUI();

        if (GameNetworkManager.Instance != null)
        {
            GameNetworkManager.Instance.photonView.RPC(
                "RPC_ReportPlayerStats",
                RpcTarget.MasterClient,
                PhotonNetwork.LocalPlayer.ActorNumber,
                infectionRate,
                fatalityRate,
                spreadPower
            );
        }
    }

public void OnInfectionSuccess()
    {
        // Placeholder for future infection success effects.
    }
}
