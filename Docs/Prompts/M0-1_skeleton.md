# [M0-1] 프로젝트 골격 — 패키지 · 폴더 · 씬 · 호스트 부트스트랩 · 튜닝 에셋 · 디버그 오버레이

너는 이 Unity 프로젝트(`C:\Users\Smin\Programming\Peak`)의 **워커 세션**이다. 아래 범위만 구현하고, 범위 밖은 건드리지 않는다.

## 먼저 읽을 것 (순서대로)
1. `CLAUDE.md`
2. `Docs/SUMMARY.md`
3. `Docs/201_common.md` — 전체 (특히 1·2·3·4·6·8장)
4. `Docs/205_network.md` 1·2·6장
5. `Docs/204_ui.md` 4장 (디버그 오버레이)
6. `Docs/202_gameplay.md` 1장 (입력 매핑), 9장 (튜닝 초기값)
7. `Docs/300_roadmap.md` M0 항목

## 목표 (한 문장)
어느 씬에서 플레이를 눌러도 Boot 가 자동 로드되어 NGO 호스트로 뜨고, Lobby → [싱글 시작] → Game 씬으로 넘어가며, 콘솔 에러 0 인 **빈 골격**을 만든다. 플레이어·등반·생성은 이 프롬프트 범위가 아니다 (M0-2, M1, M2).

## 범위 — 만들 것

### 1. 패키지 정리 (`Packages/manifest.json`)
- 추가: `com.unity.netcode.gameobjects`, `com.unity.services.multiplayer`, `com.unity.multiplayer.playmode`, `com.unity.cinemachine`(3.x). `com.unity.transport` 는 NGO 의존으로 따라오면 명시 불필요. 버전은 Unity 6000.0 에서 Package Manager 가 제공하는 최신 검증 버전
- 제거: `com.unity.ai.assistant`, `com.unity.ai.inference`, `com.unity.visualscripting`, `com.unity.timeline`, `com.unity.collab-proxy`, `com.unity.multiplayer.center`
- **유지**: `com.coplaydev.unity-mcp` 와 그 부트스트랩 `Assets/Editor/McpAutoStartBootstrap.cs`, 루트 `.mcp.json` — 절대 건드리지 않는다
- 패키지 변경 후 컴파일 에러 0 확인. Cinemachine 3 의 Timeline 의존 경고가 나오면 보고에 적는다 (Timeline 을 되살리지 말 것)

### 2. 템플릿 잔재 정리
- 삭제: `Assets/TutorialInfo/`, `Assets/Readme.asset`, `Assets/Scenes/SampleScene.unity` (새 씬을 만든 뒤)
- `Assets/InputSystem_Actions.inputactions` → `Assets/_Project/Input/PeakActions.inputactions` 로 **이동 후 재정의** (아래 6번)
- `Assets/Settings/` (URP 에셋) 는 그대로 둔다. Graphics 설정이 `PC_RPAsset` 을 쓰는지만 확인

### 3. 폴더 + asmdef (`Docs/201_common.md` 3장 그대로)
- `Assets/_Project/{Scenes, Scripts/Runtime, Scripts/Editor, Scripts/Tests, Prefabs, Data, Input, Art, UI}` + Runtime 하위 `Core, Player, Stamina, ProcGen, World, Items, Network, UI, Visual` (빈 폴더는 `.gitkeep` 대신 이번에 만드는 파일로 채워지는 곳만)
- asmdef 3개: `Peak.Runtime` (참조: `Unity.Netcode.Runtime`, `Unity.InputSystem`, `Unity.Cinemachine`, `Unity.TextMeshPro`), `Peak.Editor` (Editor 전용, 참조 `Peak.Runtime`), `Peak.Tests` (Test Assemblies, 참조 `Peak.Runtime`)
- 네임스페이스 = 폴더 (`Peak.Core`, `Peak.Network` …)

### 4. 씬 5개 (`Assets/_Project/Scenes/`) — **에디터 스크립트로 생성** (재현 가능하게)
`Peak.Editor` 에 메뉴 `Peak > Setup > Rebuild M0 Scenes` 를 만들고, 실행하면 아래 씬을 코드로 (재)생성한다. 멱등이어야 한다 (두 번 실행해도 같은 결과). 손으로 배치하지 않는다.

