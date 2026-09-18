# [M1-1] Sandbox_Climb 등반 코스 — 에디터 빌더 · 메시 지형 · 경사 색

> 발행 2026-09-18 (기획 세션). M1 분할의 첫 워커 (`300` M1 워커 분할 표). 등반 로직(M1-2)이 검증에 쓸 벽을 먼저 만든다.
> 전제: **M0-3 결과물이 커밋된 상태**에서 시작한다. `git status` 에 M0-3 파일(`HudRoot.cs`, `PlayerLocalHud.cs` 등)이 미커밋으로 보이면 작업하지 말고 사람에게 알린다.

너는 이 Unity 프로젝트(`C:\Users\Smin\Programming\Peak`)의 **워커 세션**이다. 아래 범위만 구현하고, 범위 밖은 건드리지 않는다. **플레이어 코드는 이번 범위가 아니다.**

## 먼저 읽을 것 (지정된 장만)
1. `CLAUDE.md`
2. `Docs/202_gameplay.md` 4장 첫 두 항목(부착 거리·경사 경계), 10장 체크리스트
3. `Docs/100_game_design.md` 4.5장 (낙하 높이 기준)
4. `Docs/102_required_assets.md` 2장 (색은 MaterialPropertyBlock)
5. `Docs/201_common.md` 3·4장

소스는 0장에 적힌 부분만 연다. 프로젝트 전체 탐색 금지.

## 0. 현재 상태 (그대로 사용)

| 대상 | 사실 |
|---|---|
| Sandbox_Climb 구성 | `M0SceneBuilder.BuildSandboxClimb`: `Ground` Plane (Terrain 레이어, 크기 const `SandboxGroundMeters` = 50 m), `SpawnPoint` (0, 1, 0), Directional Light, Bootstrapper, `Temp_Ramp30` (x −5)·`Temp_Ramp60` (x +5). 경사는 z 4 에서 +Z 로 올라가고 크기 4×0.5×6 m |
| 빌더 헬퍼 (`internal`) | `M0SceneBuilder.EnsureFolder`, `EnsureChild`, `EnsureComponent<T>`, `SetRef`, `LoadRequired<T>`, `SetTransform`, 상수 `ProjectRoot`, `ThemeAssetPath`. `BuildScene`·`EnsureRoot`·`DeleteRoot` 는 `private` (씬 열기·저장 방식은 `BuildScene` 을 읽고 따른다) |
| 이름 상수 | `Peak.Core.SceneNames.SandboxClimb`, `Peak.Core.Layers.Terrain` |
| 경사 경계 | `GameTuning.walkSlope` = 45° (`Data/GameTuning.asset`). 이보다 완만하면 걷고, 같거나 가파르면 등반면 |
| 지형 색 | `VisualTheme.terrainWalkColor`·`terrainWallColor` — 아직 아무 데서도 안 쓴다. 색 적용 예시는 `Visual/PlayerVisual.cs` (`_BaseColor`·`_Color` 에 MaterialPropertyBlock) |
| 플레이어 | 캡슐 높이 1.8 m·반지름 0.35 m, 눈높이 1.6 m. M1-2 의 벽 부착 거리 0.45 m |
| 현재 이동 | 경사 ≥ walkSlope 면 Airborne 이 되어 미끄러진다 (M0-2). 등반은 아직 없다 |
| 폰트 | TMP 기본 `LiberationSans SDF` (`Assets/TextMesh Pro/Resources`). 한글 동적 폰트 `MalgunGothic SDF` 는 쓰지 않는다 (플레이할 때마다 에셋이 바뀐다) |

## 목표 (한 문장)
메뉴 한 번으로 Sandbox_Climb 에 경사·높이·코너·모서리·낙하 높이별 시험 지형이 메시 콜라이더로 생기고, 걷는 면과 등반면이 색으로 구분되며, 각 지형 위에 이름표가 붙는다.

## 1. 메뉴 `Peak > Setup > Rebuild Climb Course` (새 파일 `Scripts/Editor/ClimbCourseBuilder.cs`, 멱등)

- Sandbox_Climb 씬을 열고 루트 `[ClimbCourse]` 아래에 2장의 요소를 이름으로 찾아 만들거나 값을 다시 맞춘 뒤 저장한다
- 두 번 실행해도 씬·메시 에셋 diff 없음. `Rebuild M0 Scenes` 를 그 사이에 실행해도 코스와 `Temp_Ramp*` 가 서로를 지우지 않는다
- 요소 사양은 빌더 안의 **이름 있는 사양 표**(배열) 하나로 둔다. 이름·경사·높이·폭·위치를 한곳에서 읽을 수 있게
- `M0SceneBuilder.cs` 는 다음만 바꿀 수 있다: `BuildScene`·`EnsureRoot`·`DeleteRoot` 를 `internal` 로, `SandboxGroundMeters` 값 (코스가 들어가도록, 제안 120 m). 그 밖의 변경 금지

