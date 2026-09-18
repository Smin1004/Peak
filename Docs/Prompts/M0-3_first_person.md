# [M0-3] 1인칭 전환 — 카메라 · 몸 회전 · 자기 몸 숨김 · 조준점 HUD 루트

> 발행 2026-09-18 (기획 세션). 근거: `Docs/301_decisions.md` **D16** (시점 = 1인칭 전용, 원작 동일, 토글 없음).
> 전제: **M0-2 결과물이 커밋된 상태**에서 시작한다. `git status` 에 M0-2 파일이 미커밋으로 보이면 작업하지 말고 사람에게 알린다.

너는 이 Unity 프로젝트(`C:\Users\Smin\Programming\Peak`)의 **워커 세션**이다. M0-2 는 3인칭 카메라로 완료되었고, 기획이 1인칭으로 바뀌었다. 아래 범위만 구현하고, 범위 밖은 건드리지 않는다.

## 먼저 읽을 것 (지정된 장만)
1. `CLAUDE.md`
2. `Docs/301_decisions.md` D16 한 줄
3. `Docs/202_gameplay.md` **12.1·12.2장**, 9장 (M0-2·M0-3 필드 표)
4. `Docs/204_ui.md` 2.2장 조준점 행, 3장
5. `Docs/102_required_assets.md` 2장

소스는 0장에 적힌 파일만 연다. 프로젝트 전체 탐색 금지.

## 0. M0-2 가 만든 것 (현재 상태)

| 대상 | 사실 |
|---|---|
| `Scripts/Editor/PlayerPrefabBuilder.cs` | 메뉴 `Peak > Setup > Rebuild Player Prefab`. `Player.prefab`, `CameraRig.prefab`, `Art/PlayerZeroFriction.physicMaterial` 생성 + `NetworkPrefabs.asset` 등록. 멱등 (`LoadPrefabContents` 로 열어 이름으로 찾아 값만 다시 설정). 헬퍼는 `M0SceneBuilder.EnsureFolder / EnsureChild / EnsureComponent / SetRef / LoadRequired / SetTransform`, 경로 상수 `M0SceneBuilder.ProjectRoot`, `.ThemeAssetPath` |
| Player 구조 | Root(`NetworkObject`, `Rigidbody`, `CapsuleCollider`, `NetworkTransform`(Owner), `NetworkRigidbody`, `PlayerCameraRig`, `PlayerController`) / 자식 `CameraTarget`(로컬 y = const `CameraTargetHeight` 1.5) / 자식 `Visual`(`PlayerVisual`) → `Capsule`, `Nose`(y 1.5) |
| CameraRig 구조 | 루트 / `Camera`(MainCamera, `Camera`, `CinemachineBrain`) / `CinemachineCamera`(`CinemachineCamera`, `CinemachineThirdPersonFollow`, `CinemachineDeoccluder`). 빌더 const: `ShoulderOffset`, `VerticalArmLength`, `CameraSide`, `CameraDistance`, `FollowDamping`, `Deoccluder*` |
| `Runtime/Player/PlayerCameraRig.cs` | `[DefaultExecutionOrder(-100)]`. 오너 `Activate(PeakActions)` 가 리그 인스턴스화 + `Follow`·`LookAt` = CameraTarget + 커서 잠금. `Deactivate()` 파괴. `Update` 에서 Look → `_yaw`(무제한)·`_pitch`(`pitchMin..pitchMax`). `LateUpdate` 에서 `cameraTarget.rotation = Euler(_pitch, _yaw, 0)`. 공개: `Yaw`, `Pitch`, `YawRotation`, `CameraTarget`, `IsActive`, `IsCursorLocked` |
| `Runtime/Player/PlayerController.cs` | 오너 `OnNetworkSpawn`: `ApplyTint()`(모든 인스턴스) → 입력 생성 → `_cameraRig.Activate` → 초기 상태 → 오버레이 등록. `FixedUpdate`: `ProbeGround` → `UpdateTransitions` → `CurrentState.FixedTick`. `GetWishDirection()` 는 카메라 yaw 기준. `FaceDirection(dir, dt)` 가 `turnSpeed` 로 이동 방향 회전 — **`GroundedState`·`AirborneState` 의 FixedTick 끝에서 호출**. `BuildOverlayLine()` |
| `Runtime/Player/States/PlayerState.cs` | 추상 기반: `Enter / Tick / FixedTick / Exit` |
| `Runtime/Visual/IVisualState.cs` | `void SetTint(Color)` 하나 |
| `Runtime/Visual/PlayerVisual.cs` | `Visual` 에 붙음. 자식 Renderer 전부에 `MaterialPropertyBlock` 으로 `_BaseColor`·`_Color` |
| `Runtime/Visual/VisualTheme.cs` | `playerColors[4]`, `terrainWalkColor`, `terrainWallColor`, `uiBackground`, `uiText`, `uiAccent` |
| `Runtime/Core/GameTuning.cs` | M0-2 필드: `airControl`, `groundAccelTime`, `turnSpeed`, `lookSensitivity`, `pitchMin`(−40), `pitchMax`(70), `groundCheckRadius`, `groundCheckDistance`, `spawnSpacing`. Tooltip 끝이 `M0-2 추가 — 202 9장 반영 필요` |
| 에셋 | `Data/GameTuning.asset`, `Data/VisualTheme.asset` — **cs 초기값을 바꿔도 기존 에셋 값은 안 바뀐다** |
| 오버레이 | `Peak.UI.DebugOverlay.Register/Unregister`, 오버레이 Canvas SortOrder 100 |
| 씬 | Game·Sandbox_Climb 에 카메라 없음 (리그가 담당). Sandbox_Climb 에 `Temp_Ramp30`·`Temp_Ramp60` |

