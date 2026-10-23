using UnityEngine;
using UnityEngine.InputSystem;

namespace ModularFPP
{
    public class FPPCrouch : FPPModule
    {
        public override int Order => 25;      // after Look, before Jump (50) and Movement (100)

        [Header("Crouch")]
        [SerializeField] protected float crouchHeight = 1.0f;
        [SerializeField, Range(0.1f, 1f)] protected float crouchSpeedMultiplier = 0.5f;
        [Tooltip("ON = press once to crouch, press again to stand. OFF = hold to crouch.")]
        [SerializeField] protected bool toggleMode = false;
        [SerializeField] protected bool allowCrouchInAir = true;

        [Header("Transition")]
        [SerializeField] protected float transitionSpeed = 10f;

        [Header("Ceiling Check")]
        [Tooltip("Everything EXCEPT the Player layer. (The Player's own layer is also removed automatically.)")]
        [SerializeField] protected LayerMask obstructionMask = ~0;
        [Tooltip("Extra room above standing head needed to jump out of a crouch.")]
        [SerializeField] protected float jumpStandMargin = 0.4f;

        [Header("Sound")]
        [SerializeField, Range(0f, 1f)] protected float crouchStepVolume = 0.4f;

        [Header("Debug")]
        [SerializeField] protected bool showDebug = false;

        protected float standHeight;
        protected float cameraStandY;
        protected float cameraCrouchY;

        private bool toggled;
        private bool ignoreHold;             // after a jump-cancel, wait for key release

        protected override void Awake()
        {
            base.Awake();

            if (core.CameraRoot == null)
            {
                Debug.LogError("FPPCrouch: assign a Camera Root on FPPCore before enabling crouch.", this);
                enabled = false;
                return;
            }

            CharacterController cc = core.Controller;
            standHeight = cc.height;
            crouchHeight = Mathf.Clamp(crouchHeight, cc.radius * 2f + 0.01f, standHeight - 0.1f);

            // Read the starting values from the scene so there's less to configure
            cameraStandY = core.CameraRoot.localPosition.y;
            cameraCrouchY = cameraStandY - (standHeight - crouchHeight);

            // Never let the player block itself
            if (gameObject.layer == 0)
                Debug.LogWarning("FPPCrouch: the Player is on the Default layer. Put it on its own 'Player' layer.", this);
            else
                obstructionMask = obstructionMask.value & ~(1 << gameObject.layer);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            core.Jumped += HandleJumped;
        }

        protected override void OnDisable()
        {
            if (core != null)
            {
                core.Jumped -= HandleJumped;

                bool canRestoreStandingShape = core.Controller != null
                    && core.CameraRoot != null
                    && standHeight > 0f
                    && CanStandUp();

                if (canRestoreStandingShape)
                {
                    CharacterController cc = core.Controller;
                    cc.height = standHeight;
                    cc.center = new Vector3(cc.center.x, standHeight * 0.5f, cc.center.z);

                    Vector3 cameraPosition = core.CameraRoot.localPosition;
                    cameraPosition.y = cameraStandY;
                    core.CameraRoot.localPosition = cameraPosition;

                    core.SpeedMultiplier = 1f;
                    core.StepVolumeMultiplier = 1f;
                    core.SprintBlocked = false;
                    core.JumpBlocked = false;
                    core.IsCrouching = false;
                }
                else if (standHeight > 0f && core.Controller != null
                         && core.Controller.height < standHeight - 0.01f)
                {
                    // If disabled under a low ceiling, keep the crouched state and restrictions
                    // rather than leaving a short capsule with standing-speed settings.
                    core.IsCrouching = true;
                    core.SpeedMultiplier = crouchSpeedMultiplier;
                    core.StepVolumeMultiplier = crouchStepVolume;
                    core.SprintBlocked = true;
                    core.JumpBlocked = true;
                }
                else
                {
                    core.SpeedMultiplier = 1f;
                    core.StepVolumeMultiplier = 1f;
                    core.SprintBlocked = false;
                    core.JumpBlocked = false;
                    core.IsCrouching = false;
                }
            }

            base.OnDisable();
        }

        public override void Tick(float dt)
        {
            CharacterController cc = core.Controller;

            // 1) What does the player WANT?
            bool wantsCrouch = ReadCrouchInput();
            if (!allowCrouchInAir && !core.IsGrounded && !core.IsCrouching) wantsCrouch = false;

            // 2) What are we ALLOWED to do?
            bool before = core.IsCrouching;
            bool crouching = before;

            if (wantsCrouch) crouching = true;
            else if (crouching && CanStandUp()) crouching = false;

            core.IsCrouching = crouching;

            if (showDebug && before != crouching)
                Debug.Log($"[Crouch] Crouching = {crouching}");

            // 3) Smoothly animate capsule + camera
            float t = Mathf.Clamp01(dt * transitionSpeed);

            float targetHeight = crouching ? crouchHeight : standHeight;
            float h = Mathf.Lerp(cc.height, targetHeight, t);
            if (Mathf.Abs(h - targetHeight) < 0.001f) h = targetHeight;

            cc.height = h;
            cc.center = new Vector3(cc.center.x, h * 0.5f, cc.center.z);   // keeps feet on the floor

            Vector3 pivot = core.CameraRoot.localPosition;
            pivot.y = Mathf.Lerp(pivot.y, crouching ? cameraCrouchY : cameraStandY, t);
            core.CameraRoot.localPosition = pivot;

            // 4) Tell the other features through Core's switches
            float blend = Mathf.InverseLerp(standHeight, crouchHeight, h);   // 0 = standing, 1 = crouched
            core.SpeedMultiplier = Mathf.Lerp(1f, crouchSpeedMultiplier, blend);
            core.StepVolumeMultiplier = Mathf.Lerp(1f, crouchStepVolume, blend);
            core.SprintBlocked = crouching || blend > 0.2f;
            core.JumpBlocked = crouching && !CanStandUp(jumpStandMargin);
        }

        private bool ReadCrouchInput()
        {
            InputAction action = core.CrouchAction;

            if (toggleMode)
            {
                if (action.WasPressedThisFrame()) toggled = !toggled;
                return toggled;
            }

            if (!action.IsPressed()) ignoreHold = false;
            return action.IsPressed() && !ignoreHold;
        }

        // Is the space between the crouched head and the standing head empty?
        protected virtual bool CanStandUp(float extraHeight = 0f)
        {
            CharacterController cc = core.Controller;

            Vector3 bottom = transform.position + Vector3.up * (crouchHeight - cc.radius);
            Vector3 top = transform.position + Vector3.up * (standHeight + extraHeight - cc.radius);

            return !Physics.CheckCapsule(bottom, top, cc.radius * 0.9f,
                obstructionMask, QueryTriggerInteraction.Ignore);
        }

        // Jumping cancels the crouch (Jump already checked there's room to stand)
        private void HandleJumped()
        {
            if (!core.IsCrouching) return;

            toggled = false;
            ignoreHold = true;

            // The jump is already validated against standing headroom. Update shared state now,
            // so animation/camera modules later this frame see the uncrouch transition immediately.
            core.IsCrouching = false;
            core.SpeedMultiplier = 1f;
            core.StepVolumeMultiplier = 1f;
            core.SprintBlocked = false;
            core.JumpBlocked = false;
        }

        private void OnGUI()
        {
            if (!showDebug || core == null) return;
            GUI.Label(new Rect(10, 35, 700, 25),
                $"Height {core.Controller.height:F2} | Crouching {core.IsCrouching} | CanStand {CanStandUp()} | Speed x{core.SpeedMultiplier:F2}");
        }
    }
}
