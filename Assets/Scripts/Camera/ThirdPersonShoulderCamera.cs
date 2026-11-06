using UnityEngine;
using UnityEngine.InputSystem;

namespace TopDownRPG.Networking
{
    /// <summary>
    /// Local-only scene camera for a Netcode-owned capsule.
    /// Never parent it to the player or give it a NetworkObject.
    /// Target is assigned by the owning NetworkCapsuleController.OnNetworkSpawn.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ThirdPersonShoulderCamera : MonoBehaviour
    {
        [Header("Shoulder View")]
        [SerializeField, Min(0.5f)] private float followDistance = 3.5f;
        [SerializeField, Range(-1.5f, 1.5f)] private float shoulderOffset = 0.55f;
        [SerializeField, Min(0f)] private float targetHeight = 1.55f;
        [SerializeField] private float cameraRise = 0.20f;
        [SerializeField, Range(1f, 30f)] private float positionSharpness = 16f;

        [Header("Look")]
        [SerializeField, Range(0.01f, 1f)] private float mouseDegreesPerPixel = 0.14f;
        [SerializeField, Range(30f, 400f)] private float stickDegreesPerSecond = 175f;
        [SerializeField] private float initialPitch = 12f;
        [SerializeField] private float minPitch = -60f;  // Look upward
        [SerializeField] private float maxPitch = 70f;   // Look downward
        [SerializeField] private bool invertMouseY;
        [SerializeField] private bool invertStickY;

        [Header("Camera Collision")]
        [Tooltip("Use Ground + Environment; exclude Player, Enemy, Hitbox and WeaponVisual.")]
        [SerializeField] private LayerMask cameraCollisionLayers;
        [SerializeField, Range(0.05f, 0.5f)] private float collisionRadius = 0.2f;
        [SerializeField, Range(0.01f, 0.3f)] private float wallPadding = 0.08f;

        [Header("Debug Reticle")]
        [SerializeField] private bool showReticle = true;

        private Transform target;
        private InputAction mouseLook;
        private InputAction stickLook;
        private float yaw;
        private float pitch;
        private bool snapNextFrame;
        private bool suppressPrimaryMouseAttack;

        public Transform Target
        {
            get => target;
            set
            {
                if (target == value) return;
                target = value;
                if (target != null)
                {
                    yaw = target.eulerAngles.y;
                    pitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
                    snapNextFrame = true;
                    if (Application.isPlaying) LockCursor();
                }
                else
                {
                    suppressPrimaryMouseAttack = false;
                    if (Application.isPlaying) UnlockCursor();
                }
            }
        }

        public float Yaw => yaw;
        public float Pitch => pitch;
        public Quaternion LookRotation => Quaternion.Euler(pitch, yaw, 0f);
        public bool IsCursorLocked => Cursor.lockState == CursorLockMode.Locked;
        public bool SuppressPrimaryMouseAttack => suppressPrimaryMouseAttack;

        private void Awake()
        {
            // PassThrough reliably exposes pointer delta without an InputActionAsset.
            mouseLook = new InputAction("ShoulderCameraMouseLook", InputActionType.PassThrough, "<Mouse>/delta");
            stickLook = new InputAction("ShoulderCameraStickLook", InputActionType.Value, "<Gamepad>/rightStick");
        }

        private void OnEnable()
        {
            mouseLook?.Enable();
            stickLook?.Enable();
        }

        private void OnDisable()
        {
            mouseLook?.Disable();
            stickLook?.Disable();
            if (target != null) UnlockCursor();
        }

        private void OnDestroy()
        {
            mouseLook?.Dispose();
            stickLook?.Dispose();
        }

        private void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (target == null) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                UnlockCursor();
                return;
            }

            // Click once to resume looking without that click firing the sword.
            if (!IsCursorLocked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                suppressPrimaryMouseAttack = true;
                LockCursor();
                return;
            }

            // The flag is also checked by the controller and reset on button release.
            if (suppressPrimaryMouseAttack && Mouse.current != null &&
                !Mouse.current.leftButton.isPressed)
                suppressPrimaryMouseAttack = false;

            Vector2 mouseDelta = IsCursorLocked ? mouseLook.ReadValue<Vector2>() : Vector2.zero;
            Vector2 stickDelta = stickLook.ReadValue<Vector2>() * Time.unscaledDeltaTime;
            float mouseY = invertMouseY ? 1f : -1f;
            float stickY = invertStickY ? 1f : -1f;

            yaw += mouseDelta.x * mouseDegreesPerPixel + stickDelta.x * stickDegreesPerSecond;
            pitch += mouseDelta.y * mouseDegreesPerPixel * mouseY +
                     stickDelta.y * stickDegreesPerSecond * stickY;
            yaw = Mathf.Repeat(yaw, 360f);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Quaternion orientation = LookRotation;
            Vector3 pivot = target.position + Vector3.up * targetHeight;
            Vector3 desiredPosition = pivot + orientation *
                new Vector3(shoulderOffset, cameraRise, -followDistance);

            Vector3 difference = desiredPosition - pivot;
            float distance = difference.magnitude;
            if (cameraCollisionLayers.value != 0 && distance > 0.001f &&
                Physics.SphereCast(pivot, collisionRadius, difference / distance,
                    out RaycastHit hit, distance, cameraCollisionLayers,
                    QueryTriggerInteraction.Ignore))
            {
                desiredPosition = pivot + (difference / distance) *
                    Mathf.Max(0.15f, hit.distance - wallPadding);
            }

            float blend = 1f - Mathf.Exp(-positionSharpness * Time.deltaTime);
            transform.position = snapNextFrame
                ? desiredPosition
                : Vector3.Lerp(transform.position, desiredPosition, blend);
            transform.rotation = orientation;
            snapNextFrame = false;
        }

        private void OnGUI()
        {
            if (!showReticle || target == null || !IsCursorLocked) return;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            float x = Screen.width * 0.5f;
            float y = Screen.height * 0.5f;
            Texture2D tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(x - 11f, y - 1f, 7f, 2f), tex);
            GUI.DrawTexture(new Rect(x + 4f, y - 1f, 7f, 2f), tex);
            GUI.DrawTexture(new Rect(x - 1f, y - 11f, 2f, 7f), tex);
            GUI.DrawTexture(new Rect(x - 1f, y + 4f, 2f, 7f), tex);
            GUI.color = previous;
        }
    }
}
