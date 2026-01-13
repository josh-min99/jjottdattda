using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class EventManager : MonoBehaviourPunCallbacks
{
    // 싱글톤 인스턴스: 어디서든 이벤트 매니저에 접근 가능하게 함
    public static EventManager Instance;

    // 게임 내 발생 가능한 모든 이벤트 유형 정의
    public enum EventType { None, Drone, Disaster, Media, Supply, Blackout }
    
    [Header("Current Event")]
    // 현재 진행 중인 이벤트 (RPC를 통해 동기화됨)
    public EventType currentEvent = EventType.None;
    // 언론 보도 이벤트 등으로 인한 글로벌 치사율 감소 수치
    public float globalFatalityDebuff = 0f; 

    void Awake()
    {
        Instance = this;
    }

    // 라운드 시작 시 방장(Master Client)이 호출하여 무작위 이벤트를 발생시킴
    public void TriggerRandomEvent()
    {
        // 방장이 아니면 실행 불가 (중복 실행 방지)
        if (!PhotonNetwork.IsMasterClient) return;

        // 1~5 사이의 랜덤 인덱스로 이벤트를 추첨
        int rand = Random.Range(1, 6); 
        // 모든 클라이언트에게 어떤 이벤트가 발생했는지 알림
        photonView.RPC("RPC_SetEvent", RpcTarget.All, rand);
    }

    // 4라운드 특정 턴에 재난 발생 (기획서 요구사항: 4라운드 3턴 동안 각각 재난)
    public void TriggerDisasterForRound4(int turnInRound)
    {
        // 방장이 아니면 실행 불가 (중복 실행 방지)
        if (!PhotonNetwork.IsMasterClient) return;

        // 4라운드 1턴: 방역 드론
        // 4라운드 2턴: 정전 사태
        // 4라운드 3턴: 언론 보도
        EventType disasterType = EventType.None;
        switch (turnInRound)
        {
            case 1:
                disasterType = EventType.Drone; // 방역 드론
                break;
            case 2:
                disasterType = EventType.Blackout; // 정전 사태
                break;
            case 3:
                disasterType = EventType.Media; // 언론 보도
                break;
        }

        if (disasterType != EventType.None)
        {
            int eventIndex = (int)disasterType;
            photonView.RPC("RPC_SetEvent", RpcTarget.All, eventIndex);
            Debug.Log($"[4라운드 {turnInRound}턴] 재난 발생: {disasterType}");
        }
    }

    // 모든 클라이언트에서 이벤트 정보를 동기화하고 처리하는 함수
    [PunRPC]
    public void RPC_SetEvent(int eventIndex)
    {
        // 강제 가드: 라운드4만 재난/이벤트 공개 허용 (너 요구사항 기준)
        int r = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.currentRound : 1;
        if (r != 4) return;

        // 인덱스를 Enum으로 변환하여 현재 이벤트 설정
        currentEvent = (EventType)eventIndex;
        Debug.Log($"[뉴스] 이번 라운드 재난/이벤트: {currentEvent}");
        
        // 새 이벤트 시작 전 초기화 (디버프 해제, 타일 상태 리셋)
        globalFatalityDebuff = 0f;
        ResetTileStatus();
        
        // 정전 사태 디버프 해제 (새 이벤트 시작 시)
        if (GamePlayer.LocalPlayer != null)
        {
            GamePlayer.LocalPlayer.hasBlackoutDebuff = false;
        }

        // [개별 클라이언트 처리] 각 플레이어에게 개별적으로 적용되는 로직
        // 1. 보급품(Supply): 랜덤 아이템 1개 획득
        if (currentEvent == EventType.Supply)
        {
            int randomItem = Random.Range(1, 5);
            if (ItemManager.Instance != null)
                ItemManager.Instance.AcquireItem((ItemManager.ItemType)randomItem);
            Debug.Log("보급품 도착! 랜덤 아이템을 획득했습니다.");
        }
        // 2. 정전(Blackout): 감염 가능 타일 -1 (기획서 요구사항)
        else if (currentEvent == EventType.Blackout)
        {
            if (GamePlayer.LocalPlayer != null)
            {
                GamePlayer.LocalPlayer.ApplyBlackoutEffect(20f, 0.5f);
                Debug.Log("정전 사태 발생! 전염률 +20, 치명률 1/2 적용 (턴 종료 후 복구).");
            }
        }

        // [마스터 클라이언트 처리] 맵 전체 상태 변경 등 권한이 필요한 처리
        if (PhotonNetwork.IsMasterClient)
        {
            // 이벤트 종류에 따른 맵 로직 적용 (드론, 재난 등)
            ApplyEventLogic(currentEvent);
        }
    }

    // 타일의 일시적 상태(예: 무적)를 초기화하는 함수
    void ResetTileStatus()
    {
        // 방역 드론 효과를 영구 유지하도록 초기화에서 면역 상태를 지우지 않음.
        return;
    }

    // 마스터 클라이언트가 이벤트에 따라 타일 상태 변경을 지시하는 함수
    void ApplyEventLogic(EventType type)
    {
        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles.Count == 0) return;

        // 모든 타일 ID 리스트 가져오기
        List<int> allTileIDs = new List<int>(MapGenerator.Instance.allTiles.Keys);

        switch (type)
        {
            case EventType.Drone: // 방역 드론: 특정 타일 4곳 감염 불가 (기획서 요구사항)
                int count = Mathf.Min(4, allTileIDs.Count);
                for(int i = 0; i < count; i++)
                {
                    int id = allTileIDs[Random.Range(0, allTileIDs.Count)];
                    photonView.RPC("RPC_ApplyTileEvent", RpcTarget.All, id, "Immune", 0);
                    allTileIDs.Remove(id); // 중복 방지
                }
                Debug.Log($"[방역 드론] {count}개 타일 감염 불가 설정");
                break;

            case EventType.Disaster: // 자연재해: 랜덤 1개 타일 파괴 (중립화)
                int destId = allTileIDs[Random.Range(0, allTileIDs.Count)];
                photonView.RPC("RPC_ApplyTileEvent", RpcTarget.All, destId, "Destroy", 0);
                break;
            
            case EventType.Media: // 언론 보도: 전역 치사율 디버프 설정
                photonView.RPC("RPC_SetGlobalDebuff", RpcTarget.All, 15.0f);
                break;
            
            // Supply, Blackout는 RPC_SetEvent에서 별도로 처리됨
        }
    }

    // 특정 타일의 데이터 변경을 모든 클라이언트에 적용하는 핵심 RPC
    [PunRPC]
    public void RPC_ApplyTileEvent(int tileID, string type, int value)
    {
        if (MapGenerator.Instance.allTiles.ContainsKey(tileID))
        {
            HexTile tile = MapGenerator.Instance.allTiles[tileID];

            // 한강타일이면 모든 이벤트 무시
            if (tile.type == HexTile.TileType.WATER) return;

            // 1. 방어막(Immune) 상태 체크
            // 파괴, 생화학 공격은 방어막이 있으면 무효화됨
            if (tile.isImmune && (type == "Destroy" || type == "BioAttack")) 
            {
                Debug.Log("방어막(Immune)으로 인해 효과가 무효화되었습니다.");
                return;
            }

            // 2. 이벤트 타입별 타일 상태 변경
            if (type == "Immune") // 무적 설정
            {
                tile.isImmune = true;
                tile.UpdateVisuals(tile.ownerTeam, tile.population);
            }
            else if (type == "Heal") // 인구 회복 (또는 감소)
            {
                tile.population += value; 
                if (tile.population < 0) tile.population = 0;
                tile.UpdateVisuals(tile.ownerTeam, tile.population);
            }
            else if (type == "Destroy") // 타일 파괴 (주인 없음, 인구 0)
            {
                tile.isDestroyed = true;
                tile.ownerTeam = 0;
                tile.population = 0;
                tile.UpdateVisuals(0, 0);
            }
            else if (type == "BioAttack") // 생화학 공격 (아이템 효과)
            {
                // 생화학 무기 계수를 적용하여 데미지 계산
                int damage = Mathf.RoundToInt(value * tile.bioWeaponMultiplier);
                tile.population -= damage;
                
                // 인구가 0 이하가 되면 점령 상태 해제
                if (tile.population <= 0) 
                {
                    tile.population = 0;
                    tile.ownerTeam = 0;
                }
                Debug.Log($"생화학 공격! {damage} 피해 적용됨.");
                tile.UpdateVisuals(tile.ownerTeam, tile.population);
            }
        }
    }

    // 전역 치사율 디버프 수치를 동기화하는 RPC
    [PunRPC]
    public void RPC_SetGlobalDebuff(float value)
    {
        globalFatalityDebuff = value;
        Debug.Log($"언론 보도로 인해 치사율이 {value}% 감소합니다.");
    }
}
