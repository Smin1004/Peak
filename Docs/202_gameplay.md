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
 ├─ CameraRig (오너만)    3인칭 오빗
 └─ Visual                102 — 캡슐 + 코
```

- Rigidbody: 비-kinematic, 회전 고정, `interpolation = Interpolate`. 등반 중에는 `useGravity = false` + 속도 직접 제어
- 카메라: 오너 로컬. 오빗 거리 4 m, 벽 충돌 시 당김. Cinemachine 3 `ThirdPersonFollow` 또는 직접 구현 ⚠ (`201` 1.1)

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

1. Climb 홀드 중 매 물리 프레임, 캡슐 중심에서 카메라 전방(수평 성분) 으로 `SphereCast(r=0.4, dist=0.8)` → 지형 레이어
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
- 적용: `StatusEffectStack.Add(Injury, injury)` + 짧은 경직(0.3초, 입력 무시) + 카메라 흔들림

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

## 10. 조작감 체크리스트 (M1 게이트, `Sandbox_Climb`)

- [ ] 벽에 붙는 순간 튀지 않는다 (스냅 거리 0.45 m)
- [ ] 벽을 따라 좌우로 돌 때 코너에서 떨어지지 않는다 (경사 60°~85° 볼록·오목 코너)
- [ ] 모서리에서 자동으로 올라선다. 올라선 뒤 미끄러지지 않는다
- [ ] 스태미나 0 → 떨어진다. 떨어지는 동안 재부착 가능 (스태미나가 남아 있으면)
- [ ] 낙하 피해가 높이에 비례하고, 3 m 이하는 무피해
- [ ] 걷기 가능 경사와 등반 경사의 경계가 명확하다 (45° 근처에서 애매하게 미끄러지지 않음)
- [ ] 15 m 벽을 한 번에 오를 수 있고, 20 m 벽은 못 오른다 (수치 검증)
- [ ] 20분 동안 혼자 오르내려도 짜증보다 재미가 크다 (주관 — 아니면 M1 종료 금지)

## 11. 네트워크 고려 (`205` 요약)

- 이동·상태이상·스태미나: **오너 권한**. 서버는 검증하지 않고 복제만 (친구끼리 플레이)
- 외부 원인(아이템 사용, 구역)이 남에게 상태이상을 줄 때: 원인 측 → `ServerRpc` → 대상 오너 `ClientRpc` → 오너가 `Add`
- 기절·사망·부활 상태(`PlayerPhase`)는 `NetworkVariable` 로 복제 (캠프파이어 조건, HUD 용)

---
관련 문서: `100_game_design.md` · `201_common.md` · `203_procgen.md` · `205_network.md` · `206_items.md`
