# [M1-3] 생체 값 — 스태미나 · 상태이상(허기·부상) · 낙하 피해 · 소모 연결

> 발행 2026-09-18 (기획 세션). M1 분할의 세 번째 워커 (`300` M1 워커 분할 표). M1-2 의 무한 등반에 비용을 붙여 "15 m 는 오르고 20 m 는 못 오른다" 를 만든다.
> 전제: **M1-2 가 커밋된 상태**. `git status` 에서 `Assets/` 아래에 커밋 안 된 변경이 있으면 작업하지 말고 사람에게 알린다 (`Docs/` 변경은 기획 세션 것이므로 무시).

너는 이 Unity 프로젝트(`C:\Users\Smin\Programming\Peak`)의 **워커 세션**이다. 아래 범위만 구현하고, 범위 밖은 건드리지 않는다. **HUD·화면 효과는 이번 범위가 아니다** (M1-4).

## 먼저 읽을 것 (지정된 장만)
1. `CLAUDE.md`
2. `Docs/100_game_design.md` 4.1·4.2·4.5장
3. `Docs/202_gameplay.md` **5·6·7장**, 9장 M1-3 필드 표와 그 위 "등반 가능 높이" 줄, 10장 체크리스트
4. `Docs/201_common.md` 4장 에셋 표, 5장 `PlayerVitals`·`StatusKind`·`PlayerPhase`
5. `Docs/204_ui.md` 4장 (F4)
6. `Docs/301_decisions.md` Q11·Q15 두 줄

소스는 0장에 적힌 파일만 연다. 프로젝트 전체 탐색 금지.

## 0. 현재 상태 (그대로 사용)

| 대상 | 사실 |
|---|---|
| `Runtime/Player/PlayerController.cs` | 오너 `FixedUpdate`: `ProbeGround()` → `UpdateTransitions()` → `CurrentState.FixedTick` → 몸 회전. 전이: Mantling 완료 → Grounded, Climbing 은 `UpdateClimbingTransitions()`(모서리 → 뗌 → 표면 잃음 → 아래로 내려와 섬 순), Grounded/Airborne + `CanStartClimb()` + `TryAttach` → Climbing, 그다음 기존 switch (Grounded 점프 → `Jump()`, Airborne 착지 → Grounded). `ExitClimbing(reason, next)` 가 `LastClimbExitReason` 과 시각을 기록. `SprintHeld` 는 `GroundedState`·`AirborneState` 가 목표 속도에 쓴다 |
| 이탈 사유 | `ClimbExitReason { None, Released, LostSurface, Mantled, Exhausted }`. `Exhausted` 는 아직 아무도 쓰지 않는다 |
| `States/ClimbingState.cs` | `FixedTick` 끝에 `// M1-3: 소모, 0 → Exhausted 이탈` 자리. 입력은 `Controller.MoveInput` |
| 디버그 입력 훅 | `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` 의 `SetDebugInput(Vector2 move, bool climbHeld, bool jumpPressed = false)` / `ClearDebugInput()`. 켜져 있으면 커서 규칙을 건너뛴다. **지금은 달리기를 항상 false 로 둔다** (4장에서 확장) |
| GameTuning | `climbCost` 10, `sprintCost` 5, `jumpCost` 10, `regenRate` 20, `regenDelay` 0.2, `fallSafeSpeed` 8, `fallMaxSpeed` 25, `exhaustedFallMultiplier` 1.5, `climbSpeed` 1.5, M1-2 등반 필드들 |
| 프리팹 빌더 | `Scripts/Editor/PlayerPrefabBuilder.cs` (메뉴 `Peak > Setup > Rebuild Player Prefab`, 멱등, `M0SceneBuilder.EnsureComponent` 등 헬퍼 사용) |
| 테스트 | `Scripts/Tests/` (asmdef `Peak.Tests`, EditMode, `Peak.Runtime` 참조). 예시 `RunConfigSerializationTests.cs` |
| 폴더 | `Scripts/Runtime/Stamina/` 는 아직 없다 (`201` 3장: StaminaSystem, StatusEffectStack, 정의 타입) |
| 코스 | `Scripts/Editor/ClimbCourseBuilder.cs` 의 `Course` 표 (읽기만). Ledge 형태는 앞면 경사 + 평평한 윗면 + **수직 뒷면·옆면** |

## 목표 (한 문장)
등반·달리기·점프가 스태미나를 쓰고 지상에서 회복되며, 스태미나가 바닥나면 벽에서 떨어지고, 착지 속도에 비례한 부상이 상태이상으로 쌓여 쓸 수 있는 스태미나를 줄인다.

