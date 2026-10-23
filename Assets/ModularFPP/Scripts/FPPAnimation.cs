using System.Collections.Generic;
using UnityEngine;

namespace ModularFPP
{
    public class FPPAnimation : FPPModule
    {
        public override int Order => 300;      // after everything has moved this frame

        [Header("Animators (auto-found under the Player if left empty)")]
        [SerializeField] protected Animator[] animators;

        [Header("Parameter names (must match your Animator Controller)")]
        [SerializeField] protected string XVelocityParam = "X_Velocity";
        [SerializeField] protected string YVelocityParam = "Y_Velocity";
        [SerializeField] protected string ZVelocityParam = "Z_Velocity";
        [SerializeField] protected string speedParam = "Speed";
        [SerializeField] protected string sprintingParam = "IsSprinting";
        [SerializeField] protected string crouchingParam = "IsCrouching";
        [SerializeField] protected string groundedParam = "Grounded"; 
        [SerializeField] protected string FallingParam = "Falling";
        [SerializeField] protected string jumpTrigger = "Jump";
        [SerializeField] protected string landTrigger = "Land";

        [Header("Tuning")]
        [SerializeField] protected float moveDamp = 0.1f;               // smoothing of the blend (seconds)
        [SerializeField] protected float minLandImpactForAnim = 4f;
        [SerializeField] protected bool showDebug = false;
        private float jumpTriggerResetTimer = 0f;
        [SerializeField] private float jumpTriggerResetDelay = 0.2f;

        private class Target
        {
            public Animator animator;
            public readonly HashSet<int> existing = new HashSet<int>();
        }

        private readonly List<Target> targets = new List<Target>();
        private int XVelocityHash, YVelocityHash, ZVelocityHash, speedHash, sprintingHash, crouchingHash, groundedHash, fallingHash, jumpHash, landHash;
        private Vector2 lastMove;

        protected override void Awake()
        {
            base.Awake();

            if (animators == null || animators.Length == 0)
                animators = GetComponentsInChildren<Animator>();

            XVelocityHash = Animator.StringToHash(XVelocityParam);
            YVelocityHash = Animator.StringToHash(YVelocityParam);
            ZVelocityHash = Animator.StringToHash(ZVelocityParam);
            speedHash = Animator.StringToHash(speedParam);
            sprintingHash = Animator.StringToHash(sprintingParam);
            crouchingHash = Animator.StringToHash(crouchingParam);
            groundedHash = Animator.StringToHash(groundedParam);
            fallingHash = Animator.StringToHash(FallingParam);
            jumpHash = Animator.StringToHash(jumpTrigger);
            landHash = Animator.StringToHash(landTrigger);

            foreach (Animator a in animators)
            {
                if (a == null) continue;
                if (a.runtimeAnimatorController == null)
                {
                    Debug.LogWarning($"FPPAnimation: '{a.name}' has no Animator Controller assigned.", a);
                    continue;
                }

                Target t = new Target { animator = a };
                foreach (AnimatorControllerParameter p in a.parameters) t.existing.Add(p.nameHash);
                targets.Add(t);
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
            // Real horizontal velocity, converted to the player's own directions
            Vector3 v = core.Controller.velocity;
            float verticalVelocity = v.y;
            v.y = 0f;
            Vector3 local = transform.InverseTransformDirection(v);   // x = right, z = forward
            float speed = v.magnitude;
            lastMove = new Vector2(local.x, local.z);

            bool grounded = core.IsGrounded;

            // Falling means airborne AND moving downward.
            bool falling = !grounded && verticalVelocity < -0.1f;

            bool sprinting = core.IsSprinting && speed > 0.5f;

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                Animator a = t.animator;

                if (t.existing.Contains(XVelocityHash)) a.SetFloat(XVelocityHash, local.x, moveDamp, dt);
                if (t.existing.Contains(YVelocityHash)) a.SetFloat(YVelocityHash, local.z, moveDamp, dt);
                if (t.existing.Contains(ZVelocityHash)) a.SetFloat(YVelocityHash, local.y, moveDamp, dt);
                if (t.existing.Contains(speedHash)) a.SetFloat(speedHash, speed, moveDamp, dt);

                if (t.existing.Contains(sprintingHash)) a.SetBool(sprintingHash, sprinting);
                if (t.existing.Contains(crouchingHash)) a.SetBool(crouchingHash, core.IsCrouching);


                if (t.existing.Contains(fallingHash)) a.SetBool(fallingHash, falling);

                if (t.existing.Contains(groundedHash)) a.SetBool(groundedHash, grounded);
            }
            //Debug.Log(
            //    $"[FPP Animation] " +
            //    $"MoveX: {local.x:F2} | " +
            //    $"MoveY: {local.z:F2} | " +
            //    $"Speed: {speed:F2} | " +
            //    $"Grounded: {grounded} | " +
            //    $"Falling: {falling} | " +
            //    $"Sprinting: {sprinting} | " +
            //    $"Crouching: {core.IsCrouching}"
            //);
            // Prevent an unconsumed Jump trigger from activating
            // unexpectedly on a later jump.
            if (jumpTriggerResetTimer > 0f)
            {
                jumpTriggerResetTimer -= dt;

                if (jumpTriggerResetTimer <= 0f)
                {
                    Fire(jumpHash, false);
                }
            }


        }

        private void HandleJumped()
        {
            Fire(landHash, false);
            Fire(jumpHash, false); // To Clear any previous Trigger
            Fire(jumpHash, true);
            // Clear it shortly afterward if the Animator hasn't consumed it.
            jumpTriggerResetTimer = jumpTriggerResetDelay;
        }

        private void HandleLanded(float impactSpeed, float airTime)
        {
            if (impactSpeed < minLandImpactForAnim) return;
            Fire(landHash, true);
        }

        private void Fire(int hash, bool on)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                if (!t.existing.Contains(hash)) continue;

                t.animator.ResetTrigger(hash);
                if (on) t.animator.SetTrigger(hash);
            }
        }

        private void OnGUI()
        {
            if (!showDebug) return;
            GUI.Label(new Rect(10, 85, 700, 25),
                $"MoveX {lastMove.x:F2} | MoveY {lastMove.y:F2} | Speed {lastMove.magnitude:F2}");

        }
    }
}