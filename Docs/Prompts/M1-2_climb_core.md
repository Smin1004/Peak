# [M1-2] 등반 코어 — ClimbSensor · Climbing · Mantling · 몸이 벽을 향함

> 발행 2026-09-18 (기획 세션). M1 분할의 두 번째 워커 (`300` M1 워커 분할 표). 스태미나 없이 **무한 등반**으로 조작감의 뼈대를 만든다.
> 전제: M1-1 이 커밋된 상태 (`5ad8347`). `git status` 에서 `Assets/` 아래에 커밋 안 된 변경이 있으면 작업하지 말고 사람에게 알린다 (`Docs/` 변경은 기획 세션 것이므로 무시).

너는 이 Unity 프로젝트(`C:\Users\Smin\Programming\Peak`)의 **워커 세션**이다. 아래 범위만 구현하고, 범위 밖은 건드리지 않는다.

## 먼저 읽을 것 (지정된 장만)
1. `CLAUDE.md`
2. `Docs/202_gameplay.md` **3장**(상태 표·우선순위), **4장 전체**(보강 규칙 표 포함), 9장 M1-2 필드 표, 10장 체크리스트, 12.1·12.5
3. `Docs/301_decisions.md` Q11·Q15 두 줄

소스는 0장에 적힌 파일만 연다. 프로젝트 전체 탐색 금지.

## 0. 현재 상태 (그대로 사용)

| 대상 | 사실 |
|---|---|
| `Runtime/Player/PlayerController.cs` | 오너만 `_isLocalActive`. `Update`: 입력 읽기(`_moveInput`, `_sprintHeld`, `_jumpRequested`) → `CurrentState.Tick`. `FixedUpdate`: `ProbeGround()` → `UpdateTransitions()` → `CurrentState.FixedTick` → `BodyFollowsCameraYaw` 면 `_body.MoveRotation(_cameraRig.YawRotation)`. 전이는 `UpdateTransitions` 한 곳 (주석: "M1 은 위쪽 상태의 진입 조건을 switch 앞에 추가"). `ChangeState` 가 Exit/Enter 호출. 도구: `Body`, `Tuning`, `Ground`(`GroundInfo`), `GetWishDirection()`, `Accelerate(...)`, `_capsule`, `_terrainMask`, `BuildOverlayLine()` |
| 상태 | `States/PlayerState.cs` 추상 기반 (`Enter/Tick/FixedTick/Exit`, `virtual bool BodyFollowsCameraYaw => true`). `GroundedState`: Rigidbody 중력 끄고 법선 성분만 직접 더함. `AirborneState`: 중력 켬, 등반면 쪽으로 미는 공중 제어 성분을 버림. `ClimbingState`·`MantlingState` 는 빈 껍데기 |
| 카메라 | `PlayerCameraRig`: `Yaw`, `Pitch`, `YawRotation`, `CameraTarget`(눈높이 1.6 m, 루트 자식), `SetLook(yaw, pitch)`, `IsCursorLocked`. CameraTarget 월드 회전은 LateUpdate 에서 yaw·pitch 로 고정 → 몸이 어느 쪽을 보든 시선은 자유 |
| 입력 | `PeakActions.Player.Climb` = 마우스 **좌클릭** 홀드 (게임패드 RT). 커서 재잠금 `UI.Click` 도 좌클릭 |
| 프리팹 | Root: `Rigidbody`(비-kinematic, 회전 고정, Interpolate, Continuous, 질량 70), `CapsuleCollider`(h 1.8, r 0.35, center y 0.9 → 루트 = 발), `NetworkTransform`(Owner), `NetworkRigidbody`(AutoUpdateKinematicState 기본값), `PlayerCameraRig`, `PlayerController`, `PlayerLocalHud` |
| GameTuning | `climbSpeed` 1.5, `walkSlope` 45, `airControl`, `groundAccelTime` 등. **등반 필드는 아직 없다** (5장) |
| 등반 코스 | `Scripts/Editor/ClimbCourseBuilder.cs` 의 `Course` 표 (읽기만). 요소 로컬: 원점 = 앞 발끝 선 중앙(지면), 등반면이 로컬 −Z 를 보고 +Z 로 깊어진다. `Toe`(월드 x, z), `Yaw` 상수, 경사·높이·폭·깊이. 예: `Height_85_H10` 은 Toe (11.5, 18), 향 0 → 벽이 월드 −Z(스폰 쪽)를 본다. (11.5, 0, 17.0) 에 서서 yaw 0 이면 벽을 정면으로 본다 |
| 오버레이 | `Player {State} | … m/s | grounded … | slope … | look yaw / pitch` |

