using UnityEngine;

namespace TopDownRPG.Networking
{
    // Put on the scene's Main Camera. Spawned owner assigns Target automatically.
    public sealed class TopDownCameraFollow : MonoBehaviour
    {
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 13f, -9f);
        [SerializeField, Range(0.1f, 30f)] private float followSharpness = 12f;
        public Transform Target { get; set; }

        private void LateUpdate()
        {
            if (Target == null) return;
            Vector3 destination = Target.position + cameraOffset;
            float fraction = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, destination, fraction);
            transform.rotation = Quaternion.LookRotation(Target.position + Vector3.up * 0.9f - transform.position,
                Vector3.up);
        }
    }
}
