# 202 — 게임플레이 구현 (캐릭터 · 등반 · 스태미나 · 상태이상)

> 기획 근거: `100_game_design.md` 4·5장. 이 문서는 "어떻게 구현할지". 수치의 진실은 `GameTuning` 에셋 (`201` 4장).
> 소유 폴더: `Scripts/Runtime/Player/`, `Scripts/Runtime/Stamina/`. 마일스톤: M1 (등반), M3 (기절·운반).

## 1. 입력 매핑 (`PeakActions.inputactions`)

| 액션 | 키 | 비고 |
|---|---|---|
| Move | WASD / 왼쪽 스틱 | Vector2 |
| Look | 마우스 델타 / 오른쪽 스틱 | |
| Sprint | Shift (홀드) | |
| Jump | Space | |
| Crouch | Ctrl (홀드) | T1 |
| Climb | 마우스 좌클릭 (홀드) | **아이템을 들고 있어도 등반이 우선** ⚠ 확인 필요 (원작: 등반 중 아이템 사용 불가) |
| Interact | E | |
| UseItem | 마우스 우클릭 ⚠ | 원작은 좌클릭 사용·등반 겸용. 우리는 충돌 회피를 위해 우클릭 제안 — `301` Q4 |
| Slot1~4 | 1 2 3 4 | |
| Drop | Q | |
| Inventory | Tab (홀드) | T1 |
| Chat | Enter | T1 |
| Pause | Esc | |

## 2. 캐릭터 구조

```
Player (NetworkObject, Rigidbody, CapsuleCollider 1.8×0.35)
 ├─ PlayerController      상태 기계 (3장), 입력 → 이동
 ├─ ClimbSensor           표면 탐지 (4장)
 ├─ StaminaSystem         스태미나·보너스 (5장)
 ├─ StatusEffectStack     상태이상 목록 (6장)
 ├─ FallDamage            착지 속도 → 부상 (7장)
 ├─ Interactor            E 키 레이캐스트, 대상에게 사용 (8장)
 ├─ Inventory             206
 ├─ PlayerNetSync         205 — NetworkTransform(오너 권한) + PlayerState 복제
 ├─ PlayerCameraRig       1인칭 카메라 (오너만) — 12장
 ├─ PlayerLocalHud        HudRoot 생성 (오너만) — 204
 ├─ CameraTarget          눈 위치 (로컬 y 1.6). yaw·pitch 를 받는 피벗
 │   └─ (M1) FirstPersonHands   오너 전용 손 뷰모델 — 12.3
 └─ Visual                102 — 캡슐 + 코. 오너 화면에서는 그림자만
```

- Rigidbody: 비-kinematic, 회전 고정, `interpolation = Interpolate`. 등반 중에는 `useGravity = false` + 속도 직접 제어
- 카메라: **1인칭** (`301` D16). 상세는 12장
- 몸 회전: 기본적으로 **몸 yaw = 카메라 yaw** (매 물리 프레임). 등반처럼 몸이 벽을 향해야 하는 상태는 이를 끈다 (12.1)

## 3. 상태 기계

| 상태 | 진입 조건 | 동작 | 이탈 |
|---|---|---|---|
| **Grounded** | 바닥 접촉 (경사 < `θ_walk`) | 걷기·달리기·점프·앉기. 스태미나 회복 | 점프/낙하 → Airborne, Climb 입력 + 표면 → Climbing |
| **Airborne** | 접촉 없음 | 공중 제어 약간, 중력 | 착지 → Grounded (낙하 피해 판정), Climb 입력 + 표면 → Climbing |
| **Climbing** | Climb 홀드 + `ClimbSensor` 가 표면 발견 + 스태미나 > 0 | 표면 부착, 접선 이동, 초당 `c_climb` 소모, 회복 없음 | 손 놓음 → Airborne, 스태미나 0 → Airborne(탈진 플래그), 모서리 → Mantling |
| **Mantling** | Climbing 중 위쪽 모서리 감지 | 짧은 고정 이동 (0.4초)으로 올라섬. 입력 무시 | 완료 → Grounded |
| **Hanging** | 로프·피톤 잡음 (206) | 로프: 상하 이동, `c_rope` 소모. 피톤: 정지, 회복 | 놓음 → Airborne, 로프 끝 → Grounded/Climbing |
| **Carrying** (T1) | 기절자에게 E | Grounded/Airborne 와 동일하되 속도 −30%, **Climbing 진입 불가** ⚠ | E 다시 → 내려놓음 |
| **Unconscious** | 상태이상 합 ≥ 100 | 입력 무시, 눕힘, 중력만. 업힐 수 있음 | 합 < 100 → Grounded, `t_death` 경과 → Dead |
| **Dead** | 기절 타임아웃 / 즉사 구역 | 유령 카메라(자유 비행, 오너만), 콜라이더 off, 채팅만 | 부활 → Grounded |

