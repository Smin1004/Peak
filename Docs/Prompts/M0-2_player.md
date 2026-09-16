# [M0-2] 플레이어 프리팹 — 캡슐 이동 · 오너 권한 동기화 · Cinemachine 3인칭 카메라 · 스폰

너는 이 Unity 프로젝트(`C:\Users\Smin\Programming\Peak`)의 **워커 세션**이다. **M0-1 이 완료된 상태**에서 시작한다 (M0-1 보고를 먼저 확인). 아래 범위만 구현하고, 범위 밖은 건드리지 않는다.

## 먼저 읽을 것 (순서대로)
1. `CLAUDE.md`
2. `Docs/SUMMARY.md`
3. `Docs/201_common.md` — 3·4·5·6장
4. `Docs/202_gameplay.md` — 1·2·3장 (상태 기계 표는 Grounded·Airborne 만 이번 범위), 9장
5. `Docs/205_network.md` — 3장 (플레이어 행), 6장
6. `Docs/102_required_assets.md` — 2·3장 (Root/Visual 분리)
7. `Docs/204_ui.md` 4장
8. M0-1 워커의 완료 보고 (사용자가 붙여줌) — SpawnPoint, GameConfig, DebugOverlay, Bootstrapper 사용법

## 목표 (한 문장)
`Game`·`Sandbox_*` 씬에서 플레이하면 호스트 플레이어 캡슐이 스폰되어 **걷고 달리고 점프하며, 3인칭 카메라가 돌고**, 플레이어별 색이 다르고, 이동이 `NetworkTransform`(오너 권한) 으로 복제되는 상태를 만든다. 등반·스태미나·상태이상은 **M1** 이다 — 상태 기계의 자리만 만든다.

## 범위 — 만들 것

### 1. Player 프리팹 (`Assets/_Project/Prefabs/Player.prefab`) — 에디터 스크립트로 생성
메뉴 `Peak > Setup > Rebuild Player Prefab` (멱등). 구조는 `Docs/202_gameplay.md` 2장:

```
Player (NetworkObject, Rigidbody, CapsuleCollider h=1.8 r=0.35 center y=0.9, Layer=Player)
 ├─ PlayerController      (NetworkBehaviour) 상태 기계 + 이동
 ├─ PlayerInput 처리       PeakActions 를 오너만 Enable
 ├─ NetworkTransform      AuthorityMode = Owner (NGO 2.x). 위치·회전 동기화, Interpolate 켬
 ├─ PlayerVisual          Visual 자식에 색 적용 (IVisualState 구현)
 ├─ CameraTarget (빈 Transform, y=1.5)   카메라가 따라갈 피벗. Look 입력으로 yaw/pitch 회전
 └─ Visual
     ├─ Capsule (프리미티브 메시, 콜라이더 제거)
     └─ Nose (작은 큐브, 전방 표시)
```

- Rigidbody: 비-kinematic, `freezeRotation`, `interpolation = Interpolate`, `collisionDetection = Continuous`. 질량 70
- `PlayerController`
  - 상태 enum `PlayerMoveState { Grounded, Airborne, Climbing, Mantling, Hanging, Carrying, Unconscious, Dead }` 를 **선언**하되 이번에는 `Grounded`·`Airborne` 만 구현. 각 상태는 `Enter/Tick/FixedTick/Exit` 를 가진 클래스로 분리 (`Player/States/`). 나머지 상태 클래스는 빈 껍데기 + `// M1` 주석
  - 지면 판정: 캡슐 바닥에서 `SphereCast(r=0.3, dist=0.15)` → `Terrain` 레이어. 히트 법선 경사 < `GameConfig.Tuning.walkSlope` 면 Grounded
  - Grounded: 카메라 yaw 기준으로 Move 를 월드 방향으로 변환, 속도 `walkSpeed`/`sprintSpeed`(Sprint 홀드), 경사면에서는 법선에 투영. 가속·감속은 짧게(0.1초). 정지 시 미끄러지지 않게 (`velocity` 직접 제어, 물리 마찰 0 머티리얼)
  - Airborne: 중력만 + 공중 제어 30% ⚠ (`GameTuning.airControl` 필드 추가, 초기값 0.3, Tooltip 에 "202 9장 미기재 — M0-2 추가" 명시)
  - Jump: Grounded 에서 `jumpSpeed` 만큼 수직 속도. 스태미나 소모는 M1
  - 캐릭터 회전: 이동 방향으로 부드럽게 (10 rad/s). 정지 시 유지
  - 입력은 `IsOwner` 일 때만. 비오너는 `NetworkTransform` 이 움직인다
  - `DebugOverlay.Register("player", …)` 로 상태·속도·grounded 를 오너만 표시
- `PlayerVisual`: `OnNetworkSpawn` 에서 `GameConfig.Theme.playerColors[OwnerClientId % 4]` 를 `MaterialPropertyBlock` 으로 Capsule·Nose 에 적용. 스크립트는 `Visual` 아래를 `transform.Find("Visual")` 한 번만으로 찾고, 그 안의 컴포넌트를 필드로 직접 참조하지 않는다 (`Docs/102` 2장)

