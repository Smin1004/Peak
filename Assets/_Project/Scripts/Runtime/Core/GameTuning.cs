using UnityEngine;

namespace Peak.Core
{
    /// <summary>
    /// 게임플레이 튜닝 수치. 코드에 매직 넘버 금지 — 전부 여기서 읽는다 (Docs/201_common.md 4장).
    /// 초기값과 근거: Docs/202_gameplay.md 9장 (⚠ 전부 확인 필요 상태). 에셋이 진실이며 문서는 나중에 맞춘다.
    /// 접근: <see cref="GameConfig.Tuning"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "GameTuning", menuName = "Peak/Game Tuning")]
    public sealed class GameTuning : ScriptableObject
    {
        [Header("이동 (202 9장)")]
        [Tooltip("걷기 속도 m/s — 202 9장")]
        public float walkSpeed = 4f;

        [Tooltip("달리기 속도 m/s — 202 9장")]
        public float sprintSpeed = 6.5f;

        [Tooltip("점프 초기 수직 속도 m/s (약 1.5 m 상승) — 202 9장")]
        public float jumpSpeed = 5.5f;

        [Header("등반 (202 4·9장)")]
        [Tooltip("등반 속도 m/s. 스태미나 100 ÷ 10/s = 10초 → 15 m — 202 9장")]
        public float climbSpeed = 1.5f;

        [Tooltip("등반 스태미나 소모 /s — 202 9장, 100 4.1")]
        public float climbCost = 10f;

        [Tooltip("달리기 스태미나 소모 /s — 202 9장, 100 4.1")]
        public float sprintCost = 5f;

        [Tooltip("점프 1회 스태미나 소모 — 202 9장, 100 4.1")]
        public float jumpCost = 10f;

        [Tooltip("로프 이동 스태미나 소모 /s (등반의 40%) — 202 9장")]
        public float ropeCost = 4f;

        [Header("스태미나 회복 (202 5·9장)")]
        [Tooltip("회복 속도 /s — 202 9장 (원작)")]
        public float regenRate = 20f;

        [Tooltip("회복 시작 지연 초 — 202 9장 (원작)")]
        public float regenDelay = 0.2f;

        [Header("경사 (202 4·9장, 203 과 공유)")]
        [Tooltip("걷기 가능 경사와 등반면의 경계 각도(도). 203 의 발판/벽 경사 설계와 반드시 같은 값 — 202 9장")]
        public float walkSlope = 45f;

        [Header("낙하 (202 7·9장)")]
        [Tooltip("이 착지 속도(m/s) 미만은 무피해 — 202 9장, 100 4.5")]
        public float fallSafeSpeed = 8f;

        [Tooltip("이 착지 속도(m/s) 에서 부상 100 — 202 9장, 100 4.5")]
        public float fallMaxSpeed = 25f;

        [Tooltip("탈진 낙하(스태미나 0 으로 이탈) 부상 배율 — 202 9장")]
        public float exhaustedFallMultiplier = 1.5f;

        [Header("상태이상·기절 (202 6·9장)")]
        [Tooltip("허기 누적 /s (캠프파이어 반경 밖) — 202 9장, 100 4.2")]
        public float hungerRate = 0.06f;

        [Tooltip("기절 → 사망까지 초 — 202 9장, 100 4.4")]
        public float unconsciousToDeath = 60f;

        [Tooltip("업기(Carrying) 중 이동 속도 배율 — 202 9장")]
        public float carrySpeedMultiplier = 0.7f;

        [Header("상호작용 (202 8·9장)")]
        [Tooltip("상호작용 레이캐스트 거리 m — 202 9장")]
        public float interactRange = 2.5f;

        [Header("이동 조작감 (M0-2)")]
        [Tooltip("공중 제어 배율 0..1. 지상 가속도에 곱한다. 입력이 없으면 공중 관성 유지 — M0-2 추가 — 202 9장")]
        [Range(0f, 1f)]
        public float airControl = 0.3f;

        [Tooltip("지상 가감속 시간 s. 현재 속도·목표 속도 중 큰 쪽을 이 시간에 바꾸는 가속도 — M0-2 추가 — 202 9장")]
        public float groundAccelTime = 0.1f;

        [Header("카메라 (M0-2 · 1인칭 M0-3)")]
        [Tooltip("Look 입력(마우스 델타 px) → 카메라 회전 도 배율 — M0-2 추가 — 202 9장")]
        public float lookSensitivity = 0.15f;

        [Tooltip("카메라 pitch 하한 도 (음수 = 위를 봄). 1인칭: 발밑·머리 위 벽을 볼 수 있어야 함 — M0-2 추가 — 202 9장")]
        public float pitchMin = -85f;