## 1. 순수 로직 (`Scripts/Runtime/Stamina/`, `Peak.Stamina`) — EditMode 로 시험할 수 있게 MonoBehaviour 아님

- `StatusKind`, `PlayerPhase` enum: `201` 5장 그대로
- `StatusEffectDef` (ScriptableObject, `CreateAssetMenu`): `kind`, `displayName`(한국어), `color`, `order`, `maxAmount`(100), `decayDelay`, `decayRate`
- `StatusEffectStack`: 생성자에 정의 목록과 합계 상한(`statusEffectTotalCap`). `Add` / `Remove` / `Set` / `Get` / `Sum` / `Tick(dt)` / `Clear`. 종류별 1항목, 종류별 상한과 합계 상한에서 넘치는 만큼 버림, 자연 회복은 정의대로 (`202` 6장). 정의가 없는 종류는 거부하고 `Log.Warn`. 표시 순서 = `order`
- `StaminaSystem`: `Stamina`, `Bonus`, `Usable(effectSum)` = max(0, `maxStamina` − 합). `TrySpend`(이산) / `Drain`(연속) / `Tick(canRegen, dt)` / `AddBonus` / `Reset`. 규칙은 `202` 5장 표 그대로 (스태미나 먼저·보너스 나중, 회복 지연, usable 로 클램프)
- `FallDamage.ComputeInjury(float landingSpeed, bool exhausted, GameTuning tuning)`: `202` 7장 공식. 순수 함수

## 2. `PlayerVitals` (새 파일 `Stamina/PlayerVitals.cs`, `NetworkBehaviour`, Player 루트)

- 스택·스태미나를 소유하고 `Phase`(Alive/Unconscious) 를 `202` 5장 기절·기상 판정대로 계산한다. Dead 와 기절 상태 기계 전이는 **M3** — 지금은 값과 로그 한 줄만
- `[SerializeField] StatusEffectDef[] effectDefs` (빌더가 연결)
- 오너만 동작. 스스로 FixedUpdate 하지 않고 PlayerController 가 `FixedTick(bool canRegen, float dt)` 를 호출한다 (순서 고정)
- `HasStamina` (= 스태미나 + 보너스 > 0), `ApplyFall(float speed, bool exhausted)` → 부상량 반환
- **Peak.Player 를 참조하지 않는다** (의존 방향 Player → Stamina)
- 네트워크 복제는 **M4** (`205` 3장). 복제할 값이 이 클래스 한곳에 모여 있으면 된다. 주석에 적는다
- 오너만 오버레이 줄 `vitals`: `stamina 73.2 +0 | usable 88 | 허기 0 부상 12 | Alive`
- 개발 전용(`#if UNITY_EDITOR || DEVELOPMENT_BUILD`) 공개 메서드: `DebugSet(float stamina, float bonus)`, `DebugAddEffect(StatusKind kind, float amount)`, `DebugReset()`. **F4** 키(`Keyboard.current`, DebugOverlay 의 F3 처럼 직접 읽기) = `DebugReset()` (`204` 4장)

## 3. `StatusEffectDef` 에셋 (`PlayerPrefabBuilder`)

- `Data/StatusEffectDef/Hunger.asset`, `Injury.asset` 을 **없을 때만** 만든다. 있으면 값을 건드리지 않는다 (사람이 튜닝한 값 보호)
- 초기값: 허기 = 주황, order 0 / 부상 = 빨강, order 1. 둘 다 `maxAmount` 100, `decayDelay` 0, `decayRate` 0 (`100` 4.2)
- Player 프리팹 루트에 `PlayerVitals` 추가, `effectDefs` 에 두 에셋 연결. 두 번 실행해도 diff 없음

## 4. 소모·회복·낙하 연결 (`PlayerController`, 상태들)

