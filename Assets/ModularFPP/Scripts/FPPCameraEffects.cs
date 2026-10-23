using UnityEngine;

namespace ModularFPP
{
    public class FPPCameraEffects : FPPModule
    {
        public override int Order => 200;      // runs last, after everything has moved

        [Header("Head Bob")]
        [SerializeField] protected bool enableHeadBob = true;
        [SerializeField] protected float bobAmplitudeY = 0.035f;     // meters up/down
        [SerializeField] protected float bobAmplitudeX = 0.02f;      // meters left/right
        [SerializeField] protected float sprintBobMultiplier = 1.5f;
        [SerializeField] protected float crouchBobMultiplier = 0.6f;
        [SerializeField] protected float bobFadeSpeed = 6f;

        [Header("Landing Dip")]
        [SerializeField] protected bool enableLandingDip = true;
        [SerializeField] protected float minDipSpeed = 4f;           // softer landings do nothing
        [SerializeField] protected float hardDipSpeed = 14f;         // this fast = full dip
        [SerializeField] protected float maxDip = 0.15f;             // meters
        [SerializeField] protected float dipDownTime = 0.05f;        // fast drop
        [SerializeField] protected float dipRecoverTime = 0.18f;     // slower rise

        [Header("Sprint FOV Kick")]
        [SerializeField] protected bool enableFovKick = true;
        [SerializeField] protected float sprintFovBoost = 8f;        // degrees added while sprinting
        [SerializeField] protected float fovSpeed = 8f;

        private Transform camTransform;
        private Vector3 baseLocalPos;
        private float baseFov;

        // head bob state
        private float bobTimer;
        private float bobBlend;
        private float stateMultiplier = 1f;

        // landing dip state
        private float dipCurrent, dipTarget, dipVelocity;
        private bool dipGoingDown;

        protected override void Awake()
        {
            base.Awake();

            if (core.PlayerCamera == null)
            {
                Debug.LogError("FPPCameraEffects: no camera found on FPPCore!", this);
                enabled = false;
                return;
            }

            camTransform = core.PlayerCamera.transform;
            baseLocalPos = camTransform.localPosition;
            baseFov = core.PlayerCamera.fieldOfView;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            core.Landed += HandleLanded;
        }

        protected override void OnDisable()
        {
            if (core != null)
            {
                core.Landed -= HandleLanded;

                if (camTransform != null) camTransform.localPosition = baseLocalPos;
                if (core.PlayerCamera != null) core.PlayerCamera.fieldOfView = baseFov;
            }
            base.OnDisable();
        }

        public override void Tick(float dt)
        {
            Vector3 offset = Vector3.zero;

            if (enableHeadBob) offset += CalculateBob(dt);
            if (enableLandingDip) offset.y += CalculateDip(dt);

            camTransform.localPosition = baseLocalPos + offset;

            if (enableFovKick) UpdateFov(dt);
        }

        // ---------- HEAD BOB ----------
        private Vector3 CalculateBob(float dt)
        {
            float speed = core.ActualHorizontalSpeed;
            bool moving = core.IsGrounded && !core.IsSliding && speed > 0.5f;

            // fade the bob in and out so it never pops
            bobBlend = Mathf.MoveTowards(bobBlend, moving ? 1f : 0f, bobFadeSpeed * dt);

            float targetMul = core.IsSprinting ? sprintBobMultiplier
                            : core.IsCrouching ? crouchBobMultiplier : 1f;
            stateMultiplier = Mathf.Lerp(stateMultiplier, targetMul, 1f - Mathf.Exp(-8f * dt));

            // timer counts FOOTSTEPS (same math as the footstep sounds, so they stay in sync)
            if (moving) bobTimer += speed / core.StrideLength * dt;

            float amount = bobBlend * stateMultiplier;
            float y = -Mathf.Cos(bobTimer * Mathf.PI * 2f) * bobAmplitudeY * amount;   // lowest at each footfall
            float x = Mathf.Cos(bobTimer * Mathf.PI) * bobAmplitudeX * amount;   // sways foot to foot

            return new Vector3(x, y, 0f);
        }

        // ---------- LANDING DIP ----------
        private void HandleLanded(float impactSpeed, float airTime)
        {
            if (!enableLandingDip || impactSpeed < minDipSpeed) return;

            float t = Mathf.InverseLerp(minDipSpeed, hardDipSpeed, impactSpeed);
            float amount = Mathf.Lerp(maxDip * 0.2f, maxDip, t);

            dipTarget = -amount;
            dipGoingDown = true;
        }

        private float CalculateDip(float dt)
        {
            float smoothTime = dipGoingDown ? dipDownTime : dipRecoverTime;
            dipCurrent = Mathf.SmoothDamp(dipCurrent, dipTarget, ref dipVelocity, smoothTime, Mathf.Infinity, dt);

            // once we are ~90% of the way down, start coming back up
            if (dipGoingDown && dipCurrent <= dipTarget * 0.9f)
            {
                dipGoingDown = false;
                dipTarget = 0f;
            }

            return dipCurrent;
        }

        // ---------- SPRINT FOV KICK ----------
        private void UpdateFov(float dt)
        {
            bool kicking = core.IsSprinting && core.ActualHorizontalSpeed > 0.5f;
            float target = baseFov + (kicking ? sprintFovBoost : 0f);

            Camera cam = core.PlayerCamera;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, 1f - Mathf.Exp(-fovSpeed * dt));
        }
    }
}