| 씬 | 내용 |
|---|---|
| `Boot` | `NetworkManager`(+`UnityTransport`, 127.0.0.1:7777), `GameManager`, `SceneFlow`, `DebugOverlay`, `EventSystem`. 전부 한 루트 오브젝트 `[Boot]` 아래, `DontDestroyOnLoad` |
| `Lobby` | Canvas: 제목 텍스트 "Peak", 시드 입력 필드(기본 1), [싱글 시작] 버튼, [종료] 버튼 |
| `Game` | 임시 지면: 200×200 m 평면(Plane 스케일) 레이어 `Terrain`, `SpawnPoint` 빈 오브젝트 (0, 1, 0), Directional Light, `Bootstrapper` |
| `Sandbox_Climb` | 지면 50×50 + `SpawnPoint` + `Bootstrapper`. 벽은 M1 이 채운다 |
| `Sandbox_ProcGen` | `SpawnPoint` + `Bootstrapper` 만 |

- Build Settings 씬 목록: Boot(0), Lobby(1), Game(2). Sandbox 두 개는 **빌드 제외**
- 레이어 추가: `Terrain`, `Player`, `Rope`, `Item`, `Interactable` (이번에 쓰는 건 Terrain·Player 뿐이지만 미리 예약)

### 5. 코어 스크립트 (`Scripts/Runtime/Core/`, `Network/`, `UI/`)
- `RunConfig` (struct, `INetworkSerializable`): `Seed`, `BiomeSequence(int[])`, `FogEnabled`, `Difficulty` — `Docs/201_common.md` 5장 그대로
- `MatchPhase` enum: Lobby, Generating, Playing, Result
- `GameManager` (NetworkBehaviour, Boot): `NetworkVariable<MatchPhase>`, `NetworkVariable<RunConfig>`, 호스트 전용 전이 메서드 `StartSingle(seed)` (Lobby → Generating → Game 씬 로드 → Playing. M0 에서는 Generating 이 즉시 Playing 으로 넘어간다), `ReturnToLobby()`
- `SceneFlow`: Boot 위에 애디티브로 씬을 얹고 이전 씬을 내린다. **호스트가 시작된 뒤에는 NGO `NetworkSceneManager.LoadScene(…, Additive)` 를 쓴다** (클라이언트 동기화 대비). 호스트 전이라면(Boot 단독 실행 → Lobby) 일반 `SceneManager`
- `NetService` (`Network/`): M0 에서는 `StartHostLocal()` (UnityTransport 로컬, `StartHost()`) 와 `StartClientLocal()` (127.0.0.1) 두 메서드만. Relay·Sessions 는 M4. 씬 코드가 `Unity.Netcode` 타입을 직접 만지지 않도록 `IsHost`, `IsClient`, `LocalClientId` 를 래핑
- `Bootstrapper` (`Core/`, Game·Sandbox 씬에 배치): `Awake` 에서 `NetworkManager.Singleton == null` 이면 Boot 씬을 애디티브 로드 → `GameManager` 가 준비되면 `NetService.StartHostLocal()` → `MatchPhase = Playing` (Lobby 를 거치지 않는 개발 경로). 이미 NetworkManager 가 있으면 아무것도 하지 않는다. `Docs/205_network.md` 6장
- **Multiplayer Play Mode 대비**: 가상 플레이어 태그가 `Client` 이면 `Bootstrapper` 가 `StartHostLocal()` 대신 `StartClientLocal()` 을 호출한다 (`Unity.Multiplayer.Playmode.CurrentPlayer.ReadOnlyTags()`). 이번 게이트에는 없고 M0-2 에서 확인
- `Log` (`Core/`): `Log.Info/Warn/Error(LogCategory cat, string msg)`. 카테고리 enum `Net, Gen, Player, Items, UI, Core`. 카테고리별 on/off 는 `static bool[]` + 에디터 메뉴 토글. 내부는 `Debug.Log` 에 `[Cat]` 접두. 우리 코드는 `Debug.Log` 를 직접 쓰지 않는다
- `DebugOverlay` (`UI/`, Boot): F3 토글. 별도 Canvas(SortOrder 100), 좌상단 TMP 텍스트. 기본 줄: FPS, 역할(Host/Client/None), LocalClientId, `MatchPhase`, `RunConfig.Seed`, 현재 로드된 씬 목록. **다른 시스템이 줄을 추가하는 API**: `DebugOverlay.Register(string key, Func<string> line)` / `Unregister(key)` — M0-2·M1·M2 가 쓴다

