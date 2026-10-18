using UnityEngine;
using UnityEngine.UI;

namespace ModularFPP
{
    public class FPPMovement : FPPModule
    {
        public override int Order => 100;      // runs last: it applies everyone's decisions

        [Header("Movement")]
        [SerializeField] protected float walkSpeed = 4f;
        [SerializeField] protected float sprintSpeed = 7f;
        [SerializeField] protected float acceleration = 30f;
        [SerializeField] protected float deceleration = 40f;
        [SerializeField, Range(0f, 1f)] protected float airControl = 0.4f;

        [Header("Ground Snapping")]
        [SerializeField] protected float groundedPush = -2f;
        [SerializeField] protected float snapGapThreshold = 0.1f;
        [SerializeField] protected float downStepSpeed = 8f;

        [Header("Steep Slopes")]
        [SerializeField] protected float slideSpeed = 8f;
        [SerializeField] protected float slideAcceleration = 20f;

        [Header("Stamina")]
        [SerializeField] protected float maxStamina = 100f;
        [SerializeField] protected float sprintDrainPerSecond = 20f;
        [SerializeField] protected float staminaRegenPerSecond = 15f;
        [SerializeField] protected float regenDelay = 1f;
        [SerializeField] protected float exhaustedRegenDelay = 1.5f;
        [SerializeField] protected float recoverThreshold = 25f;

        [Header("Stamina UI")]
        [SerializeField] protected Image staminaFill;
        [SerializeField] protected CanvasGroup staminaGroup;
        [SerializeField] protected bool hideWhenFull = true;
        [SerializeField] protected Color normalColor = Color.white;
        [SerializeField] protected Color exhaustedColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] protected float uiFadeSpeed = 4f;
        [SerializeField] protected bool showStaminaDebug = false;

        [Header("Footsteps")]   // default when no surface matches
        [SerializeField, Range(0f, 1f)] protected float walkStepVolume = 0.5f;
        [SerializeField, Range(0f, 1f)] protected float sprintStepVolume = 0.8f;
        [SerializeField, Range(0f, 0.3f)] protected float stepPitchVariation = 0.08f;

        protected Vector2 moveInput;
        protected Vector3 horizontalVelocity;
        protected Vector3 slideVelocity;

        protected bool isSprinting;
        protected float stamina;
        protected float regenTimer;
        protected bool isExhausted;

        private AudioSource footstepSource;
        private float stepDistance;
        private int lastStepIndex = -1;

        protected override void Awake()
        {
            base.Awake();
            stamina = maxStamina;

            footstepSource = gameObject.AddComponent<AudioSource>();
            footstepSource.playOnAwake = false;
            footstepSource.spatialBlend = 0f;
        }

        public override void Tick(float dt)
        {
            moveInput = Vector2.ClampMagnitude(core.MoveAction.ReadValue<Vector2>(), 1f);

            HandleSprint();
            HandleStamina(dt);
            core.IsSprinting = isSprinting;

            HandleHorizontalVelocity(dt);
            HandleSlide(dt);
            HandleGravity(dt);
            ApplyMovement(dt);
            HandleFootsteps(dt);
            UpdateStaminaUI(dt);
        }

        // ---------- SPRINT + STAMINA ----------
        protected virtual void HandleSprint()
        {
            bool wantsSprint = core.SprintAction.IsPressed() && moveInput.y > 0.1f;
            isSprinting = wantsSprint && !isExhausted && stamina > 0f
                          && !core.IsSliding && !core.SprintBlocked;
        }

        protected virtual void HandleStamina(float dt)
        {
            bool drainingNow = isSprinting && core.ActualHorizontalSpeed > 0.5f;

            if (drainingNow)
            {
                stamina -= sprintDrainPerSecond * dt;
                regenTimer = regenDelay;

                if (stamina <= 0f)
                {
                    stamina = 0f;
                    isExhausted = true;
                    isSprinting = false;
                    regenTimer = exhaustedRegenDelay;
                }
            }
            else
            {
                regenTimer -= dt;
                if (regenTimer <= 0f)
                    stamina = Mathf.Min(maxStamina, stamina + staminaRegenPerSecond * dt);
            }

            if (isExhausted && stamina >= recoverThreshold) isExhausted = false;
        }

        // ---------- HORIZONTAL ----------
        protected virtual float GetTargetSpeed()
            => (isSprinting ? sprintSpeed : walkSpeed) * core.SpeedMultiplier;

