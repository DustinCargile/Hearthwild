using System;
using UnityEngine;

namespace TopDownRPG.Combat
{
    [Serializable]
    public sealed class AttackSpec
    {
        [Min(0f)] public float windup = 0.16f;
        [Min(0.05f)] public float recovery = 0.32f;
        [Min(0.1f)] public float range = 2f;
        [Min(0.02f)] public float hitRadius = 0.24f;
        [Range(5f, 360f)] public float arcDegrees = 135f;
        [Min(0f)] public float force = 5f;

        public static AttackSpec Default(AttackType type)
        {
            switch (type)
            {
                case AttackType.Horizontal:
                    return new AttackSpec { windup = 0.13f, recovery = 0.31f, range = 1.9f,
                        hitRadius = 0.22f, arcDegrees = 130f, force = 5f };
                case AttackType.Spin:
                    return new AttackSpec { windup = 0.27f, recovery = 0.65f, range = 1.9f,
                        hitRadius = 0.22f, arcDegrees = 360f, force = 5f };
                case AttackType.Thrust:
                    return new AttackSpec { windup = 0.12f, recovery = 0.36f, range = 2.45f,
                        hitRadius = 0.18f, arcDegrees = 14f, force = 7f };
                default:
                    return new AttackSpec { windup = 0.25f, recovery = 0.45f, range = 2.05f,
                        hitRadius = 0.26f, arcDegrees = 25f, force = 8f };
            }
        }
    }

    [CreateAssetMenu(menuName = "TopDown RPG/Weapon Definition", fileName = "WeaponDefinition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string displayName = "Practice Sword";
        public AttackSpec horizontal = AttackSpec.Default(AttackType.Horizontal);
        public AttackSpec spin = AttackSpec.Default(AttackType.Spin);
        public AttackSpec thrust = AttackSpec.Default(AttackType.Thrust);
        public AttackSpec vertical = AttackSpec.Default(AttackType.Vertical);

        public AttackSpec Get(AttackType type)
        {
            switch (type)
            {
                case AttackType.Horizontal: return horizontal;
                case AttackType.Spin: return spin;
                case AttackType.Thrust: return thrust;
                default: return vertical;
            }
        }
    }

    // Optional: attach to a child weapon in ActiveWeapon to override the default weapon.
    public sealed class EquippedWeapon : MonoBehaviour
    {
        public WeaponDefinition definition;
    }
}
