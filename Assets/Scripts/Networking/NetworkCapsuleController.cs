using System.Collections;
using System.Collections.Generic;
using TopDownRPG.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TopDownRPG.Networking
{
    // MVP: server-authoritative movement and hit detection; client-side aiming/input.
    // Requires NetworkObject + SERVER-AUTHORITATIVE NetworkTransform + CharacterController.
    // No network prediction is implemented yet.
    [RequireComponent(typeof(CharacterController), typeof(NetworkObject))]
    public sealed class NetworkCapsuleController : NetworkBehaviour
    {
        [Header("Camera / Aim")]
        [SerializeField] private Camera ownerCamera;
        [SerializeField] private Transform localAimMarker;
        [Tooltip("Colliders the local camera can aim at (terrain, enemies, props). Exclude the local player's layer if possible.")]
        [SerializeField] private LayerMask aimLayers = ~0;
        [Tooltip("Start aimed strikes at this fraction of the capsule height above the feet.")]
        [SerializeField, Range(0.25f, 0.95f)] private float strikeOriginHeightFraction = 0.68f;
        [Tooltip("Clamp how steeply the player can aim up or down.")]
        [SerializeField, Range(0f, 89f)] private float maxAimPitch = 65f;
        [Tooltip("Screen distance from the player to the right-stick aiming cursor, as a fraction of the shortest screen dimension.")]
        [SerializeField, Range(0.05f, 0.45f)] private float gamepadAimRadius = 0.25f;
        [SerializeField, Min(0.5f)] private float maxAimRayDistance = 150f;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 4.8f;
        [SerializeField, Min(0.1f)] private float sprintSpeed = 7.5f;
        [SerializeField, Min(0.1f)] private float dodgeSpeed = 12f;
        [SerializeField, Min(0.05f)] private float dodgeDuration = 0.21f;
        [SerializeField, Min(0f)] private float dodgeCooldown = 0.7f;
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -22f;
        [SerializeField, Range(5f, 60f)] private float inputSendHz = 20f;

        [Header("Combat")]
        [SerializeField] private WeaponDefinition fallbackWeapon;
        [SerializeField] private Transform activeWeapon;
        [SerializeField] private WeaponVisualDriver weaponVisuals;
        [SerializeField] private Animator characterAnimator;
        [SerializeField, Min(0.1f)] private float tapHoldThreshold = 0.27f;
        [SerializeField, Min(0f)] private float parryDuration = 0.20f;
        [SerializeField, Min(0f)] private float parryCooldown = 0.6f;
        [Tooltip("Only colliders on these layers can receive attack hits. Include BodyHitZone triggers.")]
        [SerializeField] private LayerMask targetLayers = ~0;
        [Tooltip("Choose your Environment/Walls layer; exclude characters and weapons.")]
        [SerializeField] private LayerMask obstructionLayers = 0;

        private CharacterController capsule;
        private InputActionMap input;
        private InputAction moveAction;
        private InputAction pointerAction;
        private InputAction stickAimAction;
        private InputAction horizontalAction;
        private InputAction thrustAction;
        private InputAction defendAction;
        private InputAction mobilityAction;
        private InputAction jumpAction;

        private bool useGamepadAim;
        private float aimYaw;
        private float aimPitch;
        private Vector3 aimPoint;
        private Vector2 gamepadAimDirection = Vector2.up;
        private readonly RaycastHit[] aimRayBuffer = new RaycastHit[64];
        private float nextInputSend;
        private float horizontalPressedAt;
        private float thrustPressedAt;
        private float defendPressedAt;
        private float mobilityPressedAt;
        private bool horizontalHeld;
        private bool thrustHeld;
        private bool defendHeld;
        private bool mobilityHeld;

        // Server-only state. No client-controlled transforms or client-reported hits.
        private Vector2 serverMove;
        private float serverAimYaw;
        private float serverAimPitch;
        private bool serverSprinting;
        private float lastInputAt;
        private float verticalSpeed;
        private bool jumpQueued;
        private Vector3 dodgeDirection;
        private float dodgeRemaining;
        private float nextDodgeTime;
        private bool attacking;
        private bool blocking;
        private float parryUntil;
        private float nextParryTime;
        private float staggerUntil;

        private readonly Collider[] hitBuffer = new Collider[64];
        private readonly Dictionary<int, HitCandidate> candidates = new Dictionary<int, HitCandidate>();

        private struct HitCandidate
        {
            public MonoBehaviour receiver;
            public HitRegion region;
            public Vector3 point;
            public float score;
        }

        public float FeetWorldY => transform.position.y + capsule.center.y - capsule.height * 0.5f;
        public bool IsBlockingOnServer => IsServer && blocking;
        // The point where weapon attack vectors originate, computed independently on each network instance.
        public Vector3 StrikeOrigin => new Vector3(transform.position.x, FeetWorldY + capsule.height * strikeOriginHeightFraction, transform.position.z);

        private void Awake()
        {
            capsule = GetComponent<CharacterController>();
            if (weaponVisuals == null) weaponVisuals = GetComponentInChildren<WeaponVisualDriver>(true);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                serverAimYaw = transform.eulerAngles.y;
                lastInputAt = Time.time;
            }

            if (localAimMarker != null)
                localAimMarker.gameObject.SetActive(IsOwner && IsClient);

            if (IsOwner && IsClient)
            {
                if (ownerCamera == null) ownerCamera = Camera.main;
                if (ownerCamera != null && ownerCamera.TryGetComponent(out TopDownCameraFollow cameraFollow))
                    cameraFollow.Target = transform;

                aimYaw = transform.eulerAngles.y;
                aimPitch = 0f;
                aimPoint = StrikeOrigin + transform.forward * 3f;
                CreateInput();
                input.Enable();
            }
        }

        public override void OnNetworkDespawn() => DisposeInput();
        private void OnDestroy() => DisposeInput();

        private void DisposeInput()
        {
            if (input == null) return;
            input.Disable();
            input.Dispose();
            input = null;
        }

        private void CreateInput()
        {
            input = new InputActionMap("CapsuleGameplay");

            moveAction = input.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddBinding("<Gamepad>/leftStick");

            pointerAction = input.AddAction("Pointer", InputActionType.Value, "<Mouse>/position");
            stickAimAction = input.AddAction("StickAim", InputActionType.Value, "<Gamepad>/rightStick");

            horizontalAction = input.AddAction("HorizontalOrSpin", InputActionType.Button);
            horizontalAction.AddBinding("<Mouse>/leftButton");
            horizontalAction.AddBinding("<Gamepad>/rightShoulder");

            thrustAction = input.AddAction("ThrustOrVertical", InputActionType.Button);
            thrustAction.AddBinding("<Mouse>/rightButton");
            thrustAction.AddBinding("<Gamepad>/rightTrigger");

            defendAction = input.AddAction("ParryOrBlock", InputActionType.Button);
            defendAction.AddBinding("<Keyboard>/leftCtrl");
            defendAction.AddBinding("<Gamepad>/leftTrigger");

            mobilityAction = input.AddAction("DodgeOrSprint", InputActionType.Button);
            mobilityAction.AddBinding("<Keyboard>/leftShift");
            mobilityAction.AddBinding("<Gamepad>/buttonEast");

            jumpAction = input.AddAction("Jump", InputActionType.Button);
            jumpAction.AddBinding("<Keyboard>/space");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");
        }

        private void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner && IsClient && input != null)
                UpdateOwnerInput();

            if (IsServer)
                UpdateServerMovement(Time.deltaTime);
        }

        private void UpdateOwnerInput()
        {
            // The aim vector is 3D. The mouse or right stick chooses a point in the scene;
            // yaw + pitch come from that point relative to the character's weapon origin.
            Vector2 stick = stickAimAction.ReadValue<Vector2>();
            if (stick.sqrMagnitude > 0.06f)
            {
                useGamepadAim = true;
                gamepadAimDirection = stick.normalized;
            }
            if (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 1f)
                useGamepadAim = false;

            if (ownerCamera != null)
            {
                Vector2 cursor;
                if (useGamepadAim)
                {
                    Vector3 playerScreen = ownerCamera.WorldToScreenPoint(StrikeOrigin);
                    float pixelRadius = Mathf.Min(Screen.width, Screen.height) * gamepadAimRadius;
                    cursor = new Vector2(playerScreen.x, playerScreen.y) + gamepadAimDirection * pixelRadius;
                }
                else
                {
                    cursor = pointerAction.ReadValue<Vector2>();
                }

                Ray ray = ownerCamera.ScreenPointToRay(cursor);
                if (TryFindAimPoint(ray, out Vector3 targetPoint))
                {
                    Vector3 toPoint = targetPoint - StrikeOrigin;
                    if (toPoint.sqrMagnitude > 0.01f)
                    {
                        // One directional vector determines both the horizontal facing and
                        // the vertical tilt of all attack trajectories.
                        float horizontalDistance = new Vector2(toPoint.x, toPoint.z).magnitude;
                        if (horizontalDistance > 0.05f)
                            aimYaw = Mathf.Atan2(toPoint.x, toPoint.z) * Mathf.Rad2Deg;
                        aimPitch = Mathf.Clamp(Mathf.Atan2(toPoint.y,
                            Mathf.Max(0.05f, horizontalDistance)) * Mathf.Rad2Deg,
                            -maxAimPitch, maxAimPitch);
                        aimPoint = targetPoint;
                    }
                }
            }

            if (localAimMarker != null) localAimMarker.position = aimPoint;
            if (weaponVisuals != null) weaponVisuals.SetReadyPitch(aimPitch);

            HandleAttackButton(horizontalAction, ref horizontalPressedAt, ref horizontalHeld,
                AttackType.Horizontal, AttackType.Spin);
            HandleAttackButton(thrustAction, ref thrustPressedAt, ref thrustHeld,
                AttackType.Thrust, AttackType.Vertical);
            HandleDefenseButton();
            HandleMobilityButton();

            if (jumpAction.WasPressedThisFrame()) JumpRpc();

            if (Time.time >= nextInputSend)
            {
                nextInputSend = Time.time + 1f / inputSendHz;
                MoveInputRpc(Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f),
                    aimYaw, aimPitch, mobilityHeld);
            }
        }

        private bool TryFindAimPoint(Ray ray, out Vector3 point)
        {
            // Prefer the nearest raycast impact that isn't part of our own avatar.
            // Triggers can represent individually aimable body parts on later rigs.
            int count = Physics.RaycastNonAlloc(ray, aimRayBuffer, maxAimRayDistance,
                aimLayers, QueryTriggerInteraction.Collide);
            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            point = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = aimRayBuffer[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    point = hit.point;
                    found = true;
                }
            }
            if (found) return true;

            // No geometry under the cursor: aim toward the ground at player's feet.
            Plane ground = new Plane(Vector3.up, new Vector3(0f, FeetWorldY, 0f));
            if (ground.Raycast(ray, out float distance) && distance <= maxAimRayDistance)
            {
                point = ray.GetPoint(distance);
                return true;
            }
            return false;
        }

        // Tap attacks execute on release; held attacks execute once when the threshold passes.
        private void HandleAttackButton(InputAction action, ref float pressedAt, ref bool held,
            AttackType tapped, AttackType holdAttack)
        {
            if (action.WasPressedThisFrame())
            {
                pressedAt = Time.time;
                held = false;
            }
            if (action.IsPressed() && !held && Time.time - pressedAt >= tapHoldThreshold)
            {
                held = true;
                RequestAttackRpc((byte)holdAttack, aimYaw, aimPitch);
            }
            if (action.WasReleasedThisFrame() && !held)
                RequestAttackRpc((byte)tapped, aimYaw, aimPitch);
        }

        private void HandleDefenseButton()
        {
            if (defendAction.WasPressedThisFrame())
            {
                defendPressedAt = Time.time;
                defendHeld = false;
            }
            if (defendAction.IsPressed() && !defendHeld && Time.time - defendPressedAt >= tapHoldThreshold)
            {
                defendHeld = true;
                SetBlockingRpc(true);
            }
            if (defendAction.WasReleasedThisFrame())
            {
                if (defendHeld) SetBlockingRpc(false);
                else ParryRpc();
                defendHeld = false;
            }
        }

        private void HandleMobilityButton()
        {
            if (mobilityAction.WasPressedThisFrame())
            {
                mobilityPressedAt = Time.time;
                mobilityHeld = false;
            }
            if (mobilityAction.IsPressed() && !mobilityHeld && Time.time - mobilityPressedAt >= tapHoldThreshold)
                mobilityHeld = true;
            if (mobilityAction.WasReleasedThisFrame())
            {
                if (!mobilityHeld) DodgeRpc();
                mobilityHeld = false;
            }
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        private void MoveInputRpc(Vector2 movement, float yaw, float pitch, bool sprint, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsFinite(yaw) || !IsFinite(pitch)) return;
            if (!IsFinite(movement.x) || !IsFinite(movement.y)) return;
            serverMove = Vector2.ClampMagnitude(movement, 1f);
            serverAimYaw = Mathf.Repeat(yaw, 360f);
            serverAimPitch = Mathf.Clamp(pitch, -maxAimPitch, maxAimPitch);
            serverSprinting = sprint;
            lastInputAt = Time.time;
        }

        [Rpc(SendTo.Server)]
        private void JumpRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            jumpQueued = true;
        }

        [Rpc(SendTo.Server)]
        private void DodgeRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (attacking || blocking || dodgeRemaining > 0f || Time.time < nextDodgeTime || Time.time < staggerUntil) return;

            Vector3 move = new Vector3(serverMove.x, 0f, serverMove.y);
            dodgeDirection = move.sqrMagnitude > 0.01f
                ? move.normalized
                : Quaternion.Euler(0f, serverAimYaw, 0f) * Vector3.forward;
            dodgeRemaining = dodgeDuration;
            nextDodgeTime = Time.time + dodgeDuration + dodgeCooldown;
            DodgeVisualRpc();
        }

        [Rpc(SendTo.Server)]
        private void SetBlockingRpc(bool enabled, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (enabled && (attacking || dodgeRemaining > 0f || Time.time < staggerUntil)) return;
            blocking = enabled;
            BlockVisualRpc(enabled);
        }

        [Rpc(SendTo.Server)]
        private void ParryRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (attacking || blocking || dodgeRemaining > 0f || Time.time < nextParryTime || Time.time < staggerUntil) return;
            parryUntil = Time.time + parryDuration;
            nextParryTime = Time.time + parryCooldown;
            ParryVisualRpc();
        }

        [Rpc(SendTo.Server)]
        private void RequestAttackRpc(byte rawType, float yaw, float pitch, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (rawType > (byte)AttackType.Vertical || !IsFinite(yaw) || !IsFinite(pitch)) return;
            if (attacking || blocking || dodgeRemaining > 0f || Time.time < staggerUntil) return;

            AttackType type = (AttackType)rawType;
            float lockedPitch = Mathf.Clamp(pitch, -maxAimPitch, maxAimPitch);
            float lockedYaw = Mathf.Repeat(yaw, 360f);
            AttackSpec spec = GetAttackSpec(type);
            attacking = true;
            serverAimYaw = lockedYaw;
            serverAimPitch = lockedPitch;
            transform.rotation = Quaternion.Euler(0f, lockedYaw, 0f);

            AttackVisualRpc(rawType, lockedPitch, spec.windup + spec.recovery);
            StartCoroutine(ServerAttack(type, lockedYaw, lockedPitch, spec));
        }

        private AttackSpec GetAttackSpec(AttackType type)
        {
            WeaponDefinition equipped = fallbackWeapon;
            if (activeWeapon != null)
            {
                EquippedWeapon instance = activeWeapon.GetComponentInChildren<EquippedWeapon>(true);
                if (instance != null && instance.definition != null) equipped = instance.definition;
            }
            AttackSpec spec = equipped != null ? equipped.Get(type) : null;
            return spec ?? AttackSpec.Default(type);
        }

        private IEnumerator ServerAttack(AttackType type, float yaw, float pitch, AttackSpec spec)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, spec.windup));
            if (IsSpawned && IsServer) EvaluateStrike(type, yaw, pitch, spec);
            yield return new WaitForSeconds(Mathf.Max(0.05f, spec.recovery));
            attacking = false;
        }

        private void UpdateServerMovement(float deltaTime)
        {
            // Stop a remote client's walking if its movement messages stop arriving.
            Vector2 movement = Time.time - lastInputAt > 0.3f ? Vector2.zero : serverMove;
            bool sprinting = Time.time - lastInputAt <= 0.3f && serverSprinting;
            if (!attacking && dodgeRemaining <= 0f)
                transform.rotation = Quaternion.Euler(0f, serverAimYaw, 0f);

            Vector3 horizontal = new Vector3(movement.x, 0f, movement.y);
            float speed = sprinting ? sprintSpeed : walkSpeed;
            if (blocking) speed *= 0.45f;
            if (attacking) speed *= 0.45f;
            if (Time.time < staggerUntil) speed = 0f;
            horizontal *= speed;

            if (dodgeRemaining > 0f)
            {
                horizontal = dodgeDirection * dodgeSpeed;
                dodgeRemaining = Mathf.Max(0f, dodgeRemaining - deltaTime);
            }

            if (capsule.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            if (jumpQueued && capsule.isGrounded && dodgeRemaining <= 0f && Time.time >= staggerUntil)
            {
                verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
                JumpVisualRpc();
            }
            jumpQueued = false;
            verticalSpeed += gravity * deltaTime;
            capsule.Move((horizontal + Vector3.up * verticalSpeed) * deltaTime);
        }

        // SERVER ONLY. The entire attack pattern is tilted by aim pitch.
        // E.g. -30 degrees means a forward horizontal slice travels downward
        // at 30 degrees instead of remaining in a world-horizontal plane.
        private void EvaluateStrike(AttackType type, float yaw, float pitch, AttackSpec spec)
        {
            candidates.Clear();
            float range = Mathf.Clamp(spec.range, 0.2f, 5f);
            float radius = Mathf.Clamp(spec.hitRadius, 0.05f, 0.75f);
            Vector3 basePoint = StrikeOrigin;
            Quaternion swingFrame = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-pitch, 0f, 0f);
            Vector3 aimedForward = swingFrame * Vector3.forward;
            Vector3 tiltedUp = swingFrame * Vector3.up;

            if (type == AttackType.Horizontal || type == AttackType.Spin)
            {
                float arc = type == AttackType.Spin ? 360f : Mathf.Clamp(spec.arcDegrees, 5f, 360f);
                int steps = type == AttackType.Spin ? 24 : 12;
                for (int i = 0; i <= steps; i++)
                {
                    float degrees = -arc * 0.5f + arc * i / steps;
                    Vector3 direction = swingFrame * (Quaternion.Euler(0f, degrees, 0f) * Vector3.forward);
                    for (int r = 1; r <= 3; r++)
                    {
                        float distance = range * r / 3f;
                        Probe(basePoint + direction * distance, radius);
                    }
                }
            }
            else if (type == AttackType.Thrust)
            {
                for (int i = 1; i <= 12; i++)
                    Probe(basePoint + aimedForward * (range * i / 12f), radius);
            }
            else // Vertical slash: drops through the up/down axis of the tilted frame.
            {
                for (int i = 0; i <= 12; i++)
                {
                    float t = i / 12f;
                    Vector3 point = basePoint + aimedForward * (range * (0.55f + 0.45f * t))
                                    + tiltedUp * Mathf.Lerp(0.8f, -0.8f, t);
                    Probe(point, radius);
                }
            }

            foreach (HitCandidate candidate in candidates.Values)
            {
                Vector3 rayOrigin = basePoint + aimedForward * 0.15f;
                Vector3 rayPath = candidate.point - rayOrigin;
                if (obstructionLayers.value != 0 && rayPath.sqrMagnitude > 0.01f &&
                    Physics.Raycast(rayOrigin, rayPath.normalized, rayPath.magnitude,
                        obstructionLayers, QueryTriggerInteraction.Ignore))
                    continue;

                CombatStrike hit = new CombatStrike(OwnerClientId, NetworkObjectId, type, candidate.region,
                    candidate.point, transform.position, aimedForward, spec.force,
                    candidate.point.y - FeetWorldY);
                ((ICombatStrikeReceiver)candidate.receiver).ReceiveStrike(hit);
            }
        }

        private void Probe(Vector3 point, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(point, radius, hitBuffer,
                targetLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider collider = hitBuffer[i];
                if (collider == null || collider.transform.IsChildOf(transform)) continue;

                MonoBehaviour receiver = FindReceiver(collider);
                if (receiver == null) continue;

                Vector3 contact = collider.ClosestPoint(point);
                float distance = Vector3.Distance(contact, point);
                BodyHitZone explicitZone = collider.GetComponent<BodyHitZone>();
                HitRegion region = explicitZone != null
                    ? explicitZone.region
                    : InferRegion(contact.y, receiver.transform);
                // Explicit anatomical hitboxes outrank generic capsule colliders.
                float score = distance - (explicitZone != null ? 1f : 0f);
                int id = receiver.GetInstanceID();
                if (!candidates.TryGetValue(id, out HitCandidate old) || score < old.score)
                {
                    candidates[id] = new HitCandidate
                    {
                        receiver = receiver,
                        point = contact,
                        region = region,
                        score = score
                    };
                }
            }
        }

        private static MonoBehaviour FindReceiver(Collider collider)
        {
            MonoBehaviour[] parents = collider.GetComponentsInParent<MonoBehaviour>(true);
            foreach (MonoBehaviour component in parents)
                if (component is ICombatStrikeReceiver) return component;
            return null;
        }

        private static HitRegion InferRegion(float worldY, Transform target)
        {
            float feet = target.position.y;
            CharacterController controller = target.GetComponent<CharacterController>();
            if (controller != null)
                feet += controller.center.y - controller.height * 0.5f;
            float h = worldY - feet;
            if (h < 0.23f) return HitRegion.Feet;
            if (h < 0.68f) return HitRegion.Knee;
            if (h < 1.02f) return HitRegion.Legs;
            if (h < 1.68f) return HitRegion.Torso;
            return HitRegion.Head;
        }

        // Call on the server when another player's strike reaches this character.
        public DefenseResult EvaluateDefense(Vector3 attackerPosition)
        {
            if (!IsServer) return DefenseResult.None;
            Vector3 towardAttacker = attackerPosition - transform.position;
            towardAttacker.y = 0f;
            if (towardAttacker.sqrMagnitude < 0.01f) return DefenseResult.None;
            float facingDot = Vector3.Dot(transform.forward, towardAttacker.normalized);
            if (facingDot < 0.25f) return DefenseResult.None;
            if (Time.time <= parryUntil) return DefenseResult.Parried;
            return blocking ? DefenseResult.Blocked : DefenseResult.None;
        }

        // A successful parry may briefly prevent the attacking player from acting.
        public void ServerApplyStagger(float duration)
        {
            if (!IsServer) return;
            staggerUntil = Mathf.Max(staggerUntil, Time.time + Mathf.Clamp(duration, 0f, 2f));
        }

        public enum DefenseResult { None, Blocked, Parried }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        [Rpc(SendTo.ClientsAndHost)]
        private void AttackVisualRpc(byte rawType, float pitch, float duration)
        {
            if (weaponVisuals != null)
                weaponVisuals.Play((AttackType)rawType, pitch, duration);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BlockVisualRpc(bool value)
        {
            if (weaponVisuals != null) weaponVisuals.SetBlocking(value);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ParryVisualRpc()
        {
            if (weaponVisuals != null) weaponVisuals.PlayParry();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DodgeVisualRpc()
        {
            if (characterAnimator != null) characterAnimator.SetTrigger("Dodge");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void JumpVisualRpc()
        {
            if (characterAnimator != null) characterAnimator.SetTrigger("Jump");
        }
    }
}