### 2. 카메라 (`Player/PlayerCameraRig.cs`, 오너 전용)
- 오너 `OnNetworkSpawn` 에서 카메라 리그를 **씬에 생성** (프리팹 `Prefabs/CameraRig.prefab`, 에디터 스크립트로 생성): `Camera`(MainCamera 태그, `CinemachineBrain`) + `CinemachineCamera` (Third Person Follow: 어깨 오프셋 (0.5, 0, 0), 거리 4 m, 댐핑 0.1) + `CinemachineDeoccluder` (Collide Against = Terrain, 최소 거리 0.5)
- `Follow`/`LookAt` = `CameraTarget`. Look 입력으로 `CameraTarget` 의 yaw(무제한)·pitch(−40°~70°) 회전. 감도는 `GameTuning.lookSensitivity` 필드 추가 (초기값 0.15, Tooltip 에 M0-2 추가 명시)
- 커서: 플레이 중 잠금·숨김, Esc 로 해제 (Pause 액션 — 실제 일시정지 UI 는 범위 밖, 커서만)
- 비오너·Boot 단독·Lobby 에는 카메라 리그가 없다. Lobby 는 자체 Camera 를 가진다 (M0-1 에서 없으면 추가)
- 씬에 남아 있는 기존 `Main Camera` 는 Game·Sandbox 에서 제거 (M0-1 의 `Rebuild M0 Scenes` 를 수정해도 된다 — 그 메뉴는 이 워커의 범위에 포함)

### 3. 스폰 (`Core/PlayerSpawner.cs`, Boot `[Boot]` 아래, 호스트 전용)
- `MatchPhase == Playing` 진입 시, 그리고 이후 클라이언트가 접속할 때 (`OnClientConnectedCallback`) 그 클라이언트의 Player 를 스폰: 위치 = 활성 씬의 `SpawnPoint` + `(index × 1.5, 0, 0)`. `SpawnAsPlayerObject(clientId)`
- `NetworkManager` 의 NetworkPrefabs 목록에 Player 등록 (에디터 스크립트)
- 씬을 떠날 때(`ReturnToLobby`) 플레이어 오브젝트 Despawn

### 4. Multiplayer Play Mode 확인 (게이트 항목)
- Window > Multiplayer Play Mode 에서 가상 플레이어 1개, 태그 `Client`. M0-1 의 `Bootstrapper` 가 태그를 보고 `StartClientLocal()` 로 붙는다
- 호스트가 Game 씬을 직접 플레이 → 가상 플레이어 창에서도 Game 씬이 로드되고(NGO 씬 동기화) 캡슐 2개가 서로 다른 색으로 보이며, 한쪽을 움직이면 다른 창에서 움직인다
- 안 되면 원인을 조사하되 **Relay·Sessions 를 도입하지 않는다**. 로컬 Transport 문제면 보고에 사유를 적고 게이트 5번은 "부분" 으로 표시

## 제약
- `Docs/`, `CLAUDE.md`, `Assets/Editor/McpAutoStartBootstrap.cs`, `.mcp.json` 수정 금지
- 등반·스태미나·상태이상·낙하 피해·상호작용·HUD **구현 금지** (상태 enum 과 빈 상태 클래스만)
- 매직 넘버 금지 — 새 수치는 `GameTuning` 에 필드 추가 + Tooltip 에 "M0-2 추가, 202 9장 반영 필요" 라고 적어 기획 세션이 문서를 갱신할 수 있게 한다
- 프리팹·씬 변경은 에디터 스크립트 (멱등). unity-mcp 사용 가능
- `Debug.Log` 대신 `Log.Info(LogCategory.Player, …)`
- 커밋하지 않는다

## 완료 조건
1. `Peak > Setup > Rebuild Player Prefab` 실행 → `Player.prefab`, `CameraRig.prefab` 생성, NetworkPrefabs 등록. 두 번 실행해도 diff 없음
2. `Game` 씬 플레이 → 캡슐이 `SpawnPoint` 에 스폰, WASD 로 걷고(4 m/s) Shift 로 달리고(6.5 m/s) Space 로 점프(정점 약 1.5 m). 오버레이에 상태·속도 표시
3. 카메라가 마우스로 돌고, 지면 아래로 파고들지 않으며(Deoccluder), 캐릭터가 카메라 yaw 기준으로 이동한다
4. `Sandbox_Climb` 에서 30° 경사면 위를 걸어 오르고, 60° 면(임시로 큐브 하나 기울여 배치, 에디터 스크립트에 포함)은 오르지 못하고 미끄러진다 — `walkSlope` 45° 기준
5. Multiplayer Play Mode 가상 플레이어 `Client` 1개: 캡슐 2개, 색 다름, 상호 이동 복제 (위 4번 항목)
6. 콘솔 에러 0. 플레이 종료 시 카메라 리그·플레이어가 정리되어 두 번째 플레이도 정상
7. 정지 상태에서 경사면(30°)에 서 있어도 미끄러지지 않는다

## 보고 형식 (`Docs/201_common.md` 8장)
```
결과: 완료 / 부분 완료 (남은 것)
만든 것: 파일 목록 (경로)
GameTuning 에 추가한 필드: 이름·초기값·이유 (기획 세션이 202 9장에 반영)
문서와 달랐던 점: 무엇을 왜
확인 방법: 메뉴 이름, 씬 이름, MPM 설정
다음 워커에게: M1 (등반) 이 알아야 할 것 — 상태 클래스 추가 방법, ClimbSensor 를 어디에 끼울지, 지면 판정과 walkSlope 공유 지점, 카메라 yaw 접근법
```
