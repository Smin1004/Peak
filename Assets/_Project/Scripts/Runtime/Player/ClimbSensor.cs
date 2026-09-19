using Peak.Core;
using UnityEngine;

namespace Peak.Player
{
    /// <summary>
    /// 등반 탐지 (Docs/202_gameplay.md 4장). <see cref="PlayerController"/> 가 소유하는 일반 클래스 — 스스로 Update 하지 않고
    /// 컨트롤러(전이)와 Climbing 상태(이동)가 호출한다. 탐지는 Terrain 레이어만, 트리거 무시. 거리·오프셋은 전부 GameTuning.
    /// 위치 인자 <c>feet</c> 는 루트(= 캡슐 바닥, 발) 월드 위치. 캡슐은 서 있는 자세(축 = up) 그대로라고 본다.
    /// </summary>
    public sealed class ClimbSensor
    {
        /// <summary>방향·길이를 0 으로 보는 제곱 크기.</summary>
        private const float MinSqrMagnitude = PlayerController.MinDirectionSqrMagnitude;

        private readonly CapsuleCollider _capsule;
        private readonly PlayerCameraRig _cameraRig;
        private readonly int _terrainMask;

        public ClimbSensor(CapsuleCollider capsule, PlayerCameraRig cameraRig, int terrainMask)
        {
            _capsule = capsule;
            _cameraRig = cameraRig;
            _terrainMask = terrainMask;
        }

        // ── 캡슐 기하 (루트 기준 높이) ───────────────────────────────────

        /// <summary>캡슐 중심축 선분 아래 끝(바닥 반구 중심) 높이.</summary>
        private float AxisBottom => _capsule.center.y - _capsule.height * 0.5f + _capsule.radius;

        /// <summary>캡슐 중심축 선분 위 끝(머리 반구 중심) 높이.</summary>
        private float AxisTop => _capsule.center.y + _capsule.height * 0.5f - _capsule.radius;

        private float CenterHeight => _capsule.center.y;

        // ── 표면 기저 ──────────────────────────────────────────────────

        /// <summary>
        /// 벽을 보고 섰을 때 화면 오른쪽 접선 (4장 4번 t_r). 문서의 cross(up, n) 은 오른손 표기 — Unity(왼손)에서 오른쪽은 Cross(n, up).
        /// 예: 벽 법선 −Z (벽이 스폰 쪽을 봄) → 오른쪽 +X.
        /// </summary>
        public static Vector3 RightTangent(Vector3 normal)
        {
            Vector3 right = Vector3.Cross(normal, Vector3.up);
            return right.sqrMagnitude > MinSqrMagnitude ? right.normalized : Vector3.right;
        }

        /// <summary>벽면을 따라 위쪽 접선 (4장 4번 t_u, Unity 부호). 수직 벽이면 +Y, 기운 벽이면 벽 안쪽으로 기운다.</summary>
        public static Vector3 UpTangent(Vector3 normal, Vector3 right)
        {
            return Vector3.Cross(right, normal).normalized;
        }

        /// <summary>몸이 벽을 향하는 회전: yaw = 법선 수평 성분의 반대 (4장 보강 규칙).</summary>
        public static Quaternion FacingRotation(Vector3 normal)
        {
            var into = new Vector3(-normal.x, 0f, -normal.z);
            return into.sqrMagnitude > MinSqrMagnitude ? Quaternion.LookRotation(into, Vector3.up) : Quaternion.identity;
        }

        // ── 부착 (4장 1·2번) ────────────────────────────────────────────

        /// <summary>
        /// 캡슐 중심에서 카메라 전방 수평 성분으로 SphereCast → 등반면이 아니면 눈 위치에서 카메라 전방(pitch 포함)으로 한 번 더.
        /// 두 캐스트 모두 반지름만큼 뒤에서 출발한다 (벽에 캡슐을 붙이고 서 있을 때 구가 벽과 겹쳐 시작하면 SphereCast 가 그 콜라이더를 못 본다).
        /// </summary>
        public bool TryAttach(Vector3 feet, Quaternion bodyRotation, GameTuning tuning, out ClimbSurface surface)
        {
            Vector3 center = feet + Vector3.up * CenterHeight;
            if (TryProbe(center, _cameraRig.YawRotation * Vector3.forward, tuning, out surface))
            {
                return true;
            }
            Vector3 eye = feet + bodyRotation * _cameraRig.CameraTarget.localPosition;
            Vector3 look = Quaternion.Euler(_cameraRig.Pitch, _cameraRig.Yaw, 0f) * Vector3.forward;
            return TryProbe(eye, look, tuning, out surface);
        }

