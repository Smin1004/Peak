using UnityEngine;

namespace Peak.Player
{
    /// <summary>
    /// 한 물리 프레임의 지면 판정 결과 (<see cref="PlayerController"/> 가 매 FixedUpdate 갱신).
    /// <see cref="IsWalkable"/> 의 경사 기준은 <c>GameTuning.walkSlope</c> — M1 ClimbSensor 도 같은 값을 쓴다 (202 4장).
    /// </summary>
    public readonly struct GroundInfo
    {
        public static readonly GroundInfo None = new GroundInfo(false, Vector3.up, Vector3.zero, 0f, false);

        /// <summary>SphereCast 가 Terrain 레이어를 맞췄는지 (경사 무관).</summary>
        public readonly bool HasHit;

        /// <summary>맞은 면의 법선. 맞지 않았으면 up.</summary>
        public readonly Vector3 Normal;

        public readonly Vector3 Point;

        /// <summary>법선과 up 사이 각도 (도). 맞지 않았으면 0.</summary>
        public readonly float SlopeAngle;

        /// <summary>맞았고 경사 &lt; walkSlope — Grounded 조건.</summary>
        public readonly bool IsWalkable;

        public GroundInfo(bool hasHit, Vector3 normal, Vector3 point, float slopeAngle, bool isWalkable)
        {
            HasHit = hasHit;
            Normal = normal;
            Point = point;
            SlopeAngle = slopeAngle;
            IsWalkable = isWalkable;
        }
    }
}
