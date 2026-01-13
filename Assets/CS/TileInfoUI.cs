using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class TileInfoUI : MonoBehaviour
{
    // 싱글톤 인스턴스
    public static TileInfoUI Instance;

    [Header("Tile Info Texts (상시 표시)")]
    public TextMeshProUGUI textTileID;        // 타일 ID
    public TextMeshProUGUI textPopulation;    // 현재 인구수
    public TextMeshProUGUI textDeaths;        // 누적 사망자 수
    public TextMeshProUGUI textOwnerTeam;     // 소유 팀
    public TextMeshProUGUI textStatus;        // 상태 (청정구역, 무적 등)

    // 현재 표시 중인 타일
    private HexTile currentTile = null;
    // 타일별 누적 사망자 수 추적
    private Dictionary<int, int> tileDeaths = new Dictionary<int, int>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 시작 시 기본 메시지 표시
        UpdateTileInfo();
    }

    // 타일 정보 표시 (타일 클릭 시 호출)
    public void ShowTileInfo(HexTile tile)
    {
        if (tile == null)
        {
            // 타일이 없으면 기본 메시지 표시
            ClearTileInfo();
            return;
        }

        currentTile = tile;
        UpdateTileInfo();
    }
    
    // 정보 초기화 (타일이 선택되지 않았을 때)
    void ClearTileInfo()
    {
        if (textTileID != null) textTileID.text = "타일 ID: -";
        if (textPopulation != null) textPopulation.text = "현재 인구: -";
        if (textDeaths != null) textDeaths.text = "누적 사망자: -";
        if (textOwnerTeam != null) 
        {
            textOwnerTeam.text = "소유: -";
            textOwnerTeam.color = Color.white;
        }
        if (textStatus != null) textStatus.text = "상태: -";
    }

    // 타일 정보 업데이트
    void UpdateTileInfo()
    {
        if (currentTile == null) return;

        // 타일 ID
        if (textTileID != null)
            textTileID.text = $"타일 ID: {currentTile.tileID}";

        // 현재 인구수
        if (textPopulation != null)
            textPopulation.text = $"현재 인구: {currentTile.population}명";

        // 누적 사망자 수
        int deaths = GetTileDeaths(currentTile.tileID);
        if (textDeaths != null)
        {
            if (deaths > 0)
                textDeaths.text = $"누적 사망자: {deaths}명";
            else
                textDeaths.text = "누적 사망자: 0명";
        }

        // 소유 팀
        if (textOwnerTeam != null)
        {
            string teamName = "중립";
            Color teamColor = Color.white;
            
            switch (currentTile.ownerTeam)
            {
                case 1:
                    teamName = "TEAM A (빨강)";
                    teamColor = Color.red;
                    break;
                case 2:
                    teamName = "TEAM B (파랑)";
                    teamColor = Color.blue;
                    break;
                case 3:
                    teamName = "C형 바이러스";
                    teamColor = Color.magenta;
                    break;
            }
            
            textOwnerTeam.text = $"소유: {teamName}";
            textOwnerTeam.color = teamColor;
        }

        // 상태 정보
        if (textStatus != null)
        {
            string status = "";
            if (currentTile.isDestroyed)
                status += "파괴됨 ";
            if (currentTile.isImmune)
                status += "무적 ";
            if (currentTile.isCleanZone)
                status += "청정구역 ";
            if (currentTile.isHidden)
                status += "잠복 ";

            if (string.IsNullOrEmpty(status))
                status = "정상";
            
        if (textStatus != null) textStatus.text = "상태: -";
        }
    }


    // 타일의 누적 사망자 수 가져오기
    public int GetTileDeaths(int tileID)
    {
        if (tileDeaths.ContainsKey(tileID))
            return tileDeaths[tileID];
        return 0;
    }

    // 타일의 사망자 수 추가 (인구 감소 시 호출)
    public void AddTileDeaths(int tileID, int deathCount)
    {
        if (!tileDeaths.ContainsKey(tileID))
            tileDeaths[tileID] = 0;
        
        tileDeaths[tileID] += deathCount;

        // 현재 표시 중인 타일이면 정보 업데이트
        if (currentTile != null && currentTile.tileID == tileID)
        {
            UpdateTileInfo();
        }
    }

    // 타일 정보 실시간 업데이트 (외부에서 호출)
    public void RefreshCurrentTile()
    {
        if (currentTile != null)
        {
            UpdateTileInfo();
        }
    }
}