        private bool TryProbe(Vector3 origin, Vector3 direction, GameTuning tuning, out ClimbSurface surface)
        {
            surface = default;
            float radius = tuning.climbProbeRadius;
            if (!Physics.SphereCast(origin - direction * radius, radius, direction, out var hit, tuning.climbProbeDistance + radius, _terrainMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (!IsClimbable(hit.normal, tuning))
            {
                return false;
            }
            surface = new ClimbSurface(hit.point, hit.normal);
            return true;
        }

        // ── 따라가기 (4장 5번) ──────────────────────────────────────────

        /// <summary>
        /// 이동량 <paramref name="delta"/> 를 적용한 자리에서 벽을 다시 찾아 <paramref name="surface"/> 를 갱신하고, 그 벽 기준 이상적인 발 위치를 돌려준다.
        /// 1) 오목 코너: 이동 방향 앞에 다른 등반면이 부착 거리 안에 들어오면 그 면으로 갈아탄다.
        /// 2) 새 자리의 캡슐 중심에서 −n 으로 SphereCast. 구 접촉이라 볼록 모서리에서는 법선이 두 면 사이로 둥글게 바뀌며 코너를 돈다.
        /// 3) 그래도 못 찾으면 −n 을 좌우·상하로 45° 기울인 4방향 재탐지 (이동 방향 먼저).
        /// 전부 실패하면 false — 컨트롤러가 LostSurface 로 떨어뜨린다.
        /// </summary>
        public bool TryFollow(Vector3 feet, Vector3 delta, GameTuning tuning, ref ClimbSurface surface, out Vector3 idealFeet)
        {
            idealFeet = feet;
            Vector3 normal = surface.Normal;

            if (delta.sqrMagnitude > MinSqrMagnitude && TryFindCornerAhead(feet, delta, tuning, out var ahead))
            {
                surface = ahead;
                normal = ahead.Normal;
            }

            Vector3 moved = feet + delta;
            Vector3 center = moved + Vector3.up * CenterHeight;
            float expected = Mathf.Max(Vector3.Dot(normal, center - surface.Point), 0f);
            float length = expected + tuning.climbFollowMargin;

            if (TryFollowCast(center, -normal, length, tuning, out var found))
            {
                surface = found;
                idealFeet = SnapFeet(moved, found, tuning.climbAttachDistance);
                return true;
            }

            Vector3 right = RightTangent(normal);
            Vector3 up = UpTangent(normal, right);
            Vector3 lateral = Vector3.Dot(delta, right) >= 0f ? right : -right;
            Vector3 vertical = Vector3.Dot(delta, up) >= 0f ? up : -up;
            bool lateralFirst = Mathf.Abs(Vector3.Dot(delta, right)) >= Mathf.Abs(Vector3.Dot(delta, up));
            Vector3 first = lateralFirst ? lateral : vertical;
            Vector3 second = lateralFirst ? vertical : lateral;
            Vector3[] sides = { first, second, -second, -first };
            foreach (var side in sides)
            {
                // 45° 기울인 방향으로는 같은 벽까지 거리가 √2 배
                if (TryFollowCast(center, (side - normal).normalized, length * Mathf.Sqrt(2f), tuning, out found))
                {
                    surface = found;
                    idealFeet = SnapFeet(moved, found, tuning.climbAttachDistance);
                    return true;
                }
            }
            return false;
        }

        /// <summary>바닥 반구 중심에서 이동 방향으로 구를 밀어, 부착 거리 안에 들어오는 다른 등반면(오목 코너의 옆 벽)을 찾는다.</summary>
        private bool TryFindCornerAhead(Vector3 feet, Vector3 delta, GameTuning tuning, out ClimbSurface surface)
        {
            surface = default;
            float distance = delta.magnitude;
            float radius = tuning.climbAttachDistance - tuning.climbCornerSkin;
            Vector3 origin = feet + Vector3.up * AxisBottom;
            if (!Physics.SphereCast(origin, radius, delta / distance, out var hit, distance + tuning.climbCornerSkin, _terrainMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (!IsClimbable(hit.normal, tuning))
            {
                return false;
            }
            surface = new ClimbSurface(hit.point, hit.normal);
            return true;
        }

        private bool TryFollowCast(Vector3 origin, Vector3 direction, float length, GameTuning tuning, out ClimbSurface surface)
        {
            surface = default;
            if (!Physics.SphereCast(origin, tuning.climbFollowRadius, direction, out var hit, length, _terrainMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (!IsClimbable(hit.normal, tuning))
            {
                return false;
            }
            surface = new ClimbSurface(hit.point, hit.normal);
            return true;
        }

        /// <summary>
        /// 캡슐 중심축 선분과 표면 평면의 최단 거리가 <paramref name="attachDistance"/> 가 되는 발 위치 (4장 보강 규칙).
        /// 높이를 바꾸지 않도록 법선의 수평 방향으로만 옮긴다 — 부착 순간 발이 지면 아래로 파고들지 않는다.
        /// </summary>
        public Vector3 SnapFeet(Vector3 feet, ClimbSurface surface, float attachDistance)
        {
            Vector3 normal = surface.Normal;
            var horizontal = new Vector3(normal.x, 0f, normal.z);
            float horizontalLength = horizontal.magnitude;
            if (horizontalLength * horizontalLength <= MinSqrMagnitude)
            {
                return feet;
            }
            float shift = (attachDistance - AxisDistance(feet, surface)) / horizontalLength;
            return feet + horizontal / horizontalLength * shift;
        }

        /// <summary>캡슐 중심축 선분 → 표면 평면 부호 거리의 최솟값 (선분 양 끝 중 가까운 쪽). 음수면 평면을 넘어섰다.</summary>
        public float AxisDistance(Vector3 feet, ClimbSurface surface)
        {
            float bottom = Vector3.Dot(surface.Normal, feet + Vector3.up * AxisBottom - surface.Point);
            float top = Vector3.Dot(surface.Normal, feet + Vector3.up * AxisTop - surface.Point);
            return Mathf.Min(bottom, top);
        }

        // ── 모서리 (4장 6번) ────────────────────────────────────────────

        /// <summary>
        /// 캡슐 상단 앞 레이(발 + ledgeProbeHeight 에서 −n)가 벽을 못 찾고, 그 앞 위에서 아래로 쏜 레이가 걷는 면을 찾고,
        /// 그 자리에 캡슐이 들어가며 올라가는 경로(위 → 앞)가 막히지 않으면 true + 올라설 발 위치.
        /// 위로 이동 중일 때만 부른다 (전이 표).
        /// </summary>
        public bool TryFindLedge(Vector3 feet, ClimbSurface surface, GameTuning tuning, out Vector3 standPoint)
        {
            standPoint = feet;
            Vector3 normal = surface.Normal;
            var horizontal = new Vector3(normal.x, 0f, normal.z);
            float horizontalLength = horizontal.magnitude;
            if (horizontalLength * horizontalLength <= MinSqrMagnitude)
            {
                return false;
            }
            Vector3 into = -horizontal / horizontalLength;

            Vector3 probe = feet + Vector3.up * tuning.ledgeProbeHeight;
            float expected = Mathf.Max(Vector3.Dot(normal, probe - surface.Point), 0f);
            if (Physics.Raycast(probe, -normal, expected + tuning.climbFollowMargin, _terrainMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // 상단 레이 높이에서 벽까지 수평 거리 + 캡슐 반지름 + 안쪽 여유 만큼 앞 → 윗면 위
            float forward = expected / horizontalLength + _capsule.radius + tuning.ledgeInset;
            Vector3 downOrigin = probe + Vector3.up * tuning.ledgeProbeUp + into * forward;
            float downLength = tuning.ledgeProbeUp + tuning.ledgeProbeHeight;
            if (!Physics.Raycast(downOrigin, Vector3.down, out var hit, downLength, _terrainMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (IsClimbable(hit.normal, tuning) || hit.point.y <= feet.y)
            {
                return false;
            }

            float lift = hit.point.y + tuning.mantleClearance;
            if (!IsCapsulePathClear(feet, new Vector3(feet.x, lift, feet.z)) ||
                !IsCapsulePathClear(new Vector3(feet.x, lift, feet.z), new Vector3(hit.point.x, lift, hit.point.z)))
            {
                return false;
            }
            standPoint = hit.point;
            return true;
        }

        /// <summary>발 위치 from → to 로 캡슐을 밀었을 때 지형에 막히지 않는가 (맨틀 경로가 벽·윗면을 파고들지 않음).</summary>
        private bool IsCapsulePathClear(Vector3 from, Vector3 to)
        {
            Vector3 travel = to - from;
            float distance = travel.magnitude;
            if (distance * distance <= MinSqrMagnitude)
            {
                return true;
            }
            Vector3 bottom = from + Vector3.up * AxisBottom;
            Vector3 top = from + Vector3.up * AxisTop;
            return !Physics.CapsuleCast(bottom, top, _capsule.radius, travel / distance, distance, _terrainMask, QueryTriggerInteraction.Ignore);
        }

        private static bool IsClimbable(Vector3 normal, GameTuning tuning)
        {
            return Vector3.Angle(normal, Vector3.up) >= tuning.walkSlope;
        }
    }
}