## 목표 (한 문장)
오너는 눈높이 1인칭 카메라로 보고, 몸은 시선 yaw 를 따라 돌며, 자기 캡슐은 그림자만 보이고, 화면 중앙에 조준점이 있다. 다른 플레이어는 상대의 캡슐이 상대 시선 방향으로 도는 것을 본다.

## 1. CameraRig 프리팹 → 1인칭 (`PlayerPrefabBuilder`)

- `CinemachineCamera` 오브젝트에서 `CinemachineThirdPersonFollow`, `CinemachineDeoccluder` 를 **명시적으로 제거** (`EnsureComponent` 는 지우지 않는다. 있으면 `Object.DestroyImmediate(c, true)`, 없으면 무시 → 멱등)
- 추가: `CinemachineHardLockToTarget` (댐핑 0), `CinemachineRotateWithFollowTarget` (댐핑 0)
- 렌즈: Near Clip = const `FirstPersonNearClip` 0.05, Far Clip = const `FirstPersonFarClip` 1000. FOV 는 프리팹에 굽지 않는다 (런타임, 3장)
- `CinemachineBrain`: Update Method = LateUpdate, Blend Update Method = LateUpdate (Rigidbody 보간 위치를 프레임마다 따라가게)
- 3인칭 const(`ShoulderOffset` … `Deoccluder*`) 삭제
- 오브젝트 이름·프리팹 경로는 그대로 (`CameraRig.prefab`)

## 2. 눈높이 (`PlayerPrefabBuilder`)

- `CameraTargetHeight` 1.5 → **`EyeHeight` 1.6** (이름 변경). 근거 주석: `202` 12.1
- `Nose` 높이는 그대로 (남이 보는 앞면 표시)

## 3. `PlayerCameraRig` 수정

- `Activate`: `Follow = cameraTarget`, **`LookAt = null`** (RotateWithFollowTarget 이 Follow 회전을 쓴다). `virtualCamera.Lens.FieldOfView = tuning.fieldOfView` (구조체 Lens 를 꺼내 바꾸고 다시 대입)
- 신규 공개 메서드 `SetLook(float yaw, float pitch)`: yaw 는 0..360 정규화, pitch 는 `pitchMin..pitchMax` 클램프. 스폰 방향 지정·디버그 순간이동용 (`202` 12.1 API)
- `Activate` 의 초기 yaw 는 기존대로 몸 yaw, pitch 0
- 커서 잠금·Pause·Click 동작은 그대로
- 클래스 주석의 "3인칭" 설명을 1인칭으로 고친다

## 4. 몸 회전 = 카메라 yaw (`PlayerController`, `PlayerState`, 두 상태)

- `PlayerState` 에 `public virtual bool BodyFollowsCameraYaw => true;` 추가. 주석: "M1 의 Climbing·Hanging·Mantling 은 false 로 재정의 — 몸이 벽을 향한다 (202 12.1)"
- `PlayerController.FaceDirection` **삭제**. `GroundedState`·`AirborneState` 의 호출도 삭제
- `PlayerController.FixedUpdate` 끝에 (오너만): `if (CurrentState.BodyFollowsCameraYaw) _body.MoveRotation(_cameraRig.YawRotation);`
- `GameTuning.turnSpeed` **필드 삭제**. 에셋에 남은 키는 에셋을 다시 저장해 정리 (6장)

## 5. 자기 몸 숨김 (`IVisualState`, `PlayerVisual`, `PlayerController`)

