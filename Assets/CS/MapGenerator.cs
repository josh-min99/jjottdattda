using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class MapGenerator : MonoBehaviour
{
    // 싱글톤 인스턴스: 다른 스크립트에서 맵 데이터에 접근하기 위함
    public static MapGenerator Instance;

    [Header("Settings")]
    public GameObject hexTilePrefab; // 생성할 육각 타일 프리팹
    public GameObject tileMapPrefab; // 맵 자체 프리팹
    public Transform mapHolder; // 생성된 타일들이 들어갈 부모 객체 (Hierarchy 정리용)
    
    [Header("Target Settings")]
    // 목표 타일 개수 (이 개수에 맞춰 해상도를 자동 조절함)
    public int targetTileCount = 175;

    [Header("Map Image")]
    public Texture2D mapImage;              // 맵의 형태를 결정할 소스 이미지 (예: 지도 그림)
    public Color targetColor = Color.blue;  // 이미지에서 땅으로 인식할 색상
    [Range(0f, 1f)] public float colorThreshold = 0.3f; // 색상 허용 오차 (유사 색상 인식 범위)

    [Header("Camera Setting")]
    public bool autoFitCamera = true; // 맵 생성 후 카메라를 자동으로 맞출지 여부
    [Range(0.5f, 3.0f)]
    [Tooltip("카메라 줌 설정 시 여백 (값이 클수록 화면에 여백이 많아짐)")]
    public float padding = 2.0f;      // 카메라 줌 설정 시 여백 (기본값 증가)

    [Header("Initial Infection")]
    // 인스펙터에서 미리 넣기
    [Header("Fixed Spawn Tiles")]
    public int team1FixedSpawnTileID = 102;
    public int team2FixedSpawnTileID = 38;

    // 랜덤으로 몇 개 감염시킬지
    public int initialInfectionCount = 1;
    // 3을 C바이러스/AI로 쓸거면 3, 아니면 0/1/2
    public int initialInfectionTeam = 3;

    [Header("Tile Scale")]
    [Range(0.1f, 2.0f)]
    [Tooltip("타일 크기 배율 (1.0 = 기본 크기, 0.5 = 절반 크기)")]
    public float tileScale = 1.0f;    // 타일 크기 조절 (Inspector에서 조정 가능)

    [Header("Bridge Settings")]
    public Material bridgeMaterial;   // 다리 렌더링에 사용할 재질 (LineRenderer용)
    public float bridgeWidth = 0.15f; // 다리의 두께
    
    [Header("Starting Positions")]
    [Tooltip("팀 1(마포구)의 시작 타일 ID (7개 타일 중 가운데 타일)")]
    public int team1StartTileID = -1;
    
    [Tooltip("팀 2(강남구)의 시작 타일 ID (7개 타일 중 가운데 타일)")]
    public int team2StartTileID = -1;
    
    [Header("Bridge Connections")]
    [Tooltip("다리로 연결할 타일 쌍 (각 쌍은 두 타일 ID를 가짐)")]
    public List<BridgePair> bridgePairs = new List<BridgePair>();
    
    [Tooltip("다리 연결 그룹 (한 타일에서 여러 타일로 연결 가능)")]
    public List<BridgeGroup> bridgeGroups = new List<BridgeGroup>();    

    [System.Serializable]
    public class BridgePair
    {
        [Tooltip("첫 번째 타일 ID")]
        public int tileID_A;
        
        [Tooltip("두 번째 타일 ID")]
        public int tileID_B;
    }
    
    [System.Serializable]
    public class BridgeGroup
    {
        [Tooltip("중심 타일 ID (이 타일에서 다른 타일들로 연결)")]
        public int centerTileID;
        
        [Tooltip("연결할 타일 ID 리스트 (여러 개 가능)")]
        public List<int> connectedTileIDs = new List<int>();
    }

    // 생성된 모든 타일을 관리하는 딕셔너리 (Key: TileID, Value: HexTile)
    public Dictionary<int, HexTile> allTiles = new Dictionary<int, HexTile>();
    
    // 맵 생성 로직 내부에서 사용하는 임시 불리언 맵 (땅인지 바다인지 판별)
    private bool[,] boolMap;

    void Awake()
    {
        Instance = this;
    }

    // 외부에서 맵 생성을 요청할 때 호출
    public void GenerateMap()
    {
        AutoGenerate();
    }

    // 에디터 컨텍스트 메뉴에서도 실행 가능
    [ContextMenu("Auto Generate Map")]
    public void AutoGenerate()
    {
        HexTile.currentSelectedTile = null;

        GameObject map = Instantiate(tileMapPrefab, mapHolder);
        allTiles.Clear();

        HexTile[] tiles = map.GetComponentsInChildren<HexTile>();

        foreach (HexTile tile in tiles)
        {
            int id = tile.tileID;

            if (!allTiles.ContainsKey(id))
                allTiles.Add(id, tile);
            else
                Debug.LogWarning("중복 타일 발견");
        }

        foreach (var t in allTiles.Values)
        {
            t.isCleanZone = false;
            t.isHidden = false;
            t.isImmune = false;
            t.isDestroyed = false;

            t.hasVaccineMark = false;
            t.bioWeaponMultiplier = 1.0f;

            t.ownerTeam = 0;
            t.lastOwnerTeam = 0;

            if (t.type == HexTile.TileType.GROUND)
                t.UpdateVisuals(0, t.population);
        }        

        FitCameraToGrid();
    }

    // [다리 시스템] 고정된 다리를 설치하는 함수
    // Inspector에서 지정한 다리 쌍들과 다리 그룹들을 연결
    void GenerateFixedBridges()
    {
        if (allTiles.Count == 0) return;
        
        int bridgeCount = 0;
        
        // 1. Inspector에서 지정한 다리 쌍들을 연결 (기존 방식)
        foreach (var pair in bridgePairs)
        {
            if (pair.tileID_A >= 0 && pair.tileID_B >= 0)
            {
                if (allTiles.ContainsKey(pair.tileID_A) && allTiles.ContainsKey(pair.tileID_B))
                {
                    CreateBridge(pair.tileID_A, pair.tileID_B);
                    bridgeCount++;
                }
                else
                {
                    Debug.LogWarning($"다리 쌍 연결 실패: 타일 ID {pair.tileID_A} 또는 {pair.tileID_B}가 존재하지 않습니다.");
                }
            }
        }
        
        // 2. Inspector에서 지정한 다리 그룹들을 연결 (새로운 방식: 한 타일에서 여러 타일로)
        foreach (var group in bridgeGroups)
        {
            if (group.centerTileID >= 0 && allTiles.ContainsKey(group.centerTileID))
            {
                foreach (int connectedID in group.connectedTileIDs)
                {
                    if (connectedID >= 0 && allTiles.ContainsKey(connectedID))
                    {
                        CreateBridge(group.centerTileID, connectedID);
                        bridgeCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"다리 그룹 연결 실패: 타일 ID {connectedID}가 존재하지 않습니다.");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"다리 그룹 중심 타일 실패: 타일 ID {group.centerTileID}가 존재하지 않습니다.");
            }
        }
        
        Debug.Log($"고정 다리 건설 완료: {bridgeCount}개");
    }

    // [다리 시스템] 두 타일 사이에 다리를 생성하는 핵심 함수 (논리 + 시각)
    public void CreateBridge(int tileID_A, int tileID_B)
    {
        // 유효하지 않은 타일 ID면 중단
        if (!allTiles.ContainsKey(tileID_A) || !allTiles.ContainsKey(tileID_B)) return;

        HexTile tileA = allTiles[tileID_A];
        HexTile tileB = allTiles[tileID_B];

        // 1. [논리적 연결] 각 타일의 연결 리스트에 상대방 ID 등록
        // 이를 통해 게임 로직(이동, 공격 범위 등)에서 이웃으로 인식하게 됨
        if (!tileA.bridgeConnectedTileIDs.Contains(tileID_B))
            tileA.bridgeConnectedTileIDs.Add(tileID_B);

        if (!tileB.bridgeConnectedTileIDs.Contains(tileID_A))
            tileB.bridgeConnectedTileIDs.Add(tileID_A);

        // 2. [시각적 표현] 화면에 다리(선)를 그림
        DrawBridgeVisual(tileA.transform.position, tileB.transform.position);
    }

    // [다리 시스템] LineRenderer를 사용하여 다리를 시각화
    void DrawBridgeVisual(Vector3 startPos, Vector3 endPos)
    {
        GameObject bridgeObj = new GameObject("Bridge");
        bridgeObj.transform.SetParent(this.mapHolder); // 하이어라키 정리

        LineRenderer lr = bridgeObj.AddComponent<LineRenderer>();
        
        // 재질 설정 (할당되지 않았다면 마젠타색으로 보일 수 있음)
        if (bridgeMaterial != null) lr.material = bridgeMaterial;
        
        // 다리 색상 설정 (현재는 갈색 계열)
        lr.startColor = new Color(0.5f, 0.25f, 0f); 
        lr.endColor = new Color(0.5f, 0.25f, 0f);
        
        // 두께 및 정점 설정
        lr.startWidth = bridgeWidth;
        lr.endWidth = bridgeWidth;
        lr.positionCount = 2;
        lr.SetPosition(0, startPos);
        lr.SetPosition(1, endPos);
        
        // 2D 뷰에서 타일 뒤에 가려지지 않도록 Z축을 약간 앞으로 당김
        lr.transform.position = new Vector3(0, 0, -0.1f);
        lr.useWorldSpace = true;
    }

    // 목표 타일 수(targetTileCount)에 가장 가까운 결과를 내는 해상도(width)를 탐색
    int FindOptimalResolution()
    {
        int minW = 10;
        int maxW = 100;
        int bestW = minW;
        int minDiff = int.MaxValue;

        // 최소~최대 너비 범위 내에서 시뮬레이션을 돌려 오차가 가장 적은 값을 찾음
        for (int w = minW; w <= maxW; w++)
        {
            int count = SimulateTileCount(w);
            int diff = Mathf.Abs(count - targetTileCount);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestW = w;
            }
        }
        return bestW;
    }

    // 특정 너비(width)일 때 생성될 타일의 개수를 미리 계산해보는 함수
    int SimulateTileCount(int width)
    {
        float ratio = (float)mapImage.height / mapImage.width;
        int height = Mathf.RoundToInt(width * ratio);
        int count = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (CheckColor(x, y, width, height)) count++;
            }
        }
        return count;
    }

    // 실제 맵 생성 및 타일 배치 로직
    void GenerateMapInternal(int width)
    {
        // 기존에 생성된 맵 오브젝트들 제거 (초기화)
        List<GameObject> children = new List<GameObject>();
        foreach (Transform child in mapHolder) children.Add(child.gameObject);
        foreach (GameObject child in children)
        {
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        
        allTiles.Clear();

        // 비율에 따른 높이 계산
        float ratio = (float)mapImage.height / mapImage.width;
        int height = Mathf.RoundToInt(width * ratio);

        // 1. 이미지 색상 체크를 통해 불리언 맵 생성 (True=땅, False=바다)
        boolMap = new bool[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                boolMap[x, y] = CheckColor(x, y, width, height);
            }
        }

        // 2. 작은 구멍 메우기 (맵 품질 보정)
        FillHoles(width, height);

        // 육각형 타일 배치를 위한 오프셋 설정 (스케일에 따라 조정)
        float xOffset = 0.88f * tileScale;
        float yOffset = 0.76f * tileScale;

        // 맵 중앙 정렬을 위한 시작 좌표 계산
        float startX = -(width * xOffset) / 2f;
        float startY = -(height * yOffset) / 2f;
        int idCounter = 0;

        // 3. 실제 타일 인스턴스화
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (boolMap[x, y])
                {
                    float xPos = x * xOffset;
                    float yPos = y * yOffset;
                    // 육각형 그리드는 홀수 행마다 X축으로 반 칸씩 밀림 (지그재그)
                    if (y % 2 != 0) xPos += xOffset / 2f;

                    Vector3 pos = new Vector3(startX + xPos, startY + yPos, 0);
                    GameObject obj = Instantiate(hexTilePrefab, pos, Quaternion.identity, mapHolder);
                    obj.name = $"Tile_{idCounter}";
                    
                    // 타일 크기 조절
                    if (tileScale != 1.0f)
                    {
                        obj.transform.localScale = Vector3.one * tileScale;
                    }

                    // HexTile 컴포넌트 설정
                    HexTile tile = obj.GetComponent<HexTile>();
                    tile.tileID = idCounter;
                    tile.gridX = x; 
                    tile.gridY = y;
                    
                    // 초기 인구를 10~50 사이로 랜덤 설정
                    tile.UpdateVisuals(0, Random.Range(10, 50));

                    allTiles.Add(idCounter, tile);
                    idCounter++;
                }
            }
        }

        Debug.Log($"맵 생성 완료: 타일 {allTiles.Count}개");

        // 카메라 자동 맞춤 기능
        if (autoFitCamera && Camera.main != null)
        {
            FitCameraToGrid();
        }
    }

    // 생성된 맵 전체가 화면에 들어오도록 카메라 줌(Orthographic Size) 조절 
    void FitCameraToGrid()
    {
        if (allTiles.Count == 0) return;

        // 맵의 경계(Bounds) 계산
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var tile in allTiles.Values)
        {
            Vector3 pos = tile.transform.position;
            if (pos.x < minX) minX = pos.x;
            if (pos.x > maxX) maxX = pos.x;
            if (pos.y < minY) minY = pos.y;
            if (pos.y > maxY) maxY = pos.y;
        }

        // padding을 더 크게 적용하여 여백 증가
        float mapWidth = maxX - minX + (padding * 2f);
        float mapHeight = maxY - minY + (padding * 2f);

        // 카메라 위치를 맵의 중앙으로 이동
        Vector3 center = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, -10f);
        Camera.main.transform.position = center;

        // 화면 비율에 맞춰 줌 크기 조절
        float screenRatio = Camera.main.aspect;
        float targetRatio = mapWidth / mapHeight;

        if (screenRatio >= targetRatio)
        {
            // 화면이 더 넓으면 높이 기준으로 맞춤
            Camera.main.orthographicSize = mapHeight / 2f;
        }
        else
        {
            // 맵이 더 넓으면 너비 기준으로 맞춤 (비율 보정)
            float differenceInSize = targetRatio / screenRatio;
            Camera.main.orthographicSize = mapHeight / 2f * differenceInSize;
        }
    }

    // 이미지의 특정 픽셀이 목표 색상(targetColor)과 유사한지 판별
    bool CheckColor(int x, int y, int w, int h)
    {
        float u = (float)x / w;
        float v = (float)y / h;
        // Bilinear 필터링으로 부드럽게 색상을 가져옴
        Color pixelColor = mapImage.GetPixelBilinear(u, v);
        return IsSimilarColor(pixelColor, targetColor, colorThreshold);
    }

    // 고립된 빈 공간(구멍)을 메우는 보정 함수
    void FillHoles(int w, int h)
    {
        bool[,] newMap = (bool[,])boolMap.Clone();
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                // 현재 위치가 빈 공간인데 상하좌우가 대부분 땅이라면 땅으로 채움
                if (!boolMap[x, y])
                {
                    int neighborCount = 0;
                    if (boolMap[x + 1, y]) neighborCount++;
                    if (boolMap[x - 1, y]) neighborCount++;
                    if (boolMap[x, y + 1]) neighborCount++;
                    if (boolMap[x, y - 1]) neighborCount++;
                    
                    if (neighborCount >= 3) newMap[x, y] = true;
                }
            }
        }
        boolMap = newMap;
    }

    // 두 색상의 RGB 차이가 임계값(Threshold) 이내인지 확인
    bool IsSimilarColor(Color a, Color b, float threshold)
    {
        float diffR = Mathf.Abs(a.r - b.r);
        float diffG = Mathf.Abs(a.g - b.g);
        float diffB = Mathf.Abs(a.b - b.b);
        return (diffR < threshold && diffG < threshold && diffB < threshold);
    }

}