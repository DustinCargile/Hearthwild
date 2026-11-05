using UnityEngine;

namespace TopDownRPG.Combat
{
    // A prototype, procedural weapon animation for an empty socket + child weapon.
    // No asset animation clips or Animator Controller are required to test it.
    public sealed class WeaponVisualDriver : MonoBehaviour
    {
        [Tooltip("Tilted to aim pitch; child of player root. Should be at shoulder/weapon origin height.")]
        public Transform heightPivot;
        [Tooltip("The empty ActiveWeapon object, child of HeightPivot.")]
        public Transform activeWeapon;
        [Tooltip("If assigned, triggers Animator parameters (AttackHorizontal, AttackSpin, etc.).")]
        public Animator optionalAnimator;
        public bool useProceduralSwings = true;

        private Vector3 baseWeaponPosition;
        private Quaternion baseWeaponRotation;
        private Vector3 basePivotPosition;
        private Quaternion basePivotRotation;
        private bool initialized;

        private bool playing;
        private AttackType currentAttack;
        private float attackStartTime;
        private float attackDuration;
        private float activePitch;
        private float readyPitch;
        private bool blocking;
        private float parryUntil;

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (initialized || heightPivot == null || activeWeapon == null) return;
            baseWeaponPosition = activeWeapon.localPosition;
            baseWeaponRotation = activeWeapon.localRotation;
            basePivotPosition = heightPivot.localPosition;
            basePivotRotation = heightPivot.localRotation;
            readyPitch = 0f;
            initialized = true;
        }

        // Positive pitch aims up; negative pitch aims down.
        public void SetReadyPitch(float pitchDegrees)
        {
            Initialize();
            readyPitch = pitchDegrees;
        }

        public void Play(AttackType attack, float pitchDegrees, float totalDuration)
        {
            Initialize();
            if (!initialized) return;
            playing = true;
            currentAttack = attack;
            activePitch = pitchDegrees;
            attackDuration = Mathf.Max(0.05f, totalDuration);
            attackStartTime = Time.time;

            if (optionalAnimator != null)
                optionalAnimator.SetTrigger("Attack" + attack);
        }

        public void SetBlocking(bool value)
        {
            blocking = value;
            if (optionalAnimator != null)
                optionalAnimator.SetBool("Blocking", value);
        }

        public void PlayParry()
        {
            parryUntil = Time.time + 0.22f;
            if (optionalAnimator != null)
                optionalAnimator.SetTrigger("Parry");
        }

        private void LateUpdate()
        {
            Initialize();
            if (!initialized) return;

            if (playing && Time.time >= attackStartTime + attackDuration)
                playing = false;

            float chosenPitch = playing ? activePitch : readyPitch;
            heightPivot.localPosition = basePivotPosition;
            // Rotating this parent tilts horizontal, thrust, and vertical swings together.
            heightPivot.localRotation = basePivotRotation * Quaternion.Euler(-chosenPitch, 0f, 0f);

            if (!useProceduralSwings)
                return;

            activeWeapon.localPosition = baseWeaponPosition;
            activeWeapon.localRotation = baseWeaponRotation;

            if (playing)
            {
                float t = Mathf.Clamp01((Time.time - attackStartTime) / attackDuration);
                float sweep = Mathf.SmoothStep(0f, 1f, t);
                switch (currentAttack)
                {
                    case AttackType.Horizontal:
                        activeWeapon.localRotation = baseWeaponRotation * Quaternion.Euler(0f, Mathf.Lerp(-95f, 95f, sweep), 0f);
                        break;
                    case AttackType.Spin:
                        activeWeapon.localRotation = baseWeaponRotation * Quaternion.Euler(0f, Mathf.Lerp(-180f, 180f, sweep), 0f);
                        break;
                    case AttackType.Thrust:
                        activeWeapon.localPosition = baseWeaponPosition + Vector3.forward * (0.75f * Mathf.Sin(t * Mathf.PI));
                        break;
                    case AttackType.Vertical:
                        activeWeapon.localRotation = baseWeaponRotation * Quaternion.Euler(Mathf.Lerp(-80f, 100f, sweep), 0f, 0f);
                        break;
                }
            }
            else if (Time.time < parryUntil)
            {
                activeWeapon.localRotation = baseWeaponRotation * Quaternion.Euler(0f, -50f, -65f);
            }
            else if (blocking)
            {
                activeWeapon.localRotation = baseWeaponRotation * Quaternion.Euler(0f, -25f, 50f);
            }
        }
    }
}
