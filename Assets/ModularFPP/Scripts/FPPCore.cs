using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModularFPP
{
    [DefaultExecutionOrder(-100)]          // Core wakes up before every feature
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(AudioSource))]
    public class FPPCore : MonoBehaviour
    {
        // ---------- ASSIGN ONCE, HERE AND ONLY HERE ----------
        [Header("References")]
        [SerializeField] private InputActionAsset inputAsset;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform cameraRoot;

        [Header("World Settings")]
        [SerializeField] private LayerMask groundMask;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float groundCheckDistance = 0.3f;
        [SerializeField] private bool lockCursorOnStart = true;
        [Header("Shared Movement Feel")]
        [SerializeField] private float strideLength = 2.2f;
        public float StrideLength => strideLength;

        [Header("Surface Sounds")]
        [SerializeField] private SurfaceSound[] surfaces;

        // ---------- READ-ONLY ACCESS FOR FEATURES ----------
        public Camera PlayerCamera => playerCamera;
        public Transform CameraRoot => cameraRoot;
        public LayerMask GroundMask => groundMask;
        public float Gravity => gravity;
        public CharacterController Controller { get; private set; }
        public AudioSource Audio { get; private set; }

        public InputAction MoveAction { get; private set; }
        public InputAction LookAction { get; private set; }
        public InputAction JumpAction { get; private set; }
        public InputAction SprintAction { get; private set; }
        public InputAction CrouchAction { get; private set; }

        public bool CursorIsLocked => Cursor.lockState == CursorLockMode.Locked;

        // ---------- SHARED STATE (features read and write these) ----------
        public bool IsGrounded { get; set; }
        public bool IsSliding { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public float SlopeAngle { get; private set; }
        public float GroundGap { get; private set; }
        public Collider GroundCollider { get; private set; }

        public float VerticalVelocity { get; set; }
        public bool LaunchedThisFrame { get; set; }

        public bool IsSprinting { get; set; }
        public bool IsCrouching { get; set; }

        // "Switches" that one feature can flip to influence another (Crouch will use these)
        public float SpeedMultiplier { get; set; } = 1f;
        public bool SprintBlocked { get; set; }
        public bool JumpBlocked { get; set; }
        public float StepVolumeMultiplier { get; set; } = 1f;

        public float ActualHorizontalSpeed
        {
            get { Vector3 v = Controller.velocity; return new Vector2(v.x, v.z).magnitude; }
        }

        // ---------- EVENTS (camera effects etc. subscribe to these) ----------
        public event Action Jumped;
        public event Action<float, float> Landed;   // impactSpeed, airTime
        public void NotifyJumped() => Jumped?.Invoke();
        public void NotifyLanded(float impactSpeed, float airTime) => Landed?.Invoke(impactSpeed, airTime);

        // ---------- INTERNAL ----------
        private InputActionMap playerMap;
        private Collider lastGroundCollider;
        private readonly List<FPPModule> modules = new List<FPPModule>();

        public void Register(FPPModule module)
        {
            if (!modules.Contains(module)) modules.Add(module);
            modules.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public void Unregister(FPPModule module) => modules.Remove(module);

        private void Awake()
        {
            Controller = GetComponent<CharacterController>();
            Audio = GetComponent<AudioSource>();

            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (cameraRoot == null && playerCamera != null) cameraRoot = playerCamera.transform.parent;

            if (inputAsset == null)
            {
                Debug.LogError("FPPCore: Input Asset is not assigned!", this);
                enabled = false;
                return;
            }

            playerMap = inputAsset.FindActionMap("Player", true);
            MoveAction = playerMap.FindAction("Move", true);
            LookAction = playerMap.FindAction("Look", true);
            JumpAction = playerMap.FindAction("Jump", true);
            SprintAction = playerMap.FindAction("Sprint", true);
            CrouchAction = playerMap.FindAction("Crouch", true);
        }

        private void OnEnable()
        {
            playerMap?.Enable();
            if (lockCursorOnStart) SetCursorLock(true);
        }

        private void OnDisable() => playerMap?.Disable();

        // ---------- THE CONDUCTOR ----------
        private void Update()
        {
            HandleCursorLock();

            LaunchedThisFrame = false;
            UpdateGroundState();

            float dt = Time.deltaTime;
            for (int i = 0; i < modules.Count; i++)
                modules[i].Tick(dt);          // features run in Order: low number first
        }

        // ---------- GROUND DETECTION (every feature needs this, so Core owns it) ----------
        private void UpdateGroundState()
        {
            float r = Controller.radius * 0.95f;
            float originHeight = Controller.radius + 0.05f;
            Vector3 origin = transform.position + Vector3.up * originHeight;

            IsGrounded = Physics.SphereCast(origin, r, Vector3.down, out RaycastHit sphereHit,
                0.05f + groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);

            if (VerticalVelocity > 0.1f) IsGrounded = false;    // moving up = not grounded

            GroundNormal = Vector3.up;
            GroundCollider = null;
            GroundGap = 0f;

        
            if (IsGrounded)
            {
                float bottomOffset = originHeight - r;
                GroundGap = Mathf.Max(0f, sphereHit.distance - bottomOffset);
                GroundCollider = sphereHit.collider;

                // True surface normal: ray straight down onto the exact spot the sphere touched.
                // (Shooting from the capsule's center misses steep slopes.)
                if (Physics.Raycast(sphereHit.point + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit,
                        0.3f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    GroundNormal = hit.normal;
                    GroundCollider = hit.collider;
                }
            }

            SlopeAngle = Vector3.Angle(GroundNormal, Vector3.up); 
            IsSliding = IsGrounded && SlopeAngle > Controller.slopeLimit - 0.5f;
            if (GroundCollider != null) lastGroundCollider = GroundCollider;
        }

        public SurfaceSound GetCurrentSurface()
        {
            // Falls back to the last ground we touched, so a coyote-time jump still has a surface
            Collider c = GroundCollider != null ? GroundCollider : lastGroundCollider;
            if (c == null || surfaces == null) return null;

            int layerBit = 1 << c.gameObject.layer;
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != null && (surfaces[i].layers.value & layerBit) != 0)
                    return surfaces[i];
            }
            return null;
        }

        // ---------- CURSOR + SOUND HELPERS ----------
        private void HandleCursorLock()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetCursorLock(false);
            else if (!CursorIsLocked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                SetCursorLock(true);
        }

        public void SetCursorLock(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void PlaySound(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            Audio.pitch = pitch;
            Audio.PlayOneShot(clip, volume);
        }
    }
}