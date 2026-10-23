using System.Collections.Generic;
using UnityEngine;

namespace ModularFPP
{
    public class FPPAnimation : FPPModule
    {
        public override int Order => 300;

        [Header("Animators")]
        [SerializeField] private Animator[] animators;

        [Header("Parameters")]
        [SerializeField] private string XVelocityParam = "X_Velocity";
        [SerializeField] private string YVelocityParam = "Y_Velocity";
        [SerializeField] private string ZVelocityParam = "Z_Velocity";
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string sprintingParam = "IsSprinting";
        [SerializeField] private string crouchingParam = "IsCrouching";
        [SerializeField] private string fallingParam = "Falling";
        [SerializeField] private string jumpTrigger = "Jump";
        [SerializeField] private string landTrigger = "Land"; // ADDED BACK

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float moveDamp = 0.1f;

        // NEW: Prevents the falling animation from playing while winding up for a jump
        [Tooltip("How fast must the player be falling to trigger the Falling animation?")]
        [SerializeField] private float fallingVelocityThreshold = -2f;

        [SerializeField] private bool showDebug;
        [SerializeField, Min(0.01f)] private float triggerResetDelay = 0.2f;

        private sealed class Target
        {
            public Animator animator;
            public bool hasX, hasY, hasZ, hasSpeed;
            public bool hasSprinting, hasCrouching, hasGrounded, hasFalling;
            public bool hasJumpTrigger, hasLandTrigger;
        }

        private readonly List<Target> targets = new List<Target>(2);
        private int xHash, yHash, zHash, speedHash, sprintingHash, crouchingHash;
        private int  fallingHash, jumpHash, landHash;

        private float jumpResetTimer;
        private float landResetTimer;
        private bool jumpTriggeredThisFrame;
        private bool landTriggeredThisFrame;

        protected override void Awake()
        {
            base.Awake();

            if (animators == null || animators.Length == 0)
                animators = GetComponentsInChildren<Animator>();

            xHash = Animator.StringToHash(XVelocityParam);
            yHash = Animator.StringToHash(YVelocityParam);
            zHash = Animator.StringToHash(ZVelocityParam);
            speedHash = Animator.StringToHash(speedParam);
            sprintingHash = Animator.StringToHash(sprintingParam);
            crouchingHash = Animator.StringToHash(crouchingParam);
            fallingHash = Animator.StringToHash(fallingParam);
            jumpHash = Animator.StringToHash(jumpTrigger);
            landHash = Animator.StringToHash(landTrigger);

            foreach (Animator animator in animators)
            {
                if (animator == null) continue;
                if (animator.runtimeAnimatorController == null) continue;

                var target = new Target { animator = animator };
                AnimatorControllerParameter[] parameters = animator.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    if (p.type == AnimatorControllerParameterType.Float)
                    {
                        if (p.nameHash == xHash) target.hasX = true;
                        else if (p.nameHash == yHash) target.hasY = true;
                        else if (p.nameHash == zHash) target.hasZ = true;
                        else if (p.nameHash == speedHash) target.hasSpeed = true;
                    }
                    else if (p.type == AnimatorControllerParameterType.Bool)
                    {
                        if (p.nameHash == sprintingHash) target.hasSprinting = true;
                        else if (p.nameHash == crouchingHash) target.hasCrouching = true;
                        else if (p.nameHash == fallingHash) target.hasFalling = true;
                    }
                    else if (p.type == AnimatorControllerParameterType.Trigger)
                    {
                        if (p.nameHash == jumpHash) target.hasJumpTrigger = true;
                        else if (p.nameHash == landHash) target.hasLandTrigger = true;
                    }
                }
                targets.Add(target);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (core == null) return;
            core.Jumped += HandleJumped;
            core.Landed += HandleLanded; // Re-subscribed
        }

        protected override void OnDisable()
        {
            if (core != null)
            {
                core.Jumped -= HandleJumped;
                core.Landed -= HandleLanded;
            }
            base.OnDisable();
        }

        public override void Tick(float dt)
        {
            Vector3 localVelocity = transform.InverseTransformDirection(core.ActualVelocity);
            float horizontalSpeed = core.ActualHorizontalSpeed;

            bool grounded = core.IsGrounded;

            // Use threshold to ensure windup doesn't trigger falling
            bool falling = !grounded && localVelocity.y < fallingVelocityThreshold;

            bool sprinting = core.IsSprinting && horizontalSpeed > 0.5f;
            bool crouching = core.IsCrouching;

            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                Animator animator = target.animator;
                if (animator == null || !animator.isActiveAndEnabled) continue;

                if (target.hasX) animator.SetFloat(xHash, localVelocity.x, moveDamp, dt);
                if (target.hasY) animator.SetFloat(yHash, localVelocity.z, moveDamp, dt);
                if (target.hasZ) animator.SetFloat(zHash, localVelocity.y, moveDamp, dt);
                if (target.hasSpeed) animator.SetFloat(speedHash, horizontalSpeed, moveDamp, dt);
                if (target.hasSprinting) animator.SetBool(sprintingHash, sprinting);
                if (target.hasCrouching) animator.SetBool(crouchingHash, crouching);
                if (target.hasFalling) animator.SetBool(fallingHash, falling);
            }

            HandleTriggerResets(dt);
        }

        private void HandleJumped()
        {
            Fire(jumpHash, true);
            jumpResetTimer = triggerResetDelay;
            jumpTriggeredThisFrame = true;
        }

        private void HandleLanded(float impactSpeed, float airTime)
        {
            // Fire Land trigger instantly when touched ground
            Fire(landHash, true);
            landResetTimer = triggerResetDelay;
            landTriggeredThisFrame = true;
        }

        private void HandleTriggerResets(float dt)
        {
            if (jumpTriggeredThisFrame) jumpTriggeredThisFrame = false;
            else if (jumpResetTimer > 0f)
            {
                jumpResetTimer -= dt;
                if (jumpResetTimer <= 0f) Fire(jumpHash, false);
            }

            if (landTriggeredThisFrame) landTriggeredThisFrame = false;
            else if (landResetTimer > 0f)
            {
                landResetTimer -= dt;
                if (landResetTimer <= 0f) Fire(landHash, false);
            }
        }

        private void Fire(int hash, bool activate)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                if (target.animator == null) continue;

                bool hasTrigger = hash == jumpHash ? target.hasJumpTrigger : target.hasLandTrigger;
                if (!hasTrigger) continue;

                target.animator.ResetTrigger(hash);
                if (activate) target.animator.SetTrigger(hash);
            }
        }
    }
}