- 상태 전이는 `PlayerController` 한 곳에서. 각 상태는 `Enter/Tick/Exit` 를 가진 클래스 (enum + switch 도 허용, 단 파일 분리)
- 전이 순서 우선순위: Dead > Unconscious > Mantling > Climbing > Hanging > Carrying > Airborne > Grounded

## 4. 등반 탐지와 이동 (`ClimbSensor`)

1. Climb 홀드 중 매 물리 프레임, 캡슐 중심에서 카메라 전방(수평 성분) 으로 `SphereCast(r=0.4, dist=0.8)` → 지형 레이어. 실패하면 **눈 위치에서 카메라 전방 그대로(pitch 포함)** 한 번 더 — 1인칭에서는 "조준점이 향한 벽에 붙는다" 가 직관이다 (벽 위쪽을 올려다보며 붙기) ⚠
2. 히트 법선 `n` 의 경사각 `θ = acos(n·up)`. `θ ≥ θ_walk` 이면 등반 가능 표면. `θ < θ_walk` 는 바닥이므로 부착하지 않음 (걸어감)
3. 부착: 위치를 표면에서 `r_attach` (0.45 m) 띄운 지점으로 스냅, 속도 0, 중력 off
4. 이동: 입력 `(x, y)` 를 표면 접선 기저로 투영 — 오른쪽 `t_r = normalize(cross(up, n))`, 위쪽 `t_u = normalize(cross(n, t_r))`. 속도 = `(x·t_r + y·t_u) · v_climb`
5. 이동 후 표면 재탐지: 새 위치에서 `-n` 방향 레이캐스트. 잃으면 좌우·상하 인접 4방향 재탐지 (코너 돌기). 전부 실패 → Airborne
6. **모서리 감지**: `y > 0` 이고 캡슐 상단 앞쪽 레이(위→앞)가 표면을 못 찾고, 그 앞 아래 레이가 바닥(경사 < `θ_walk`)을 찾으면 → Mantling
7. 스태미나: 매 프레임 `c_climb · dt` 소모 (정지 상태로 매달려 있어도 소모 ⚠ 확인 필요 — 원작은 매달려만 있어도 소모). 0 → 강제 이탈 + `exhaustedFall = true`

- 하이트맵 지형은 90° 벽이 없으므로 `θ_walk` ~ 89° 범위가 전부 등반면. 오버행은 T2 (`203` 4장)
- `θ_walk` 초기값 45° ⚠. 걷기 가능 경사와 등반면의 경계이며 `203` 의 발판/벽 경사 설계와 반드시 같은 값을 공유 (`GameTuning.walkSlope`)

## 5. 스태미나 (`StaminaSystem`)

```
maxStamina   = 100
effectSum    = Σ effects.amount (0..200)
usable       = max(0, maxStamina − effectSum)     // 실제 쓸 수 있는 최대치
stamina      = clamp(stamina, 0, usable)
```

| 규칙 | 구현 |
|---|---|
| 소모 | `TryConsume(amount)` — `stamina ≥ amount` 면 차감 후 true. 등반·달리기는 `dt` 곱해 호출. 부족하면 보너스에서 차감 |
| 회복 | Grounded/Hanging(피톤) 에서 `regenDelay`(0.2초) 후 초당 `regenRate`(20). Climbing·Airborne 회복 없음 |
| 보너스 | `bonus` 별도. 재생 없음, `stamina == 0` 일 때만 소모. 캠프파이어 +25, 음식 일부 |
| 기절 판정 | `usable ≤ 0` → `PlayerPhase.Unconscious`. 매 프레임 확인 |
| 기상 판정 | Unconscious 중 `usable > 0` → Alive (`stamina = 0` 부터 회복 시작) |

