using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class GameNetworkManager : MonoBehaviourPunCallbacks
{
    // 싱글톤 인스턴스: 어디서든 게임 매니저에 접근 가능
    public static GameNetworkManager Instance;
    // 플레이어 캐릭터 프리팹 (네트워크 스폰용)
    public GameObject playerPrefab;

    // Ready 2명 되면 true
    public bool gameStartApproved = false;

    public int expectedPlayers = 2;
    private int readyPlayers = 0;
    private bool gameStarted = false;

    [Header("Room Code Settings")]
    private string roomCode = ""; // 방 코드 (4자리 숫자 등)
    private bool waitingForRoomCode = true; // 방 코드 입력 대기 중

    [Header("Game Rule Settings")]
    public int cleanZoneCountPerRound = 3; // (참고용) 실제 생성은 2~3개 랜덤
    public int moneyPerKill = 10;          // 킬 당 기본 보상 (현재 로직에서는 미사용 가능성 있음)
    public int moneyBonusCleanZone = 375;  // 청정 구역 점령 시 즉시 지급 보너스
    
    [Tooltip("누적 사망자 1명당 라운드 시작 시 지급되는 돈")]
    public float moneyPerCumulativeDeath = 0.2f; // 누적 사망자 x 0.2 (내림) 지급
    
    [Tooltip("잔류인원 1명당 푼 문제 1개당 지급되는 돈")]
    public int moneyPerSurvivorProblem = 2; // (잔류 인원 x 문제 수 x 이 값) 만큼 보상

    [Header("Round & Turn Settings")]
    public int currentRound = 1;        // 현재 진행 중인 라운드
    public int maxRounds = 6;            // 게임의 최대 라운드 수
    public int turnsPerRound = 3;        // 한 라운드당 진행되는 턴 수
    public int currentTurn = 0;          // 현재 전체 턴 수 (누적)
    public int maxTurns = 18;            // 게임 종료까지의 총 턴 수 (6라운드 x 3턴)
    public int populationGrowth = 10;    // (사용 안 함) 자연 증가 비활성화
    public float roundDuration = 1800.0f; // 라운드 제한 시간 (초 단위, 30분)
    public float turnDuration = 600.0f;   // 턴 제한 시간 (초 단위, 10분)  
    
    private float currentTurnTimer = 0f;  // 현재 턴 남은 시간
    private float currentRoundTimer = 0f; // 현재 라운드 남은 시간
    public bool isMyTurnFinished = false; // 현재 턴에서 행동을 마쳤는지 여부
    private int finishedPlayersCount = 0; // 턴을 종료한 플레이어 수 (동기화용)
    private bool isAdvancingTurn = false; // 턴 중복 진행 방지

    private bool cleanZoneSpawnedRound3 = false;

    private Dictionary<int, int> pendingVaccineReverts = new Dictionary<int, int>();

    private HashSet<int> transitionReady = new HashSet<int>();

    // actorNum -> currentVirusIndex (마스터가 기억)
    private Dictionary<int, int> actorVirusIndex = new Dictionary<int, int>();
    private Dictionary<int, float> teamFatalityRates = new Dictionary<int, float>();
    private Dictionary<int, float> lastAttackTimeByActor = new Dictionary<int, float>();
    private Dictionary<int, int> lastAttackTileByActor = new Dictionary<int, int>();
    private float lastLocalAttackSendTime = -1f;
    private int lastLocalAttackTile = -1;
    private int localAttackSeq = 0;
    private HashSet<int> consumedAttackIdsLocal = new HashSet<int>();

    // ✅ 생화학 무기: "사용한 플레이어(actor)" 기준으로 이번 턴만 치사율 +10%
    private Dictionary<int, int> bioWeaponBuffUntilTurnByActor = new Dictionary<int, int>();
    private const float BIO_WEAPON_ADD = 10f; // 치사율 +10 (가산)

    [Header("Transition UI")]
    public GameObject panelMovePrompt;
    public TextMeshProUGUI txtMovePrompt;
    public GameObject minigamePanel; // MinigameSystem 붙어있는 패널

    // 누적 사망자 수 (라운드 시작 시 지원금 계산에 사용)
    private int cumulativeDeathsTeam1 = 0;
    private int cumulativeDeathsTeam2 = 0;

    [Header("Pause / Minigame")]
    public bool isTimePaused = false;      // 타이머 정지 여부
    private int pauseRequestCount = 0;     // (선택) 양쪽 준비 동기화용
    private HashSet<int> pauseRequesters = new HashSet<int>();

    public bool bioWeaponBuffActiveLocal = false;
    private int bioWeaponBuffUntilTurnLocal = -1;

    public enum Phase
    {
        Action, // 일반 턴 행동
        MovePrompt1,     // "이동하라" 1차 안내
        MiniGame,        // 미니게임 진행
        MovePrompt2      // "이동하라" 2차 안내 (미니게임 후)
    }

    public Phase currentPhase = Phase.Action;
    private int pendingNextTurn = -1;   // 다음 턴 번호를 임시 저장
    private int pendingRound = -1;

    // GameNetworkManager class 안
    private bool simultaneousAttackHappenedThisTurn = false;
    private HashSet<int> simultaneousAttackTilesThisTurn = new HashSet<int>(); // (선택) 어느 타일에서 발생했는지
    private HashSet<int> pendingRevealTileIDs = new HashSet<int>();
    private HashSet<int> pendingDestroyedTileIDs = new HashSet<int>();
    private HashSet<int> newlyInfectedTileIdsThisTurn = new HashSet<int>();
    private HashSet<long> processedAttackKeysThisTurn = new HashSet<long>();
    private bool virusConsumedThisTurnLocal = false;

    [Header("UI References")]
    public TextMeshProUGUI txtTurnInfo;     // 상단 라운드/턴 정보 텍스트
    public GameObject btnEndTurn;           // 턴 종료 버튼
    public GameObject panelVictory;         // 승리/패배 결과 패널
    public TextMeshProUGUI txtVictoryMessage; // 결과 메시지

    [Header("Mode Settings")]
    public bool isAttackMode = true; // 현재 공격 모드인지 여부 (UI 버튼으로 토글)

    [Header("Minigame Settings")]
    // 이번 라운드에서 미니게임을 이미 했는지 여부
    private bool miniGamePlayedThisRound = false;

    // 다리 저장
    Dictionary<int, int> bridgeRules = new Dictionary<int, int>
    {
        { 79, 100 },
        { 100, 79 },

        { 83, 49 },
        { 49, 83 },

        { 71, 108 },
        { 108, 71 },
    };    

    // 동시 도착 공격 처리용 데이터 구조
    [System.Serializable]
    public class AttackRequest
    {
        public int tileID;
        public int attackerActorNum;
        public float infectionRate;
        public float fatalityRate;
        public int virusTypeIndex;
        public float resistance;
        public float timestamp; // 요청 시간
        public int attackId;
        public int originalPopulation; // 공격 시점의 타일 초기 인구
        public int originalOwnerTeam; // 공격 시점의 타일 소유 팀
    }

    [System.Serializable]
    public class PendingRevealResult
    {
        public int tileID;
        public int newTeam;
        public int newPop;
        public bool isSuccess;
        public int actorNum;
        public int earnedMoney;
        public bool shouldHide;
        public int deathCount;
    }

    private List<PendingRevealResult> pendingRevealResults = new List<PendingRevealResult>();

    // 타일ID별로 공격 요청을 모아두는 딕셔너리
    private Dictionary<int, List<AttackRequest>> pendingAttacks = new Dictionary<int, List<AttackRequest>>();
    private HashSet<int> preProcessedAttackTiles = new HashSet<int>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (panelVictory != null) panelVictory.SetActive(false);

        // 방 코드 입력 대기 (UI에서 ConnectWithRoomCode()를 호출할 때까지 대기)
        Debug.Log("⏳ 방 코드 입력 대기 중... (UI에서 입력해주세요)");
    }

    // UI에서 호출: 방 코드를 입력받아서 연결 시작
    public void ConnectWithRoomCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length < 4)
        {
            Debug.LogError("❌ 방 코드는 최소 4자리여야 합니다!");
            return;
        }

        roomCode = code.ToUpper(); // 대문자로 통일
        waitingForRoomCode = false;

        Debug.Log($"🔌 방 코드 '{roomCode}'로 Photon 서버 연결 시도 중...");
        Debug.Log($"📍 설정: Protocol=WebSocket, Region=kr, AppId={PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime.Substring(0, 8)}...");

        PhotonNetwork.ConnectUsingSettings();

        // 30초 연결 타임아웃 체크
        Invoke(nameof(CheckConnectionTimeout), 30f);
    }

    // 연결 타임아웃 체크
    void CheckConnectionTimeout()
    {
        if (!PhotonNetwork.IsConnected && !PhotonNetwork.IsConnectedAndReady)
        {
            Debug.LogError("⏱️ 연결 타임아웃! 30초 동안 연결되지 않음");
            Debug.LogError($"현재 상태: {PhotonNetwork.NetworkClientState}");
            Debug.LogError("방화벽/네트워크 설정을 확인해주세요.");

            // 재시도
            RetryConnection();
        }
    }

    // 연결 끊김 처리
    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogError($"❌ Photon 연결 끊김: {cause}");

        // 게임 중이 아니면 자동 재연결 시도
        if (!gameStarted)
        {
            Debug.Log("🔄 5초 후 재연결 시도...");
            Invoke(nameof(RetryConnection), 5f);
        }
        else
        {
            Debug.LogWarning("⚠️ 게임 중 연결 끊김 - 수동 재연결 필요");
            // TODO: UI에 "연결이 끊겼습니다" 메시지 표시
        }
    }

    // 재연결 시도
    void RetryConnection()
    {
        if (PhotonNetwork.IsConnected)
        {
            Debug.Log("✅ 이미 연결됨");
            return;
        }

        Debug.Log("🔄 재연결 중...");
        PhotonNetwork.ConnectUsingSettings();
    }

    bool CanPlayerActNow()
    {
        return currentPhase == Phase.Action && !isMyTurnFinished && !isTimePaused;
    }

    // 마스터 서버 연결 성공 시 호출
    public override void OnConnectedToMaster()
    {
        Debug.Log($"✅ 마스터 서버 연결 성공! (리전: {PhotonNetwork.CloudRegion}, Ping: {PhotonNetwork.GetPing()}ms)");
        Debug.Log($"🔍 방 코드 '{roomCode}'로 방 찾기/생성 시도 중...");

        // 타임아웃 체크 취소
        CancelInvoke(nameof(CheckConnectionTimeout));

        // 방 코드로 방 입장 시도 (없으면 자동으로 생성됨)
        RoomOptions roomOptions = new RoomOptions { MaxPlayers = 2 };
        PhotonNetwork.JoinOrCreateRoom(roomCode, roomOptions, null);
    }

    // 방 입장 실패(방이 없음) 시 호출 -> 방 생성
    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log($"⚠️ 방 찾기 실패 (코드: {returnCode}) - 새 방 생성 중...");
        PhotonNetwork.CreateRoom(null, new RoomOptions { MaxPlayers = 2 });
    }

    // 방 생성 성공 시 호출
    public override void OnCreatedRoom()
    {
        Debug.Log("✅ 방 생성 완료! 다른 플레이어 대기 중...");
    }

    // 방 생성 실패 시 호출
    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"❌ 방 생성 실패: {message} (코드: {returnCode})");
    }

    // 다른 플레이어가 방에 입장했을 때
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"👤 플레이어 입장: {newPlayer.NickName} (ActorNumber: {newPlayer.ActorNumber})");
    }

    // 다른 플레이어가 방을 떠났을 때
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.LogWarning($"👋 플레이어 퇴장: {otherPlayer.NickName} (ActorNumber: {otherPlayer.ActorNumber})");

        // 마스터 클라이언트만 처리
        if (PhotonNetwork.IsMasterClient)
        {
            // 해당 플레이어의 대기 중인 공격을 즉시 확정 처리
            if (pendingAttacks.Count > 0)
            {
                Debug.Log($"[긴급 처리] 퇴장한 플레이어 {otherPlayer.ActorNumber}의 대기 중인 공격 즉시 확정");

                // 퇴장한 플레이어의 공격만 필터링하여 처리
                var attacksToProcess = new Dictionary<int, List<AttackRequest>>();
                foreach (var kvp in pendingAttacks)
                {
                    var playerAttacks = kvp.Value.Where(a => a.attackerActorNum == otherPlayer.ActorNumber).ToList();
                    if (playerAttacks.Count > 0)
                    {
                        attacksToProcess[kvp.Key] = playerAttacks;
                    }
                }

                // 퇴장한 플레이어의 공격을 즉시 확정 처리
                foreach (var kvp in attacksToProcess)
                {
                    int tileID = kvp.Key;
                    List<AttackRequest> attacks = kvp.Value;

                    Debug.Log($"[긴급 처리] 타일 {tileID}: {attacks.Count}개 공격 확정");

                    if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) continue;
                    HexTile targetTile = MapGenerator.Instance.allTiles[tileID];

                    if (attacks.Count == 1)
                    {
                        ProcessSingleAttack_Final(attacks[0], targetTile);
                    }
                    else
                    {
                        ProcessSimultaneousAttacks_Final(tileID, attacks);
                    }

                    // pendingAttacks에서 제거
                    foreach (var attack in attacks)
                    {
                        pendingAttacks[tileID].Remove(attack);
                    }

                    // 리스트가 비었으면 키 자체를 제거
                    if (pendingAttacks[tileID].Count == 0)
                    {
                        pendingAttacks.Remove(tileID);
                    }
                }
            }
        }
    }

    // 방 입장 성공 시 호출
    public override void OnJoinedRoom()
    {
        Debug.Log($"✅ 방 입장 완료! (방 코드: {roomCode}, 플레이어 수: {PhotonNetwork.CurrentRoom.PlayerCount}/2, 역할: {(PhotonNetwork.IsMasterClient ? "마스터" : "게스트")})");

        // 방 이름이 roomCode와 일치하는지 확인
        if (PhotonNetwork.CurrentRoom.Name == roomCode)
        {
            Debug.Log($"🎯 올바른 방에 입장했습니다!");
        }
        else
        {
            Debug.LogWarning($"⚠️ 예상과 다른 방에 입장했습니다. (예상: {roomCode}, 실제: {PhotonNetwork.CurrentRoom.Name})");
        }

        // 네트워크상에 플레이어 오브젝트 생성
        if (playerPrefab != null)
        {
            PhotonNetwork.Instantiate(playerPrefab.name, Vector3.zero, Quaternion.identity);
        }

        // 마스터 클라이언트(방장)가 게임 초기화 주도
        if (PhotonNetwork.IsMasterClient)
        {
            MapGenerator.Instance.GenerateMap(); // 맵 생성
        }
        else
        {
            MapGenerator.Instance.GenerateMap(); // 게스트도 맵 데이터 생성
        }

        int myTeam = PhotonNetwork.IsMasterClient ? 1 : 2; // 2인 고정이면 이게 제일 단순
        var props = new ExitGames.Client.Photon.Hashtable { { "TEAM", myTeam } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);

        // GamePlayer에도 반영(너 구조에 맞게)
        if (GamePlayer.LocalPlayer != null) GamePlayer.LocalPlayer.myTeam = myTeam;
    }

    // 게임 시작 로직 (라운드 시작)
    void StartGameLogic()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (!gameStartApproved) return;

        if (MapGenerator.Instance != null && MapGenerator.Instance.allTiles != null && MapGenerator.Instance.allTiles.Count > 0)
        {
            PrepareAndBroadcastStartTiles();  // ✅ 시작 타일 확정 + 공유
            Invoke(nameof(ApplyStartTilesToTeams), 0.3f); // ✅ 맵 초기화 덮어쓰기 방지 (마무리 후 적용)
        }
        else
        {
            Invoke("StartGameLogic", 0.5f);
            return;
        }

        currentRound = 1;
        currentTurn = 0; // 시작은 0
        currentRoundTimer = roundDuration;
        currentTurnTimer = turnDuration;

        // 첫 라운드는 다음 라운드가 아니라 그냥 라운드 1 시작
        photonView.RPC("RPC_StartNextRound", RpcTarget.All, 1);

        photonView.RPC("RPC_ResetAllBuffFlags", RpcTarget.All);
    }

    void PrepareAndBroadcastStartTiles()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // 고정 스폰 사용
        int t1 = MapGenerator.Instance.team1FixedSpawnTileID; // 105
        int t2 = MapGenerator.Instance.team2FixedSpawnTileID; // 34

        // 존재/중복 체크 + fallback
        if (!MapGenerator.Instance.allTiles.ContainsKey(t1))
            t1 = PickRandomGroundTile(excludeID: -1);

        if (!MapGenerator.Instance.allTiles.ContainsKey(t2) || t2 == t1)
            t2 = PickRandomGroundTile(excludeID: t1);

        Debug.Log($"StartTiles chosen: T1={t1}, T2={t2}");

        var props = new ExitGames.Client.Photon.Hashtable
    {
        { "START_T1", t1 },
        { "START_T2", t2 }
    };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    [PunRPC]
    void RPC_ResetAllBuffFlags()
    {
        if (GamePlayer.LocalPlayer == null) return;
        GamePlayer.LocalPlayer.hasMutantBuff = false;
        GamePlayer.LocalPlayer.hasBlackoutDebuff = false;
        GamePlayer.LocalPlayer.usedGuaranteedInfection = false;
        GamePlayer.LocalPlayer.mutantBuffRound = 0;
    }

    int PickRandomGroundTile(int excludeID)
    {
        var list = new List<int>();
        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile.type == HexTile.TileType.GROUND &&
                !tile.isDestroyed &&
                tile.tileID != excludeID)
            {
                list.Add(tile.tileID);
            }
        }
        if (list.Count == 0) return -1;
        return list[Random.Range(0, list.Count)];
    }

    public void RequestPauseTime(bool pause)
    {
        if (!PhotonNetwork.InRoom) return;

        // 마스터에게 "내가 일시정지 원함/해제함" 요청
        photonView.RPC(nameof(RPC_RequestPauseTime), RpcTarget.MasterClient,
            PhotonNetwork.LocalPlayer.ActorNumber, pause);
    }

    [PunRPC]
    private void RPC_RequestPauseTime(int actorNum, bool pause)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (pause) pauseRequesters.Add(actorNum);
        else pauseRequesters.Remove(actorNum);

        int playerCount = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : expectedPlayers;

        // 둘 다(현재 방 인원 전원) pause 요청했을 때만 진짜 정지
        bool shouldPause = (pauseRequesters.Count >= playerCount);

        photonView.RPC(nameof(RPC_SetTimePaused), RpcTarget.All, shouldPause);
    }

    [PunRPC]
    private void RPC_SetTimePaused(bool pause)
    {
        isTimePaused = pause;

        if (GameUIManager.Instance != null)
            GameUIManager.Instance.UpdatePhaseText(pause ? "미니게임 진행 중(시간 정지)" : "행동 단계 (Action)");
    }

    void ApplyStartTilesToTeams()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null) return;

        // 여기서 고정 시작타일 사용
        int team1TileID = MapGenerator.Instance.team1StartTileID; // 예: 105
        int team2TileID = MapGenerator.Instance.team2StartTileID; // 예: 34

        // (선택) 방 프로퍼티로 이미 확정해놨으면 그걸 우선 적용
        if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties != null)
        {
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("START_T1", out object v1))
                team1TileID = (int)v1;
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("START_T2", out object v2))
                team2TileID = (int)v2;
        }

        if (!MapGenerator.Instance.allTiles.ContainsKey(team1TileID) ||
            !MapGenerator.Instance.allTiles.ContainsKey(team2TileID) ||
            team1TileID == team2TileID)
        {
            Debug.LogError($"[StartTiles] Invalid. T1={team1TileID}, T2={team2TileID}");
            return;
        }

        HexTile t1 = MapGenerator.Instance.allTiles[team1TileID];
        HexTile t2 = MapGenerator.Instance.allTiles[team2TileID];

        Debug.Log($"[StartTiles FIXED] Team1->{team1TileID}, Team2->{team2TileID}");

        // 점령 적용
        photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
            team1TileID, 1, t1.population, true, 0, 0, false, 0);

        photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
            team2TileID, 2, t2.population, true, 0, 0, false, 0);
    }

    void SendResultToActorOnlyAndQueueReveal(int actorNum,
    int tileID, int newTeam, int newPop, bool isSuccess, int earnedMoney, bool shouldHide, int deathCount)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // 1) 행동한 플레이어에게만 즉시 결과 적용
        Player p = PhotonNetwork.CurrentRoom.GetPlayer(actorNum);
        if (p != null)
        {
            photonView.RPC("RPC_SyncTileResult", p,
                tileID, newTeam, newPop, isSuccess, actorNum, earnedMoney, shouldHide, deathCount);
        }

        // 2) 턴 종료 때 전체 공개할 목록에 저장
        pendingRevealResults.Add(new PendingRevealResult
        {
            tileID = tileID,
            newTeam = newTeam,
            newPop = newPop,
            isSuccess = isSuccess,
            actorNum = actorNum,
            earnedMoney = earnedMoney,
            shouldHide = shouldHide,
            deathCount = deathCount
        });

        pendingRevealTileIDs.Add(tileID);
    }

    void QueueRevealOnly(int actorNum,
    int tileID, int newTeam, int newPop, bool isSuccess, int earnedMoney, bool shouldHide, int deathCount)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        pendingRevealResults.Add(new PendingRevealResult
        {
            tileID = tileID,
            newTeam = newTeam,
            newPop = newPop,
            isSuccess = isSuccess,
            actorNum = actorNum,
            earnedMoney = earnedMoney,
            shouldHide = shouldHide,
            deathCount = deathCount
        });

        pendingRevealTileIDs.Add(tileID);
    }

    [PunRPC]
    private void RPC_ReportCurrentVirus(int actorNum, int virusIndex)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        actorVirusIndex[actorNum] = virusIndex;
        Debug.Log($"[VirusReport] actor={actorNum} virusIndex={virusIndex}");
    }

    [PunRPC]
    private void RPC_ReportPlayerStats(int actorNum, float infectionRate, float fatalityRate, float spreadPower)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        int team = GetTeamByActorNumSafe(actorNum);
        if (team <= 0) return;

        teamFatalityRates[team] = fatalityRate;
    }

    private void ClearExpiredBioWeapons_Master(int newTurn)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (bioWeaponBuffUntilTurnByActor.Count == 0) return;

        // newTurn 시작 시점에서, untilTurn < newTurn 인 것들 해제
        var toClear = bioWeaponBuffUntilTurnByActor
            .Where(kv => kv.Value < newTurn)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var actorNum in toClear)
        {
            bioWeaponBuffUntilTurnByActor.Remove(actorNum);
        }
    }

    [PunRPC]
    private void RPC_SetTileDestroyed(int tileID, bool destroyed)
    {
        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null) return;
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;

        var tile = MapGenerator.Instance.allTiles[tileID];
        tile.isDestroyed = destroyed;

        // 파괴되면 소유/인구도 정리(원하면 유지도 가능)
        if (destroyed)
        {
            tile.ownerTeam = 0;
            tile.population = 0;
            tile.isHidden = false;
            tile.isCleanZone = false;
            tile.bioWeaponMultiplier = 1.0f;
        }

        tile.UpdateVisuals(tile.ownerTeam, tile.population);
    }


    //[PunRPC]
    //private void RPC_ClearBioWeapon(int tileID)
    //{
    //    if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null) return;
    //    if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;

    //    MapGenerator.Instance.allTiles[tileID].bioWeaponMultiplier = 1.0f;
    //}

    // 초기 타일을 플레이어들에게 공평하게 분배 (마스터 클라이언트 전용)
    void DistributeInitialTiles()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null) 
        {
            Debug.LogError("MapGenerator 또는 allTiles가 null입니다!");
            return;
        }

        // Inspector에서 지정한 시작 위치 사용
        int team1TileID = MapGenerator.Instance.team1StartTileID;
        int team2TileID = MapGenerator.Instance.team2StartTileID;

        // 각 플레이어에게 지정된 시작 타일 할당
        foreach (var player in PhotonNetwork.PlayerList)
        {
            int team = (player.ActorNumber == 1) ? 1 : 2; // ActorNumber 1은 Team 1, 나머지는 Team 2
            int startTileID = (team == 1) ? team1TileID : team2TileID;

            // 시작 타일 ID가 유효한지 확인
            if (startTileID < 0 || !MapGenerator.Instance.allTiles.ContainsKey(startTileID))
            {
                Debug.LogWarning($"[초기 타일 분배] 팀 {team}의 시작 타일 ID({startTileID})가 유효하지 않습니다! 랜덤 타일로 대체합니다.");
                
                // 랜덤 타일로 대체
                List<int> availableTiles = new List<int>();
                foreach (var availableTile in MapGenerator.Instance.allTiles.Values)
                {
                    if (availableTile.ownerTeam == 0 && !availableTile.isDestroyed && !availableTile.isCleanZone)
                    {
                        availableTiles.Add(availableTile.tileID);
                    }
                }
                
                if (availableTiles.Count > 0)
                {
                    startTileID = availableTiles[Random.Range(0, availableTiles.Count)];
                }
                else
                {
                    Debug.LogError($"[초기 타일 분배] 사용 가능한 타일이 없습니다!");
                    return;
                }
            }

            HexTile tile = MapGenerator.Instance.allTiles[startTileID];
            
            Debug.Log($"[초기 타일 분배] 플레이어 {player.ActorNumber} (팀 {team})에게 타일 {startTileID} 할당 (인구: {tile.population})");
            
            // 모든 클라이언트에 할당 결과 동기화
            photonView.RPC("RPC_SyncTileResult", RpcTarget.All, startTileID, team, tile.population, true, 0, 0, false, 0);
        }
        
        Debug.Log("[초기 타일 분배] 완료!");
    }

    void Update()
    {
        bool canAct = CanPlayerActNow();

        // 게임 진행 중일 때 타이머 업데이트
        if (currentTurn > 0 && currentTurn <= maxTurns && panelVictory != null && !panelVictory.activeSelf)
        {
            if (!isTimePaused)
            {
                currentTurnTimer -= Time.deltaTime;
                currentRoundTimer -= Time.deltaTime;
            }

            if (GameUIManager.Instance != null)
            {
                GameUIManager.Instance.UpdateTimer(currentTurnTimer);
                GameUIManager.Instance.UpdateRoundInfo(currentRound, maxRounds, currentTurn, turnsPerRound);
            }

            // 마스터 클라이언트가 시간 초과를 감시하고 강제 진행
            if (PhotonNetwork.IsMasterClient)
            {
                // 라운드 시간 초과
                if (currentRoundTimer <= 0f)
                {
                    Debug.Log("라운드 시간 초과! 강제로 다음 라운드로 넘깁니다.");
                    ProceedToNextRound();
                }
                // 턴 시간 초과
                else if (currentTurnTimer <= 0f)
                {
                    Debug.Log("턴 시간 초과! 강제로 턴을 넘깁니다.");
                    ProceedToNextTurn();
                }
            }
        }

        if (!canAct)
        {
            if (ItemManager.Instance == null ||
                ItemManager.Instance.currentItem == ItemManager.ItemType.None)
                return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            DetectClick();
        }
    }

    // GameNetworkManager 안에 추가
    [PunRPC]
    void RPC_VaccineResult_ToRequester(int actorNum, bool success, int tileID)
    {
        // 요청한 사람만 처리
        if (PhotonNetwork.LocalPlayer.ActorNumber != actorNum) return;

        if (GamePlayer.LocalPlayer != null)
        {
            GamePlayer.LocalPlayer.OnVaccineUsed(); // ✅ 여기서 사용횟수 증가/재계산
        }

        // ✅ UI 갱신도 여기서 확실히
        if (GameUIManager.Instance != null && GamePlayer.LocalPlayer != null)
        {
            var p = GamePlayer.LocalPlayer;
            GameUIManager.Instance.UpdateVaccineStats(
                p.vaccineRecoveryRate,
                p.vaccineSupplyRate,
                p.vaccineRange,
                p.currentVaccineUses,
                p.maxVaccineUses
            );
        }

        Debug.Log($"[백신 결과] success={success}, tile={tileID}");
    }

    public void NotifyMinigameFinished()
    {
        photonView.RPC("RPC_ShowMovePromptAfterMinigame", RpcTarget.All);
    }

    [PunRPC]
    void RPC_StartMiniGame()
    {
        currentPhase = Phase.MiniGame;

        if (panelMovePrompt != null) panelMovePrompt.SetActive(false);

        // 미니게임 패널 ON
        if (minigamePanel != null) minigamePanel.SetActive(true);

        // 시간은 계속 정지 상태 유지
        isTimePaused = true;

        if (GameUIManager.Instance != null)
            GameUIManager.Instance.UpdatePhaseText("미니게임 진행");
    }

    [PunRPC]
    void RPC_ShowMovePromptAfterMinigame()
    {
        currentPhase = Phase.MovePrompt2;

        if (panelMovePrompt != null) panelMovePrompt.SetActive(true);
        if (txtMovePrompt != null) txtMovePrompt.text = "이동하라";

        isTimePaused = true;

        if (GameUIManager.Instance != null)
            GameUIManager.Instance.UpdatePhaseText("이동 단계(미니게임 후)");
    }

    // '턴 종료' 버튼 클릭 시 호출
    public void OnClick_EndTurn()
    {
        if (currentPhase != Phase.Action) return;
        if (isMyTurnFinished) return;

        isMyTurnFinished = true;
        if (btnEndTurn != null) btnEndTurn.SetActive(false); 

        if (GameUIManager.Instance != null)
            GameUIManager.Instance.UpdatePhaseText("상대방 기다리는 중...");

        // 마스터에게 턴 종료 알림
        photonView.RPC("RPC_PlayerFinishedTurn", RpcTarget.MasterClient);
    }

    [PunRPC]
    void RPC_BeginTurnTransition(int nextTurn, int roundNum)
    {
        pendingNextTurn = nextTurn;
        pendingRound = roundNum;

        // 1) "이동하라" 띄우기
        currentPhase = Phase.MovePrompt1;

        if (panelMovePrompt != null) panelMovePrompt.SetActive(true);
        if (txtMovePrompt != null) txtMovePrompt.text = "이동하라";

        // 행동 막기
        isTimePaused = true;
        if (btnEndTurn != null) btnEndTurn.SetActive(false);

        if (GameUIManager.Instance != null)
            GameUIManager.Instance.UpdatePhaseText("이동 단계");
    }

    public void OnClick_ProceedTransition()
    {
        // 현재 "이동하라" 패널 떠 있을 때만
        if (currentPhase != Phase.MovePrompt1 && currentPhase != Phase.MovePrompt2) return;

        photonView.RPC("RPC_TransitionReady", RpcTarget.MasterClient, PhotonNetwork.LocalPlayer.ActorNumber, (int)currentPhase);
    }

    [PunRPC]
    void RPC_TransitionReady(int actorNum, int phaseInt)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        transitionReady.Add(actorNum);
        int playerCount = PhotonNetwork.CurrentRoom.PlayerCount;

        Debug.Log($"[TRANSITION READY] phase={(Phase)phaseInt} readyCount={transitionReady.Count}/{playerCount}");

        if (transitionReady.Count < playerCount) return;

        transitionReady.Clear();

        Phase phase = (Phase)phaseInt;
        Debug.Log($"[TRANSITION GO] phase={phase} -> next");

        if (phase == Phase.MovePrompt1)
        {
            // 👉 다음 턴이 라운드에서 몇 번째 턴인지 계산
            int turnInRound = ((pendingNextTurn - 1) % turnsPerRound) + 1;

            // 👉 "다음 턴이 2턴"이고, 아직 이 라운드에서 미니게임을 안 했다면 → 이번에 딱 한 번만 미니게임 실행
            if (turnInRound == 2 && !miniGamePlayedThisRound)
            {
                miniGamePlayedThisRound = true;
                photonView.RPC("RPC_StartMiniGame", RpcTarget.All);
            }
            else
            {
                // 그 외에는 그냥 바로 다음 턴으로
                photonView.RPC("RPC_StartNextTurn", RpcTarget.All, pendingNextTurn, pendingRound);
            }
        }
        else if (phase == Phase.MovePrompt2)
        {
            // 미니게임 끝나고 "이동하라" 한 번 더 누르면 무조건 다음 턴으로
            photonView.RPC("RPC_StartNextTurn", RpcTarget.All, pendingNextTurn, pendingRound);
        }
    }

    // 플레이어가 턴을 마쳤음을 마스터가 집계
    [PunRPC]
    public void RPC_PlayerFinishedTurn()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        finishedPlayersCount++;
        // 모든 플레이어가 턴을 마치면 다음 턴으로 진행
        if (finishedPlayersCount >= PhotonNetwork.CurrentRoom.PlayerCount)
        {
            ProceedToNextTurn();
        }
    }

    // 다음 턴 진행 로직
    void ProceedToNextTurn()
    {
        if (isAdvancingTurn) return;
        isAdvancingTurn = true;
        finishedPlayersCount = 0;
        currentTurnTimer = turnDuration;

        int turnInRound = ((currentTurn - 1) % turnsPerRound) + 1;
        ProcessPendingAttacks_EndTurn();

        // (1) 6라운드 마지막 턴이면: 다음 라운드/다음 턴 보내기 전에 여기서 끝내야 함
        if (currentRound == 6 && turnInRound == turnsPerRound)
        {
            RevealPendingResultsToAll();

            int winner = DetermineWinnerByTileCount();
            photonView.RPC("RPC_GameOver", RpcTarget.All, winner);
            return;
        }

        // (2) 일반 승리 조건 체크 (중간에 올킬/게임 종료 등)
        int winnerNormal = CheckVictoryCondition();
        if (winnerNormal != -1)
        {
            // 원하면 여기서도 결과 공개
            RevealPendingResultsToAll();

            photonView.RPC("RPC_GameOver", RpcTarget.All, winnerNormal);
            return;
        }

        // (3) 턴 종료 시 상대에게 결과 공개 (지속 치사율 적용 전에 반영)
        RevealPendingResultsToAll();

        // (4) 턴 종료 시 지속 치사율 적용
        ApplyOngoingDeaths_Master();

        // (5) 인구 5 미만 처리 + 0 타일 보너스 반영
        ApplyZeroPopulationPenalty();

        // (6) 턴 종료 시 누적 사망자 보상 지급
        DistributeMoneyBasedOnCumulativeDeaths();

        // (5) 라운드 마지막 턴이면 다음 라운드로
        if (turnInRound == turnsPerRound)
        {
            // 라운드 끝날 때 상대에게 결과 공개
            RevealPendingResultsToAll();

            ProceedToNextRound();
            return;
        }

        // (7) 다음 턴으로 넘어갈 때만 적용될 로직들
        ApplyPendingVaccineReverts();

        int nextTurnVal = currentTurn + 1;

        // 다음 턴 시작
        photonView.RPC("RPC_BeginTurnTransition", RpcTarget.All, nextTurnVal, currentRound);
    }

    // 다음 라운드 진행 로직
    void ProceedToNextRound()
    {
        // 최대 라운드 초과 시 게임 종료
        if (currentRound >= maxRounds)
        {
            int winner = CheckVictoryCondition();
            if (winner != -1)
            {
                photonView.RPC("RPC_GameOver", RpcTarget.All, winner);
                return;
            }
        }

        int nextRound = currentRound + 1;
        currentRoundTimer = roundDuration;
        
        photonView.RPC("RPC_StartNextRound", RpcTarget.All, nextRound);
    }

    // 누적 사망자 기반 지원금 지급 (마스터 전용)
    void DistributeMoneyBasedOnCumulativeDeaths()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        Debug.Log($"[디버그] 라운드 {currentRound} 시작 직전 누적 사망자 - T1: {cumulativeDeathsTeam1}, T2: {cumulativeDeathsTeam2}");

        int moneyForTeam1 = Mathf.FloorToInt(cumulativeDeathsTeam1 * moneyPerCumulativeDeath);
        int moneyForTeam2 = Mathf.FloorToInt(cumulativeDeathsTeam2 * moneyPerCumulativeDeath);

        Debug.Log($"[디버그] 지급 금액 - T1: {moneyForTeam1}, T2: {moneyForTeam2}");

        photonView.RPC("RPC_GiveMoneyToTeam", RpcTarget.All, 1, moneyForTeam1);
        photonView.RPC("RPC_GiveMoneyToTeam", RpcTarget.All, 2, moneyForTeam2);

    }

    // 턴마다 소유 타일에 치사율 적용 (마스터 전용)
    void ApplyOngoingDeaths_Master()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null) return;

        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (newlyInfectedTileIdsThisTurn.Contains(tile.tileID)) continue;
            if (tile.type != HexTile.TileType.GROUND) continue;
            if (tile.isDestroyed) continue;
            if (tile.ownerTeam != 1 && tile.ownerTeam != 2) continue;
            if (tile.population <= 0) continue;

            if (!teamFatalityRates.TryGetValue(tile.ownerTeam, out float fatalityRate))
                fatalityRate = 0f;

            float finalFatality = fatalityRate * tile.fatalityMultiplier * tile.bioWeaponMultiplier;
            int deaths = VirusCalculator.Instance.CalculateDeaths(tile.population, finalFatality);
            if (deaths <= 0) continue;

            int newPop = tile.population - deaths;
            if (newPop < 0) newPop = 0;
            if (newPop > 0 && newPop < 5)
            {
                newPop = 0;
            }

            if (tile.ownerTeam == 1) cumulativeDeathsTeam1 += deaths;
            else if (tile.ownerTeam == 2) cumulativeDeathsTeam2 += deaths;

            photonView.RPC(nameof(RPC_ApplyOngoingDeaths), RpcTarget.All, tile.tileID, newPop, deaths, tile.ownerTeam);
        }

        newlyInfectedTileIdsThisTurn.Clear();
    }

    [PunRPC]
    void RPC_ApplyOngoingDeaths(int tileID, int newPop, int deathCount, int ownerTeam)
    {
        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null) return;
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;

        HexTile tile = MapGenerator.Instance.allTiles[tileID];
        tile.population = newPop;
        if (deathCount > 0) tile.AddDeaths(deathCount);
        tile.UpdateVisuals(tile.ownerTeam, tile.population);

        if (deathCount > 0 && GameUIManager.Instance != null)
            GameUIManager.Instance.UpdateScore(ownerTeam, 0, deathCount);
    }

    // 특정 팀에게 돈을 지급하는 RPC
    [PunRPC]
    void RPC_GiveMoneyToTeam(int team, int amount)
    {
        if (GamePlayer.LocalPlayer != null && GamePlayer.LocalPlayer.myTeam == team && amount > 0)
        {
            GamePlayer.LocalPlayer.AddMoney(amount);
            Debug.Log($"[라운드 보너스] 누적 사망자 기반으로 {amount}원 지급받았습니다.");
        }
    }

    // [Admin] 조교 입력에 따른 잔류인원 보너스 처리
    [PunRPC]
    public void RPC_ProcessSurvivorProblems(int team, int problemCount)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // 해당 팀의 현재 총 잔류 인구 계산
        int survivorCount = CalculateSurvivorCount(team);
        
        // 계산된 인원 수를 바탕으로 해당 팀원에게 보상 지급 RPC 전송
        photonView.RPC("RPC_GiveSurvivorMoneyToPlayer", RpcTarget.All, team, problemCount, survivorCount);
        
        Debug.Log($"[잔류인원 보너스] 팀 {team}: 문제 {problemCount}개 × 잔류인원 {survivorCount}명 처리");
    }

    // 팀의 총 인구수 계산 함수
    int CalculateSurvivorCount(int team)
    {
        int total = 0;
        if (MapGenerator.Instance != null)
        {
            foreach (var tile in MapGenerator.Instance.allTiles.Values)
            {
                if (tile.ownerTeam == team && !tile.isDestroyed)
                {
                    total += tile.population;
                }
            }
        }
        return total;
    }

    // 잔류 인원 보너스 지급 RPC
    [PunRPC]
    void RPC_GiveSurvivorMoneyToPlayer(int team, int problemCount, int survivorCount)
    {
        // 해당 팀 플레이어인지 확인
        if (GamePlayer.LocalPlayer != null && GamePlayer.LocalPlayer.myTeam == team && problemCount > 0 && survivorCount > 0)
        {
            // 보상 공식: 문제 수 * 잔류 인원 * 계수
            int earnedMoney = problemCount * survivorCount * moneyPerSurvivorProblem;
            if (earnedMoney > 0)
            {
                GamePlayer.LocalPlayer.AddMoney(earnedMoney);
                Debug.Log($"[잔류인원 보너스] 문제 {problemCount}개 × 잔류인원 {survivorCount}명 = {earnedMoney}원 지급");
            }
        }
    }

    // 턴 종료 시 인구 5 미만 -> 0 처리 + 0인 타일당 누적 사망자 +50
    void ApplyZeroPopulationPenalty()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null) return;

        int zeroPopTeam1 = 0;
        int zeroPopTeam2 = 0;

        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile.isDestroyed) continue;
            if (tile.ownerTeam != 1 && tile.ownerTeam != 2) continue;

            bool willBeZero = tile.population == 0;
            if (tile.population > 0 && tile.population < 5)
            {
                int remaining = tile.population;
                if (remaining > 0)
                {
                    if (tile.ownerTeam == 1) cumulativeDeathsTeam1 += remaining;
                    else if (tile.ownerTeam == 2) cumulativeDeathsTeam2 += remaining;
                }
                // 인구가 5 미만이면 0으로 처리
                willBeZero = true;
                photonView.RPC("RPC_SyncTileResult", RpcTarget.All, tile.tileID, tile.ownerTeam, 0, true, 0, 0, false, remaining);
            }

            if (willBeZero)
            {
                if (tile.ownerTeam == 1) zeroPopTeam1++;
                else if (tile.ownerTeam == 2) zeroPopTeam2++;
            }
        }

        if (zeroPopTeam1 > 0)
        {
            int bonus = zeroPopTeam1 * 50;
            cumulativeDeathsTeam1 += bonus;
            photonView.RPC("RPC_AddDeathBonus", RpcTarget.All, 1, bonus);
        }

        if (zeroPopTeam2 > 0)
        {
            int bonus = zeroPopTeam2 * 50;
            cumulativeDeathsTeam2 += bonus;
            photonView.RPC("RPC_AddDeathBonus", RpcTarget.All, 2, bonus);
        }
    }

    [PunRPC]
    void RPC_AddDeathBonus(int team, int bonus)
    {
        if (bonus <= 0) return;
        if (GameUIManager.Instance != null)
            GameUIManager.Instance.UpdateScore(team, 0, bonus);
    }

    // 승리 조건 판별 (1: Team1 승, 2: Team2 승, 3: 무승부, -1: 계속 진행)
    int CheckVictoryCondition()
    {
        if (currentTurn == 0) return -1;

        // 1턴에는 게임 종료 안되게 (최소 2턴 이상 진행되어야 승패 판정)
        if (currentTurn < maxTurns)
            return -1;

        int countA = 0;
        int countB = 0;

        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile.ownerTeam == 1) countA++;
            else if (tile.ownerTeam == 2) countB++;
        }

        Debug.Log($"[VICTORY CHECK] turn={currentTurn}/{maxTurns} A={countA} B={countB}");
        if (countA > countB) return 1;
        if (countB > countA) return 2;
        return 3; // 무승부        
    }

    // [RPC] 다음 턴 시작 처리
    [PunRPC]
    public void RPC_StartNextTurn(int newTurn, int roundNum)
    {
        isAdvancingTurn = false;
        if (bioWeaponBuffActiveLocal && bioWeaponBuffUntilTurnLocal < newTurn)
        {
            bioWeaponBuffActiveLocal = false;
            bioWeaponBuffUntilTurnLocal = -1;
            GameUIManager.Instance?.RefreshPlayerStatsUI();
        }

        currentPhase = Phase.Action;
        isTimePaused = false;
        if (panelMovePrompt != null) panelMovePrompt.SetActive(false);
        if (minigamePanel != null) minigamePanel.SetActive(false);

        currentTurn = newTurn;
        currentRound = roundNum;
        isMyTurnFinished = false;
        currentTurnTimer = turnDuration;

        if (btnEndTurn != null) btnEndTurn.SetActive(true);
        if (txtTurnInfo != null)
        {
            int turnInRound = ((newTurn - 1) % turnsPerRound) + 1;
            txtTurnInfo.text = $"Round {roundNum} / {maxRounds} - Turn {turnInRound} / {turnsPerRound}";
        }

        if (GameUIManager.Instance != null)
        {
            GameUIManager.Instance.UpdatePhaseText("행동 단계 (Action)");
            GameUIManager.Instance.UpdateRoundInfo(roundNum, maxRounds, newTurn, turnsPerRound);
        }

        if (PhotonNetwork.LocalPlayer != null)
        {
            consumedAttackIdsLocal.Clear();
            virusConsumedThisTurnLocal = false;
        }

        if (GamePlayer.LocalPlayer != null)
            GamePlayer.LocalPlayer.OnTurnStart(newTurn);

        // 로컬 플레이어의 턴 시작 처리 (행동력 초기화 등)
        if (GamePlayer.LocalPlayer != null)
        {
            GamePlayer.LocalPlayer.OnTurnStart(newTurn);
        }

        Debug.Log($"=== Round {roundNum} - Turn {newTurn} Start ===");

        // 서버(마스터)에서만 "턴 시작 시 시스템 처리"를 수행하고 All로 동기화
        if (PhotonNetwork.IsMasterClient)
        {
            ClearExpiredBioWeapons_Master(newTurn);

            int turnInRound = ((newTurn - 1) % turnsPerRound) + 1;

            ClearExpiredHiddenTiles_Master();
            processedAttackKeysThisTurn.Clear();

            // 라운드 시작(해당 라운드의 1턴)에서만 실행되는 것들
            if (turnInRound == 1)
            {

                // 3라운드: 청정구역 5개 생성 (라운드 시작 1회)
                if (roundNum == 2)
                    SpawnRandomCleanZones();

                // 5라운드: 돌연변이 버프 적용 (라운드 시작 1회)
                if (roundNum == 5)
                    ApplyMutantBuff();

                // 6라운드: 돌연변이 버프 해제
                if (roundNum == 6)
                    RemoveMutantBuff();
            }

            // 이벤트/재난은 "턴마다" 실행하되, 4라운드는 고정 재난만
            if (roundNum == 4)
            {
                EventManager.Instance?.TriggerDisasterForRound4(turnInRound);
            }
            else
            {
                //
            }
        }
    }

    [PunRPC]
    public void RPC_PlayerReady(int actorNum)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (gameStarted) return;

        // 방에 아직 2명 안 찼으면 Ready 인정하지 않기
        if (PhotonNetwork.CurrentRoom.PlayerCount < expectedPlayers)
        {
            Debug.Log($"[READY] Ignored: room not full ({PhotonNetwork.CurrentRoom.PlayerCount}/{expectedPlayers})");
            return;
        }

        readyPlayers++;
        Debug.Log($"[READY] {readyPlayers}/{expectedPlayers}");

        if (readyPlayers >= expectedPlayers)
        {
            gameStarted = true;
            gameStartApproved = true;
            StartGameLogic();
        }
    }

    // [RPC] 다음 라운드 시작 처리
    [PunRPC]
    public void RPC_StartNextRound(int newRound)
    {
        // 라운드가 바뀔 때마다 "이번 라운드 미니게임 했는지" 플래그 리셋
        miniGamePlayedThisRound = false;

        if (newRound == 1)
        {
            // 게임 시작 때 청정구역 싹 초기화
            foreach (var t in MapGenerator.Instance.allTiles.Values)
            {
                t.isCleanZone = false;
                t.UpdateVisuals(t.ownerTeam, t.population);
            }
            cleanZoneSpawnedRound3 = false;
        }

        if (newRound != 3) cleanZoneSpawnedRound3 = false;

        currentRound = newRound;
        currentRoundTimer = roundDuration;

        int firstTurnInRound = ((newRound - 1) * turnsPerRound) + 1;
        photonView.RPC("RPC_StartNextTurn", RpcTarget.All, firstTurnInRound, newRound);

        Debug.Log($"=== Round {newRound} Start (Turn {firstTurnInRound}) ===");

        if (bioWeaponBuffActiveLocal && bioWeaponBuffUntilTurnLocal < currentTurn)
        {
            bioWeaponBuffActiveLocal = false;
            bioWeaponBuffUntilTurnLocal = -1;
            GameUIManager.Instance?.RefreshPlayerStatsUI();
        }
    }

    // 돌연변이 버프 적용 (5라운드 시작 시)
    void ApplyMutantBuff()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        // 모든 플레이어에게 돌연변이 버프 적용
        photonView.RPC("RPC_ApplyMutantBuff", RpcTarget.All);
        Debug.Log("[돌연변이 버프] 5라운드 시작 - 모든 플레이어에게 버프 적용");
    }

    // 돌연변이 버프 해제 (6라운드 시작 시)
    void RemoveMutantBuff()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        // 모든 플레이어에게 돌연변이 버프 해제
        photonView.RPC("RPC_RemoveMutantBuff", RpcTarget.All);
        Debug.Log("[돌연변이 버프] 6라운드 시작 - 버프 해제");
    }

    // [RPC] 돌연변이 버프 적용
    [PunRPC]
    void RPC_ApplyMutantBuff()
    {
        if (GamePlayer.LocalPlayer != null)
        {
            GamePlayer.LocalPlayer.hasMutantBuff = true;
            GamePlayer.LocalPlayer.mutantBuffRound = currentRound;
            GamePlayer.LocalPlayer.usedGuaranteedInfection = false;
            Debug.Log("[돌연변이 버프] 적용됨 - 치사율 +20%, 감염 가능 타일 +1, 감염 성공률 100% (1번만), 자기 타일 인구 자동 회복");
        }
    }

    // [RPC] 돌연변이 버프 해제
    [PunRPC]
    void RPC_RemoveMutantBuff()
    {
        if (GamePlayer.LocalPlayer != null)
        {
            GamePlayer.LocalPlayer.hasMutantBuff = false;
            GamePlayer.LocalPlayer.mutantBuffRound = 0;
            GamePlayer.LocalPlayer.usedGuaranteedInfection = false;
            Debug.Log("[돌연변이 버프] 해제됨");
        }
    }

    // ActorNumber로 플레이어 찾기
    GamePlayer GetPlayerByActorNum(int actorNum)
    {
        GamePlayer[] players = FindObjectsOfType<GamePlayer>();
        foreach (var player in players)
        {
            if (player.photonView != null && player.photonView.OwnerActorNr == actorNum)
            {
                return player;
            }
        }
        return null;
    }

    // [RPC] 돌연변이 버프: 자기 타일 인구 자동 회복
    [PunRPC]
    void RPC_HealMyTiles(int actorNum)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        GamePlayer player = GetPlayerByActorNum(actorNum);
        if (player == null) return;

        int team = player.myTeam;
        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile.ownerTeam == team && tile.population > 0)
            {
                // 자기 타일 인구 자동 회복 (10명씩 회복)
                int oldPop = tile.population;
                int healedPop = tile.population + 10;
                photonView.RPC("RPC_SyncTileResult", RpcTarget.All, tile.tileID, team, healedPop, true, actorNum, 0, false, 0);
                Debug.Log($"[돌연변이 버프] 타일 {tile.tileID} 인구 회복: {oldPop} -> {healedPop}");
            }
        }
    }

    // [RPC] 게임 종료 처리
    [PunRPC]
    public void RPC_GameOver(int winnerTeam)
    {
        if (panelVictory != null)
        {
            panelVictory.SetActive(true);
            if (btnEndTurn != null) btnEndTurn.SetActive(false);

            if (winnerTeam == 3) txtVictoryMessage.text = "무승부!";
            else txtVictoryMessage.text = $"TEAM {winnerTeam} 승리!";
        }
    }

    // 청정 구역(보너스 타일) 랜덤 생성 (마스터 전용)
    // 기획서: 3라운드에만 5개 생성
    void SpawnRandomCleanZones()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // 3라운드가 아니면 생성하지 않음
        if (currentRound != 2) return;

        if (cleanZoneSpawnedRound3) return;
        cleanZoneSpawnedRound3 = true;

        List<int> neutralTiles = new List<int>();

        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile.type == HexTile.TileType.WATER) continue;
            // "진짜 중립"만 청정구역 후보
            // 감염/점령된 땅(1,2,3)은 절대 제외
            if (tile.ownerTeam != 0) continue;

            // 이미 청정구역이면 제외
            if (tile.isCleanZone) continue;

            // 파괴/무적/기타 상호작용 불가면 제외(원하면)
            if (tile.isDestroyed) continue;
            if (tile.isImmune) continue;

            // (선택) 잠복으로 중립처럼 보이는 거 방지용: 숨김 타일이면 제외
            // if (tile.isHidden) continue;

            // (중요) "턴 종료 공개" 시스템을 쓰고 있다면,
            // 공개 대기중인 타일도 청정구역 후보에서 제외해야 함
            // 아래 HashSet/Dictionary 이름은 네 프로젝트에 맞게 바꿔야 함
            // if (pendingRevealTileIDs.Contains(tile.tileID)) continue;

            // 이번 턴 결과 공개 대기중이면 청정구역 후보에서 제외
            if (pendingRevealTileIDs.Contains(tile.tileID)) continue;

            neutralTiles.Add(tile.tileID);
        }

        if (neutralTiles.Count <= 0) return;

        int cleanZoneCount = 5;
        int actualCount = Mathf.Min(cleanZoneCount, neutralTiles.Count);

        for (int i = 0; i < actualCount; i++)
        {
            int randomIndex = Random.Range(0, neutralTiles.Count);
            int tileID = neutralTiles[randomIndex];

            photonView.RPC("RPC_SetCleanZone", RpcTarget.All, tileID);
            neutralTiles.RemoveAt(randomIndex);
        }

        Debug.Log($"[청정 지역] 3라운드 - {actualCount}개 생성됨");
    }

    // 청정 구역 설정 RPC
    [PunRPC]
    public void RPC_SetCleanZone(int tileID)
    {
        if (currentRound != 2) return;
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;

        HexTile tile = MapGenerator.Instance.allTiles[tileID];
        
        if (tile.ownerTeam != 0) return;

        tile.isCleanZone = true;
        
        tile.UpdateVisuals(tile.ownerTeam, tile.population);
    }

    // 마우스 클릭 시 로직 (공격, 아이템 사용, 백신 등)
    void DetectClick()
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);

        if (hit.collider == null) return;

        HexTile targetTile = hit.collider.GetComponent<HexTile>();
        if (targetTile == null) return;

        GamePlayer me = GamePlayer.LocalPlayer;
        if (me == null) return;

        // 1) 아이템 사용 모드 우선
        if (ItemManager.Instance != null && ItemManager.Instance.currentItem != ItemManager.ItemType.None)
        {
            ItemManager.Instance.TryUseItemOnTile(targetTile);
            return;
        }

        // 공통 상호작용 불가
        if (targetTile.isDestroyed)
        {
            Debug.Log("[클릭] 타일이 파괴되었습니다.");
            return;
        }

        if (targetTile.type == HexTile.TileType.WATER)
        {
            Debug.LogWarning("한강 타일입니다.");
            return;
        }

        // 2) 공격 모드
        if (isAttackMode)
        {
            if (targetTile.isImmune)
            {
                Debug.Log("[클릭] 타일이 무적 상태입니다.");
                return;
            }

            if (!me.CanUseCurrentVirus())
            {
                Debug.Log($"[클릭] 바이러스 사용 불가: {me.currentVirus}");
                return;
            }

            if (targetTile.ownerTeam == me.myTeam)
            {
                Debug.Log("[클릭] 내 땅입니다.");
                return;
            }

            // 기본 인접
            bool isAdjacent = IsAdjacentToMyTerritory(targetTile, me.myTeam);

            // 돌연변이 버프: 사거리 +1
            if (!isAdjacent && me.hasMutantBuff)
                isAdjacent = IsInRangeOfMyTerritory(targetTile, me.myTeam, 1);

            bool canAttack = isAdjacent;

            // 다리 규칙으로 공격 허용
            if (!canAttack && CanAttackByBridge(targetTile, me.myTeam))
            {
                canAttack = true;
                Debug.Log($"[다리 규칙] 타일 {targetTile.tileID}는 다리 연결로 공격 가능합니다.");
            }

            Debug.Log($"[클릭] 타일 {targetTile.tileID} 공격시도 - adj={isAdjacent} (mutant={me.hasMutantBuff}, blackout={me.hasBlackoutDebuff})");

            if (!canAttack)
            {
                Debug.LogWarning($"[클릭] 타일 {targetTile.tileID}는 내 영토와 인접하지 않습니다!");
                return;
            }

            if (!me.TryUseActionPoint())
            {
                Debug.Log("[클릭] 행동력이 부족합니다.");
                return;
            }

            float resistance = (targetTile.ownerTeam != 0 && targetTile.ownerTeam != me.myTeam) ? 50f : 0f;

            float now = Time.time;
            if (lastLocalAttackTile == targetTile.tileID && (now - lastLocalAttackSendTime) < 0.2f)
            {
                Debug.LogWarning($"[클릭] 중복 공격 전송 방지: tile={targetTile.tileID}");
                return;
            }
            lastLocalAttackTile = targetTile.tileID;
            lastLocalAttackSendTime = now;

            int attackId = ++localAttackSeq;

            photonView.RPC("RPC_RequestAttack", RpcTarget.MasterClient,
                targetTile.tileID,
                PhotonNetwork.LocalPlayer.ActorNumber,
                me.infectionRate,
                me.fatalityRate,
                (int)me.currentVirus,
                resistance,
                attackId
            );

            return;
        }

        // 3) 백신 모드 (방어/관리)
        // ✅ 항상 로그부터
        Debug.Log($"[VAC CLICK] tile={targetTile.tileID} owner={targetTile.ownerTeam} myTeam={me.myTeam} " +
                  $"uses={me.currentVaccineUses}/{me.maxVaccineUses} range={me.vaccineRange} rate={me.vaccineRecoveryRate}");

        // 상대땅만 허용
        if (targetTile.ownerTeam == 0)
        {
            Debug.Log("[VAC BLOCK] 중립 타일엔 백신 불가");
            return;
        }

        if (targetTile.ownerTeam == me.myTeam)
        {
            Debug.Log("[VAC BLOCK] 내 땅엔 백신 불가");
            return;
        }

        // 사용 횟수 체크
        if (!me.CanUseVaccine())
        {
            Debug.Log($"[VAC BLOCK] 횟수 초과 ({me.currentVaccineUses}/{me.maxVaccineUses})");
            return;
        }

        // 사거리 판정은 "내 땅 기반"이라 내 땅이 0이면 무조건 실패함
        int myTileCount = 0;
        foreach (var t in MapGenerator.Instance.allTiles.Values)
            if (t.ownerTeam == me.myTeam && !t.isDestroyed) myTileCount++;

        if (myTileCount == 0)
        {
            Debug.LogWarning("[VAC BLOCK] 내 땅이 0개로 인식됨 → 시작타일 동기화 문제 가능성");
            return;
        }

        bool inRange = IsInRangeOfMyTerritory(targetTile, me.myTeam, me.vaccineRange);
        Debug.Log($"[VAC RANGE] inRange={inRange} myTiles={myTileCount}");

        if (!inRange)
        {
            Debug.LogWarning($"[VAC BLOCK] 타일 {targetTile.tileID} 사거리 밖");
            return;
        }

        Debug.Log($"[VAC SEND] RPC_RequestVaccine -> tile={targetTile.tileID}");
        photonView.RPC("RPC_RequestVaccine", RpcTarget.MasterClient,
            targetTile.tileID,
            PhotonNetwork.LocalPlayer.ActorNumber,
            me.vaccineRecoveryRate
        );
    }

    [PunRPC]
    public void RPC_UseItem(int tileID, int actorNum, int itemIndex)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null)
        {
            photonView.RPC("RPC_ItemFailed_ToRequester", RpcTarget.All, actorNum, "맵이 아직 준비 안됨");
            return;
        }
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID))
        {
            photonView.RPC("RPC_ItemFailed_ToRequester", RpcTarget.All, actorNum, "타일 없음");
            return;
        }        

        var item = (ItemManager.ItemType)itemIndex;
        var tile = MapGenerator.Instance.allTiles[tileID];

        // 공통 가드
        if (currentPhase != Phase.Action || isTimePaused)
        {
            photonView.RPC("RPC_ItemFailed_ToRequester", RpcTarget.All, actorNum, "지금은 사용 불가(페이즈/정지)");
            return;
        }
        if (tile.isDestroyed)
        {
            photonView.RPC("RPC_ItemFailed_ToRequester", RpcTarget.All, actorNum, "파괴된 타일");
            return;
        }

        int attackerTeam = GetTeamByActorNumSafe(actorNum);
        if (attackerTeam == -1)
        {
            photonView.RPC("RPC_ItemFailed_ToRequester", RpcTarget.All, actorNum, "팀 정보를 찾을 수 없음");
            return;
        }

        // 여기서부터 아이템별 룰 + 적용
        bool success = false;

        switch (item)
        {
            case ItemManager.ItemType.Bomb:
                {
                    // 행동자는 즉시 파괴(검은색), 상대는 턴 종료 시 공개
                    int deaths = tile.population;
                    QueueRevealOnly(
                        actorNum,
                        tileID, 0, 0, true, 0, false, deaths);
                    pendingDestroyedTileIDs.Add(tileID);

                    Player p = PhotonNetwork.CurrentRoom.GetPlayer(actorNum);
                    if (p != null)
                        photonView.RPC(nameof(RPC_SetTileDestroyed), p, tileID, true);

                    photonView.RPC("RPC_ConsumeItem_ToRequester", RpcTarget.All, actorNum, (int)ItemManager.ItemType.Bomb);

                    success = true;
                    break;
                }

            case ItemManager.ItemType.BioWeapon:
                {
                    bioWeaponBuffUntilTurnByActor[actorNum] = currentTurn;

                    // ✅ 요청자에게만 로컬 UI 버프 ON
                    Player p = PhotonNetwork.CurrentRoom.GetPlayer(actorNum);
                    if (p != null)
                        photonView.RPC(nameof(RPC_SetBioWeaponBuffLocal), p, currentTurn);

                    photonView.RPC("RPC_ConsumeItem_ToRequester", RpcTarget.All, actorNum, (int)ItemManager.ItemType.BioWeapon);
                    success = true;
                    break;
                }

            case ItemManager.ItemType.GeneScissors:
                {
                    if (tile.ownerTeam == 0 || tile.ownerTeam == attackerTeam)
                    {
                        photonView.RPC("RPC_ItemFailed_ToRequester", RpcTarget.All, actorNum, "적 타일에만 사용 가능");
                        return;
                    }

                    // 상대가 이미 감염시킨 타일 중 하나를 내 땅으로 변경
                    // 결과는 행동자에게만 즉시 적용, 상대는 턴 종료 시 공개
                    int newPop = tile.population;
                    SendResultToActorOnlyAndQueueReveal(
                        actorNum,
                        tileID, attackerTeam, newPop, true, 0, false, 0);

                      // 소비 승인
                      photonView.RPC("RPC_ConsumeItem_ToRequester", RpcTarget.All, actorNum, (int)ItemManager.ItemType.GeneScissors);

                    success = true;
                    break;
                }

            default:
                photonView.RPC("RPC_ItemFailed_ToRequester", RpcTarget.All, actorNum, "지원 안 하는 아이템");
                return;
        }
    }

        // 아이템 사용 처리
        void HandleItemUsage(HexTile target, GamePlayer me)
    {
        if (ItemManager.Instance == null) return;

        // ✅ 아이템별 조건은 마스터에서 최종 검증하니까,
        // 여기서는 최소 UX 조건만 두고 요청 보내는 구조가 깔끔함.
        bool sent = ItemManager.Instance.TryUseItemOnTile(target);

        if (!sent)
            Debug.Log("[Item] 요청 전송 실패");
    }

    [PunRPC]
    void RPC_SetVirusTypeForTeam(int team, int virusIndex)
    {
        var vt = (GamePlayer.VirusType)virusIndex;

        foreach (var p in FindObjectsOfType<GamePlayer>())
        {
            if (p.myTeam == team && p.photonView.IsMine)
            {
                p.TrySetVirus(vt);
                GameUIManager.Instance?.RefreshPlayerStatsUI();
                Debug.Log($"[GeneScissors] Team {team} virus -> {vt}");
            }
        }
    }

    // 내 영토와 인접 여부 확인 (다리 연결 포함)
    bool IsAdjacentToMyTerritory(HexTile target, int myTeam)
    {
        bool hasAnyLand = false;
        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile.ownerTeam == myTeam) { hasAnyLand = true; break; }
        }
        if (!hasAnyLand) return true; // 땅이 하나도 없으면 어디든 시작 가능

        // 직접 인접 확인
        List<HexTile> neighbors = HexGridHelper.Instance.GetNeighbors(target);        

        foreach (HexTile neighbor in neighbors)
        {
            if (neighbor.ownerTeam == myTeam) return true;
        }

        // 다리 연결 확인
        foreach (int connectedID in target.bridgeConnectedTileIDs)
        {
            if (MapGenerator.Instance.allTiles.ContainsKey(connectedID))
            {
                HexTile connectedTile = MapGenerator.Instance.allTiles[connectedID];
                if (connectedTile.ownerTeam == myTeam) return true;
            }
        }

        return false;
    }

    // 사거리(Range) 내에 내 영토가 있는지 확인
    bool IsInRangeOfMyTerritory(HexTile target, int myTeam, int range)
    {
        foreach (var myTile in MapGenerator.Instance.allTiles.Values)
        {
            if (myTile.ownerTeam == myTeam)
            {
                List<HexTile> tilesInRange = HexGridHelper.Instance.GetTilesInRange(myTile, range);
                if (tilesInRange.Contains(target)) return true;
            }
        }
        return false;
    }

    int GetTeamByActorNumSafe(int actorNum)
    {
        Player p = PhotonNetwork.CurrentRoom?.GetPlayer(actorNum);
        if (p != null && p.CustomProperties != null && p.CustomProperties.TryGetValue("TEAM", out object t))
            return (int)t;

        // 진짜 최후: 그래도 못 찾으면 -1로 리턴해서 사용 자체를 막아버리는 게 안전
        return -1;
    }

    [PunRPC]
    void RPC_RequestEnemyStats_FromMaster(int requesterActorNum)
    {
        // 내가 "요청을 받은 상대"인지 체크:
        // 2인 게임이면 requester가 아닌 사람이 응답하면 됨
        if (PhotonNetwork.LocalPlayer.ActorNumber == requesterActorNum) return;

        if (GamePlayer.LocalPlayer == null) return;

        var p = GamePlayer.LocalPlayer;
        string stats = $"확산:{p.spreadPower} / 치사:{p.fatalityRate} / 전염:{p.infectionRate}";

        // 요청자에게만 답장
        photonView.RPC("RPC_ShowEnemyStats_ToRequester", RpcTarget.All, requesterActorNum, stats);
    }

    [PunRPC]
    void RPC_ShowEnemyStats_ToRequester(int requesterActorNum, string statsMsg)
    {
        if (PhotonNetwork.LocalPlayer.ActorNumber != requesterActorNum) return;

        Debug.Log($"[1급 기밀] 적군 바이러스 정보: {statsMsg}");
        // TODO: UI 팝업 연결
    }

    [PunRPC]
    void RPC_ApplySuperAntibody(int targetActorNum)
    {
        if (PhotonNetwork.LocalPlayer.ActorNumber != targetActorNum) return;
        if (GamePlayer.LocalPlayer == null) return;

        GamePlayer.LocalPlayer.vaccineRecoveryRate += 20f;
        GamePlayer.LocalPlayer.vaccineSupplyRate += 20f;
        GamePlayer.LocalPlayer.vaccineRange += 1;

        // 추가: UI/횟수 재계산 반영
        GamePlayer.LocalPlayer.RecalculateVaccineUses();

        Debug.Log("[슈퍼 항체] 백신 성능 강화됨 (성공률 +20, 사거리 +1)");
    }

    // 생화학 무기 설치 RPC (예: 이번 턴까지만 적용)
    // [PunRPC]
    //public void RPC_SetBioWeapon(int tileID, int untilTurn)
    //{
    //    if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles == null) return;
    //    if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;

    //    // 효과 적용 (원하는 값으로)
    //    MapGenerator.Instance.allTiles[tileID].bioWeaponMultiplier = 1.1f; // +10% 예시

    //    // 만료 턴은 마스터만 저장
    //    if (PhotonNetwork.IsMasterClient)
    //        bioWeaponUntilTurn[tileID] = untilTurn;

    //    Debug.Log($"[BioWeapon] Set tile={tileID} untilTurn={untilTurn}");
    //}

    // [RPC] 유전자 가위: 한 바이러스를 다른 바이러스로 치환
    [PunRPC]
    public void RPC_ChangeVirusType(int targetTeam)
    {
        // 타겟 팀의 플레이어를 찾아서 바이러스 타입 변경
        GamePlayer[] players = FindObjectsOfType<GamePlayer>();
        foreach (var player in players)
        {
            if (player.myTeam == targetTeam && player.photonView.IsMine)
            {
                // 현재 바이러스 타입을 다른 랜덤 바이러스 타입으로 변경
                GamePlayer.VirusType currentType = player.currentVirus;
                GamePlayer.VirusType newType;
                
                // 현재 바이러스가 None이면 랜덤 바이러스 선택
                if (currentType == GamePlayer.VirusType.None)
                {
                    int randomVirus = Random.Range(1, 5); // Rush, Ambush, Gamble, Mutant 중 하나
                    newType = (GamePlayer.VirusType)randomVirus;
                }
                else
                {
                    // 현재 바이러스와 다른 랜덤 바이러스 선택
                    do
                    {
                        int randomVirus = Random.Range(1, 5);
                        newType = (GamePlayer.VirusType)randomVirus;
                    } while (newType == currentType);
                }
                
                player.TrySetVirus(newType);
                Debug.Log($"[유전자 가위] Team {targetTeam}의 바이러스 타입이 {currentType}에서 {newType}로 변경되었습니다.");
                break;
            }
        }
    }

    // 타일 숨김 처리 (암살 바이러스 등)
    [PunRPC]
    public void RPC_SetTileHidden(int tileID, bool isHidden)
    {
        if (MapGenerator.Instance.allTiles.ContainsKey(tileID))
        {
            HexTile tile = MapGenerator.Instance.allTiles[tileID];
            tile.isHidden = isHidden;
            tile.UpdateVisuals(tile.ownerTeam, tile.population);
        }
    }

    // [RPC] 공격 요청 처리 및 결과 계산 (서버)
    // 동시 도착 처리: 같은 타일로 향하는 공격들을 모아서 한 번에 처리
    [PunRPC]
    public void RPC_RequestAttack(int tileID, int attackerActorNum, float infectionRate, float fatalityRate, int virusTypeIndex, float resistance, int attackId)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;
        long attackKey = (((long)attackerActorNum) << 32) | (uint)attackId;
        if (processedAttackKeysThisTurn.Contains(attackKey))
        {
            Debug.LogWarning($"[BLOCK] Duplicate attackId ignored: actor={attackerActorNum} attackId={attackId} tile={tileID}");
            return;
        }
        processedAttackKeysThisTurn.Add(attackKey);
        float now = Time.time;
        if (lastAttackTimeByActor.TryGetValue(attackerActorNum, out float lastTime) &&
            lastAttackTileByActor.TryGetValue(attackerActorNum, out int lastTile) &&
            lastTile == tileID && (now - lastTime) < 0.1f)
        {
            Debug.LogWarning($"[BLOCK] Duplicate attack ignored: actor={attackerActorNum} tile={tileID}");
            return;
        }
        lastAttackTimeByActor[attackerActorNum] = now;
        lastAttackTileByActor[attackerActorNum] = tileID;

        HexTile targetTile = MapGenerator.Instance.allTiles[tileID];
        if (targetTile.isImmune) return;
        // 강과 인접한 타일도 점령 가능하므로 isRiver 체크 제거

        // [규칙] 백신 치료 성공으로 "복구 예약"된 타일은 이번 턴 감염 불가
        if (pendingVaccineReverts.ContainsKey(tileID))
        {
            Debug.LogWarning($"[BLOCK] Tile {tileID} is pending vaccine revert -> cannot be infected this turn");
            return;
        }

        // 공격 요청을 큐에 추가 (타일의 초기 상태 저장)
        AttackRequest request = new AttackRequest
        {
            tileID = tileID,
            attackerActorNum = attackerActorNum,
            infectionRate = infectionRate,
            fatalityRate = fatalityRate,
            virusTypeIndex = virusTypeIndex,
            resistance = resistance,
            timestamp = Time.time,
            attackId = attackId,
            originalPopulation = targetTile.population, // 공격 시점의 초기 인구
            originalOwnerTeam = targetTile.ownerTeam    // 공격 시점의 소유 팀
        };

        // 요청자에게만 즉시 결과를 보여줌 (임시 UI 업데이트)
        ProcessSingleAttack_Immediate(request, targetTile);

        // 해당 타일의 공격 리스트에 추가 (턴 종료 시 확정 처리)
        if (!pendingAttacks.ContainsKey(tileID))
        {
            pendingAttacks[tileID] = new List<AttackRequest>();
        }
        pendingAttacks[tileID].Add(request);
    }

    void ProcessPendingAttacks_EndTurn()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (pendingAttacks.Count == 0) return;

        Debug.Log($"[턴 종료] {pendingAttacks.Count}개 타일에 대한 공격 확정 처리 중...");

        foreach (var kvp in pendingAttacks)
        {
            if (kvp.Value == null || kvp.Value.Count == 0) continue;

            int tileID = kvp.Key;
            int attackCount = kvp.Value.Count;

            Debug.Log($"[턴 종료] 타일 {tileID}: {attackCount}개 공격 확정");

            // 동시 도착 여부 판정 및 최종 결과 확정
            if (attackCount > 1)
            {
                // 동시 도착: 확률 계산으로 승자 결정 후 모두에게 브로드캐스트
                pendingRevealResults.RemoveAll(r => r.tileID == tileID);
                pendingRevealTileIDs.Remove(tileID);
            }

            ProcessSimultaneousAttacks_Final(tileID, kvp.Value);
        }

        pendingAttacks.Clear();
    }

    // 턴 종료 시 최종 확정 처리 (모두에게 브로드캐스트)
    void ProcessSimultaneousAttacks_Final(int tileID, List<AttackRequest> attacks)
    {
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;
        HexTile targetTile = MapGenerator.Instance.allTiles[tileID];
        if (targetTile.isImmune) return;
        float cleanZoneInfectionMultiplier = targetTile.isCleanZone ? 0.5f : 1f;

        // 공격이 2개 이상이면 동시 도착 이벤트로 기록
        if (attacks.Count >= 2)
        {
            simultaneousAttackHappenedThisTurn = true;
            simultaneousAttackTilesThisTurn.Add(tileID);
            Debug.Log($"[최종 확정] 타일 {tileID} 동시 도착 {attacks.Count}개 공격 - 확률 판정 시작");
        }

        // 단일 공격: 재계산 후 모두에게 브로드캐스트
        if (attacks.Count == 1)
        {
            ProcessSingleAttack_Final(attacks[0], targetTile);
            return;
        }

        // 2개 공격: 확률 계산
        if (attacks.Count == 2)
        {
            float a = Mathf.Clamp01((attacks[0].infectionRate * cleanZoneInfectionMultiplier) / 100f);
            float b = Mathf.Clamp01((attacks[1].infectionRate * cleanZoneInfectionMultiplier) / 100f);
            float denom = 1f - (a * b);

            float probA, probB, probFail;

            if (denom <= 0f)
            {
                float sum = a + b;
                if (sum <= 0f)
                {
                    probA = 0f;
                    probB = 0f;
                    probFail = 1f;
                }
                else
                {
                    probA = a / sum;
                    probB = b / sum;
                    probFail = 0f;
                }
            }
            else
            {
                probA = (a * (1f - b)) / denom;
                probB = (b * (1f - a)) / denom;
                probFail = ((1f - a) * (1f - b)) / denom;
            }

            Debug.Log($"[최종 확정] 타일 {tileID}: A={a:0.##}, B={b:0.##}, A확률={probA:0.###}, B확률={probB:0.###}, 실패={probFail:0.###}");

            float r = Random.Range(0f, 1f);
            if (r < probA)
            {
                ProcessSingleAttack_Final(attacks[0], targetTile);
                Debug.Log($"[최종 확정] Actor {attacks[0].attackerActorNum} 승리!");
            }
            else if (r < probA + probB)
            {
                ProcessSingleAttack_Final(attacks[1], targetTile);
                Debug.Log($"[최종 확정] Actor {attacks[1].attackerActorNum} 승리!");
            }
            else
            {
                // 둘 다 실패
                foreach (var attack in attacks)
                {
                    photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
                        tileID, targetTile.ownerTeam, targetTile.population, false, attack.attackerActorNum, 0, false, 0);
                }
                Debug.Log($"[최종 확정] 둘 다 실패!");
            }
            return;
        }

        // 3개 이상: 가중 확률
        List<float> rates = attacks.Select(a => Mathf.Max(0f, a.infectionRate * cleanZoneInfectionMultiplier)).ToList();
        float totalRate = rates.Sum();
        float failureRate = Mathf.Max(0f, 100f - totalRate);
        float total = totalRate + failureRate;

        Debug.Log($"[최종 확정] 타일 {tileID}: {attacks.Count}개 공격, 실패율: {failureRate:0.##}%");

        float randomValue = Random.Range(0f, total);
        float cumulative = 0f;
        int successfulAttackIndex = -1;

        for (int i = 0; i < rates.Count; i++)
        {
            cumulative += rates[i];
            if (randomValue <= cumulative)
            {
                successfulAttackIndex = i;
                break;
            }
        }

        if (successfulAttackIndex >= 0)
        {
            ProcessSingleAttack_Final(attacks[successfulAttackIndex], targetTile);
            Debug.Log($"[최종 확정] Actor {attacks[successfulAttackIndex].attackerActorNum} 승리!");
        }
        else
        {
            // 모두 실패
            foreach (var attack in attacks)
            {
                photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
                    tileID, targetTile.ownerTeam, targetTile.population, false, attack.attackerActorNum, 0, false, 0);
            }
            Debug.Log($"[최종 확정] 모두 실패!");
        }
    }

    // 단일 공격 최종 확정 (모두에게 브로드캐스트)
    void ProcessSingleAttack_Final(AttackRequest attack, HexTile targetTile)
    {
        int attackerTeam = (attack.attackerActorNum == 1) ? 1 : 2;
        GamePlayer.VirusType vType = (GamePlayer.VirusType)attack.virusTypeIndex;

        // 최종 치사율 계산 (기존 로직과 동일)
        float finalFatality = attack.fatalityRate;
        if (PhotonNetwork.IsMasterClient &&
            bioWeaponBuffUntilTurnByActor.TryGetValue(attack.attackerActorNum, out int untilTurn) &&
            untilTurn >= currentTurn)
        {
            finalFatality += BIO_WEAPON_ADD;
        }

        float finalInfectionRate = attack.infectionRate;
        if (targetTile.isCleanZone) finalInfectionRate *= 0.5f;

        GamePlayer attackerPlayer = GetPlayerByActorNum(attack.attackerActorNum);
        if (attackerPlayer != null && attackerPlayer.hasMutantBuff)
        {
            finalFatality *= 1.2f;
        }

        if (EventManager.Instance != null)
            finalFatality -= EventManager.Instance.globalFatalityDebuff;
        if (finalFatality < 0) finalFatality = 0;

        // 바이러스 사용 기록
        if (vType != GamePlayer.VirusType.None)
            photonView.RPC(nameof(RPC_OnVirusUsed), RpcTarget.All, attack.attackerActorNum, attack.attackId);

        // 사망자 및 인구 계산 (공격 시점의 초기 인구 사용)
        int deaths = VirusCalculator.Instance.CalculateDeaths(attack.originalPopulation, finalFatality);
        int newPop = attack.originalPopulation - deaths;
        if (newPop < 0) newPop = 0;

        Debug.Log($"[최종 사망자 계산] 타일 {attack.tileID} - 초기인구:{attack.originalPopulation}, 치사율:{finalFatality:F1}%, 사망:{deaths}, 남은인구:{newPop}");

        int immediateMoney = 0;
        if (targetTile.isCleanZone) immediateMoney = moneyBonusCleanZone;

        bool shouldHide = (vType == GamePlayer.VirusType.Ambush);
        if (shouldHide)
        {
            photonView.RPC(nameof(RPC_SetTileHidden), RpcTarget.All, attack.tileID, true);
            int hideUntilTurn = currentTurn + 2;
            photonView.RPC(nameof(RPC_SetTileHiddenUntilTurn), RpcTarget.All, attack.tileID, hideUntilTurn);
        }

        // 타일 상태 업데이트
        newlyInfectedTileIdsThisTurn.Add(attack.tileID);
        pendingVaccineReverts.Remove(attack.tileID);

        // 모두에게 최종 결과 브로드캐스트
        photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
            attack.tileID, attackerTeam, newPop, true, attack.attackerActorNum, immediateMoney, shouldHide, deaths);

        Debug.Log($"[최종 확정] 타일 {attack.tileID} → 팀{attackerTeam}, 인구{newPop}, 사망{deaths} (모두에게 브로드캐스트)");
    }

    // 동시 도착 공격 처리 함수 (기획서 확률 계산 로직 적용)
    void ProcessSimultaneousAttacks(int tileID, List<AttackRequest> attacks)
    {
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;
        HexTile targetTile = MapGenerator.Instance.allTiles[tileID];
        if (targetTile.isImmune) return;
        float cleanZoneInfectionMultiplier = targetTile.isCleanZone ? 0.5f : 1f;

        // 공격이 2개 이상이면 동시 도착 이벤트로 기록
        if (attacks.Count >= 2)
        {
            simultaneousAttackHappenedThisTurn = true;
            simultaneousAttackTilesThisTurn.Add(tileID);
        }

        // 기존 로직 유지...
        if (attacks.Count == 1)
        {
            ProcessSingleAttack(attacks[0], targetTile);
            return;
        }

        // 여러 공격이 동시에 도착한 경우: 감염률(치명률)에 비례해 승자 결정
        if (attacks.Count == 2)
        {
            float a = Mathf.Clamp01((attacks[0].infectionRate * cleanZoneInfectionMultiplier) / 100f);
            float b = Mathf.Clamp01((attacks[1].infectionRate * cleanZoneInfectionMultiplier) / 100f);
            float denom = 1f - (a * b);

            float probA;
            float probB;
            float probFail;

            if (denom <= 0f)
            {
                float sum = a + b;
                if (sum <= 0f)
                {
                    probA = 0f;
                    probB = 0f;
                    probFail = 1f;
                }
                else
                {
                    probA = a / sum;
                    probB = b / sum;
                    probFail = 0f;
                }
            }
            else
            {
                probA = (a * (1f - b)) / denom;
                probB = (b * (1f - a)) / denom;
                probFail = ((1f - a) * (1f - b)) / denom;
            }

            Debug.Log($"[동시 도착 처리] 타일 {tileID}: 2개 공격, A={a:0.##}, B={b:0.##}, A확률={probA:0.###}, B확률={probB:0.###}, 실패={probFail:0.###}");

            float r = Random.Range(0f, 1f);
            if (r < probA)
            {
                ProcessSingleAttackImmediate(attacks[0], targetTile);
                return;
            }
            if (r < probA + probB)
            {
                ProcessSingleAttackImmediate(attacks[1], targetTile);
                return;
            }

            foreach (var attack in attacks)
            {
                photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
                    tileID, targetTile.ownerTeam, targetTile.population, false, attack.attackerActorNum, 0, false, 0);
            }
            return;
        }

        List<float> rates = attacks.Select(a => Mathf.Max(0f, a.infectionRate * cleanZoneInfectionMultiplier)).ToList();
        float totalRate = rates.Sum();
        float failureRate = Mathf.Max(0f, 100f - totalRate);
        float total = totalRate + failureRate;

        Debug.Log($"[동시 도착 처리] 타일 {tileID}: {attacks.Count}개 공격, 감염률: [{string.Join(", ", attacks.Select(a => $"{a.infectionRate * cleanZoneInfectionMultiplier}%"))}], 실패: {failureRate:0.##}%");

        // 랜덤 값으로 승자/실패 결정
        float randomValue = Random.Range(0f, total);
        float cumulative = 0f;
        int successfulAttackIndex = -1;

        for (int i = 0; i < rates.Count; i++)
        {
            cumulative += rates[i];
            if (randomValue <= cumulative)
            {
                successfulAttackIndex = i;
                break;
            }
        }

        if (successfulAttackIndex >= 0)
        {
            ProcessSingleAttackImmediate(attacks[successfulAttackIndex], targetTile);
        }
        else
        {
            // 모든 공격 실패
            foreach (var attack in attacks)
            {
                photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
                    tileID, targetTile.ownerTeam, targetTile.population, false, attack.attackerActorNum, 0, false, 0);
            }
        }
    }

    void ProcessSingleAttackImmediate(AttackRequest attack, HexTile targetTile)
    {
        int attackerTeam = (attack.attackerActorNum == 1) ? 1 : 2;
        GamePlayer.VirusType vType = (GamePlayer.VirusType)attack.virusTypeIndex;

        float finalFatality = attack.fatalityRate;
        if (PhotonNetwork.IsMasterClient &&
            bioWeaponBuffUntilTurnByActor.TryGetValue(attack.attackerActorNum, out int untilTurn) &&
            untilTurn >= currentTurn)
        {
            finalFatality += BIO_WEAPON_ADD;
        }

        float finalInfectionRate = attack.infectionRate;
        if (targetTile.isCleanZone) finalInfectionRate *= 0.5f;

        GamePlayer attackerPlayer = GetPlayerByActorNum(attack.attackerActorNum);
        if (attackerPlayer != null && attackerPlayer.hasMutantBuff)
        {
            finalFatality *= 1.2f;
        }

        if (EventManager.Instance != null)
            finalFatality -= EventManager.Instance.globalFatalityDebuff;
        if (finalFatality < 0) finalFatality = 0;

        bool isSuccess;
        if (attackerPlayer != null && attackerPlayer.hasMutantBuff && !attackerPlayer.usedGuaranteedInfection)
        {
            isSuccess = true;
            attackerPlayer.usedGuaranteedInfection = true;
        }
        else
        {
            isSuccess = VirusCalculator.Instance.TryInfect(finalInfectionRate, attack.resistance);
        }

        if (!isSuccess)
        {
            photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
                attack.tileID, targetTile.ownerTeam, targetTile.population, false, attack.attackerActorNum, 0, false, 0);
            return;
        }

        newlyInfectedTileIdsThisTurn.Add(attack.tileID);

        pendingVaccineReverts.Remove(attack.tileID);

        int deaths = VirusCalculator.Instance.CalculateDeaths(targetTile.population, finalFatality);
        int newPop = targetTile.population - deaths;
        if (newPop < 0) newPop = 0;

        int immediateMoney = 0;
        if (targetTile.isCleanZone) immediateMoney = moneyBonusCleanZone;

        bool shouldHide = (vType == GamePlayer.VirusType.Ambush);
        if (shouldHide)
        {
            photonView.RPC(nameof(RPC_SetTileHidden), RpcTarget.All, attack.tileID, true);
            int hideUntilTurn = currentTurn + 2;
            photonView.RPC(nameof(RPC_SetTileHiddenUntilTurn), RpcTarget.All, attack.tileID, hideUntilTurn);
        }

        photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
            attack.tileID, attackerTeam, newPop, true, attack.attackerActorNum, immediateMoney, shouldHide, deaths);
    }

    // 즉시 공격 처리 함수 (본인에게만 임시 결과 전송, 저장 안 함)
    void ProcessSingleAttack_Immediate(AttackRequest attack, HexTile targetTile)
    {
        int attackerTeam = (attack.attackerActorNum == 1) ? 1 : 2;
        GamePlayer.VirusType vType = (GamePlayer.VirusType)attack.virusTypeIndex;

        // 최종 치사율 계산
        float finalFatality = attack.fatalityRate;
        if (PhotonNetwork.IsMasterClient &&
            bioWeaponBuffUntilTurnByActor.TryGetValue(attack.attackerActorNum, out int untilTurn) &&
            untilTurn >= currentTurn)
        {
            finalFatality += BIO_WEAPON_ADD;
        }

        float finalInfectionRate = attack.infectionRate;
        if (targetTile.isCleanZone) finalInfectionRate *= 0.5f;

        // 돌연변이 버프: 치사율 +20% 증가
        GamePlayer attackerPlayer = GetPlayerByActorNum(attack.attackerActorNum);
        if (attackerPlayer != null && attackerPlayer.hasMutantBuff)
        {
            finalFatality *= 1.2f;
        }

        // 언론 보도 디버프 적용
        if (EventManager.Instance != null)
            finalFatality -= EventManager.Instance.globalFatalityDebuff;
        if (finalFatality < 0) finalFatality = 0;

        // 감염 성공 여부 판단
        bool isSuccess = VirusCalculator.Instance.TryInfect(finalInfectionRate, attack.resistance);

        if (isSuccess)
        {
            // 사망자 수 계산 (공격 시점의 초기 인구 사용)
            int deaths = VirusCalculator.Instance.CalculateDeaths(attack.originalPopulation, finalFatality);
            int newPop = attack.originalPopulation - deaths;
            if (newPop < 0) newPop = 0;

            int immediateMoney = 0;
            if (targetTile.isCleanZone) immediateMoney = moneyBonusCleanZone;

            bool shouldHide = (vType == GamePlayer.VirusType.Ambush);

            // 본인에게만 즉시 결과 전송 (pendingRevealResults에 저장하지 않음)
            Player p = PhotonNetwork.CurrentRoom.GetPlayer(attack.attackerActorNum);
            if (p != null)
            {
                photonView.RPC("RPC_SyncTileResult", p,
                    attack.tileID, attackerTeam, newPop, true, attack.attackerActorNum, immediateMoney, shouldHide, deaths);
            }

            Debug.Log($"[즉시 반영] Actor {attack.attackerActorNum}에게만 타일 {attack.tileID} 임시 결과 전송");
        }
        else
        {
            // 실패 시에도 본인에게만 알림 (원래 타일 상태 유지)
            Player p = PhotonNetwork.CurrentRoom.GetPlayer(attack.attackerActorNum);
            if (p != null)
            {
                photonView.RPC("RPC_SyncTileResult", p,
                    attack.tileID, attack.originalOwnerTeam, attack.originalPopulation, false, attack.attackerActorNum, 0, false, 0);
            }

            Debug.Log($"[즉시 반영] Actor {attack.attackerActorNum}에게 타일 {attack.tileID} 공격 실패 알림");
        }
    }

    // 단일 공격 처리 함수 (기존 로직)
    void ProcessSingleAttack(AttackRequest attack, HexTile targetTile)
    {
        int attackerTeam = (attack.attackerActorNum == 1) ? 1 : 2;
        GamePlayer.VirusType vType = (GamePlayer.VirusType)attack.virusTypeIndex;

        // 최종 치사율 계산
        float finalFatality = attack.fatalityRate;
        if (PhotonNetwork.IsMasterClient &&
    bioWeaponBuffUntilTurnByActor.TryGetValue(attack.attackerActorNum, out int untilTurn) &&
    untilTurn >= currentTurn)
        {
            finalFatality += BIO_WEAPON_ADD;
        }

        float finalInfectionRate = attack.infectionRate;
        if (targetTile.isCleanZone) finalInfectionRate *= 0.5f; // 청정지역은 치명률 반감

        // 돌연변이 버프: 치사율 +20% 증가 (공격자 확인 필요)
        GamePlayer attackerPlayer = GetPlayerByActorNum(attack.attackerActorNum);
        if (attackerPlayer != null && attackerPlayer.hasMutantBuff)
        {
            finalFatality *= 1.2f; // 치사율 +20% 증가
            Debug.Log($"[돌연변이 버프] 치사율 +20% 증가: {finalFatality:F1}%");
        }

        // 언론 보도 디버프 적용
        if (EventManager.Instance != null)
            finalFatality -= EventManager.Instance.globalFatalityDebuff;
        if (finalFatality < 0) finalFatality = 0;

        // 감염 성공 여부 판단
        bool isSuccess = false;
        if (attackerPlayer != null && attackerPlayer.hasMutantBuff && !attackerPlayer.usedGuaranteedInfection)
        {
            // 돌연변이 버프: 감염 성공률 100% (1번만)
            isSuccess = true;
            attackerPlayer.usedGuaranteedInfection = true;
            Debug.Log($"[돌연변이 버프] 감염 성공률 100% 적용 (1번만)");
        }
        else
        {
            isSuccess = VirusCalculator.Instance.TryInfect(finalInfectionRate, attack.resistance);
        }

        if (vType != GamePlayer.VirusType.None)
            photonView.RPC(nameof(RPC_OnVirusUsed), RpcTarget.All, attack.attackerActorNum, attack.attackId);

        if (isSuccess)
        {
            newlyInfectedTileIdsThisTurn.Add(attack.tileID);

            // 이 타일이 이번 턴에 다시 감염되면 백신 복구 예약은 취소
            pendingVaccineReverts.Remove(attack.tileID);

            // 사망자 수 계산
            int deaths = VirusCalculator.Instance.CalculateDeaths(targetTile.population, finalFatality);
            int newPop = targetTile.population - deaths;
            if (newPop < 0) newPop = 0;

            // 청정 지역 점령 시에만 즉시 보너스 지급
            int immediateMoney = 0;
            if (targetTile.isCleanZone) immediateMoney = moneyBonusCleanZone;
            
            bool shouldHide = (vType == GamePlayer.VirusType.Ambush); // 매복 바이러스는 타일 정보를 숨김

            // 결과 동기화 (사망자 수 포함)
            SendResultToActorOnlyAndQueueReveal(
                attack.attackerActorNum,
                attack.tileID, attackerTeam, newPop, true,
                immediateMoney, shouldHide, deaths
            );
        }
        else
        {
            // 실패 시 변화 없음 알림
            SendResultToActorOnlyAndQueueReveal(
                attack.attackerActorNum,
                attack.tileID, targetTile.ownerTeam, targetTile.population, false,
                0, false, 0
            );
        }
    }

    [PunRPC]
    void RPC_OnVirusUsed(int actorNum, int attackId)
    {
        if (PhotonNetwork.LocalPlayer == null) return;
        if (PhotonNetwork.LocalPlayer.ActorNumber != actorNum) return;
        if (GamePlayer.LocalPlayer == null) return;

        if (consumedAttackIdsLocal.Contains(attackId)) return;
        consumedAttackIdsLocal.Add(attackId);
        if (virusConsumedThisTurnLocal) return;
        virusConsumedThisTurnLocal = true;
        GamePlayer.LocalPlayer.OnVirusUsed();
    }

    bool CanUseItem_Master(int actorNum, int tileID, ItemManager.ItemType item)
    {
        // 마스터 전용 호출 가정
        if (currentPhase != Phase.Action) return false;
        if (isTimePaused) return false;
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return false;

        // 필요하면 "이번 턴 끝낸 사람은 아이템 못 씀"도 막기
        // (이건 actor별로 저장해야 해서 지금은 생략)
        return true;
    }

    void RevealPendingResultsToAll()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (pendingRevealResults.Count == 0)
        {
            // 공개할 건 없어도 동시도착 뉴스는 띄울 수 있음 (원하면 조건 제거)
            TryBroadcastSimultaneousNews_AfterReveal();
            SyncDeathTotalsToAll();
            return;
        }

        foreach (var r in pendingRevealResults)
        {
            if (r.shouldHide)
            {
                int hideUntilTurn = currentTurn + 2;
                photonView.RPC(nameof(RPC_SetTileHiddenUntilTurn), RpcTarget.All, r.tileID, hideUntilTurn);
            }

            photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
                r.tileID, r.newTeam, r.newPop, r.isSuccess, r.actorNum, r.earnedMoney, r.shouldHide, r.deathCount);
        }

        pendingRevealResults.Clear();

        foreach (var tileID in pendingDestroyedTileIDs)
        {
            photonView.RPC(nameof(RPC_SetTileDestroyed), RpcTarget.All, tileID, true);
        }
        pendingDestroyedTileIDs.Clear();

        // 공개가 끝난 다음 뉴스
        TryBroadcastSimultaneousNews_AfterReveal();

        pendingRevealTileIDs.Clear();

        SyncDeathTotalsToAll();
    }

    void SyncDeathTotalsToAll()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        photonView.RPC(nameof(RPC_SetDeathTotals), RpcTarget.All, cumulativeDeathsTeam1, cumulativeDeathsTeam2);
    }

    [PunRPC]
    void RPC_SetDeathTotals(int team1Deaths, int team2Deaths)
    {
        if (GameUIManager.Instance != null)
            GameUIManager.Instance.SetDeathTotals(team1Deaths, team2Deaths);
    }

    void TryBroadcastSimultaneousNews_AfterReveal()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (simultaneousAttackHappenedThisTurn)
        {
            // 타일도 보여주고 싶으면 메시지에 포함 가능
            string msg = "동시 도착!";
            // 예: msg = $"동시 도착! (타일: {string.Join(", ", simultaneousAttackTilesThisTurn)})";

            photonView.RPC(nameof(RPC_ShowNews), RpcTarget.All, msg);

            // 턴 단위 초기화
            simultaneousAttackHappenedThisTurn = false;
            simultaneousAttackTilesThisTurn.Clear();
        }
    }

    [PunRPC]
    void RPC_ShowNews(string msg)
    {
        if (GameUIManager.Instance != null)
            GameUIManager.Instance.ShowNews(msg);  // 아래 GameUIManager에 구현
        else
            Debug.Log($"[NEWS] {msg}");
    }

    // [RPC] 백신 요청 처리 (서버)
    [PunRPC]
    public void RPC_RequestVaccine(int tileID, int actorNum, float recoveryRate)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;

        HexTile targetTile = MapGenerator.Instance.allTiles[tileID];

        // 이미 예약이면 거부
        if (pendingVaccineReverts.ContainsKey(tileID))
        {
            photonView.RPC(nameof(RPC_VaccineResult_ToRequester), RpcTarget.All, actorNum, false, tileID);
            return;
        }

        bool isSuccess = Random.Range(0f, 100f) <= recoveryRate;

        if (isSuccess)
        {
            pendingVaccineReverts[tileID] = 0;
        }

        // 무조건 결과 통지 (여기서 사용횟수/UI 처리)
        photonView.RPC(nameof(RPC_VaccineResult_ToRequester), RpcTarget.All, actorNum, isSuccess, tileID);
    }

    void ApplyPendingVaccineReverts()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (pendingVaccineReverts.Count == 0) return;

        foreach (var kv in pendingVaccineReverts.ToList())
        {
            int tileID = kv.Key;
            int revertTeam = kv.Value;

            if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) continue;
            HexTile tile = MapGenerator.Instance.allTiles[tileID];

            // 다음 턴 시작 시 중립(흰색)으로 복구
            photonView.RPC("RPC_SyncTileResult", RpcTarget.All,
                tileID, revertTeam, tile.population, true, 0, 0, false, 0);
        }

        pendingVaccineReverts.Clear();
    }

    [PunRPC]
    public void RPC_SetTileHiddenUntilRound(int tileID, int untilRound)
    {
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;
        MapGenerator.Instance.allTiles[tileID].hiddenUntilRound = untilRound;
    }

    [PunRPC]
    public void RPC_SetTileHiddenUntilTurn(int tileID, int untilTurn)
    {
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;
        MapGenerator.Instance.allTiles[tileID].hiddenUntilTurn = untilTurn;
    }

    void ClearExpiredHiddenTiles_Master()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        foreach (var t in MapGenerator.Instance.allTiles.Values)
        {
            if (t.isHidden && t.hiddenUntilTurn > 0 && currentTurn >= t.hiddenUntilTurn)
            {
                photonView.RPC(nameof(RPC_SetTileHidden), RpcTarget.All, t.tileID, false);
                photonView.RPC(nameof(RPC_SetTileHiddenUntilTurn), RpcTarget.All, t.tileID, -1);
            }
        }
    }

    // [RPC] 타일 상태 동기화 및 결과 처리 (클라이언트)
    [PunRPC]
    public void RPC_SyncTileResult(int tileID, int newTeam, int newPop, bool isSuccess, int actorNum, int earnedMoney, bool shouldHide, int deathCount)
    {
        if (MapGenerator.Instance.allTiles.ContainsKey(tileID))
        {
            HexTile tile = MapGenerator.Instance.allTiles[tileID];

            // 중복 적용 방지: 본인이 같은 턴에 이미 같은 결과를 받았으면 스킵
            // 단, 결과가 다르면 턴 종료 시 확정된 최종 결과로 덮어쓰기
            if (PhotonNetwork.LocalPlayer != null &&
                PhotonNetwork.LocalPlayer.ActorNumber == actorNum &&
                tile.lastAppliedTurn == currentTurn)
            {
                if (tile.ownerTeam == newTeam && tile.population == newPop)
                {
                    Debug.Log($"[중복 방지] 타일 {tileID} - 이미 적용된 동일 결과 무시");
                    return;
                }
                else
                {
                    Debug.Log($"[최종 갱신] 타일 {tileID} - 임시 결과 → 확정 결과로 갱신 (팀:{tile.ownerTeam}→{newTeam}, 인구:{tile.population}→{newPop})");
                }
            }

            if (isSuccess)
            {
                Debug.Log($"[RPC_SyncTileResult] 타일 {tileID} 업데이트 - 팀: {tile.ownerTeam} -> {newTeam}, 인구: {tile.population} -> {newPop}, 사망자: {deathCount}");
                
                // 사망자 수를 타일에 추가 (타일별 사망자 추적)
                if (deathCount > 0)
                {
                    tile.AddDeaths(deathCount);
                }

                if (newTeam != tile.ownerTeam)
                {
                    tile.lastOwnerTeam = tile.ownerTeam;
                }

                tile.lastAppliedTurn = currentTurn;

                // 시각적 상태 업데이트
                tile.isHidden = shouldHide;

                tile.ownerTeam = newTeam;
                tile.population = newPop;

                tile.UpdateVisuals(newTeam, newPop);

                // 마스터: 누적 사망자 집계 (다음 라운드 보상용)
                if (deathCount > 0 && PhotonNetwork.IsMasterClient)
                {
                    int attackerTeam = (actorNum == 1) ? 1 : 2;
                    if (attackerTeam == 1) cumulativeDeathsTeam1 += deathCount;
                    else if (attackerTeam == 2) cumulativeDeathsTeam2 += deathCount;
                }

                // UI 점수판 업데이트
                if (GameUIManager.Instance != null) 
                    GameUIManager.Instance.UpdateScore(newTeam, 1, deathCount);

                // 행동을 수행한 본인일 경우 돈 지급 등 처리
                if (PhotonNetwork.LocalPlayer.ActorNumber == actorNum && GamePlayer.LocalPlayer != null)
                {
                    // 청정 지역 보너스 즉시 지급
                    if (earnedMoney > 0 && tile.isCleanZone) 
                    {
                        GamePlayer.LocalPlayer.AddMoney(earnedMoney);
                    }
                    // 점령 성공 시 바이러스 특성 경험치 등 처리
                    if (newTeam != 0) GamePlayer.LocalPlayer.OnInfectionSuccess();
                }
            }
            else
            {
                Debug.Log($"[RPC_SyncTileResult] 타일 {tileID} 공격 실패");

                // 공격 실패 시 타일을 원래 상태로 복구
                // (즉시 반영 단계에서 변경된 것을 되돌림)
                if (PhotonNetwork.LocalPlayer != null &&
                    PhotonNetwork.LocalPlayer.ActorNumber == actorNum)
                {
                    // 공격자 본인의 화면에서만 복구
                    Debug.Log($"[공격 실패 복구] 타일 {tileID}을 원래 상태로 되돌림 - 팀: {tile.ownerTeam} -> {newTeam}, 인구: {tile.population} -> {newPop}");

                    tile.ownerTeam = newTeam;
                    tile.population = newPop;
                    tile.UpdateVisuals(newTeam, newPop);
                }
            }
        }
        else
        {
            Debug.LogError($"[RPC_SyncTileResult] 타일 {tileID}를 찾을 수 없습니다!");
        }
    }

    // 자신이 가지고 있는 타일인지 찾는 함수
    bool IsOwned(int tileID, int myTeam)
    {
        if (MapGenerator.Instance == null) return false;
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return false;

        HexTile tile = MapGenerator.Instance.allTiles[tileID];
        return tile.ownerTeam == myTeam && !tile.isDestroyed;
    }

    bool CanAttackByBridge(HexTile target, int myTeam)
    {
        // 이 타일에 대한 다리 규칙이 없으면 false
        if (!bridgeRules.TryGetValue(target.tileID, out int requiredTileID))
            return false;

        // 짝 타일을 내가 소유하고 있는지 확인
        return IsOwned(requiredTileID, myTeam);
    }

    [PunRPC]
    public void RPC_ItemFailed_ToRequester(int actorNum, string reason)
    {
        if (PhotonNetwork.LocalPlayer.ActorNumber != actorNum) return;
        if (ItemManager.Instance != null) ItemManager.Instance.OnItemFailed(reason);
    }

    [PunRPC]
    public void RPC_ConsumeItem_ToRequester(int actorNum, int itemIndex)
    {
        if (PhotonNetwork.LocalPlayer.ActorNumber != actorNum) return;
        if (ItemManager.Instance != null)
            ItemManager.Instance.OnItemConsumedApproved((ItemManager.ItemType)itemIndex);
    }

    int DetermineWinnerByTileCount()
    {
        int countA = 0;
        int countB = 0;

        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile.ownerTeam == 1) countA++;
            else if (tile.ownerTeam == 2) countB++;
        } 

        Debug.Log($"[FINAL WINNER] A={countA} B={countB}");

        if (countA > countB) return 1;
        if (countB > countA) return 2;
        return 3;
    }

    [PunRPC]
    void RPC_SetBioWeaponBuffLocal(int untilTurn)
    {
        bioWeaponBuffActiveLocal = true;
        bioWeaponBuffUntilTurnLocal = untilTurn;

        // UI 갱신 호출 (프로젝트에 맞는 함수로)
        GameUIManager.Instance?.RefreshPlayerStatsUI();
        StatLobbyUI.Instance?.RefreshLobbyStatsText();
    }

}
