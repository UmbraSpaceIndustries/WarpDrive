using UnityEngine;

namespace WarpEngine
{
    public class ShipInfo
    {
        public Part ShipPart { get; set; }
        public float BreakingForce { get; set; }
        public float BreakingTorque { get; set; }
        public float CrashTolerance { get; set; }
        public RigidbodyConstraints Constraints { get; set; }
    }
}