| 지점 | 규칙 |
|---|---|
| 달리기 | 달리기 = Sprint 홀드 + 이동 입력 + `HasStamina`. Grounded 에서 달리는 동안 `Drain(sprintCost·dt)`. 스태미나가 없으면 걷기 속도. `SprintHeld` 를 쓰던 두 상태는 이 판정(예: `IsSprinting`)을 쓴다 |
| 점프 | `TrySpend(jumpCost)` 가 true 일 때만 점프 |
| 등반 진입 | `CanStartClimb()` 에 `HasStamina` 추가 |
| 등반 중 | `ClimbingState` 자리에서 `Drain(climbCost·dt)`, 이동 입력이 없으면 × `climbIdleCostMultiplier` (Q11 ⚠). 바닥나면 컨트롤러가 `ExitClimbing(Exhausted, Airborne)` — 모서리 판정보다는 뒤, 뗌보다는 앞 |
| 회복 | `canRegen` = 상태가 Grounded. Mantling·Climbing·Airborne 은 회복 없음 |
| 착지 | Airborne → Grounded 전이 때만. 착지 속도는 **Airborne 동안 매 물리 스텝 기록한 수직 속도 중 착지 직전 값** (`202` 7장 측정 주의). 탈진 배율은 `Exhausted` 이탈 뒤 첫 착지 한 번만 |
| 경직 | 부상 > 0 인 착지 뒤 `landingStunDuration` 동안 이동·점프·등반 입력 무시 |
| 착지 이벤트 | `PlayerController` 에 `public event Action<float, float> Landed` (착지 속도, 부상량). M1-4 가 흔들림·플래시를 건다. 부상 0 착지도 발생 |

- 디버그 훅 확장: `SetDebugInput(Vector2 move, bool climbHeld, bool jumpPressed = false, bool sprintHeld = false)` (기존 호출과 호환)
- 컨트롤러 오버레이 줄 끝에 마지막 착지 `| land 14.0 m/s → 38` 추가

## 5. GameTuning 필드 (`Core/GameTuning.cs`)

`202` 9장 M1-3 표를 그대로 추가한다. Tooltip 끝은 `M1-3 추가 — 202 9장`. 기존 필드는 건드리지 않는다.

| 필드 | 값 |
|---|---|
| `maxStamina` | 100 |
| `statusEffectTotalCap` | 200 |
| `fallMinInjury` / `fallMaxInjury` | 5 / 100 |
| `landingStunDuration` | 0.3 s |
| `climbIdleCostMultiplier` | 1 |

- 이 밖에 수치가 필요하면 추가하되 Tooltip 끝을 `M1-3 추가 — 202 9장 반영 필요` 로 하고 보고에 표로 적는다
- `Data/GameTuning.asset` 에 새 키가 초기값으로 저장되었는지 확인한다 (기존 값은 바꾸지 않는다)

## 6. EditMode 테스트 (`Scripts/Tests/`)

- `StaminaSystemTests`: usable 클램프, 스태미나 → 보너스 차감 순서, `TrySpend` 부족 시 아무것도 안 뺌, `Drain` 이 바닥에서 멈춤, 회복 지연·속도
- `StatusEffectStackTests`: 종류별 상한 100, 합계 상한 200 (테스트 안에서 정의를 3개 이상 만들어 확인), `Set`, 정의 없는 종류 거부, 자연 회복 지연
- `FallDamageTests`: v < 8 → 0, v = 8 → 5, v = 25 → 100, v = 30 → 100, 탈진 ×1.5
- 튜닝은 `ScriptableObject.CreateInstance<GameTuning>()` 기본값을 쓴다

## 제약

- 수정 허용: 새 `Stamina/` 파일들, `PlayerController.cs`, `States/GroundedState.cs`·`AirborneState.cs`·`ClimbingState.cs`, `Core/GameTuning.cs`, `Data/GameTuning.asset`(새 키만), `Data/StatusEffectDef/` 에셋, `PlayerPrefabBuilder.cs`, 빌더가 다시 만드는 `Player.prefab`, 새 테스트 파일. **그 밖의 파일 수정 금지** — 카메라 리그, HUD(`HudRoot`·`PlayerLocalHud`), 씬·코스 빌더, 씬 파일 포함
- `Docs/`, `CLAUDE.md`, `Assets/Editor/McpAutoStartBootstrap.cs`, `.mcp.json` 수정 금지. `Docs/101` 항목 구현 금지
- **구현 금지**: 스태미나 바·부상 플래시·착지 흔들림·손(M1-4), 허기 누적 소스 `HungerTicker`(M3), 기절·사망 상태 전이(M3), 음식·붕대·캠프파이어, 네트워크 복제(M4), 등반 중 도약(Q15 미결)
- 매직 넘버 금지 → GameTuning 또는 StatusEffectDef. `Debug.Log` 금지 → `Log.*(LogCategory.Player, …)`
- 커밋 금지
- 토큰 절약: 확인은 스크린샷 대신 `execute_code` 로 한 줄 상태를 받고, `read_console` 은 스택 트레이스 없이. 위치는 `Course` 표에서 계산한다

## 완료 조건