## 6. 상태이상 (`StatusEffectStack`)

- 자료구조: `List<(StatusKind, float)>`, 종류별 1항목. 표시 순서 = `StatusEffectDef.order`
- `Add(kind, amount)`, `Remove(kind, amount)`, `Set(kind, amount)` (무게용)
- 자연 회복: 매 프레임 종류별 `StatusEffectDef` 규칙 — `decayDelay` 초 동안 새 누적이 없으면 `decayRate`/초 감소. 허기·부상·무게는 `decayRate = 0`
- 상한: 종류별 100, 합계 200 (넘치는 만큼은 버림)
- 누적 원인 ("소스") 처리

| 소스 | 방식 |
|---|---|
| 허기 | `HungerTicker` 컴포넌트 — 캠프파이어 반경 밖에서 초당 `hunger_rate` |
| 구역 (안개·눈보라·용암·독물) | `HazardVolume` 트리거 안에 있는 플레이어에게 초당 `rate` 만큼 `Add`. **오너 클라이언트가 자기 플레이어에 대해 판정** (205 3장) |
| 낙하 | 7장 |
| 아이템 | 206 — 사용자 → 대상 오너에게 RPC |
| 무게 | `Inventory` 변경 이벤트 → `Set(Weight, Σ weight)` |

## 7. 낙하 피해 (`FallDamage`)

- Airborne 진입 시 최고점 추적 불필요. **착지 순간 수직 속도** `v = −velocity.y` 사용
- `v < v_safe` → 0. 아니면 `injury = lerp(5, 100, inverseLerp(v_safe, v_max, v))`. `exhaustedFall` 이면 ×1.5 ⚠
- 물 트리거(`WaterVolume.shallow`) 위 착지는 0
- 적용: `StatusEffectStack.Add(Injury, injury)` + 짧은 경직(0.3초, 입력 무시) + 카메라 흔들림 (부상량 비례, 12.4)

## 8. 상호작용 (`Interactor`)

- 카메라 전방 레이캐스트 2.5 m, `IInteractable` 탐색. HUD 에 프롬프트 표시 (`204`)
- 대상별 동작

| 대상 | 동작 |
|---|---|
| 가방 (`Luggage`) | 열기 → 아이템 스폰 (서버) |
| 바닥 아이템 | 줍기 (서버에 소유 요청) |
| 캠프파이어 | 점화 (조건 충족 시, 서버 판정) |
| 정상 표식 | 조명탄 사용 / T0 는 즉시 클리어 |
| 기절자 | 업기 (Carrying), 다시 E 로 내려놓기 |
| 팀원 (들고 있는 아이템이 사용 가능형이면) | UseItem 키로 **대상에게 사용** — 붕대·음식 |

## 9. 튜닝 초기값 (`GameTuning`, ⚠ 전부 확인 필요)

| 필드 | 값 | 근거 |
|---|---|---|
| `walkSpeed` / `sprintSpeed` | 4 / 6.5 m/s | |
| `jumpSpeed` | 5.5 m/s (약 1.5 m) | |
| `climbSpeed` | 1.5 m/s | 스태미나 100 ÷ 10/s = 10초 → 15 m |
| `climbCost` / `sprintCost` / `jumpCost` | 10/s / 5/s / 10 | `100` 4.1 |
| `ropeCost` | 4/s | 등반의 40% |
| `regenRate` / `regenDelay` | 20/s / 0.2 s | 원작 |
| `walkSlope` (`θ_walk`) | 45° | `203` 과 공유 |
| `fallSafeSpeed` / `fallMaxSpeed` | 8 / 25 m/s | `100` 4.5 |
| `exhaustedFallMultiplier` | 1.5 | |
| `hungerRate` | 0.06/s | `100` 4.2 |
| `unconsciousToDeath` | 60 s | `100` 4.4 |
| `carrySpeedMultiplier` | 0.7 | |
| `interactRange` | 2.5 m | |

