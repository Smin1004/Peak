# 201 — 공통 기술 규약 (프로젝트 설정 · 구조 · 데이터 계약 · 세션 운영)

> 1인 개발 + AI 워커 세션 여러 개가 같은 프로젝트를 건드리기 위한 규약. **워커 세션은 작업 전 반드시 읽을 것.**
> 기획 근거: `100_game_design.md`. 이 문서의 최대 목적은 "워커가 만든 것끼리 맞물리게" 하는 것.

## 1. 프로젝트 설정

| 항목 | 값 |
|---|---|
| Unity 버전 | 6000.0.66f2 (Unity 6) |
| 템플릿 | 3D URP. `Assets/Settings/PC_RPAsset` 사용, Mobile 에셋은 삭제 가능 |
| 입력 | 신 Input System. `Assets/InputSystem_Actions.inputactions` 를 `_Project/Input/PeakActions.inputactions` 로 옮겨 재정의 (`202` 1장 매핑) |
| 물리 | 기본 3D Physics. 고정 타임스텝 0.02. 지형 = MeshCollider |
| 해상도 기준 | 1920×1080, Canvas Scaler = Scale With Screen Size |
| 직렬화 | Force Text (기본값 확인) |
| 네트워크 | Netcode for GameObjects + Unity Multiplayer Services (`205`) |
| 형상 관리 | Git. LFS 는 외부 에셋이 들어오기 전까지 미사용 |

### 1.1 패키지 정리 (M0 첫 작업)

| 추가 | 이유 |
|---|---|
| `com.unity.netcode.gameobjects` | NGO |
| `com.unity.services.multiplayer` | Sessions + Relay (M4 부터 사용, 미리 설치) |
| `com.unity.transport` | NGO 의존 |
| `com.unity.multiplayer.playmode` | 한 PC 다중 클라이언트 테스트 |
| `com.unity.cinemachine` (3.x) | 1인칭 카메라 (하드락), 착지 흔들림(Impulse), 기절·유령 카메라 전환 (`301` D16) |

| 제거 후보 | 이유 |
|---|---|
| `com.unity.ai.assistant`, `com.unity.ai.inference` | 미사용, 컴파일 시간 |
| `com.unity.visualscripting`, `com.unity.timeline` | 미사용 |
| `com.unity.collab-proxy` | Git 사용 |
| `com.unity.multiplayer.center` | 패키지 설치 안내용, 설치 후 제거 가능 |

- `com.coplaydev.unity-mcp` 는 유지 — 워커 세션이 에디터를 조작하는 통로 (8장)
- 템플릿 잔재 삭제: `Assets/TutorialInfo`, `Assets/Readme.asset`, `Assets/Scenes/SampleScene` (샌드박스로 재활용 가능)

## 2. 씬 구조

| 씬 | 역할 | 내용 |
|---|---|---|
| **Boot** | 루트, 항상 로드 | `GameManager`(매치 FSM), `NetService`, `RunConfig` 보관. `DontDestroyOnLoad` |
| **Lobby** | 메뉴 | 방 만들기(시드·안개·난이도), 참가 코드 입력, 플레이어 목록, 시작 버튼 |
| **Game** | 플레이 | 빈 씬. 진입 시 `MountainGenerator` 가 시드로 산을 생성하고 플레이어를 스폰. 결과 화면은 이 씬의 오버레이 |
| Sandbox_Climb | 개발용 | 손으로 만든 벽·발판. 등반 조작감 튜닝 전용. 빌드 제외 |
| Sandbox_ProcGen | 개발용 | 시드 입력 → 생성 → 기즈모·솔버 결과 확인. 빌드 제외 |

- Boot 가 애디티브로 Lobby/Game 을 얹는다. 전환 시 이전 씬 언로드
- 에디터에서 Game 씬을 직접 플레이하면 `Bootstrapper` 가 Boot 를 자동 로드하고 호스트로 시작 (개발 편의, `205` 6장)

## 3. 폴더 구조

