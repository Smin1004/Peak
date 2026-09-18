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
        [Tooltip("공중 제어 배율 0..1. 지상 가속도에 곱한다. 입력이 없으면 공중 관성 유지 — M0-2 추가 — 202 9장 반영 필요")]
        [Range(0f, 1f)]
        public float airControl = 0.3f;

        [Tooltip("지상 가감속 시간 s. 현재 속도·목표 속도 중 큰 쪽을 이 시간에 바꾸는 가속도 — M0-2 추가 — 202 9장 반영 필요")]
        public float groundAccelTime = 0.1f;

        [Tooltip("캐릭터가 이동 방향으로 도는 최대 각속도 rad/s — M0-2 추가 — 202 9장 반영 필요")]
        public float turnSpeed = 10f;

        [Header("카메라 (M0-2)")]
        [Tooltip("Look 입력(마우스 델타 px) → 카메라 회전 도 배율 — M0-2 추가 — 202 9장 반영 필요")]
        public float lookSensitivity = 0.15f;

        [Tooltip("카메라 pitch 하한 도 (음수 = 위를 봄) — M0-2 추가 — 202 9장 반영 필요")]
        public float pitchMin = -40f;

        [Tooltip("카메라 pitch 상한 도 (양수 = 아래를 봄) — M0-2 추가 — 202 9장 반영 필요")]
        public float pitchMax = 70f;

        [Header("지면 판정 (M0-2)")]
        [Tooltip("지면 SphereCast 반지름 m. 캡슐 반지름보다 작아야 한다 — M0-2 추가 — 202 9장 반영 필요")]
        public float groundCheckRadius = 0.3f;

        [Tooltip("캡슐 바닥 아래로 지면을 찾는 거리 m — M0-2 추가 — 202 9장 반영 필요")]
        public float groundCheckDistance = 0.15f;

        [Header("스폰 (205 4장)")]
        [Tooltip("플레이어 스폰 간격 m (SpawnPoint + 접속 순서 × 간격, X 축) — M0-2 추가 — 202 9장 반영 필요")]
        public float spawnSpacing = 1.5f;
    }
}