**M0-2 가 추가한 필드** (2026-09-18 코드 기준 반영)

| 필드 | 값 | 근거 |
|---|---|---|
| `airControl` | 0.3 | 공중 제어 배율 (지상 가속도에 곱함) |
| `groundAccelTime` | 0.1 s | 지상 가감속 시간 |
| `lookSensitivity` | 0.15 | 마우스 델타 px → 도 |
| `pitchMin` / `pitchMax` | **−85° / 85°** | M0-2 는 −40/70 (3인칭). 1인칭은 발밑과 머리 위 벽을 봐야 하므로 M0-3 에서 확장 (D16) |
| `groundCheckRadius` / `groundCheckDistance` | 0.3 / 0.15 m | 지면 SphereCast |
| `spawnSpacing` | 1.5 m | `205` 4장 |
| ~~`turnSpeed`~~ | ~~10 rad/s~~ | M0-3 에서 **삭제** — 몸 yaw 가 카메라 yaw 를 즉시 따른다 (D16) |

**M0-3 가 추가하는 필드**

| 필드 | 값 | 근거 |
|---|---|---|
| `fieldOfView` | 65° (수직) ⚠ | `301` Q13 |

## 10. 조작감 체크리스트 (M1 게이트, `Sandbox_Climb`)

- [ ] 벽에 붙는 순간 튀지 않는다 (스냅 거리 0.45 m)
- [ ] 벽을 따라 좌우로 돌 때 코너에서 떨어지지 않는다 (경사 60°~85° 볼록·오목 코너)
- [ ] 모서리에서 자동으로 올라선다. 올라선 뒤 미끄러지지 않는다
- [ ] 스태미나 0 → 떨어진다. 떨어지는 동안 재부착 가능 (스태미나가 남아 있으면)
- [ ] 낙하 피해가 높이에 비례하고, 3 m 이하는 무피해
- [ ] 걷기 가능 경사와 등반 경사의 경계가 명확하다 (45° 근처에서 애매하게 미끄러지지 않음)
- [ ] 15 m 벽을 한 번에 오를 수 있고, 20 m 벽은 못 오른다 (수치 검증)
- [ ] **1인칭**: 벽에 붙은 채 위를 올려다보고 아래를 내려다볼 수 있다. 화면이 벽 안으로 잘리지 않는다
- [ ] **1인칭**: 모서리에 올라설 때 화면이 튀지 않는다 (맨틀 카메라 보간, 12.5)
- [ ] **1인칭**: 등반 중 손이 보여 "벽을 잡고 있다" 가 느껴진다 (12.3)
- [ ] 20분 동안 혼자 오르내려도 짜증보다 재미가 크다 (주관 — 아니면 M1 종료 금지). 멀미가 나면 재미 실패로 본다

## 11. 네트워크 고려 (`205` 요약)

- 이동·상태이상·스태미나: **오너 권한**. 서버는 검증하지 않고 복제만 (친구끼리 플레이)
- 외부 원인(아이템 사용, 구역)이 남에게 상태이상을 줄 때: 원인 측 → `ServerRpc` → 대상 오너 `ClientRpc` → 오너가 `Add`
- 기절·사망·부활 상태(`PlayerPhase`)는 `NetworkVariable` 로 복제 (캠프파이어 조건, HUD 용)

## 12. 1인칭 표현 (`301` D16)

원작 PEAK 는 1인칭이다. 3인칭·시점 토글은 없다. 디버깅은 Scene 뷰와 Multiplayer Play Mode 의 다른 플레이어 창으로 한다.

### 12.1 카메라 구성 (M0-3)

