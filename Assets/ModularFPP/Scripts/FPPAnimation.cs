using System.Collections.Generic;
using UnityEngine;

namespace ModularFPP
{
    public class FPPAnimation : FPPModule
    {
        public override int Order => 300; // Runs after the movement module updates CharacterController velocity.

        [Header("Animators (auto-found under the Player if empty)")]
        [SerializeField] private Animator[] animators;

        [Header("Parameter names (must match your Animator Controller)")]
        [SerializeField] private string XVelocityParam = "X_Velocity";
        [SerializeField] private string YVelocityParam = "Y_Velocity";
        [SerializeField] private string ZVelocityParam = "Z_Velocity";
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string sprintingParam = "IsSprinting";
        [SerializeField] private string crouchingParam = "IsCrouching";
        [SerializeField] private string groundedParam = "Grounded";
        [SerializeField] private string FallingParam = "Falling";
        [SerializeField] private string jumpTrigger = "Jump";
        [SerializeField] private string landTrigger = "Land";

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float moveDamp = 0.1f;
        [SerializeField, Min(0f)] private float minLandImpactForAnim = 4f;
        [SerializeField] private bool showDebug;
        [SerializeField, Min(0.01f)] private float jumpTriggerResetDelay = 0.2f;

        private sealed class Target
        {
            public Animator animator;
            public bool hasX, hasY, hasZ, hasSpeed;
            public bool hasSprinting, hasCrouching, hasGrounded, hasFalling;
            public bool hasJumpTrigger, hasLandTrigger;
        }

        private readonly List<Target> targets = new List<Target>(2);
        private int xHash, yHash, zHash, speedHash, sprintingHash, crouchingHash;
        private int groundedHash, fallingHash, jumpHash, landHash;
        private float jumpTriggerResetTimer;
        private bool jumpTriggeredThisFrame;
        private Vector2 lastMove;
        private float lastSpeed;

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
            groundedHash = Animator.StringToHash(groundedParam);
            fallingHash = Animator.StringToHash(FallingParam);
            jumpHash = Animator.StringToHash(jumpTrigger);
            landHash = Animator.StringToHash(landTrigger);

            foreach (Animator animator in animators)
            {
                if (animator == null) continue;
                if (animator.runtimeAnimatorController == null)
                {
                    Debug.LogWarning($"FPPAnimation: '{animator.name}' has no Animator Controller assigned.", animator);
                    continue;
                }

                var target = new Target { animator = animator };
                AnimatorControllerParameter[] parameters = animator.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    AnimatorControllerParameter parameter = parameters[i];
                    int hash = parameter.nameHash;
                    bool isFloat = parameter.type == AnimatorControllerParameterType.Float;
                    bool isBool = parameter.type == AnimatorControllerParameterType.Bool;
                    bool isTrigger = parameter.type == AnimatorControllerParameterType.Trigger;

                    if (isFloat)
                    {
                        if (hash == xHash) target.hasX = true;
                        else if (hash == yHash) target.hasY = true;
                        else if (hash == zHash) target.hasZ = true;
                        else if (hash == speedHash) target.hasSpeed = true;
                    }
                    else if (isBool)
                    {
                        if (hash == sprintingHash) target.hasSprinting = true;
                        else if (hash == crouchingHash) target.hasCrouching = true;
                        else if (hash == groundedHash) target.hasGrounded = true;
                        else if (hash == fallingHash) target.hasFalling = true;
                    }
                    else if (isTrigger)
                    {
                        if (hash == jumpHash) target.hasJumpTrigger = true;
                        else if (hash == landHash) target.hasLandTrigger = true;
                    }
                }

                targets.Add(target);
            }

            if (targets.Count == 0)
            {
                Debug.LogWarning("FPPAnimation: no usable Animator found under the Player.", this);
                enabled = false;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (core == null) return;
            core.Jumped += HandleJumped;
            core.Landed += HandleLanded;
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
            // Core caches velocity after CharacterController.Move; no repeated native velocity reads.
            Vector3 localVelocity = transform.InverseTransformDirection(core.ActualVelocity);
            float horizontalSpeed = core.ActualHorizontalSpeed;
            lastMove = new Vector2(localVelocity.x, localVelocity.z);
            lastSpeed = horizontalSpeed;

            bool grounded = core.IsGrounded;
            bool falling = !grounded && localVelocity.y < -0.1f;
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
                if (target.hasGrounded) animator.SetBool(groundedHash, grounded);
                if (target.hasFalling) animator.SetBool(fallingHash, falling);
            }

            // Clear a trigger if the Animator could not consume it during the reset window.
            if (jumpTriggeredThisFrame)
            {
                // Never clear the trigger before Unity has had a chance to evaluate this frame's Animator.
                jumpTriggeredThisFrame = false;
            }
            else if (jumpTriggerResetTimer > 0f)
            {
                jumpTriggerResetTimer -= dt;
                if (jumpTriggerResetTimer <= 0f) Fire(jumpHash, false);
            }
        }

        private void HandleJumped()
        {
            Fire(landHash, false);
            Fire(jumpHash, true);
            jumpTriggerResetTimer = jumpTriggerResetDelay;
            jumpTriggeredThisFrame = true;
        }

        private void HandleLanded(float impactSpeed, float airTime)
        {
            if (impactSpeed >= minLandImpactForAnim) Fire(landHash, true);
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

        private void OnGUI()
        {
            if (!showDebug) return;
            GUI.Label(new Rect(10, 85, 480, 25),
                $"MoveX {lastMove.x:F2} | MoveY {lastMove.y:F2} | Speed {lastSpeed:F2}");
        }
    }
}