## 목표 (한 문장)
좌클릭을 누른 채 벽을 보면 벽에 붙고, WASD 로 벽면을 따라 오르내리고 옆으로 돌며, 볼록·오목 코너를 넘고, 윗변에 닿으면 0.4초 동안 부드럽게 올라서며, 손을 놓으면 떨어진다. 스태미나는 없다 (M1-3).

## 1. `ClimbSensor` (새 파일 `Runtime/Player/ClimbSensor.cs`, `Peak.Player`)

- **PlayerController 가 소유하는 일반 클래스** (MonoBehaviour 아님, `202` 2장). Awake 에서 캡슐·카메라 리그·지형 마스크를 받아 생성. 스스로 Update 하지 않고 상태와 컨트롤러가 호출한다
- 결과 구조체 `ClimbSurface { Point, Normal, SlopeAngle }` (파일 분리 가능)
- `TryAttach(out ClimbSurface)`: `202` 4장 1·2번. 캡슐 중심에서 카메라 전방의 수평 성분으로 SphereCast(`climbProbeRadius`, `climbProbeDistance`) → 실패하면 눈 위치에서 카메라 전방 그대로(pitch 포함) 한 번 더. 경사 ≥ `walkSlope` 인 면만 성공
- `TryFollow(...)`: 4장 5번. 이동 후 새 위치에서 벽 쪽(−n)으로 재탐지해 법선을 갱신. 잃으면 인접 방향 재탐지로 코너를 돈다. 전부 실패하면 false
- `TryFindLedge(...)`: 4장 6번. 위로 이동 중, 캡슐 상단 앞 레이가 벽을 못 찾고 그 앞 위에서 아래로 쏜 레이가 걷는 면을 찾으면 true + 올라설 발 위치
- 탐지에 쓰는 거리·오프셋은 전부 GameTuning (5장). 탐지는 `QueryTriggerInteraction.Ignore`, Terrain 레이어만

## 2. `ClimbingState` (`States/ClimbingState.cs`)

- `BodyFollowsCameraYaw => false`. 몸 yaw = 벽 법선 수평 성분의 반대 (4장 보강 규칙)
- **Rigidbody 권장 방식**: Enter 에서 오너 Rigidbody `isKinematic = true`, 이동은 `MovePosition`/`MoveRotation`. Exit 에서 `isKinematic = false`. 중력은 다음 상태 Enter 가 정한다. ⚠ `NetworkRigidbody` 가 kinematic 을 되돌리는지 확인하고 보고. 다른 방식을 쓰면 이유를 보고
- 부착: 캡슐 **중심축 선분**과 벽 평면의 최단 거리 = `climbAttachDistance` 가 되도록 스냅 (4장 보강 규칙). 속도 0
- 이동: 4장 4번 접선 기저 × `climbSpeed`. **D 키 = 화면 오른쪽** 이 되도록 부호를 맞춘다 (Unity 왼손 좌표). 매 FixedTick 후 `TryFollow` 로 법선·거리 재정렬
- Jump 무시 (Q15). 스태미나 소모 없음 — `// M1-3: 소모, 0 → Exhausted 이탈` 자리 주석만

## 3. `MantlingState` (`States/MantlingState.cs`)

- `BodyFollowsCameraYaw => false`. 입력 무시
- `mantleDuration` 동안 경로 보간: 먼저 위로(발이 윗면보다 높아질 때까지), 그다음 앞으로 발 위치까지. 이징(smoothstep 류). **순간이동 금지** (12.5). kinematic `MovePosition` 이므로 Rigidbody 보간이 카메라를 부드럽게 끈다
- 경로가 벽이나 윗면을 파고들지 않는다. 끝나면 Grounded

## 4. 전이 (`PlayerController.UpdateTransitions`, 3장 우선순위)

| 현재 | 조건 | 다음 |
|---|---|---|
| Grounded / Airborne | Climb 홀드 + 커서 잠김 + 재부착 지연 끝남 + `TryAttach` 성공 | Climbing |
| Climbing | 위 입력 중 `TryFindLedge` 성공 | Mantling (사유 `Mantled`) |
| Climbing | Climb 뗌 | Airborne (사유 `Released`) |
| Climbing | `TryFollow` 실패 | Airborne (사유 `LostSurface`) |
| Climbing | 아래 입력 중 발밑 지면 판정이 걷는 면 | Grounded |
| Mantling | 경로 완료 | Grounded |