**워커가 자동 확인** (unity-mcp, Sandbox_Climb 직접 플레이, 입력은 디버그 훅)
1. 컴파일 에러 0, 우리 코드 경고 0. 6장 EditMode 테스트 전부 통과, 기존 `RunConfigSerializationTests` 도 통과. `GameTuning.asset` 에 5장 키가 초기값으로 있다. `Rebuild Player Prefab` 두 번 → diff 없음
2. 15 m: 풀 스태미나로 `Height_85_H15` 를 위 입력으로 오른다 → Mantling → 윗면 Grounded. 남은 스태미나 > 0 (보고에 값, 예상 약 10)
3. 20 m: `Height_85_H20` 을 오르다 스태미나 0 → Airborne, `LastClimbExitReason = Exhausted`. 착지 부상 = 탈진 배율이 적용된 값 (보고에 착지 속도·부상량, 예상 15 m 안팎 높이에서 떨어져 약 84)
4. 낙하 표 — 각 윗면에서 **수직 뒷면 쪽으로** 걸어 떨어진다. 기록된 착지 속도 = √(2gh) ± 5 %
   - `Drop_H3` → 부상 0
   - `Drop_H4` → 약 10 (± 3)
   - `Height_85_H10` → 약 38 (± 10 %)
   - `Height_85_H20` 윗면(순간이동으로 올림) → 약 71 (± 10 %)
5. 달리기: 스태미나 100 에서 4초 달리기 → 20 ± 2 감소, 속도 6.5. `DebugSet(0, 0)` 에서 달리기 입력 → 속도 4.0. 멈춘 뒤 0.2초 후 회복 시작, 0 → 100 에 5.2 ± 0.2초
6. 점프: 100 → 90. `DebugSet(5, 0)` → 점프 안 함. `DebugSet(5, 10)` → 점프, 스태미나 0·보너스 5
7. 정지 매달림: 붙은 채 입력 없이 2초 → 20 ± 2 감소
8. 등반 차단: `DebugSet(0, 0)` 에서 벽을 보고 Climb 홀드 → 붙지 않음
9. 상태이상: 부상 30 추가 → usable 70, 스태미나 ≤ 70. 허기 50 + 부상 60 → usable 0, `Phase = Unconscious`, 로그 한 줄. 부상 150 추가 → 100 에서 잘림
10. 경직: 부상이 생긴 착지 직후 이동 입력을 넣어도 `landingStunDuration` 동안 수평 속도 ≈ 0, 그 뒤 움직인다. `Landed` 이벤트가 착지마다 한 번 발생
11. `DebugReset()` → 스태미나 100, 보너스 0, 상태이상 0, Alive
12. 회귀: M1-2 자동 확인 2·3·5·6·7·8 (풀 스태미나에서), M1-1 자동 확인 6, M0-3 자동 확인 4·5·8 그대로 통과. 에러 0

**사람이 확인** (보고에 체크리스트로 남긴다)
13. 15 m 벽은 스태미나를 조금 남기고 올라서고, 20 m 벽은 중간에 손이 풀린다
14. 탈진으로 떨어진 것이 스스로 놓은 같은 높이 낙하보다 확실히 더 아프다 (오버레이 숫자)
15. `Drop_H3` 은 뛰어내려도 무피해, `Drop_H4` 는 작은 부상
16. 달리기 소모와 회복 템포가 답답하지 않다. 착지 경직 0.3초가 거슬리지 않다
17. 가만히 매달려도 스태미나가 주는 것(Q11 제안)이 긴장감을 주는지, 억울한지 — 느낌을 보고에 한 줄
18. F4 로 스태미나·상태이상이 초기화된다

## 보고 형식 (`Docs/201_common.md` 8장)
```
결과: 완료 / 부분 완료 (남은 것)
만든 것 / 바꾼 것: 파일 목록 (경로)
GameTuning 추가 필드: 이름 | 값 | 용도 (5장 밖에서 추가한 것 표시)
문서와 달랐던 점: 무엇을 왜 (착지 속도 측정 방식, 소모 호출 위치, 경직 처리 방식 포함)
확인 방법: 자동 확인 1~12 결과(수치 포함), 사람 확인 13~18 절차
다음 워커에게: M1-4 가 알아야 할 것 — Landed 이벤트 구독 방법, PlayerVitals 에서 바 그리기에 필요한 값(스태미나·보너스·usable·종류별 양·정의 색·순서) 읽는 법, 등반 상태·이동 입력으로 손 포즈를 정할 때 쓸 값
```
