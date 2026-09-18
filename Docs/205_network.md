# 205 — 네트워크 (Netcode for GameObjects + Unity Relay 참가 코드)

> 온라인 2~4인 협동. 배포 없음, 친구끼리 플레이 → **치트 방지 없음, 단순함 우선**.
> 기획 근거: `100_game_design.md` 3·10장. 공통 규칙: `201` 5·6장.
> 소유 폴더: `Scripts/Runtime/Network/`. 마일스톤: M0 (호스트 골격), M4 (Relay·동기화 완성).

## 1. 결정과 근거

| 항목 | 결정 | 근거 |
|---|---|---|
| 라이브러리 | **Netcode for GameObjects (NGO)** + Unity Transport | 이전 프로젝트(뜻밖의난관)에서 검증한 스택. 학습 비용 0 |
| 접속 | **Unity Multiplayer Services Sessions + Relay**, 6자리 참가 코드 | 포트포워딩 없이 친구 접속. 무료 한도(월 50 CCU 평균)로 충분 |
| 토폴로지 | **호스트(리슨 서버)**. 전용 서버 없음 | 배포 없음 |
| 권한 | 호스트 권한 + **플레이어 자기 상태는 오너 권한** | 반응성. 검증 불필요 |
| 싱글 | 같은 코드로 호스트 1인 세션 (Relay 없이 로컬 `StartHost`) | 코드 경로 하나 |
| 늦은 참가 | **없음**. `Playing` 진입 시 세션 잠금 | 생성 동기화·상태 복구 생략 |
| 호스트 이탈 | 세션 종료 → 전원 Result(실패) → Lobby | 호스트 마이그레이션 없음 (T2 도 미정) |
| WebGL | 없음 | |

**M0 부터 NGO 를 켠다** (`301` D13). 플레이어·아이템·캠프파이어가 전부 네트워크 오브젝트인데 나중에 붙이면 전부 다시 만든다. 비용: Boot 씬에 `NetworkManager` 가 있고, `StartHost()` 를 자동 호출하는 `Bootstrapper` 가 있어야 한다 (6장). M0~M3 은 Relay 없이 로컬 Unity Transport 로 호스트만 띄운다.

## 2. 매치 FSM (`GameManager`, Boot 씬, 호스트 권한)

```
Lobby ──(호스트 [시작])──→ Generating ──(전원 GenerationDone)──→ Playing ──(클리어/전멸)──→ Result ──→ Lobby
```

| 상태 | 호스트 | 클라이언트 |
|---|---|---|
| Lobby | 세션 생성, `RunConfig` 편집, 설정 변경 시 `NetworkVariable<RunConfig>` 갱신 | 코드로 참가, 설정 표시 |
| Generating | `RunConfig` 확정 → 세션 잠금 → Game 씬 로드(NGO 씬 관리) → 로컬 생성 → 전원 완료 대기 | Game 씬 로드 → 로컬 생성 → `GenerationDoneServerRpc(hash)` |
| Playing | 플레이어 스폰, 월드 오브젝트 스폰(캠프파이어·가방·표식·안개), 시간 기록 | 스폰 대기 |
| Result | 결과 데이터 `ClientRpc` → 결과 화면 | 결과 화면 |

- `MatchPhase` 는 `NetworkVariable`. 전환은 호스트만
- 씬 전환은 NGO `NetworkSceneManager` (호스트가 로드하면 전원 따라감)

## 3. 동기화 목록과 권한

| 대상 | 방식 | 권한 | 비고 |
|---|---|---|---|
| 플레이어 위치·회전 | `NetworkTransform` (오너 권한) | 오너 | 보간 켬. 몸 yaw = 카메라 yaw (1인칭), 등반 중은 표면 법선 기준 — 회전도 전송 |
| 시선 pitch (M4) | `NetworkVariable<sbyte>` (1° 단위, 오너 쓰기) | 오너 | 1인칭 협동에서 남이 어디를 보는지 표시 — 비오너 Visual 의 코·머리 기울임 (`202` 12.7). yaw 는 몸 회전으로 이미 전달됨 |
| 플레이어 상태 기계 상태 | `NetworkVariable<byte>` | 오너 | Visual 표현·마커용 |
| 스태미나·보너스 | `NetworkVariable<float>` ×2 (전송 주기 10Hz) | 오너 | 남의 바를 볼 일은 마커 정도 |
| 상태이상 목록 | `NetworkList<(kind, amount)>` 또는 고정 배열 `NetworkVariable` | 오너 | 캠프파이어 조건·마커 색에 필요 |
| `PlayerPhase` (Alive/Unconscious/Dead) | `NetworkVariable` | 오너 | 캠프파이어 점화 조건은 **호스트가 이 값들을 읽어** 판정 |
| 남에게 상태이상 부여 (아이템 사용, 부활) | `ServerRpc(target, kind, amount)` → 호스트가 `ClientRpc` 로 대상 오너에게 → 오너가 적용 | 요청자 → 호스트 → 오너 | 오너 권한 원칙 유지 |
| 업기/내려놓기 | `ServerRpc` → 호스트가 `carriedBy` `NetworkVariable` 설정 → 업힌 쪽은 오너가 `NetworkTransform` 을 끄고 운반자에 부착 | 호스트 결정, 오너 적용 | 위치는 운반자 기준 로컬 오프셋. 운반자 화면에서 업힌 몸은 그림자만 (`202` 12.6) |
| 캠프파이어 점화 상태·점화 시각 | `NetworkVariable<bool>`, `NetworkVariable<double>`(서버 시간) | 호스트 | 점화 시각으로 안개를 결정적 계산 |
| 안개 | 전송 없음. 각 클라이언트가 `점화 시각 + GameTuning` 으로 반경 계산 | — | 서버 시간은 `NetworkManager.ServerTime` |
| 가방 열림, 아이템 스폰 | 호스트가 `NetworkObject` 스폰 | 호스트 | 아이템 내용은 가방의 시드로 결정 (`206` 3장) — 전송은 스폰만 |
| 아이템 소유·슬롯 | `NetworkObject` 오너십 이전 + 인벤토리 `NetworkList` | 호스트 승인 | 줍기 = `ServerRpc` → 호스트가 `ChangeOwnership` + 부모 설정 |
| 로프·피톤 설치 | `ServerRpc(anchor pos, dir)` → 호스트가 스폰 | 호스트 | 로프 세그먼트 위치는 스폰 시 결정적 (물리 없음, `206` 4장) |
| 위험 구역 판정 | 전송 없음. 오너 클라이언트가 자기 플레이어에 대해 트리거 판정 | 오너 | 구역 위치는 생성 결과라 동일 |
| 채팅 | `ServerRpc` → `ClientRpc` 브로드캐스트 | 호스트 | |
| 결과 데이터 | `ClientRpc` | 호스트 | |