```
Assets/
  _Project/                 ← 우리 것은 전부 여기. 템플릿·외부 에셋과 분리
    Scenes/                 Boot, Lobby, Game, Sandbox_Climb, Sandbox_ProcGen
    Scripts/
      Runtime/              (asmdef: Peak.Runtime)
        Core/               GameManager, RunConfig, 매치 FSM, Bootstrapper
        Player/             컨트롤러, 상태 기계, 카메라, 상호작용
        Stamina/            StaminaSystem, StatusEffectStack, 정의 타입
        ProcGen/            RouteGraph, HeightField, MeshBuilder, Solver, BiomeDef
        World/              Campfire, Fog, HazardVolume, Luggage, SummitMarker
        Items/              ItemDef, Inventory, 아이템 동작 (Rope, Piton ...)
        Network/            NetService, 동기화 헬퍼
        UI/                 HUD, Lobby UI, Result
        Visual/             VisualTheme, IVisualState, 프리미티브 빌더
      Editor/               (asmdef: Peak.Editor) 생성 미리보기, 배치 테스트 메뉴
      Tests/                (asmdef: Peak.Tests) PlayMode/EditMode 테스트
    Prefabs/                Player, Campfire, Luggage, Items/, Fog, ...
    Data/                   ScriptableObject 에셋: GameTuning, BiomeDef/, ItemDef/, StatusEffectDef/, VisualTheme
    Input/                  PeakActions.inputactions
    Art/                    머티리얼, (추후) 모델
    UI/                     폰트, 스프라이트
  Settings/                 URP 에셋 (템플릿 유지)
Docs/                       기획·기술 문서
```

- 네임스페이스 = 폴더: `Peak.Player`, `Peak.ProcGen` …
- 씬 파일과 프리팹은 **가능하면 에디터 스크립트 또는 unity-mcp 로 생성**해 재현 가능하게 (수동 배치 최소화)

## 4. 튜닝 데이터 규칙

- 게임플레이 수치는 **전부 ScriptableObject** 에 둔다. 코드에 매직 넘버 금지
- 문서(100·202·203·206)는 초기값과 근거만 적는다. **에셋이 진실**. 문서와 에셋이 다르면 에셋이 맞고, 문서를 나중에 고친다
- 에셋 목록

| 에셋 | 내용 | 정의 문서 |
|---|---|---|
| `GameTuning` | 스태미나 소모·회복, 낙하 피해, 허기 속도, 기절→사망 시간, 안개 상수, 캠프파이어 반경 | `100` 4·6·7장, `202` 9장 |
| `StatusEffectDef` (종류별) | 이름, 색, 자연 회복 규칙 (지연·속도), 상한 | `100` 4.2 |
| `BiomeDef` (바이옴별) | 높이, 경사 분포, 노드 간격, 발판 반경, 가방 밀도, 위험물 목록·밀도, 팔레트, 안개 계수 | `203` 5장 |
| `ItemDef` (아이템별) | 이름, 무게, 사용 방식, 효과 목록, 프리팹, 스폰 가중치 | `206` 1장 |
| `VisualTheme` | 색 팔레트 (플레이어 4색, 바이옴, 아이템, UI) | `102` |
| `DifficultyDef` (T2) | Ascent 배율 | `101` 2장 |

## 5. 공유 데이터 계약

씬·시스템 사이를 오가는 핵심 타입. **변경 시 이 문서를 먼저 수정.**

```csharp
// 한 판의 설정 — 호스트가 정하고 전원에게 동기화 (205 4장). 이것만 있으면 같은 산이 나와야 한다
struct RunConfig {
    int   Seed;
    int[] BiomeSequence;   // BiomeDef 인덱스 순서 (T0: [Shore, Peak])
    bool  FogEnabled;
    int   Difficulty;      // 0 = 기본. T2 Ascent
}

// 생성 결과 — 각 클라이언트 로컬. 전송하지 않는다
class MountainData {
    RouteGraph  Route;          // 노드(발판)·간선(등반 구간) — 203 3장
    HeightField Height;         // 지형 높이 필드 — 203 4장
    CampfireSpawn[] Campfires;  // 바이옴 경계 위치
    LuggageSpawn[]  Luggage;    // 가방 위치 + 내용 시드
    HazardSpawn[]   Hazards;
    Vector3 StartPos, SummitPos;
}

// 플레이어 생체 값 — 오너가 계산, 네트워크로 복제 (205 3장, 복제는 M4)
// 이름 주의: 상태 기계 기반 클래스 Peak.Player.States.PlayerState 와 겹치지 않도록 PlayerVitals (2026-09-18 개명)
class PlayerVitals {
    float Stamina;              // 0..100
    float BonusStamina;
    List<(StatusKind kind, float amount)> Effects;   // 합 ≤ 200
    PlayerPhase Phase;          // Alive / Unconscious / Dead
    float UnconsciousSince;
}

enum StatusKind { Hunger, Injury, Weight, Cold, Poison, Heat /* T2: Spores, Drowsy, Thorns, Curse, Petrify */ }
enum PlayerPhase { Alive, Unconscious, Dead }

// 매치 흐름 (Boot 소유)
enum MatchPhase { Lobby, Generating, Playing, Result }
```

- `RunConfig` 는 값 타입이며 네트워크 직렬화 가능해야 한다 (`INetworkSerializable`)
- `MountainData` 는 **직렬화하지 않는다**. 결정성 검증용 해시(`Hash()`)만 노출

