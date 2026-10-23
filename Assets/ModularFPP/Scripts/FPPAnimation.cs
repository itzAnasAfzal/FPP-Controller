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
        [SerializeField] private string movingParam = "Moving";

        [SerializeField] private string crouchingParam = "Crouch";
        [SerializeField] private string fallingParam = "Falling";
        [SerializeField] private string jumpTrigger = "Jump";
        [SerializeField] private string landTrigger = "Land"; // ADDED BACK
        [Header("Moving Detection")]
        [Tooltip("Speed at which Moving flips true.")]
        [SerializeField] private float movingEnterSpeed = 0.1f;

        [Tooltip("Speed below which Moving flips false (must be < enter speed).")]
        [SerializeField] private float movingExitSpeed = 0.05f;

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
            public bool hasX, hasY, hasZ;
            public bool hasCrouching, hasFalling, hasMoving;
            public bool hasJumpTrigger, hasLandTrigger;
        }

        private readonly List<Target> targets = new List<Target>(2);
        private int xHash, yHash, zHash, crouchingHash;
        private int fallingHash, jumpHash, landHash, movingHash;

        private float jumpResetTimer;
        private float landResetTimer;
        private bool jumpTriggeredThisFrame;
        private bool landTriggeredThisFrame;
        private bool isMoving; // cached state

        // Debug throttle so logs don't spam every frame (logs every N frames)
        private const int DebugLogInterval = 10;
        private int debugFrameCounter;

        protected override void Awake()
        {
            base.Awake();

            if (animators == null || animators.Length == 0)
                animators = GetComponentsInChildren<Animator>();

            xHash = Animator.StringToHash(XVelocityParam);
            yHash = Animator.StringToHash(YVelocityParam);
            zHash = Animator.StringToHash(ZVelocityParam);
            crouchingHash = Animator.StringToHash(crouchingParam);
            fallingHash = Animator.StringToHash(fallingParam);
            jumpHash = Animator.StringToHash(jumpTrigger);
            landHash = Animator.StringToHash(landTrigger);
            movingHash = Animator.StringToHash(movingParam);

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
                    }
                    else if (p.type == AnimatorControllerParameterType.Bool)
                    {
                        if (p.nameHash == crouchingHash) target.hasCrouching = true;
                        else if (p.nameHash == fallingHash) target.hasFalling = true;
                        else if (p.nameHash == movingHash) target.hasMoving = true;
                    }
                    else if (p.type == AnimatorControllerParameterType.Trigger)
                    {
                        if (p.nameHash == jumpHash) target.hasJumpTrigger = true;
                        else if (p.nameHash == landHash) target.hasLandTrigger = true;
                    }
                }
                targets.Add(target);

                if (showDebug)
                {
                    Debug.Log(
                        $"[FPPAnimation] Registered animator '{animator.name}' " +
                        $"(hasX={target.hasX}, hasY={target.hasY}, hasZ={target.hasZ}, " +
                        $"hasCrouching={target.hasCrouching}, hasFalling={target.hasFalling}, " +
                        $"hasJumpTrigger={target.hasJumpTrigger}, hasLandTrigger={target.hasLandTrigger})",
                        this);
                }
            }

            if (showDebug)
                Debug.Log($"[FPPAnimation] Awake complete. Targets registered: {targets.Count}", this);
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
            float speed = horizontalSpeed; // same thing, kept for clarity

            bool grounded = core.IsGrounded;
            bool falling = !grounded && localVelocity.y < fallingVelocityThreshold;
            bool sprinting = core.IsSprinting && horizontalSpeed > 0.5f;
            bool crouching = core.IsCrouching;

            if (isMoving)
            {
                if (speed < movingExitSpeed) isMoving = false;
            }
            else
            {
                if (speed > movingEnterSpeed) isMoving = true;
            }
            if (showDebug)
            {
                debugFrameCounter++;
                if (debugFrameCounter >= DebugLogInterval)
                {
                    debugFrameCounter = 0;
                    Debug.Log(
                        $"[FPPAnimation] Core values -> " +
                         $"ActualSpeed={speed} " +
                        $"ActualVelocity={core.ActualVelocity} " +
                        $"LocalVelocity={localVelocity} " +
                        $"ActualHorizontalSpeed={horizontalSpeed:F3} " +
                        $"IsGrounded={grounded} " +
                        $"Falling={falling} " +
                        $"IsSprinting={core.IsSprinting} " +
                        $"Sprinting={sprinting} " +
                        $"IsCrouching={crouching} " +
                        $"moveDamp={moveDamp:F3} fallingVelocityThreshold={fallingVelocityThreshold:F3}",
                        this);
                }
            }


            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                Animator animator = target.animator;
                if (animator == null || !animator.isActiveAndEnabled) continue;

               
                    if (target.hasX) animator.SetFloat(xHash, localVelocity.x, moveDamp, dt);
                    if (target.hasY) animator.SetFloat(yHash, localVelocity.z, moveDamp, dt);
                    if (target.hasZ) animator.SetFloat(zHash, localVelocity.y, moveDamp, dt);
                

                if (target.hasCrouching) animator.SetBool(crouchingHash, crouching);
                if (target.hasFalling) animator.SetBool(fallingHash, falling);
                if (target.hasMoving) animator.SetBool(movingHash, isMoving);
            }

            HandleTriggerResets(dt);
        }

        private void HandleJumped()
        {
            if (showDebug)
                Debug.Log($"[FPPAnimation] Core.Jumped fired -> triggering '{jumpTrigger}'", this);

            Fire(jumpHash, true);
            jumpResetTimer = triggerResetDelay;
            jumpTriggeredThisFrame = true;
        }

        private void HandleLanded(float impactSpeed, float airTime)
        {
            if (showDebug)
                Debug.Log(
                    $"[FPPAnimation] Core.Landed fired -> triggering '{landTrigger}' " +
                    $"(impactSpeed={impactSpeed:F3}, airTime={airTime:F3})",
                    this);

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
                if (jumpResetTimer <= 0f)
                {
                    if (showDebug) Debug.Log($"[FPPAnimation] Resetting trigger '{jumpTrigger}'", this);
                    Fire(jumpHash, false);
                }
            }

            if (landTriggeredThisFrame) landTriggeredThisFrame = false;
            else if (landResetTimer > 0f)
            {
                landResetTimer -= dt;
                if (landResetTimer <= 0f)
                {
                    if (showDebug) Debug.Log($"[FPPAnimation] Resetting trigger '{landTrigger}'", this);
                    Fire(landHash, false);
                }
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