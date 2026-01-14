using UnityEngine;
using TMPro;
using Photon.Pun; 
using System.Collections.Generic;
using UnityEngine.UI;

public class HexTile : MonoBehaviour
{
    public static HexTile currentSelectedTile;
    public enum TileType { GROUND, WATER }

    [Header("Data")]
    // 타일의 속성
    public TileType type;
    // 타일의 고유 식별 번호 (0 ~ N)
    public int tileID;          
    // 타일 소유 팀 (0: 중립, 1: Red, 2: Blue, 3: AI/Virus)
    public int ownerTeam = 0;   
    // 현재 타일의 인구 수
    public int population = 0;
    // 이전 인구 수 (사망자 계산용)
    private int previousPopulation = 0;
    // 누적 사망자 수
    private int cumulativeDeaths = 0;  
    // 육각형 그리드 상의 X, Y 좌표
    public int gridX;           
    public int gridY;           
    
    [Header("Status Flags")]
    // 청정 구역 여부 (점령 시 보너스, 치사율 반감)
    public bool isCleanZone = false;
    // 잠복(Ambush) 상태 여부 (적에게는 중립처럼 보임)
    public bool isHidden = false;           
    // 치사율 증폭 배율 (기본 1.0)
    public float fatalityMultiplier = 1.0f; 
    // 생화학 무기 전용 배율 (ItemManager와 연동)
    public float bioWeaponMultiplier = 1.0f; 

    [Header("Event Flags")]
    // 방역 드론 효과 (공격 면역/무적 상태)
    public bool isImmune = false;    
    // 재난(폭우/산불)으로 인한 파괴 여부 (사용 불가)
    public bool isDestroyed = false; 

    // 다리로 연결된 타일들의 ID 목록
    public List<int> bridgeConnectedTileIDs = new List<int>();

    [Header("Vaccine Restore")]
    public int lastOwnerTeam = 0;   // 감염되기 전 소유팀

    [Header("Visual Components")]
    // 타일 색상을 변경할 스프라이트 렌더러
    public SpriteRenderer sprRenderer;
    // 인구 수를 표시할 텍스트 메쉬 (UI용)
    public Text textPopulation;

    [Header("Colors")]
    public Color colorNeutral;         // 중립 (흰색)
    public Color colorTeamA = new Color(1f, 0.5f, 0.5f); // 1팀 (빨강 계열)
    public Color colorTeamB = new Color(0.5f, 0.5f, 1f); // 2팀 (보라 계열)
    public Color colorClean = new Color(0.5f, 1f, 0.5f); // 청정 구역 (초록)
    public Color colorImmune = Color.cyan;               // 무적 상태 (하늘색)
    public Color colorDestroyed = Color.black;           // 파괴됨 (검정)
    public Color colorSelect;                            // 선택됨 (회색)
    public Color testColor;                              // 테스트 컬러
    // 필요 시 AI(Team 3) 색상 추가 가능 (예: 보라색)

    public bool hasVaccineMark = false;
    public Color colorVaccine = new Color(0.5f, 1f, 1f); // 예: 민트/하늘

    public int lastAppliedTurn = -1;

    public int hiddenUntilRound = -1; // 이 라운드 전까지 숨김 유지(예: 4면 3라운드까지 숨김)
    public int hiddenUntilTurn = -1;  // 이 턴 전까지 숨김 유지

    void Awake()
    {
        // 컴포넌트 자동 할당 (누락 방지)
        if (sprRenderer == null) sprRenderer = GetComponent<SpriteRenderer>();
        if (textPopulation == null && type == TileType.GROUND) textPopulation = GetComponentInChildren<Text>();
    }

    private void Start()
    {        
        colorNeutral = sprRenderer.color;        

        // 물이면
        if (type == TileType.WATER)
        {
            population = 0;
        }
    }

    private void Update()
    {
        if (type == TileType.GROUND && textPopulation != null)
            textPopulation.text = population.ToString();
    }

