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
        [SerializeField] protected string moveXParam = "MoveX";
        [SerializeField] protected string moveYParam = "MoveY";
        [SerializeField] protected string speedParam = "Speed";
        [SerializeField] protected string sprintingParam = "IsSprinting";
        [SerializeField] protected string crouchingParam = "IsCrouching";
        [SerializeField] protected string groundedParam = "IsGrounded";
        [SerializeField] protected string jumpTrigger = "Jump";
        [SerializeField] protected string landTrigger = "Land";

        [Header("Tuning")]
        [SerializeField] protected float moveDamp = 0.1f;               // smoothing of the blend (seconds)
        [SerializeField] protected float minLandImpactForAnim = 4f;
        [SerializeField] protected bool showDebug = false;

        private class Target
        {
            public Animator animator;
            public readonly HashSet<int> existing = new HashSet<int>();
        }

        private readonly List<Target> targets = new List<Target>();
        private int moveXHash, moveYHash, speedHash, sprintingHash, crouchingHash, groundedHash, jumpHash, landHash;
        private Vector2 lastMove;

        protected override void Awake()
        {
            base.Awake();

            if (animators == null || animators.Length == 0)
                animators = GetComponentsInChildren<Animator>();

            moveXHash = Animator.StringToHash(moveXParam);
            moveYHash = Animator.StringToHash(moveYParam);
            speedHash = Animator.StringToHash(speedParam);
            sprintingHash = Animator.StringToHash(sprintingParam);
            crouchingHash = Animator.StringToHash(crouchingParam);
            groundedHash = Animator.StringToHash(groundedParam);
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
            v.y = 0f;
            Vector3 local = transform.InverseTransformDirection(v);   // x = right, z = forward
            float speed = v.magnitude;
            lastMove = new Vector2(local.x, local.z);

            bool sprinting = core.IsSprinting && speed > 0.5f;

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                Animator a = t.animator;

                if (t.existing.Contains(moveXHash)) a.SetFloat(moveXHash, local.x, moveDamp, dt);
                if (t.existing.Contains(moveYHash)) a.SetFloat(moveYHash, local.z, moveDamp, dt);
                if (t.existing.Contains(speedHash)) a.SetFloat(speedHash, speed, moveDamp, dt);

                if (t.existing.Contains(sprintingHash)) a.SetBool(sprintingHash, sprinting);
                if (t.existing.Contains(crouchingHash)) a.SetBool(crouchingHash, core.IsCrouching);
                if (t.existing.Contains(groundedHash)) a.SetBool(groundedHash, core.IsGrounded);
            }
        }

        private void HandleJumped()
        {
            Fire(landHash, false);
            Fire(jumpHash, true);
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