## 2. 코스 요소 (`[ClimbCourse]` 자식, 이름 고정)

| 그룹 | 이름 | 형태 | 목적 (`202` 10장) |
|---|---|---|---|
| 경사 경계 | `Boundary_40`, `Boundary_44`, `Boundary_46`, `Boundary_50` | 경사로(쐐기) + 뒤쪽 평평한 윗면. 높이 2 m, 폭 4 m | 45° 경계가 명확한가. 45° 정확히는 부동소수 오차로 애매하므로 44/46 으로 양쪽을 본다 |
| 경사 레인 | `Slope_46_H10`, `Slope_60_H10`, `Slope_75_H10` | 한쪽 면만 기운 벽 + 뒤쪽 평평한 윗면(깊이 ≥ 3 m). 높이 10 m, 폭 4 m | 경사별 등반 감각, 모서리 올라서기 |
| 높이 레인 | `Height_85_H5`, `Height_85_H10`, `Height_85_H15`, `Height_85_H20` | 위와 같은 형태, 경사 85° | 15 m 는 한 번에 오르고 20 m 는 못 오른다 (수치 검증) |
| 볼록 코너 | `Convex_60_H8`, `Convex_85_H8` | 사각뿔대: 네 옆면이 같은 경사, 평평한 윗면(≥ 3×3 m). 높이 8 m | 옆으로 돌 때 볼록 코너에서 떨어지지 않는가 |
| 오목 코너 | `Concave_60_H8`, `Concave_85_H8` | ㄷ자 담: 안쪽 세 면이 같은 경사로 안뜰을 향하고, 두 오목 코너가 생긴다. 한쪽(폭 ≥ 3 m)은 열려 있어 걸어 들어간다. 안뜰 바닥 한 변 ≥ 6 m, 위 테두리 폭 ≥ 2 m, 바깥 면은 수직. 높이 8 m | 오목 코너 통과 |
| 낙하대 | `Drop_H3`, `Drop_H4` | 평평한 단(윗면 ≥ 3×3 m) + 30° 경사로로 걸어 올라감 | 3 m 이하 무피해, 4 m 는 작은 부상 (M1-3 이 검증). 등반 없이 올라갈 수 있어야 한다 |

- 경사 레인·높이 레인·낙하대의 윗면이 모서리 올라서기와 뛰어내리기 시험대다. 벽 윗변과 윗면 사이에 틈·턱이 없어야 한다 (≤ 0.01 m)
- 배치: 겹치지 않게 요소 간 간격 ≥ 3 m, `SpawnPoint` 반경 6 m 와 `Temp_Ramp*` 주변은 비운다. 모든 등반면은 `SpawnPoint` 쪽에서 보이도록 향한다. 그룹끼리 모아 둔다 (구체 좌표는 자유)
- 모든 요소는 지면(y 0) 위에 놓인다. 지면 아래로 파지 않는다

## 3. 지형 메시

- 요소마다 메시를 코드로 생성해 에셋으로 저장: `Assets/_Project/Art/Meshes/ClimbCourse/<이름>.asset`. 다시 실행하면 같은 에셋을 덮어써 GUID 를 유지한다
- 면마다 정점을 나눠 평평한 법선. 삼각형 감기 방향은 바깥(고체 밖)을 향한다
- 서브메시 2개: 0 = 걷는 면 (법선 경사 < `walkSlope`), 1 = 등반면 (≥ `walkSlope`). `walkSlope` 는 `GameTuning.asset` 에서 읽는다. walkSlope 를 튜닝하면 이 메뉴를 다시 돌려 나눈다
- 컴포넌트: `MeshFilter`, `MeshRenderer`(슬롯 2개), `MeshCollider`(비-convex, `sharedMesh` = 같은 메시), `SlopeTint`(4장). 레이어 Terrain, Static
- 머티리얼: 지면 Plane 과 같은 기본 URP Lit 머티리얼을 두 슬롯이 공유한다. 새 머티리얼 에셋을 만들거나 색을 머티리얼에 굽지 않는다

## 4. 경사 색 `SlopeTint` (새 파일 `Scripts/Runtime/Visual/SlopeTint.cs`, `Peak.Visual`)

- `[ExecuteAlways]` MonoBehaviour. `[SerializeField] VisualTheme theme` (빌더가 `ThemeAssetPath` 에셋을 연결)
- `OnEnable` 에서 MaterialPropertyBlock 을 서브메시 인덱스별로 적용: 0 → `terrainWalkColor`, 1 → `terrainWallColor`. 속성은 `PlayerVisual` 과 같이 `_BaseColor`·`_Color`
- 에디터 Scene 뷰에서도 색이 보여야 한다 (사람 확인용). `GameConfig` 에 의존하지 않는다 (Sandbox 직접 플레이 때 Boot 보다 먼저 켜진다)
- 주석: M2 지형 메시가 같은 규칙(걷는 면 / 등반면 색)을 재사용할 수 있다

