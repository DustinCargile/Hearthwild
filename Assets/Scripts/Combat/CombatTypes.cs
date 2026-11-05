using UnityEngine;

namespace TopDownRPG.Combat
{
    public enum AttackType : byte
    {
        Horizontal = 0,
        Spin = 1,
        Thrust = 2,
        Vertical = 3
    }

    public enum HitRegion : byte
    {
        Unspecified = 0,
        Feet = 1,
        Knee = 2,
        Legs = 3,
        Torso = 4,
        Arms = 5,
        Head = 6
    }

    // Data-only hit notification. The server invokes this; no hit points are assumed.
    public readonly struct CombatStrike
    {
        public readonly ulong AttackerClientId;
        public readonly ulong AttackerNetworkObjectId;
        public readonly AttackType Attack;
        public readonly HitRegion Region;
        public readonly Vector3 Point;
        public readonly Vector3 AttackerPosition;
        public readonly Vector3 Direction;
        public readonly float Force;
        public readonly float HeightAboveAttackerFeet;

        public CombatStrike(ulong attackerClientId, ulong attackerNetworkObjectId, AttackType attack, HitRegion region,
            Vector3 point, Vector3 attackerPosition, Vector3 direction,
            float force, float heightAboveAttackerFeet)
        {
            AttackerClientId = attackerClientId;
            AttackerNetworkObjectId = attackerNetworkObjectId;
            Attack = attack;
            Region = region;
            Point = point;
            AttackerPosition = attackerPosition;
            Direction = direction;
            Force = force;
            HeightAboveAttackerFeet = heightAboveAttackerFeet;
        }
    }

    // Implement this in an armor/injury/conditions receiver later.
    // ReceiveStrike must only be called on the authoritative server.
    public interface ICombatStrikeReceiver
    {
        void ReceiveStrike(CombatStrike strike);
    }

}
