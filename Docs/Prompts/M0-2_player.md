# [M0-2] 플레이어 — 캡슐 이동 · 오너 권한 동기화 · Cinemachine 3인칭 카메라 · 스폰

> **2026-09-18 주의**: 이 프롬프트의 3인칭 카메라·이동 방향 회전(`turnSpeed`)은 `301` D16(1인칭 전용)으로 폐기되었다. 전환 작업은 `M0-3_first_person.md`. 이 파일은 발행 이력으로만 보관한다 — 재실행 금지.

> 개정 2026-09-17 (기획 세션): M0-1 실제 결과물(커밋 `330025d`)에 맞춰 수정. **M0-1 보고를 따로 붙일 필요 없다** — 필요한 사실은 0장에 있다.

너는 이 Unity 프로젝트(`C:\Users\Smin\Programming\Peak`)의 **워커 세션**이다. M0-1 은 완료·검증·커밋되었다. 아래 범위만 구현하고, 범위 밖은 건드리지 않는다.

## 먼저 읽을 것 (지정된 장만)
1. `CLAUDE.md`
2. `Docs/SUMMARY.md`
3. `Docs/201_common.md` 3·4·6장
4. `Docs/202_gameplay.md` 1·2·3·9장 (3장 표는 Grounded·Airborne 만)
5. `Docs/205_network.md` 3장 플레이어 행, 6장
6. `Docs/102_required_assets.md` 2장

M0-1 소스는 0장에 적힌 파일을 필요할 때만 연다. 프로젝트 전체 탐색 금지.

## 0. M0-1 이 만든 것 (그대로 사용)

| 대상 | 사실 |
|---|---|
| 튜닝·테마 | `Peak.Core.GameConfig.Tuning` / `.Theme`. Boot 의 `GameManager.Awake` 가 바인딩. Boot 로드 전에는 null |
| 플레이어 색 | `VisualTheme.playerColors`, 길이 `VisualTheme.PlayerCount` (= 4) |
| 매치 상태 | `GameManager.Instance.Phase` (`NetworkVariable<MatchPhase>`, 호스트만 쓰기). `ReturnToLobby()` 는 Phase 를 Lobby 로 바꾼 **뒤** 씬을 전환한다 |
| 콘텐츠 씬 | `SceneFlow.Instance.CurrentSceneName`. Boot 경로·직접 플레이 모두 이 씬이 활성 씬 |
| 스폰 위치 | Game·Sandbox_Climb·Sandbox_ProcGen 의 루트 오브젝트 `SpawnPoint` (0, 1, 0) |
| Boot 구조 | 루트 `NetworkManager` (NGO 제약으로 별도 루트, EnableSceneManagement = true) + 루트 `[Boot]` (in-scene NetworkObject: BootRoot·GameManager / 자식: SceneFlow·DebugOverlay·EventSystem) |
| 네트워크 프리팹 목록 | `Assets/_Project/Data/NetworkPrefabs.asset`. `M0SceneBuilder` 가 NetworkManager 에 연결 |
| 네트워크 진입 | `Peak.Network.NetService` (`IsHost`, `IsServer`, `LocalClientId`, `StartHostLocal()`, `StartClientLocal()`) |
| 직접 플레이 | `Bootstrapper`: NetworkManager 가 없으면 Boot 애디티브 로드 → 호스트 → `GameManager.BeginDirectPlay()`. MPPM 가상 플레이어 태그가 `Client` 면 `StartClientLocal()` (`PEAK_HAS_MPPM` 정의 시). **클라이언트 경로는 아직 미검증** |
| 오버레이 | `Peak.UI.DebugOverlay.Register(string key, Func<string> line)` / `Unregister(string key)` |
| 로그 | `Peak.Core.Log.Info/Warn/Error(LogCategory.Player, …)` |
| 입력 | `Assets/_Project/Input/PeakActions.inputactions` + 생성 클래스 `PeakActions` (`Player` 맵: Move·Look·Sprint·Jump·Pause 등, `UI` 맵: Click 등) |
| 레이어 | `Terrain`, `Player` 존재. Game·Sandbox_Climb 의 `Ground` 는 Terrain |
| 씬 빌더 | 메뉴 `Peak > Setup > Rebuild M0 Scenes` (`Scripts/Editor/M0SceneBuilder.cs`, 멱등, `Ensure*` 헬퍼) |
| 패키지 | NGO 2.13.2 · Cinemachine 3.1.7 · Multiplayer Play Mode 1.6.3 · Input System 1.17.0 |