## 5. 이름표

- 요소마다 자식 `Label`: 월드 공간 `TextMeshPro` (UGUI 아님), 기본 폰트, ASCII 만. 내용 예: `75° 10m`, `Convex 85° 8m`, `Drop 3m`, `Boundary 44°`
- 요소 윗면보다 조금 위, `SpawnPoint` 쪽을 향함. 콜라이더 없음, 레이어 Default, 크기는 20 m 밖에서 읽힐 정도 (빌더 const)

## 제약
- 수정 허용 파일: 새 `ClimbCourseBuilder.cs`, 새 `SlopeTint.cs`, `Art/Meshes/ClimbCourse/` 메시 에셋, 빌더가 저장하는 `Sandbox_Climb.unity`, `M0SceneBuilder.cs` (1장에 적은 두 가지만). **그 밖의 파일 수정 금지** — 플레이어·카메라·GameTuning 코드 포함
- `Docs/`, `CLAUDE.md`, `Assets/Editor/McpAutoStartBootstrap.cs`, `.mcp.json` 수정 금지. `Docs/101` 항목 구현 금지
- 코스 사양(이름·경사·높이·크기·간격)은 빌더의 이름 있는 const 또는 사양 표. 런타임 코드(`SlopeTint`)에는 수치가 없다
- `Debug.Log` 금지 → `Log.*(LogCategory.Core 또는 UI, …)`
- 커밋 금지
- 토큰 절약: 확인은 스크린샷 대신 `execute_code` 로 한 줄 상태를 받고, `read_console` 은 스택 트레이스 없이

## 완료 조건

**워커가 자동 확인** (unity-mcp)
1. `Rebuild Climb Course` 두 번 → 두 번째 실행 후 씬·메시 에셋 diff 없음. 이어서 `Rebuild M0 Scenes` → `Rebuild Climb Course` → diff 없음, 코스와 `Temp_Ramp30`·`Temp_Ramp60` 둘 다 존재. 컴파일 에러 0, 우리 코드 경고 0
2. `[ClimbCourse]` 자식이 2장 표의 17개 이름과 정확히 같다. 각각 MeshCollider(비-convex, sharedMesh = MeshFilter 메시), Terrain 레이어, 렌더러 슬롯 2개
3. 각도·높이 검사 (레이캐스트): 기운 면의 법선 경사 = 표의 각도 ± 0.5°, 윗면 법선 경사 < 1°, 윗면 높이 = 표의 높이 ± 0.02 m. 볼록·오목 코너 요소는 세 면 이상을 검사
4. 서브메시 분류: `RaycastHit.triangleIndex` 로 찾은 서브메시가 `Boundary_44` 경사면 = 0, `Boundary_46` 경사면 = 1, 모든 윗면 = 0
5. Sandbox_Climb 직접 플레이: 에러 0, 플레이어 스폰·Grounded, 코스 렌더러 전부 `HasPropertyBlock()` true, `Label` 17개·콜라이더 0
6. 현재 이동 코드로 경계 확인: 플레이어를 `Boundary_44` 경사면 중간에 옮기고 2초 → Grounded, 수평 이동 < 0.05 m. `Boundary_46` → Grounded 아님, 미끄러져 내려감. `Temp_Ramp30`·`Temp_Ramp60` 도 M0-3 때와 같은 결과

**사람이 확인** (보고에 체크리스트로 남긴다)
7. Scene 뷰와 플레이 화면에서 코스 배치가 한눈에 읽히고, 이름표가 스폰 근처에서 읽히며, 걷는 면과 등반면 색이 구분된다
8. `Boundary_40`·`Boundary_44` 는 걸어 올라가고 `Boundary_46`·`Boundary_50` 은 못 올라간다
9. `Drop_H3`·`Drop_H4` 는 경사로로 걸어 올라가 윗면에 설 수 있다

## 보고 형식 (`Docs/201_common.md` 8장)
```
결과: 완료 / 부분 완료 (남은 것)
만든 것 / 바꾼 것: 파일 목록 (경로)
코스 사양 표: 이름 | 형태 | 경사 | 높이 | 위치 (빌더의 표를 그대로)
문서와 달랐던 점: 무엇을 왜 (지면 크기, 오목 코너 형태, 머티리얼 선택 포함)
확인 방법: 메뉴 실행 순서, 자동 확인 1~6 결과, 사람 확인 7~9 절차
다음 워커에게: M1-2 (등반 코어) 가 알아야 할 것 — 요소 이름과 용도, 벽 윗변 좌표 계산 방법(맨틀 시험 위치), 오목 코너 안쪽 진입 방향, SlopeTint 재사용 방법
```
