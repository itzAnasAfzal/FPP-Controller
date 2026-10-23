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

        [Header("Forgiveness")]
        [SerializeField, Min(0f)] protected float coyoteTime = 0.12f;
        [SerializeField, Min(0f)] protected float jumpBufferTime = 0.15f;

        [Header("Ceiling Check")]
        [SerializeField, Min(0f)] protected float ceilingClearance = 0.4f;

        [Header("Sounds")]
        [SerializeField, Range(0f, 1f)] protected float jumpVolume = 0.6f;
        [SerializeField, Range(0.5f, 1.5f)] protected float landPitch = 0.8f;
        [SerializeField, Min(0f)] protected float minLandSpeed = 3f;
        [SerializeField, Min(0.01f)] protected float hardLandSpeed = 14f;
        [SerializeField, Min(0f)] protected float minAirTimeForLanding = 0.08f;

        private float coyoteTimer;
        private float bufferTimer;
        private float airTime;
        private float peakFallSpeed;
        private bool wasGrounded = true;

        public override void Tick(float dt)
        {
            // Keep a short grace period after walking off an edge; zero disables only the grace period.
            coyoteTimer = core.IsGrounded ? coyoteTime : Mathf.Max(0f, coyoteTimer - dt);

            bool pressedThisFrame = core.JumpAction.WasPressedThisFrame();
            bufferTimer = pressedThisFrame
                ? jumpBufferTime
                : Mathf.Max(0f, bufferTimer - dt);

            TrackAirAndLanding(dt);

            // Input, jump impulse, event, and animation trigger all happen in this frame.
            bool hasJumpRequest = pressedThisFrame || bufferTimer > 0f;
            bool hasGroundGrace = core.IsGrounded || coyoteTimer > 0f;
            if (hasJumpRequest && hasGroundGrace && CanJump()) PerformJump();
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

        protected virtual void PerformJump()
        {
            // Called immediately after CanJump succeeds in Tick; no delayed launch means no second
            // ceiling physics query is needed in the same frame.
            core.VerticalVelocity = Mathf.Sqrt(Mathf.Max(0f, jumpHeight * -2f * core.Gravity));
            core.IsGrounded = false;
            core.LaunchedThisFrame = true;
            wasGrounded = false;
            coyoteTimer = 0f;
            bufferTimer = 0f;
            airTime = 0f;
            peakFallSpeed = 0f;

            PlayJumpSound();
            core.NotifyJumped();
        }

        private void TrackAirAndLanding(float dt)
        {
            if (!core.IsGrounded)
            {
                airTime += dt;
                peakFallSpeed = Mathf.Max(peakFallSpeed, -core.VerticalVelocity);
            }
            else if (!wasGrounded)
            {
                if (airTime >= minAirTimeForLanding)
                {
                    PlayLandSound(peakFallSpeed);
                    core.NotifyLanded(peakFallSpeed, airTime);
                }

                airTime = 0f;
                peakFallSpeed = 0f;
            }

            wasGrounded = core.IsGrounded;
        }

        protected virtual void PlayLandSound(float impactSpeed)
        {
            if (impactSpeed < minLandSpeed) return;

            SurfaceSound surface = core.GetCurrentSurface();
            if (surface == null || !HasClips(surface.landClips)) return;

            float impactBlend = Mathf.InverseLerp(minLandSpeed, hardLandSpeed, impactSpeed);
            float volume = Mathf.Lerp(0.4f, 1f, impactBlend) * surface.volumeMultiplier;
            AudioClip clip = surface.landClips[Random.Range(0, surface.landClips.Length)];
            core.PlaySound(clip, volume, landPitch);
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
