using TopDownRPG.Networking;
using Unity.Netcode;
using UnityEngine;

namespace TopDownRPG.Combat
{
    // TEST ONLY. Replace this with your injury + armor + conditions pipeline.
    // It intentionally does NOT subtract arbitrary hit points.
    public sealed class CombatTestReceiver : MonoBehaviour, ICombatStrikeReceiver
    {
        [SerializeField] private bool logHits = true;
        [SerializeField] private NetworkCapsuleController playerIfApplicable;

        private void Awake()
        {
            if (playerIfApplicable == null)
                playerIfApplicable = GetComponent<NetworkCapsuleController>();
        }

        public void ReceiveStrike(CombatStrike strike)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                return;

            if (playerIfApplicable != null)
            {
                NetworkCapsuleController.DefenseResult defense =
                    playerIfApplicable.EvaluateDefense(strike.AttackerPosition);
                if (defense != NetworkCapsuleController.DefenseResult.None)
                {
                    if (defense == NetworkCapsuleController.DefenseResult.Parried &&
                        NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                            strike.AttackerNetworkObjectId, out NetworkObject attackerObject) &&
                        attackerObject.TryGetComponent(out NetworkCapsuleController attacker))
                    {
                        attacker.ServerApplyStagger(0.5f);
                    }
                    if (logHits)
                        Debug.Log($"{name} {defense} an incoming {strike.Attack} attack", this);
                    return;
                }
            }

            if (logHits)
                Debug.Log($"[SERVER HIT] {name} - {strike.Region} by {strike.Attack}, " +
                    $"force={strike.Force:0.0}, point={strike.Point}", this);

            // TODO: forward to ArmorResolver.Resolve(strike) and Conditions.Apply(...)
        }
    }
}