## 목표 (한 문장)
Game·Sandbox_Climb 에서 플레이하면 호스트 캡슐이 SpawnPoint 에 스폰되어 걷고·달리고·점프하며, 3인칭 카메라가 돌고, 플레이어별 색이 다르고, 이동이 오너 권한으로 복제된다. 등반·스태미나·상태이상은 **M1** — 상태 기계의 자리만 만든다.

## 1. Player 프리팹 — 메뉴 `Peak > Setup > Rebuild Player Prefab` (새 에디터 스크립트, 멱등)

경로 `Assets/_Project/Prefabs/Player.prefab`.

```
Player                     Layer = Player
  NetworkObject, Rigidbody, CapsuleCollider (h 1.8, r 0.35, center y 0.9, 마찰 0 PhysicsMaterial)
  NetworkTransform (오너 권한), NetworkRigidbody
  PlayerController (NetworkBehaviour)
  ├─ CameraTarget          빈 Transform, 로컬 y 1.5
  └─ Visual                PlayerVisual (IVisualState 구현). 콜라이더 없음
      ├─ Capsule           프리미티브 메시, 콜라이더 제거
      └─ Nose              작은 큐브, 전방(+Z) 표시, 콜라이더 제거
```

- Rigidbody: 비-kinematic, 회전 고정, Interpolate, Continuous, 질량 70
- NetworkTransform: NGO 2.13 의 오너 권한 모드, 보간 켬. ⚠ 오너 권한 속성이 없으면 오너 권한 서브클래스로 대체하고 보고
- NetworkRigidbody: 비오너 인스턴스의 Rigidbody 를 kinematic 으로 만들기 위함. 옵션이 애매하면 기본값 + 보고
- 프리팹에 굽는 기하 수치(캡슐 크기, 질량, CameraTarget 높이, Nose 크기)는 빌더의 **이름 있는 const**. 근거 `202` 2장, `102` 2장 4번
- 메뉴 마지막에 `NetworkPrefabs.asset` 에 Player 등록 (중복 없이). `NetworkConfig.PlayerPrefab` 은 **비워 둔다** — 자동 스폰 금지, 6장 스포너가 담당
- 마찰 0 PhysicsMaterial 과 `CameraRig.prefab`(4장)도 이 메뉴가 만든다. 경로는 `201` 3장 폴더 규칙 (머티리얼류 `Art/`, 프리팹 `Prefabs/`)

## 2. 로직 / 비주얼 분리 (`102` 2장)

- `Scripts/Runtime/Visual/IVisualState.cs` 신설: `void SetTint(Color color);` 하나만. 이동 상태 표현 등 확장은 M1
- `PlayerVisual : MonoBehaviour, IVisualState` 는 **`Visual` 오브젝트에** 붙는다. 자기 자식 Renderer 에 `MaterialPropertyBlock` 으로 색 적용. 머티리얼 에셋 수정 금지
- Root 쪽 코드는 `GetComponentInChildren<IVisualState>()` 로만 통지한다. Capsule·Nose 를 필드로 참조하지 않는다
- 색: `OnNetworkSpawn` 에서 `GameConfig.Theme.playerColors[OwnerClientId % VisualTheme.PlayerCount]`. 오너·비오너 모든 인스턴스가 각자 계산

## 3. PlayerController + 상태 (`Scripts/Runtime/Player/`, `Player/States/`)

- `PlayerMoveState { Grounded, Airborne, Climbing, Mantling, Hanging, Carrying, Unconscious, Dead }` 선언
- 상태 클래스는 `Enter / Tick / FixedTick / Exit`, 파일 분리. **Grounded·Airborne 만 구현**, 나머지는 빈 클래스 + `// M1` 주석. 전이는 PlayerController 한 곳에서 (`202` 3장)
- 입력·물리 제어는 오너만. 비오너는 NetworkTransform 이 움직인다
- 지면 판정: 캡슐 바닥에서 SphereCast → `Terrain` 레이어. 법선 경사 < `walkSlope` 면 Grounded
- Grounded: 카메라 yaw 기준으로 Move 를 월드 방향 변환 → 지면 법선에 투영 → `walkSpeed` / `sprintSpeed`(Sprint 홀드). `velocity` 직접 제어, 짧은 가감속. **정지 시 30° 경사에서 미끄러지지 않는다** (중력 처리 방식은 자유, 보고에 적는다)
- Airborne: 중력 + 약한 공중 제어. 경사 ≥ `walkSlope` 인 면 위는 Airborne 이므로 미끄러져 내려간다
- Jump: Grounded 에서 수직 속도 = `jumpSpeed`. 스태미나 소모 없음 (M1)
- 캐릭터 회전: 이동 방향으로 부드럽게. 정지 시 유지
- 오너만 `DebugOverlay.Register("player", …)`: 상태, 수평 속도(m/s, 소수 1자리), grounded, 지면 경사각. Despawn 시 `Unregister`