- `IVisualState` 에 `void SetLocalView(bool isLocalView);` 추가. 주석: 오너 로컬 화면이면 true — 몸은 그림자만 렌더 (`202` 12.2)
- `PlayerVisual.SetLocalView`: 자식 Renderer 전부 `shadowCastingMode = isLocalView ? ShadowsOnly : On`
- `PlayerController.OnNetworkSpawn`: `ApplyTint()` 옆에서 **모든 인스턴스**가 `visual.SetLocalView(IsOwner)` 호출 (IVisualState 조회는 한 번만 하도록 정리해도 된다)

## 6. GameTuning·VisualTheme 필드

| 파일 | 변경 |
|---|---|
| `GameTuning.cs` | `turnSpeed` 삭제. `pitchMin` 초기값 −85, `pitchMax` 85 (Tooltip 에 "1인칭: 발밑·머리 위 벽을 볼 수 있어야 함"). 신규 `fieldOfView` = 65 (수직 도, `[Range(40, 110)]`, Tooltip `M0-3 추가 — 202 9장, 301 Q13`). M0-2 필드 Tooltip 끝의 `— 202 9장 반영 필요` 를 `— 202 9장` 으로 (기획 세션이 반영 완료) |
| `VisualTheme.cs` | 신규 `crosshairColor` = (1, 1, 1, 0.75), Header "HUD (204 2.2)" |
| `GameTuning.asset` | **값을 직접 갱신**: `pitchMin` −85, `pitchMax` 85, `fieldOfView` 65. 잔여 `turnSpeed` 키 제거 (SerializedObject 로 로드·`ApplyModifiedProperties`·`SetDirty`·`SaveAssets` 하면 정리된다. 안 되면 보고) |
| `VisualTheme.asset` | `crosshairColor` 가 초기값으로 들어갔는지 확인 |

- 에셋 값 갱신은 일회성이므로 빌더에 넣지 않는다 (빌더가 에셋 값을 덮어쓰면 사람이 튜닝한 값이 날아간다). unity-mcp 로 한 번 실행하고 보고에 적는다

## 7. 조준점 HUD 루트 (신규)

- `Scripts/Runtime/UI/HudRoot.cs` (`Peak.UI`): `[SerializeField] Image crosshair`. `ApplyTheme(VisualTheme)` → 조준점 색. `SetCrosshairVisible(bool)`. 주석: "M1 이후 스태미나 바 등 HUD 요소는 이 프리팹에 추가 (204 3장)"
- `Scripts/Runtime/Player/PlayerLocalHud.cs` (`Peak.Player`, MonoBehaviour, Player 루트): `[SerializeField] GameObject hudPrefab`. `Activate()` 인스턴스화 + 테마 적용, `Deactivate()` 파괴. `LateUpdate` 에서 `crosshair` 표시 = `PlayerCameraRig.IsCursorLocked`
- `PlayerController`: 오너 스폰 시 `_cameraRig.Activate` 다음에 `PlayerLocalHud.Activate()`, `DeactivateLocal` 에서 `Deactivate()`
- `Prefabs/UI/HudRoot.prefab` — **`PlayerPrefabBuilder` 가 생성** (같은 메뉴, Player 보다 먼저): 루트 `Canvas`(Screen Space Overlay, SortOrder = const `HudSortOrder` 10), `CanvasScaler`(Scale With Screen Size, 1920×1080, match 0.5), GraphicRaycaster 없음. 자식 `Crosshair`: `Image`, 중앙 앵커, 크기 const `CrosshairSize` 6 px, 스프라이트 = 내장 `UI/Skin/Knob.psd` (`AssetDatabase.GetBuiltinExtraResource<Sprite>`), `raycastTarget = false`. 색은 프리팹에 굽지 않고 `ApplyTheme` 에서
- Player 프리팹에 `PlayerLocalHud` 추가 + `hudPrefab` 참조 연결

## 8. 오버레이

- `BuildOverlayLine` 끝에 `| look {yaw:0} / {pitch:0}` 추가

## 제약

- 수정 허용: `PlayerPrefabBuilder.cs`, `PlayerCameraRig.cs`, `PlayerController.cs`, `PlayerState.cs`, `GroundedState.cs`, `AirborneState.cs`, `IVisualState.cs`, `PlayerVisual.cs`, `GameTuning.cs`, `VisualTheme.cs`, 두 에셋(6장), 신규 `HudRoot.cs`·`PlayerLocalHud.cs`·`HudRoot.prefab`, 빌더가 재생성하는 `Player.prefab`·`CameraRig.prefab`. **그 밖의 파일 수정 금지** (`M0SceneBuilder.cs` 포함 — 씬 변경이 필요하면 멈추고 보고)
- `Docs/`, `CLAUDE.md`, `Assets/Editor/McpAutoStartBootstrap.cs`, `.mcp.json` 수정 금지. `Docs/101` 항목 구현 금지
- **구현 금지**: 3인칭·시점 토글, 헤드밥, 손 뷰모델(M1), Impulse 흔들림(M1), 시선 pitch 네트워크 복제(M4), 등반·스태미나·상태이상·상호작용, 조준점 외 HUD
- 매직 넘버 금지: 런타임 수치는 GameTuning, 프리팹에 굽는 수치는 빌더 const
- `Debug.Log` 금지 → `Log.*(LogCategory.Player 또는 UI, …)`
- 커밋 금지
- 토큰 절약: 확인은 스크린샷 대신 `execute_code` 로 한 줄 상태를 받고, `read_console` 은 스택 트레이스 없이