        [Tooltip("카메라 pitch 상한 도 (양수 = 아래를 봄). 1인칭: 발밑·머리 위 벽을 볼 수 있어야 함 — M0-2 추가 — 202 9장")]
        public float pitchMax = 85f;

        [Tooltip("1인칭 카메라 수직 FOV 도 (16:9 에서 65° ≈ 수평 95°). 런타임에 CinemachineCamera.Lens 에 적용 — M0-3 추가 — 202 9장, 301 Q13")]
        [Range(40f, 110f)]
        public float fieldOfView = 65f;

        [Header("지면 판정 (M0-2)")]
        [Tooltip("지면 SphereCast 반지름 m. 캡슐 반지름보다 작아야 한다 — M0-2 추가 — 202 9장")]
        public float groundCheckRadius = 0.3f;

        [Tooltip("캡슐 바닥 아래로 지면을 찾는 거리 m — M0-2 추가 — 202 9장")]
        public float groundCheckDistance = 0.15f;

        [Header("스폰 (205 4장)")]
        [Tooltip("플레이어 스폰 간격 m (SpawnPoint + 접속 순서 × 간격, X 축) — M0-2 추가 — 202 9장")]
        public float spawnSpacing = 1.5f;

        [Header("등반 탐지·맨틀 (M1-2, 202 4장)")]
        [Tooltip("부착 탐지 SphereCast 반지름 m (4장 1번) — M1-2 추가 — 202 9장")]
        public float climbProbeRadius = 0.4f;

        [Tooltip("부착 탐지 SphereCast 거리 m (4장 1번) — M1-2 추가 — 202 9장")]
        public float climbProbeDistance = 0.8f;

        [Tooltip("캡슐 중심축 선분과 벽 평면의 최단 거리 m = r_attach (4장 3번·보강 규칙). 캡슐 반지름 0.35 + 틈 0.1 — M1-2 추가 — 202 9장")]
        public float climbAttachDistance = 0.45f;

        [Tooltip("모서리 올라서기 경로 보간 시간 s (3장 Mantling, 12.5) — M1-2 추가 — 202 9장")]
        public float mantleDuration = 0.4f;

        [Tooltip("Climbing 을 떠난 뒤 다시 붙지 않는 시간 s (같은 벽 즉시 재부착 떨림 방지, 4장 보강 규칙) — M1-2 추가 — 202 9장")]
        public float climbReattachDelay = 0.2f;

        [Tooltip("벽과의 거리 보정 최대 속도 m/s. 부착·코너에서 캡슐(=카메라)이 한 프레임에 튀지 않도록 거리 차이를 이 속도로 메운다 — M1-2 추가 — 202 9장 반영 필요")]
        public float climbSnapSpeed = 3f;

        [Tooltip("등반 중 벽 재탐지 SphereCast 반지름 m (4장 5번). 볼록 모서리에서 접촉 법선이 두 면 사이로 둥글게 바뀌어 코너를 돈다 — M1-2 추가 — 202 9장 반영 필요")]
        public float climbFollowRadius = 0.2f;

        [Tooltip("벽 재탐지·모서리 판정 레이를 예상 거리보다 더 쏘는 여유 m (4장 5·6번) — M1-2 추가 — 202 9장 반영 필요")]
        public float climbFollowMargin = 0.3f;

        [Tooltip("오목 코너 앞 벽 탐지 구 반지름 = climbAttachDistance − 이 값 m. 지금 붙은 벽과 겹치지 않게 하는 틈 (4장 5번) — M1-2 추가 — 202 9장 반영 필요")]
        public float climbCornerSkin = 0.05f;

        [Tooltip("모서리 판정 '캡슐 상단 앞 레이' 의 발 기준 높이 m (4장 6번). 눈(1.6)보다 조금 낮아 윗면이 보일 때 올라선다 — M1-2 추가 — 202 9장 반영 필요")]
        public float ledgeProbeHeight = 1.5f;

        [Tooltip("모서리 판정 '앞 아래 레이' 를 상단 레이보다 이만큼 위에서 쏜다 m (4장 6번) — M1-2 추가 — 202 9장 반영 필요")]
        public float ledgeProbeUp = 0.3f;

        [Tooltip("올라선 발 위치를 벽 윗변에서 캡슐 반지름보다 이만큼 더 안쪽으로 m (4장 6번) — M1-2 추가 — 202 9장 반영 필요")]
        public float ledgeInset = 0.15f;

        [Tooltip("맨틀 경로의 앞으로 가는 구간에서 발을 윗면보다 띄우는 높이 m (4장 보강 규칙 '경로가 윗면을 파고들지 않는다') — M1-2 추가 — 202 9장 반영 필요")]
        public float mantleClearance = 0.05f;
    }
}