    // ★ [핵심 기능] 타일 클릭 시 상호작용 처리
    void OnMouseDown()
    {
        // 1. 파괴된 타일은 상호작용 불가
        if (isDestroyed) return;
        // 2. 한강 타일 상호작용 불가
        if (type == TileType.WATER) return;

        //// 2. 아이템 사용 모드일 경우 아이템 로직 우선 처리
        //if (ItemManager.Instance != null && ItemManager.Instance.selectedItem != ItemManager.ItemType.None)
        //{
        //    // 아이템 사용에 성공했다면 이후 로직 건너뜀
        //    bool used = ItemManager.Instance.TryUseItemOnTile(this);
        //    if (used) return;
        //}

        // 3. 타일 정보 UI에 표시 (상시 표시)
        if (TileInfoUI.Instance != null)
        {
            TileInfoUI.Instance.ShowTileInfo(this);
        }

        // 4. 일반 클릭 (정보 확인용 로그)
        // 실제 게임 로직(공격 등)은 GameNetworkManager의 Update()에서 Raycast로 처리됨
        Debug.Log($"[일반 클릭] 타일 ID: {tileID}, 인구: {population}");

        if (currentSelectedTile != null && currentSelectedTile != this )
        {
            currentSelectedTile.Deselect();
        }
        currentSelectedTile = this;

        Select();
    }

    // 서버 데이터를 기반으로 타일의 외형(색상, 텍스트)을 갱신하는 함수
    public void UpdateVisuals(int team, int pop)
    {
        if (type == TileType.WATER) return;
        ownerTeam = team;
        
        // 이전 인구수 저장 (초기화 시)
        if (previousPopulation == 0)
        {
            previousPopulation = pop;
        }
        
        population = pop;

        // 인구 수 및 사망자 수 텍스트 갱신 (상시 표시)
        UpdatePopulationText();

        if (hasVaccineMark)
        {
            sprRenderer.color = colorVaccine;
            return;
        }

        if (sprRenderer != null)
        {
            // [우선순위 1] 파괴된 타일 (최우선 표시)
            if (isDestroyed)
            {
                sprRenderer.color = colorDestroyed; // 검은색
                UpdatePopulationText(); // X 표시
                return; // 이후 색상 로직 무시
            }

            // [우선순위 2] 방역 드론 (무적 상태)
            if (isImmune)
            {
                sprRenderer.color = colorImmune; // 하늘색
                return;
            }

            // --- 팀 색상 및 잠복 로직 ---

            // 현재 로컬 플레이어의 팀 확인
            int myTeam = 0;
            if (GamePlayer.LocalPlayer != null) 
                myTeam = GamePlayer.LocalPlayer.myTeam;

            // [우선순위 3] 잠복(Ambush) 로직
            // 주인이 있고(중립 아님) + 잠복 상태이며 + 내 땅이 아닐 경우 -> 중립/청정 구역으로 위장
            if (ownerTeam != 0 && isHidden && ownerTeam != myTeam)
            {
                sprRenderer.color = isCleanZone ? colorClean : colorNeutral;
                // (기획에 따라 여기서 인구 수를 '?'로 숨기는 로직 추가 가능)
            }
            else
            {
                // [우선순위 4] 정상적인 소유권 색상 표시
                switch (ownerTeam)
                {
                    case 1: sprRenderer.color = colorTeamA; break;
                    case 2: sprRenderer.color = colorTeamB; break;
                    case 3: sprRenderer.color = testColor; break;
                    // AI(C형 바이러스)가 Team 3를 쓴다면 여기에 case 3 추가 필요
                    default: sprRenderer.color = isCleanZone ? colorClean : colorNeutral; break;
                }
            }
        }
    }

    [PunRPC]
    public void RPC_SetTileVaccineMark(int tileID, bool on)
    {
        if (!MapGenerator.Instance.allTiles.ContainsKey(tileID)) return;
        var tile = MapGenerator.Instance.allTiles[tileID];
        tile.hasVaccineMark = on;
        tile.UpdateVisuals(tile.ownerTeam, tile.population);
    }

    // 인구수 텍스트 업데이트 (타일 위에 표시)
    void UpdatePopulationText()
    {
        if (textPopulation != null)
        {
            if (isDestroyed)
            {
                textPopulation.text = "X";
            }
            else
            {
                // 타일 위에는 인구수만 표시
                textPopulation.text = population.ToString();
            }
        }
    }
    
    // 사망자 수를 추가하는 함수 (외부에서 호출)
    public void AddDeaths(int deathCount)
    {
        if (deathCount > 0)
        {
            cumulativeDeaths += deathCount;
            UpdatePopulationText(); // 텍스트 즉시 업데이트
        }
    }
    
    // 누적 사망자 수 가져오기
    public int GetCumulativeDeaths()
    {
        return cumulativeDeaths;
    }

    // 타일 선택
    public void Select()
    {
        sprRenderer.color = colorSelect;
    }

    // 타일 선택해제
    public void Deselect()
    {
        UpdateVisuals(ownerTeam, population);
    }
}