| 항목 | 값 |
|---|---|
| 리그 | `CameraRig.prefab`: Camera(MainCamera) + `CinemachineBrain`(Update·Blend = LateUpdate) + `CinemachineCamera` |
| 위치 | `CinemachineHardLockToTarget` → `CameraTarget` (눈높이 1.6 m, 댐핑 0) |
| 회전 | `CinemachineRotateWithFollowTarget` (댐핑 0). `CameraTarget` 월드 회전 = `Euler(pitch, yaw, 0)` 을 `PlayerCameraRig` 가 LateUpdate 에서 Brain 보다 먼저 확정 |
| 렌즈 | FOV = `GameTuning.fieldOfView` (런타임 적용), Near Clip 0.05 m (벽 밀착 시 잘림 방지 — 부착 거리 0.45 m 와 캡슐 반지름 0.35 m 사이 여유가 작다) |
| 제거 | Third Person Follow, Deoccluder — 1인칭에서는 관통할 거리가 없다 |
| 헤드밥 | 없음 (멀미, 등반 가독성) |
| 몸 회전 | 오너 FixedUpdate 에서 `Rigidbody.MoveRotation(yaw)`. 상태가 `BodyFollowsCameraYaw = false` 면 생략 — Climbing·Hanging·Mantling 은 몸이 벽 법선 반대를 향하고 카메라만 자유 (M1) |
| 입력 기준 | 지상 이동은 카메라 yaw 기준 (M0-2 와 동일). 등반 이동은 표면 접선 기준 (4장) |
| API | `PlayerCameraRig.Yaw`, `.Pitch`, `.YawRotation`, `.SetLook(yaw, pitch)` (스폰 방향·순간이동 디버그용) |

### 12.2 자기 몸 (M0-3)

- 오너 인스턴스의 `Visual` 은 **그림자만 렌더** (`ShadowCastingMode.ShadowsOnly`). 그림자는 발 위치·고도감 단서라 남긴다
- 다른 플레이어 인스턴스는 정상 렌더
- 통지는 `IVisualState.SetLocalView(bool)` 로만 (`102` 2장)

### 12.3 손 뷰모델 (M1)

- `CameraTarget` 아래 `FirstPersonHands` (오너만 활성): 작은 큐브 2개 (플레이어 색). 로직은 `IFirstPersonView.SetPose(HandPose, Vector3 surfaceNormal)` 로만 통지
- 포즈: `Hidden`(지상, 화면 밖 아래) · `Climbing`(좌우 손이 화면 상단 좌우에서 벽을 짚고, 이동 시 번갈아 뻗음) · `Hanging`(두 손 위로 모음) · `Mantling`(두 손이 아래로 밀어냄)
- 애니메이터 없음, 코드 보간. 벽 관통은 T0 에서 허용 (뷰모델 전용 카메라 스택은 T2) ⚠
- 들고 있는 아이템 표시는 T1 (`206` 2장)

### 12.4 착지·피해 피드백 (M1)

- `CinemachineImpulseSource` (플레이어) + `CinemachineImpulseListener` (CinemachineCamera). 낙하 부상량에 비례한 세기
- 부상 시 화면 가장자리 붉은 플래시는 HUD (`204` 2.2)

### 12.5 맨틀 (M1)

- 순간이동 금지. 0.4초 경로 보간 동안 카메라가 부드럽게 올라간다. 1인칭에서 순간이동은 멀미를 부른다 — **컷 불가**

### 12.6 기절·업힘·유령 (M3·M4)

| 상황 | 카메라 |
|---|---|
| 기절 (M3) | 눈높이를 바닥 0.3 m 로 0.5초 보간, 약간 기울임. 시선 허용 ⚠ `301` Q14. 비네트는 HUD |
| 업기 — 운반자 (M4) | 업힌 몸은 운반자 로컬에서 그림자만 (시야 가림 방지) |
| 업힘 — 기절자 (M4) | 운반자 등 위 눈높이에 고정, 시선 허용 |
| 유령 (M4) | 같은 `CinemachineCamera` 의 Follow 를 자유 비행 피벗으로 교체. 팀원 관전 전환은 Follow 대상만 바꾼다 |

### 12.7 시선 공유 (M4)

- 1인칭 협동에서 "저기 봐" 가 통하려면 남의 시선 방향이 보여야 한다. `pitch` 를 오너 권한 `NetworkVariable<sbyte>`(1° 단위)로 복제하고, 비오너 `Visual` 이 코(머리)를 기울인다 (`205` 3장)

---
관련 문서: `100_game_design.md` · `201_common.md` · `203_procgen.md` · `205_network.md` · `206_items.md`