## 6. 코딩 규칙

1. **NetworkBehaviour 기준**: 플레이어·월드 오브젝트는 M0 부터 `NetworkBehaviour`. 싱글도 호스트 모드로 돈다 (`205` 1장, `301` D13)
2. **입력은 로컬 오너만**: `IsOwner` 아니면 입력 처리 안 함. 카메라·HUD 도 오너만 생성
3. **권한 규칙 준수**: `205` 3장 표를 따른다. 서버 권한 오브젝트는 `ServerRpc` 로 요청
4. **결정적 생성**: `ProcGen/` 안에서 `UnityEngine.Random`, `Physics`, `Time`, `Dictionary` 순회 순서 의존 **금지**. `System.Random(seed)` 하나를 인자로 넘겨 쓴다. 부동소수점은 같은 플랫폼(Windows x64)만 가정
5. **프레임 독립**: 상태이상 누적·회복은 `deltaTime` 기반. 안개는 점화 시각 기준 결정적 계산 (`205` 3장)
6. **로직/비주얼 분리**: `102` 2장. 스크립트는 `Visual` 자식을 모른다
7. **매직 넘버 금지**: 4장
8. **어셈블리 분리**: Runtime / Editor / Tests. Runtime 은 Editor 참조 금지
9. **로그**: `Debug.Log` 대신 `Log.Info(category, msg)` 래퍼 (카테고리별 on/off). 네트워크·생성 카테고리 필수
10. **주석·문서는 한국어**, 식별자는 영어
11. **테스트 최소선**: 솔버·생성기는 EditMode 테스트 (100 시드 통과). 컨트롤러는 샌드박스 씬 수동 확인

## 7. 테스트 환경

| 대상 | 방법 |
|---|---|
| 생성기 | `Sandbox_ProcGen` + EditMode 테스트 `Generate(seed)` × 100 → 솔버 100% 통과, 생성 시간 측정 |
| 등반 조작감 | `Sandbox_Climb` 수동. 체크리스트는 `202` 10장 |
| 멀티플레이 | Multiplayer Play Mode 로 한 PC 2~4 클라이언트. Relay 전에는 로컬 호스트 |
| 완주 | `Game` 씬 시드 고정(예: 1) 으로 T0 완주 → 회귀 기준 시드 |

## 8. 세션 운영 규칙 (브레인 / 워커)

| 세션 | 권한 | 역할 |
|---|---|---|
| **기획 세션 (브레인)** | `Docs/`·`CLAUDE.md` 만 수정 | 기획 정리·검토·제안, 마일스톤 관리, 워커 프롬프트 작성. Assets·코드 수정 금지 |
| **워커 세션** | 프롬프트에 명시된 파일·기능만 | 구현. 범위 밖 수정 금지. 완료 보고 형식은 아래 |

**워커 프롬프트 형식** (브레인이 작성):
```
목표: (한 문장)
근거 문서: Docs/xxx.md N장
범위: 만들 파일·프리팹·씬 (명시된 것만)
제약: 권한 규칙, 결정성, 로직/비주얼 분리 등 해당 항목
완료 조건: 실행 가능한 검증 (테스트 통과 / 샌드박스에서 X 확인)
보고: 아래 형식으로
```

**워커 완료 보고 형식**:
```
결과: 완료 / 부분 완료 (남은 것)
만든 것: 파일 목록
문서와 달랐던 점: (있으면) 무엇을 왜 다르게 했는지
확인 방법: 씬·메뉴·테스트 이름
다음 워커에게: 주의점
```

- 워커는 **unity-mcp** 로 에디터를 조작할 수 있다 (씬 생성, 프리팹 구성, 플레이 모드 실행, 콘솔 확인). 수동 배치보다 우선
- 워커는 커밋하지 않는다. 커밋은 사람이 보고를 검토한 뒤 (9장)
- 동시에 여는 워커는 **서로 다른 폴더**를 맡긴다 (예: ProcGen 워커 + UI 워커). 같은 파일을 두 워커가 만지지 않는다

## 9. Git

- 브랜치: **`main` 직행** (`301` D15). 워커를 병렬로 돌릴 때만 `feat/<이름>` 후 병합
- 커밋 단위 = 워커 작업 1건. 메시지: `[M1] 등반 상태 기계 v1` 처럼 마일스톤 태그 접두
- `ProjectSettings/`·`Packages/manifest.json` 변경은 별도 커밋 (되돌리기 쉽게)
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` 는 이미 무시됨. `Builds/` 도 무시

---
관련 문서: `SUMMARY.md` · `100_game_design.md` · `202_gameplay.md` · `203_procgen.md` · `205_network.md` · `300_roadmap.md`