- 우선순위 Mantling > Climbing > Airborne > Grounded 를 지킨다. 진입 조건은 기존 switch 앞에 추가한다
- 이탈 사유 enum `ClimbExitReason { None, Released, LostSurface, Mantled, Exhausted }` 를 두고 공개 속성 `LastClimbExitReason` 으로 노출 (`Exhausted` 는 M1-3 이 쓴다)
- 재부착 지연: Climbing 을 떠난 시각부터 `climbReattachDelay` 동안 Climbing 진입 금지
- 커서 규칙 (4장 보강): 커서 해제 중 Climb 무시. 재잠금 클릭은 등반 시작으로 치지 않는다 — 잠긴 뒤 새로 눌린 Climb 만 인정

## 5. GameTuning 필드 (`Core/GameTuning.cs`)

`202` 9장 M1-2 표를 그대로 추가한다. Tooltip 끝은 `M1-2 추가 — 202 9장`. 기존 필드는 건드리지 않는다.

| 필드 | 값 |
|---|---|
| `climbProbeRadius` / `climbProbeDistance` | 0.4 / 0.8 m |
| `climbAttachDistance` | 0.45 m |
| `mantleDuration` | 0.4 s |
| `climbReattachDelay` | 0.2 s |

- 이 밖에 필요한 탐지 오프셋(모서리 레이 높이·길이 등)은 추가하되 Tooltip 끝을 `M1-2 추가 — 202 9장 반영 필요` 로 하고 보고에 표로 적는다
- `Data/GameTuning.asset` 에 새 키가 초기값으로 저장되었는지 확인한다 (기존 값은 바꾸지 않는다)

## 6. 자동 검증 훅 (`PlayerController`)