## 4. 카메라 (`Player/PlayerCameraRig.cs`, 오너 전용)

- `Prefabs/CameraRig.prefab` (1장 메뉴가 생성): Camera (MainCamera 태그) + CinemachineBrain + CinemachineCamera (Third Person Follow: 어깨 오프셋, 거리 4 m, 짧은 댐핑)
- 지형 관통 방지: D14 대로 `CinemachineDeoccluder` (Terrain) 를 먼저 쓴다. ⚠ Third Person Follow 내장 장애물 회피와 겹쳐 떨리면 둘 중 하나만 쓰고 보고. 목적은 지형 관통 방지 하나
- 오너 `OnNetworkSpawn` 에서 리그 인스턴스화, Follow / LookAt = CameraTarget. `OnNetworkDespawn` 에서 파괴
- Look → CameraTarget yaw(무제한)·pitch(제한). `CinemachineInputAxisController` 는 붙이지 않는다 (입력 이중 처리 방지)
- 커서: 플레이 중 잠금·숨김. Pause(Esc) → 해제, 해제 중 Look 무시, `UI/Click` → 재잠금. 일시정지 UI 는 범위 밖
- 리그에 굽는 값(어깨 오프셋·거리·댐핑)은 빌더 const. 런타임이 읽는 값은 5장

## 5. GameTuning 필드 추가 (`Core/GameTuning.cs`)

런타임 코드가 읽는 새 수치는 전부 여기. 각 Tooltip 끝에 `M0-2 추가 — 202 9장 반영 필요`. 기존 필드는 건드리지 않는다.

| 필드 | 제안 초기값 |
|---|---|
| `airControl` | 0.3 |
| `groundAccelTime` | 0.1 s |
| `turnSpeed` | 10 rad/s |
| `lookSensitivity` | 0.15 |
| `pitchMin` / `pitchMax` | −40° / 70° |
| `groundCheckRadius` / `groundCheckDistance` | 0.3 m / 0.15 m |
| `spawnSpacing` | 1.5 m (`205` 4장) |

이름·개수는 필요하면 바꿔도 되지만 보고에 표로 적는다.

## 6. 스폰 (`Core/PlayerSpawner.cs`, 호스트 전용)

- `M0SceneBuilder` 가 `[Boot]` 아래에 배치하고 Player 프리팹 참조를 연결. Player.prefab 이 없으면 `Log.Error` 로 "Rebuild Player Prefab 먼저" 안내
- `Phase` 가 Playing 이 되면 (구독 시점에 이미 Playing 이면 즉시) 접속 중이면서 PlayerObject 가 없는 클라이언트를 모두 스폰. Playing 중 새 접속(`OnClientConnectedCallback`)도 스폰
- 위치 = 콘텐츠 씬 루트 `SpawnPoint` + (접속 순서 인덱스 × `spawnSpacing`, 0, 0). `SpawnAsPlayerObject(clientId)`
- 콘텐츠 씬에 `SpawnPoint` 가 없으면 스폰하지 않고 `Log.Info` 한 줄
- `Phase` 가 Lobby 로 바뀌면 모든 PlayerObject 를 Despawn (씬 언로드 전에)

## 7. M0SceneBuilder 수정 (허용 범위)

- Game·Sandbox_Climb: 기존 루트 `Main Camera` **삭제**. `Ensure*` 는 삭제를 하지 않으므로 명시적으로 지운다. Lobby 카메라는 유지
- Sandbox_ProcGen: 루트 `SpawnPoint` **삭제**. 이 씬은 플레이어 없이 호스트·오버레이·기존 카메라만. 이 씬의 스폰 방식은 M2 가 생성 결과로 정한다
- Sandbox_Climb: 임시 경사 2개 추가. Terrain 레이어, 이름 `Temp_Ramp30`·`Temp_Ramp60`, SpawnPoint 에서 걸어 닿는 위치. M1 벽 세트가 대체한다
- `[Boot]` 아래 PlayerSpawner (6장)
- 실행 순서: **Rebuild Player Prefab → Rebuild M0 Scenes**. 각 메뉴를 두 번 실행해도 diff 없음

