using UnityEngine;

namespace Peak.Player
{
    /// <summary>
    /// 등반 중인 표면 한 점 (<see cref="ClimbSensor"/> 결과, Docs/202_gameplay.md 4장).
    /// 볼록 모서리에서는 SphereCast 접촉 법선이라 두 면 법선 사이 값이 될 수 있다 — 모서리를 둥글게 돌기 위해 의도한 것.
    /// </summary>
    public readonly struct ClimbSurface
    {
        /// <summary>표면 위 접촉점 (월드).</summary>
        public readonly Vector3 Point;

        /// <summary>표면 바깥 방향 법선 (단위 벡터).</summary>
        public readonly Vector3 Normal;

        /// <summary>법선과 up 사이 각도 (도). walkSlope 이상이면 등반면.</summary>
        public readonly float SlopeAngle;

        public ClimbSurface(Vector3 point, Vector3 normal)
        {
            Point = point;
            Normal = normal;
            SlopeAngle = Vector3.Angle(normal, Vector3.up);
        }
    }
}