- `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 블록의 공개 메서드 `SetDebugInput(Vector2 move, bool climbHeld, bool jumpPressed = false)` / `ClearDebugInput()`. 켜져 있는 동안 장치 입력 대신 이 값을 쓰고, **커서 잠금 규칙을 건너뛴다** (unity-mcp 로 돌릴 때 게임 뷰에 포커스가 없어 커서가 잠기지 않는다)
- 공개 읽기 전용: `LastClimbExitReason`, 현재 `ClimbSurface`(Climbing 중)
- M1-3·M1-4 도 이 훅으로 검증한다. 주석에 그렇게 적는다

## 7. 오버레이

- Climbing·Mantling 중 `BuildOverlayLine` 끝에 `| wall {경사:0}° | exit {LastClimbExitReason}` 추가

## 제약

- 수정 허용: 새 `ClimbSensor.cs`(+ `ClimbSurface`·`ClimbExitReason` 파일), `PlayerController.cs`, `States/ClimbingState.cs`, `States/MantlingState.cs`, `States/PlayerState.cs`·`States/AirborneState.cs`(전이에 꼭 필요할 때만, 보고), `Core/GameTuning.cs`, `Data/GameTuning.asset`(새 키만). **그 밖의 파일 수정 금지** — 카메라 리그, 프리팹 빌더, 씬·코스 빌더, 씬 파일 포함
- `Docs/`, `CLAUDE.md`, `Assets/Editor/McpAutoStartBootstrap.cs`, `.mcp.json` 수정 금지. `Docs/101` 항목 구현 금지
- **구현 금지**: 스태미나·상태이상·낙하 피해(M1-3), 손 뷰모델·HUD·착지 흔들림(M1-4), Hanging·로프·피톤, 등반 중 도약(Q15), 시선 pitch 네트워크 복제(M4)
- 입력·물리·오버레이는 오너만. 비오너는 NetworkTransform 이 벽에 붙은 캡슐을 보여 준다
- 매직 넘버 금지 → GameTuning. `Debug.Log` 금지 → `Log.*(LogCategory.Player, …)`
- 커밋 금지
- 토큰 절약: 확인은 스크린샷 대신 `execute_code` 로 한 줄 상태를 받고, `read_console` 은 스택 트레이스 없이. 위치는 `Course` 표에서 계산한다

## 완료 조건

**워커가 자동 확인** (unity-mcp, Sandbox_Climb 직접 플레이, 입력은 6장 훅)
1. 컴파일 에러 0, 우리 코드 경고 0. `GameTuning.asset` 에 5장 키 5개가 초기값으로 있다
2. 부착: (11.5, 0, 17.0) · yaw 0 에서 Climb 홀드 → 0.5초 안에 Climbing. 캡슐 축–벽 평면 최단 거리 = 0.45 ± 0.02 m, 몸 yaw 가 벽을 향함 (± 2°)
3. 오르기·맨틀: 이어서 위 입력 → 수직 상승 속도 = `climbSpeed × sin 85°` ± 10% → 10초 안에 Mantling → Grounded. 발 높이 = 10 ± 0.1 m, 이후 2초 동안 수평 이동 < 0.05 m. Mantling 지속 = `mantleDuration` ± 0.05 s, FixedUpdate 한 번당 위치 변화 < 0.35 m (순간이동 없음)
4. 놓기·재부착: `Height_85_H15` 에서 6 m 쯤 오른 뒤 Climb 뗌 → Airborne(`Released`). 0.1초 뒤 다시 홀드해도 지연 동안은 Airborne, 지연이 지나면 낙하 중 다시 Climbing
5. 볼록 코너: `Convex_85_H8`·`Convex_60_H8` 앞면에 붙어 2 m 오른 뒤 옆 입력 6초 → 그동안 Airborne 없음, 벽 법선의 수평 방향이 80° 이상 바뀜
6. 오목 코너: `Concave_85_H8`·`Concave_60_H8` 안뜰 안쪽 벽에 붙어 옆 입력 6초 → 그동안 Airborne 없음, 법선 수평 방향이 80° 이상 바뀜
7. 경계: `Boundary_44` 를 보고 Climb 홀드 → 붙지 않음 (걷는 면). `Boundary_46` → 붙음
8. 내려가기: `Height_85_H5` 에 붙어 1초 오른 뒤 아래 입력 → 3초 안에 Grounded, 발 높이 ≈ 0
9. 등반 중 Jump → 상태 유지 (Climbing)
10. 회귀: M1-1 자동 확인 6 (`Boundary_44` 정지·`Boundary_46` 미끄러짐), M0-3 자동 확인 4·5·8 (카메라 위치·SetLook·ReturnToLobby 정리) 그대로 통과. 에러 0

**사람이 확인** (보고에 체크리스트로 남긴다)
11. 벽에 붙는 순간 화면이 튀지 않는다
12. 벽에 붙은 채 위·아래를 둘러볼 수 있고 화면이 벽 안으로 잘리지 않는다
13. 조준점으로 벽 위쪽을 올려다보며 붙을 수 있다 (두 번째 탐지)
14. 볼록·오목 코너를 옆으로 돌 때 떨어지지 않고 어색하지 않다 (60°·85°)
15. 모서리에서 올라서는 0.4초가 부드럽고 멀미가 없다. 올라선 뒤 미끄러지지 않는다
16. Esc 로 커서를 푼 상태에서 클릭해도 벽에 붙지 않고, 잠긴 뒤 다시 누르면 붙는다
17. `Height_85_H20` 을 끝까지 오를 수 있다 (스태미나가 없으므로 정상 — 20 m 제한은 M1-3 이 검증)
18. MPPM `Client` 1개: 상대가 벽에 붙어 벽을 향한 캡슐로 보이고 오르내림이 부드럽게 복제된다

## 보고 형식 (`Docs/201_common.md` 8장)
```
결과: 완료 / 부분 완료 (남은 것)
만든 것 / 바꾼 것: 파일 목록 (경로)
GameTuning 추가 필드: 이름 | 값 | 용도 (5장 밖에서 추가한 것 표시)
문서와 달랐던 점: 무엇을 왜 (Rigidbody 처리 방식, NetworkRigidbody 영향, 코너 재탐지 방식, 모서리 판정 레이 배치 포함)
확인 방법: 자동 확인 1~10 결과(수치 포함), 사람 확인 11~18 절차
다음 워커에게: M1-3 이 알아야 할 것 — 스태미나 소모를 넣을 지점(Climbing FixedTick, 달리기, 점프), Exhausted 이탈을 거는 방법, 착지 순간(Airborne → Grounded)을 잡을 지점과 그때의 수직 속도 얻는 법, 디버그 입력 훅 사용법
```
