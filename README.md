# 쪼뜯따 (JJOTTDATTDA)

2인 온라인 전략 대전 게임

---

## 개발 정보

- **개발 기간**: 2026년 겨울
- **개발팀**: KAIST 카이누리 26기 겨울창작단

---

## 기술 스택

| 구분 | 기술 |
|------|------|
| **게임 엔진** | Unity |
| **네트워크** | Photon PUN (실시간 멀티플레이어) |
| **언어** | C# |

---

## 프로젝트 구조

```
Assets/
├── CS/                     # 게임 스크립트
│   ├── GameNetworkManager.cs   # 네트워크/멀티플레이어 관리
│   ├── GamePlayer.cs           # 플레이어 상태 및 행동
│   ├── GameUIManager.cs        # UI 관리
│   ├── MapGenerator.cs         # 맵 생성
│   ├── HexTile.cs              # 육각형 타일 로직
│   ├── HexGridHelper.cs        # 타일 그리드 유틸리티
│   ├── VirusCalculator.cs      # 바이러스 계산 로직
│   ├── VirusSelectUI.cs        # 바이러스 선택 UI
│   ├── ShopManager.cs          # 상점 시스템
│   ├── ItemManager.cs          # 아이템 관리
│   ├── EventManager.cs         # 게임 이벤트 처리
│   ├── MinigameSystem.cs       # 미니게임
│   └── ...
├── Scenes/                 # Unity 씬 파일
├── Resources/              # 리소스 파일
└── Photon/                 # Photon 네트워크 설정
```

---

## 실행 방법

1. Unity Hub에서 프로젝트 열기
2. `Assets/Scenes/SampleScene.unity` 씬 열기
3. Play 버튼으로 실행

---

## 팀원

| 이름 | 역할 |
|------|------|
| | |
| | |
| | |

---

## 라이선스

본 프로젝트는 KAIST 카이누리 겨울창작단 활동의 결과물입니다.