- **오너 권한의 함의**: 클라이언트가 자기 스태미나를 속일 수 있다. 무시한다 (친구끼리)
- `NetworkVariable` 기본 전송 주기는 틱 30Hz. 스태미나는 `NetworkVariable` 의 변경 감지에 의존하므로 0.1초 단위로만 갱신 (트래픽)

## 4. 산 생성 동기화

1. 호스트 `RunConfig` 확정 (`NetworkVariable`, 전원 최신값 보유)
2. Game 씬 로드 완료 → 각자 `MountainGenerator.Generate(RunConfig)` (메인 스레드, 프레임 분할 코루틴 ⚠ — 2초면 그냥 블로킹도 허용)
3. 클라이언트 `GenerationDoneServerRpc(clientId, hash)`. 호스트는 자기 해시와 비교
   - 일치: 카운트
   - 불일치: 콘솔 경고 + HUD 디버그 표시. 개발 중에는 계속 진행. ⚠ 릴리스 빌드에서 어떻게 할지는 `301` Q5
4. 전원 완료 → `Playing`. 플레이어 스폰(시작점 + 플레이어 인덱스 × 1.5 m 오프셋), 월드 오브젝트 스폰 (호스트). 월드 오브젝트의 **위치는 각자 생성 결과에서** 읽으므로 스폰 RPC 에는 인덱스만 담는다

## 5. 세션 (M4)

| 항목 | 설계 |
|---|---|
| 생성 | `MultiplayerService.Instance.CreateSessionAsync(options)` → `session.Code` 표시 |
| 참가 | `JoinSessionByCodeAsync(code)` |
| 인원 | `MaxPlayers = 4` |
| 인증 | 익명 로그인 (`AuthenticationService`) |
| 닉네임 | 세션 플레이어 프로퍼티 |
| 잠금 | `Generating` 진입 시 `session.IsLocked = true` |
| 래퍼 | `NetService` 싱글턴 — 씬 코드는 `Unity.Services.*` 타입을 직접 보지 않는다 |

- 준비: Project Settings → Services → Unity Cloud 프로젝트 연결 (사람이 1회), Relay 활성화. `ProjectSettings/` 에 `cloudProjectId` 가 들어가므로 커밋
- 이전 프로젝트 `260905Hackathon/Docs/205_network.md` 2·3장의 절차와 동일 — 워커에게 참고 경로로 제공 가능

## 6. 개발 편의 (M0)

- `Bootstrapper` (Game·Sandbox 씬에 배치): 플레이 시작 시 `NetworkManager` 가 없으면 Boot 씬을 애디티브 로드하고 `StartHost()` (Relay 없음, 로컬 Transport). 결과: **어느 씬에서 플레이를 눌러도 1인 호스트로 바로 동작**
- Multiplayer Play Mode: 가상 플레이어 2~3개로 한 PC 에서 참가 테스트. Relay 없이 로컬 `127.0.0.1` 로 붙는 개발 모드 토글 (`NetService.useRelay = false`)
- 디버그 오버레이(`204` 4장)에 역할(Host/Client), clientId, 핑, 해시 표시

## 7. 테스트 시나리오 (M4 게이트)

- [ ] 한 PC 에서 호스트 + 가상 클라이언트 2: 방 생성 → 참가 → 생성 완료 → 해시 일치 → 플레이어 3명 스폰
- [ ] 두 클라이언트가 서로의 등반·낙하·기절 상태를 본다 (마커 색 변화)
- [ ] A 가 B 에게 붕대 사용 → B 의 부상이 준다
- [ ] A 가 기절한 B 를 업고 캠프파이어 반경 진입 → "집결 N/N" 충족 → 점화 → 전원 회복
- [ ] 안개 반경이 두 화면에서 같다 (디버그 수치)
- [ ] 다른 PC·다른 네트워크에서 참가 코드로 접속 (Relay)
- [ ] 호스트 종료 → 클라이언트 Result → Lobby 복귀 (예외 없음)

---
관련 문서: `201_common.md` · `202_gameplay.md` · `203_procgen.md` · `204_ui.md` · `206_items.md` · `300_roadmap.md`
