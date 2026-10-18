using UnityEngine;

namespace ModularFPP
{
    [RequireComponent(typeof(FPPMovement))]    // Jump needs Movement for gravity + moving
    public class FPPJump : FPPModule
    {
        public override int Order => 50;      // after Look, before Movement

        [Header("Jump")]
        [SerializeField] protected float jumpHeight = 1.3f;
        [SerializeField] protected bool canJumpWhileSliding = true;

        [Header("Forgiveness")]
        [SerializeField] protected float coyoteTime = 0.12f;
        [SerializeField] protected float jumpBufferTime = 0.15f;

        [Header("Ceiling Check")]
        [SerializeField] protected float ceilingClearance = 0.4f;

        [Header("Sounds")]
    
        [SerializeField, Range(0f, 1f)] protected float jumpVolume = 0.6f;
        [SerializeField, Range(0.5f, 1.5f)] protected float landPitch = 0.8f;
        [SerializeField] protected float minLandSpeed = 3f;
        [SerializeField] protected float hardLandSpeed = 14f;
        [SerializeField] protected float minAirTimeForLanding = 0.08f;

        protected float coyoteTimer, bufferTimer, airTime, peakFallSpeed;
        protected bool wasGrounded = true;

        public override void Tick(float dt)
        {
            coyoteTimer = core.IsGrounded ? coyoteTime : coyoteTimer - dt;

            if (core.JumpAction.WasPressedThisFrame()) bufferTimer = jumpBufferTime;
            else bufferTimer -= dt;

            TrackAirAndLanding(dt);

            if (bufferTimer > 0f && coyoteTimer > 0f && CanJump())
                PerformJump();
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
            float r = cc.radius * 0.9f;
            Vector3 headSphere = transform.position + Vector3.up * (cc.height - cc.radius);
            float distance = ceilingClearance + (cc.radius - r);

            return !Physics.SphereCast(headSphere, r, Vector3.up, out _, distance,
                core.GroundMask, QueryTriggerInteraction.Ignore);
        }

        protected virtual void PerformJump()
        {
            core.VerticalVelocity = Mathf.Sqrt(jumpHeight * -2f * core.Gravity);   // v = sqrt(2*g*h)
            core.IsGrounded = false;
            core.LaunchedThisFrame = true;       // tells Movement: skip gravity this frame

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
            if (surface == null || !HasClips(surface.landClips)) return;   // no card = no sound

            // volume scales with impact: lerp(0.4, 1.0, InverseLerp(3, 14, speed))
            float volume = Mathf.Lerp(0.4f, 1f, Mathf.InverseLerp(minLandSpeed, hardLandSpeed, impactSpeed))
                           * surface.volumeMultiplier;

            AudioClip clip = surface.landClips[Random.Range(0, surface.landClips.Length)];
            core.PlaySound(clip, Mathf.Clamp01(volume), landPitch);
        }

        protected virtual void PlayJumpSound()
        {
            SurfaceSound surface = core.GetCurrentSurface();
            if (surface == null || !HasClips(surface.jumpClips)) return;

            float volume = jumpVolume * surface.volumeMultiplier;
            core.PlaySound(surface.jumpClips[Random.Range(0, surface.jumpClips.Length)], Mathf.Clamp01(volume), 1f);
        }
    }
}