### 6. 입력 (`Assets/_Project/Input/PeakActions.inputactions`)
`Docs/202_gameplay.md` 1장 표대로 `Player` 액션 맵을 정의: Move(Vector2), Look(Vector2), Sprint, Jump, Crouch, Climb, Interact, UseItem(우클릭), Slot1~Slot4, Drop, Inventory, Chat, Pause. 키보드+마우스와 게임패드 바인딩. **Generate C# Class** 켜서 `Peak.Input.PeakActions` 생성. `UI` 맵은 기본 것 유지. 이번에 실제로 읽는 액션은 없다 (M0-2 부터)

### 7. 튜닝·테마 에셋 (`Scripts/Runtime/Core/` 정의, `Assets/_Project/Data/` 에셋)
- `GameTuning` (ScriptableObject): `Docs/202_gameplay.md` 9장 표의 필드 전부, 초기값 그대로. 필드마다 `[Tooltip]` 에 근거 문서 장 번호
- `VisualTheme` (ScriptableObject): `playerColors[4]`, `terrainWalkColor/terrainWallColor`(임시 회색 2종), `uiBackground/uiText/uiAccent`, 상태이상 색은 M1 이 추가
- 접근: `GameManager` 가 두 에셋을 인스펙터 참조로 들고 `GameConfig.Tuning` / `GameConfig.Theme` static 프로퍼티로 노출 (Resources 미사용)
- 에셋 파일: `Data/GameTuning.asset`, `Data/VisualTheme.asset` — 에디터 스크립트(4번 메뉴)가 없으면 생성

## 제약
- `Docs/` 와 `CLAUDE.md` 는 수정 금지. 문서와 다르게 해야 했다면 코드 주석 + 완료 보고에 적는다
- `Assets/Editor/McpAutoStartBootstrap.cs`, `.mcp.json` 수정·이동 금지
- 매직 넘버 금지 (포트 번호·오버레이 위치 같은 인프라 상수는 `const` 로 이름 붙이면 허용)
- 씬·프리팹·에셋은 에디터 스크립트로 생성. unity-mcp 를 쓸 수 있으면 메뉴 실행·플레이 모드 진입·콘솔 확인에 사용
- 플레이어, 등반, 스태미나, 생성, Relay 는 **구현하지 않는다**
- 커밋하지 않는다
- 새 파일은 전부 `Assets/_Project/` 아래

## 완료 조건 (전부 만족해야 완료)
1. 패키지 변경 후 컴파일 에러 0, 우리 코드에서 나온 경고 0
2. `Peak > Setup > Rebuild M0 Scenes` 실행 → 씬 5개·에셋 2개 생성. 두 번 실행해도 diff 없음 (멱등)
3. `Boot` 씬 플레이 → Lobby 표시 → [싱글 시작] → Game 씬 로드, 오버레이에 `Host / Playing / Seed 1`. 콘솔 에러 0
4. `Game` 씬을 직접 열고 플레이 → Boot 자동 로드 → 호스트 시작 → 오버레이 `Host / Playing`. 콘솔 에러 0
5. `Sandbox_Climb`, `Sandbox_ProcGen` 도 4와 동일
6. F3 으로 오버레이 토글. `DebugOverlay.Register` 로 넣은 테스트 줄이 보였다가 `Unregister` 로 사라진다 (테스트 코드는 남기지 말고 확인만)
7. `Peak.Tests` 에 EditMode 테스트 1개: `RunConfig` 직렬화 왕복 (`FastBufferWriter/Reader`) 후 값 동일 — 통과
8. Build Settings 에 Boot·Lobby·Game 만 있고 Sandbox 는 없다

## 보고 형식 (`Docs/201_common.md` 8장)
```
결과: 완료 / 부분 완료 (남은 것)
만든 것: 파일 목록 (경로)
패키지: 추가·제거된 패키지와 실제 설치된 버전
문서와 달랐던 점: 무엇을 왜
확인 방법: 메뉴 이름, 씬 이름, 테스트 이름
다음 워커에게: M0-2 (플레이어) 가 알아야 할 것 — SpawnPoint 이름, GameConfig 접근법, DebugOverlay API, Bootstrapper 동작
```
