# Peak — PEAK 모작 (협동 등반 · 절차 생성)

Unity 6 (6000.0.66f2), 3D URP, 신 Input System, Netcode for GameObjects + Unity Multiplayer Services. 1인 개발 + AI 워커 세션. 배포 없음.

## 기획 문서가 코드보다 우선한다

기획서는 `Docs/` 에 있다. **문서에 근거 없는 코드 작성 금지.** 구현 전 해당 분야 문서를 반드시 읽을 것.

필독 3종 (이 순서로):
1. `Docs/SUMMARY.md` — 프로젝트 한 장 요약, 티어, 핵심 결정
2. `Docs/100_game_design.md` — 게임 룰 (우선순위의 기준)
3. `Docs/201_common.md` — 프로젝트 설정, 씬/폴더, 데이터 계약, 코딩 규칙, 세션 운영 규칙

이후 담당 문서: `202_gameplay.md` (캐릭터·등반·스태미나) · `203_procgen.md` (절차 생성 — 프로젝트 중심) · `204_ui.md` · `205_network.md` · `206_items.md`.
`101_extra_design.md` 는 T2 확장 — **프롬프트에 명시되기 전까지 구현 금지**. `300_roadmap.md` 로 현재 마일스톤 확인. `301_decisions.md` 의 미결 질문(Qn)은 임의로 확정하지 말 것.

## 세션 규칙

- **기획 세션(브레인)**: `Docs/` 와 이 파일만 수정. Assets·코드 수정 금지
- **워커 세션**: 프롬프트에 명시된 파일·기능만. 범위 밖 수정 금지. 완료 보고는 `Docs/201_common.md` 8장 형식
- 워커는 커밋하지 않는다. 문서 안의 **⚠ 확인 필요** 는 제안 상태 — 임의로 확정하지 말고 보고에 적는다

## 작업 규칙 (Docs/201_common.md 요약)

- 우리 에셋은 전부 `Assets/_Project/` 아래. 네임스페이스 = 폴더 (`Peak.Player`, `Peak.ProcGen` …). asmdef: Runtime / Editor / Tests
- 플레이어·월드 오브젝트는 `NetworkBehaviour`. 입력·카메라·HUD 는 `IsOwner` 만. 권한 규칙은 `205` 3장
- 생성(`ProcGen/`)은 결정적: `System.Random(seed)` 만, `UnityEngine.Random`·`Physics`·`Time`·`Dictionary` 순회 금지
- 튜닝 수치는 ScriptableObject(`GameTuning`, `BiomeDef`, `ItemDef`, `StatusEffectDef`, `VisualTheme`). 코드에 매직 넘버 금지
- 프리팹 = Root(로직·콜라이더) + `Visual` 자식(프리미티브). 스크립트는 `Visual` 내부를 참조하지 않는다. 색은 `VisualTheme`
- 씬·프리팹은 가능하면 에디터 스크립트 또는 unity-mcp 로 생성 (재현 가능하게)

## 폴더

```
Assets/_Project/   Scenes/ Scripts/{Runtime,Editor,Tests}/ Prefabs/ Data/ Input/ Art/ UI/
Docs/              기획·기술 문서 (변경 시 Docs/README.md 변경 이력 갱신)
```
