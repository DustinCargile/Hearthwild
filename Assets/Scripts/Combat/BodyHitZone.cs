using UnityEngine;

namespace TopDownRPG.Combat
{
    // Put on dedicated knee/head/arm/etc. child colliders when models are available.
    public sealed class BodyHitZone : MonoBehaviour
    {
        public HitRegion region = HitRegion.Knee;
    }
}