## 완료 조건

**워커가 자동 확인** (unity-mcp)
1. `Rebuild Player Prefab` 두 번 → 두 번째 실행 후 에셋 diff 없음. 이어서 `Rebuild M0 Scenes` 두 번 → diff 없음. 컴파일 에러 0, 우리 코드 경고 0
2. `CameraRig.prefab`: ThirdPersonFollow·Deoccluder 없음, HardLockToTarget·RotateWithFollowTarget 있음, Near Clip 0.05, Brain Update Method LateUpdate. `Player.prefab`: `CameraTarget` 로컬 y 1.6, `PlayerLocalHud.hudPrefab` 연결. `HudRoot.prefab` 존재
3. `GameTuning.asset`: `pitchMin` −85, `pitchMax` 85, `fieldOfView` 65, 파일에 `turnSpeed` 문자열 없음. 코드 전체에 `turnSpeed`·`FaceDirection` 참조 없음
4. Game 직접 플레이 (몇 프레임 후): MainCamera 월드 위치와 `CameraTarget` 월드 위치 차이 < 0.01 m, 카메라 높이 − 플레이어 발 높이 ≈ 1.6, 카메라 FOV = 65
5. 같은 플레이 중 `SetLook(90, 30)` 호출 → 0.2초 후 플레이어 몸 yaw 가 90 ± 1°, 카메라 pitch 30 ± 1°. `SetLook(0, 120)` → pitch 85 로 클램프
6. 오너 플레이어 `Visual` 아래 Renderer 전부 `ShadowsOnly`
7. `HudRoot` 인스턴스 정확히 1개, `Crosshair` 활성. 커서 잠금을 코드로 해제 → 다음 프레임 `Crosshair` 비활성
8. Boot → Lobby → [싱글 시작] → 스폰 → `ReturnToLobby()` → PlayerObject 0, CameraRig 0, HudRoot 0, 에러 0 → 다시 [싱글 시작] 정상
9. 회귀: Sandbox_Climb 에서 `Temp_Ramp30` 위 2초 → 수평 이동 < 0.05 m, Grounded. `Temp_Ramp60` 위 2초 → 미끄러짐, Grounded 아님

**사람이 확인** (보고에 체크리스트로 남긴다)
10. 1인칭으로 둘러볼 때 발밑과 머리 위까지 볼 수 있고, 자기 캡슐이 화면을 가리지 않으며, 자기 그림자는 보인다
11. WASD 가 보는 방향 기준이고, 달리면서 마우스를 돌려도 화면이 떨리지 않는다 (Rigidbody 보간·카메라 사이 지터 없음)
12. `Temp_Ramp60` 에 몸을 바짝 붙이고 벽을 봐도 화면이 벽 안으로 잘리지 않는다
13. 화면 중앙에 작은 조준점이 있고 Esc 로 커서를 풀면 사라졌다가 클릭하면 다시 보인다
14. MPPM `Client` 1개: 내 화면에 내 몸은 없고 상대 캡슐은 보인다. 상대가 마우스를 돌리면 상대 캡슐(코)이 그 방향으로 돈다
15. 5분 돌아다녀도 어지럽지 않다 (어지러우면 FOV 값과 함께 보고 — `301` Q13)

## 보고 형식 (`Docs/201_common.md` 8장)
```
결과: 완료 / 부분 완료 (남은 것)
만든 것 / 바꾼 것: 파일 목록 (경로)
삭제한 것: 필드·메서드·컴포넌트·const
GameTuning / VisualTheme 변경: 이름 | 값 | 비고 (표)
문서와 달랐던 점: 무엇을 왜 (Cinemachine 컴포넌트 선택, Lens 설정 방식, 에셋 잔여 키 정리 결과 포함)
확인 방법: 메뉴 실행 순서, 자동 확인 1~9 결과, 사람 확인 10~15 절차
다음 워커에게: M1 이 알아야 할 것 — BodyFollowsCameraYaw 재정의 지점, SetLook 사용처, 손 뷰모델을 CameraTarget 아래 붙일 때 주의점, HudRoot 에 스태미나 바를 추가하는 방법
```