        protected virtual void HandleHorizontalVelocity(float dt)
        {
            Vector3 wishDir = transform.right * moveInput.x + transform.forward * moveInput.y;
            Vector3 target = wishDir * GetTargetSpeed();

            float rate = target.sqrMagnitude > 0.01f ? acceleration : deceleration;
            if (!core.IsGrounded) rate *= airControl;

            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, rate * dt);
        }

        protected virtual void HandleSlide(float dt)
        {
            Vector3 target = Vector3.zero;
            if (core.IsSliding)
                target = Vector3.ProjectOnPlane(Vector3.down, core.GroundNormal).normalized * slideSpeed;

            slideVelocity = Vector3.MoveTowards(slideVelocity, target, slideAcceleration * dt);
        }

        // ---------- GRAVITY ----------
        protected virtual void HandleGravity(float dt)
        {
            if (core.LaunchedThisFrame) return;     // Jump already set the velocity this frame

            float v = core.VerticalVelocity;

            if (core.IsGrounded && v <= 0f)
                v = core.GroundGap > snapGapThreshold ? Mathf.Min(v, -downStepSpeed) : groundedPush;
            else
                v += core.Gravity * dt;

            core.VerticalVelocity = Mathf.Max(v, -50f);
        }

        // ---------- THE ONE PLACE WE MOVE ----------
        protected virtual void ApplyMovement(float dt)
        {
            Vector3 planar = horizontalVelocity;

            if (core.IsGrounded && !core.IsSliding && planar.sqrMagnitude > 0.0001f)
                planar = Vector3.ProjectOnPlane(planar, core.GroundNormal).normalized * planar.magnitude;

            Vector3 velocity = planar + slideVelocity + Vector3.up * core.VerticalVelocity;
            CollisionFlags flags = core.Controller.Move(velocity * dt);

            if ((flags & CollisionFlags.Above) != 0 && core.VerticalVelocity > 0f)
                core.VerticalVelocity = 0f;
        }

        // ---------- FOOTSTEPS ----------
        protected virtual void HandleFootsteps(float dt)
        {
            if (!core.IsGrounded || core.IsSliding) return;

            float speed = core.ActualHorizontalSpeed;
            if (speed < 0.5f) return;

            stepDistance += speed * dt;
            if (stepDistance >= core.StrideLength)
            {
                stepDistance -= core.StrideLength;
                PlayFootstep();
            }
        }

        protected virtual void PlayFootstep()
        {
            SurfaceSound surface = core.GetCurrentSurface();
            if (surface == null || !HasClips(surface.footstepClips)) return;   // no surface card = no sound

            AudioClip[] clips = surface.footstepClips;

            int index = Random.Range(0, clips.Length);
            if (clips.Length > 1 && index == lastStepIndex) index = (index + 1) % clips.Length;
            lastStepIndex = index;

            float volume = (isSprinting ? sprintStepVolume : walkStepVolume)
                           * surface.volumeMultiplier * core.StepVolumeMultiplier;

            footstepSource.pitch = 1f + Random.Range(-stepPitchVariation, stepPitchVariation);
            footstepSource.PlayOneShot(clips[index], Mathf.Clamp01(volume));
        }

        // ---------- STAMINA UI ----------
        protected virtual void UpdateStaminaUI(float dt)
        {
            if (staminaFill != null)
            {
                staminaFill.fillAmount = stamina / maxStamina;
                staminaFill.color = isExhausted ? exhaustedColor : normalColor;
            }

            if (staminaGroup != null)
            {
                bool show = !hideWhenFull || stamina < maxStamina - 0.01f || isExhausted;
                staminaGroup.alpha = Mathf.MoveTowards(staminaGroup.alpha, show ? 1f : 0f, uiFadeSpeed * dt);
            }
        }

        private void OnGUI()
        {
            if (!showStaminaDebug || core == null) return;
            GUI.Label(new Rect(10, 10, 600, 25),
                $"Stamina {stamina:F1}/{maxStamina} | Sprinting {isSprinting} | Exhausted {isExhausted} | Speed {core.ActualHorizontalSpeed:F1}");
            GUI.Label(new Rect(10, 60, 600, 25),
                $"Grounded {core.IsGrounded} | Slope {core.SlopeAngle:F0}° | Sliding {core.IsSliding} | Gap {core.GroundGap:F2}");
        }
    }
}