## 8. Multiplayer Play Mode (게이트 항목)

- 가상 플레이어 1개, 태그 `Client`. 호스트가 Game 을 직접 플레이 → 클라이언트 창에도 캡슐 2개(색 다름), 한쪽 이동이 다른 창에 복제
- 주의: 가상 플레이어도 Game 씬이 열린 상태에서 Boot 를 얹은 뒤 접속한다. NGO 씬 동기화가 Game 을 **중복 로드**하거나 내리는지 먼저 확인한다. 원인 조사를 위한 `Bootstrapper`·`SceneFlow`·`NetService` 수정은 허용 (보고에 이유)
- Relay·Sessions 도입 금지. 해결하지 못하면 원인과 시도한 것을 보고하고 결과를 "부분"으로
- MPPM 창 설정을 unity-mcp 로 할 수 없으면 사람이 할 절차를 보고에 단계로 적는다

## 제약

- 수정 허용 파일: 위에서 새로 만드는 파일 + `GameTuning.cs` + `M0SceneBuilder.cs` + (8장 조사 시) `Bootstrapper.cs`·`SceneFlow.cs`·`NetService.cs`. 그 밖의 M0-1 파일 수정 금지
- `Docs/`, `CLAUDE.md`, `Assets/Editor/McpAutoStartBootstrap.cs`, `.mcp.json` 수정 금지. `Docs/101` 항목 구현 금지
- 등반·스태미나·상태이상·낙하 피해·상호작용·HUD 구현 금지
- 매직 넘버 금지: 런타임 수치는 GameTuning, 프리팹에 굽는 수치는 빌더 const
- `Debug.Log` 금지 → `Log.*(LogCategory.Player 또는 Net, …)`
- 커밋 금지
- 토큰 절약: 확인은 스크린샷 대신 `execute_code` 로 한 줄 상태를 받고, `read_console` 은 스택 트레이스 없이

## 완료 조건

**워커가 자동 확인** (unity-mcp)
1. 두 메뉴를 순서대로 실행, 각각 두 번째 실행 후 에셋·씬 diff 없음. 컴파일 에러 0, 우리 코드 경고 0
2. Game 직접 플레이: PlayerObject 1개가 SpawnPoint 근처, Layer Player, 상태 Grounded, 오버레이에 player 줄, MainCamera 태그 카메라 1개(리그), 에러 0
3. Sandbox_Climb: 플레이어를 `Temp_Ramp30` 위로 옮기고 2초 → 수평 이동 < 0.05 m, Grounded. `Temp_Ramp60` 위로 옮기고 2초 → 아래로 미끄러짐, Grounded 아님
4. Sandbox_ProcGen 직접 플레이: 플레이어 없음, Role Host, 에러 0
5. Boot → Lobby → [싱글 시작] → 스폰 → `ReturnToLobby()` → PlayerObject 0, 리그 없음, 에러 0 → 다시 [싱글 시작] 해도 정상
6. 플레이 종료 후 두 번째 플레이도 에러 0

**사람이 확인** (보고에 체크리스트로 남긴다)
7. WASD 걷기 4 m/s, Shift 달리기 6.5 m/s, Space 점프 정점 약 1.5 m, 이동 방향이 카메라 yaw 기준
8. 마우스로 카메라 회전, 지면 관통 없음, Esc 로 커서 해제·클릭으로 재잠금
9. MPPM `Client` 1개: 캡슐 2개 색 다름, 상호 이동 복제 (8장)

## 보고 형식 (`Docs/201_common.md` 8장)
```
결과: 완료 / 부분 완료 (남은 것)
만든 것: 파일 목록 (경로)
GameTuning 추가 필드: 이름 | 초기값 | 용도 (표)
문서와 달랐던 점: 무엇을 왜 (Deoccluder 선택, NetworkTransform 권한 방식, 경사 정지 처리 방식 포함)
확인 방법: 메뉴 실행 순서, 자동 확인 1~6 결과, 사람 확인 7~9 절차
다음 워커에게: M1 이 알아야 할 것 — 상태 클래스 추가 방법, ClimbSensor 끼울 위치, 지면 판정과 walkSlope 공유 지점, 카메라 yaw 접근법, IVisualState 확장 지점
```
