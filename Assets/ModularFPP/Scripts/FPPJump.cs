using UnityEngine;

namespace ModularFPP
{
    [RequireComponent(typeof(FPPMovement))]
    public class FPPJump : FPPModule
    {
        public override int Order => 50;

        [Header("Jump")]
        [SerializeField, Min(0f)] protected float jumpHeight = 1.3f;
        [SerializeField] protected bool canJumpWhileSliding = true;

        // NEW: Delay physics to match animation wind-up
        [Tooltip("How long to wait before applying the upward physics force. Match this to the wind-up frames of your jump animation.")]
        [SerializeField, Min(0f)] protected float jumpPhysicsDelay = 0.15f;

        [Header("Forgiveness")]
        [SerializeField, Min(0f)] protected float coyoteTime = 0.12f;
        [SerializeField, Min(0f)] protected float jumpBufferTime = 0.15f;

        [Header("Ceiling Check")]
        [SerializeField, Min(0f)] protected float ceilingClearance = 0.4f;

        [Header("Sounds")]
        [SerializeField, Range(0f, 1f)] protected float jumpVolume = 0.6f;

        private float coyoteTimer;
        private float bufferTimer;
        private float airTime;
        private float peakFallSpeed;
        private bool wasGrounded = true;

        // NEW: Physics delay tracking
        private float physicsDelayTimer = 0f;
        private bool isJumpPending = false;

        public override void Tick(float dt)
        {
            coyoteTimer = core.IsGrounded ? coyoteTime : Mathf.Max(0f, coyoteTimer - dt);

            bool pressedThisFrame = core.JumpAction.WasPressedThisFrame();
            bool canBuffer = core.IsGrounded || coyoteTimer > 0f;

            if (pressedThisFrame && canBuffer)
            {
                bufferTimer = jumpBufferTime;
            }
            else
            {
                bufferTimer = Mathf.Max(0f, bufferTimer - dt);
            }

            TrackAirState(dt);

            bool hasJumpRequest = bufferTimer > 0f;
            bool hasGroundGrace = core.IsGrounded || coyoteTimer > 0f;

            // If we haven't started the jump yet, check if we can
            if (!isJumpPending && hasJumpRequest && hasGroundGrace && CanJump())
            {
                TriggerJumpSequence();
            }

            // Handle the delayed physics launch
            if (isJumpPending)
            {
                physicsDelayTimer -= dt;
                if (physicsDelayTimer <= 0f)
                {
                    ApplyPhysicsJump();
                }
            }
        }

        protected virtual bool CanJump()
        {
            if (core.JumpBlocked) return false;
            if (core.IsSliding && !canJumpWhileSliding) return false;
            return HasHeadroom();
        }

        protected virtual bool HasHeadroom()
        {
            CharacterController cc = core.Controller;
            float radius = cc.radius * 0.9f;
            Vector3 headCenter = transform.position + Vector3.up * (cc.height - cc.radius);
            float castDistance = ceilingClearance + (cc.radius - radius);

            return !Physics.SphereCast(headCenter, radius, Vector3.up, out _, castDistance,
                core.GroundMask, QueryTriggerInteraction.Ignore);
        }

        // Renamed from PerformJump to TriggerJumpSequence
        protected virtual void TriggerJumpSequence()
        {
            // Clear the input buffer so we don't double jump
            bufferTimer = 0f;
            coyoteTimer = 0f;
            isJumpPending = true;
            physicsDelayTimer = jumpPhysicsDelay;

            wasGrounded = false;
            airTime = 0f;
            peakFallSpeed = 0f;

            PlayJumpSound();
            // Fire the animation event IMMEDIATELY
            core.NotifyJumped();
        }

        // NEW: Applies the actual physics force after the delay
        private void ApplyPhysicsJump()
        {
            isJumpPending = false;
            core.VerticalVelocity = Mathf.Sqrt(Mathf.Max(0f, jumpHeight * -2f * core.Gravity));
            core.IsGrounded = false;
            core.LaunchedThisFrame = true;
        }

        private void TrackAirState(float dt)
        {
            if (!core.IsGrounded && !isJumpPending)
            {
                airTime += dt;
                peakFallSpeed = Mathf.Max(peakFallSpeed, -core.VerticalVelocity);
            }
            else if (core.IsGrounded && !wasGrounded)
            {
                // Fire the land event the EXACT frame the ground is touched
                if (airTime >= 0.08f)
                {
                    core.NotifyLanded(peakFallSpeed, airTime);
                }

                airTime = 0f;
                peakFallSpeed = 0f;
            }

            wasGrounded = core.IsGrounded;
        }

        protected virtual void PlayJumpSound()
        {
            SurfaceSound surface = core.GetCurrentSurface();
            if (surface == null || !HasClips(surface.jumpClips)) return;

            AudioClip clip = surface.jumpClips[Random.Range(0, surface.jumpClips.Length)];
            core.PlaySound(clip, jumpVolume * surface.volumeMultiplier);
        }
    }
}