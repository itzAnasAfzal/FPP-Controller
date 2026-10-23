using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModularFPP
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(AudioSource))]
    public class FPPCore : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InputActionAsset inputAsset;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform cameraRoot;

        [Header("World Settings")]
        [SerializeField] private LayerMask groundMask;
        [SerializeField] private float gravity = -20f;
        [SerializeField, Min(0.01f)] private float groundCheckDistance = 0.3f;
        [SerializeField] private bool lockCursorOnStart = true;

        [Header("Shared Movement Feel")]
        [SerializeField, Min(0.1f)] private float strideLength = 2.2f;

        [Header("Surface Sounds")]
        [SerializeField] private SurfaceSound[] surfaces;

        public Camera PlayerCamera => playerCamera;
        public Transform CameraRoot => cameraRoot;
        public LayerMask GroundMask => groundMask;
        public float Gravity => gravity;
        public float StrideLength => strideLength;
        public CharacterController Controller { get; private set; }
        public AudioSource Audio { get; private set; }

        public InputAction MoveAction { get; private set; }
        public InputAction LookAction { get; private set; }
        public InputAction JumpAction { get; private set; }
        public InputAction SprintAction { get; private set; }
        public InputAction CrouchAction { get; private set; }

        public bool CursorIsLocked => Cursor.lockState == CursorLockMode.Locked;

        // Shared movement state. Core writes ground/motion data; modules write their own state.
        public bool IsGrounded { get; set; }
        public bool IsSliding { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public float SlopeAngle { get; private set; }
        public float GroundGap { get; private set; }
        public Collider GroundCollider { get; private set; }
        public Vector3 ActualVelocity { get; private set; }
        public float ActualHorizontalSpeed { get; private set; }

        public float VerticalVelocity { get; set; }
        public bool LaunchedThisFrame { get; set; }
        public bool IsSprinting { get; set; }
        public bool IsCrouching { get; set; }
        public float SpeedMultiplier { get; set; } = 1f;
        public bool SprintBlocked { get; set; }
        public bool JumpBlocked { get; set; }
        public float StepVolumeMultiplier { get; set; } = 1f;

        public event Action Jumped;
        public event Action<float, float> Landed;

        public void NotifyJumped() => Jumped?.Invoke();
        public void NotifyLanded(float impactSpeed, float airTime) => Landed?.Invoke(impactSpeed, airTime);

        private InputActionMap playerMap;
        private Collider lastGroundCollider;
        private readonly List<FPPModule> modules = new List<FPPModule>(8);
        private readonly SurfaceSound[] surfaceByLayer = new SurfaceSound[32];

        private void Awake()
        {
            Controller = GetComponent<CharacterController>();
            Audio = GetComponent<AudioSource>();

            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (cameraRoot == null && playerCamera != null) cameraRoot = playerCamera.transform.parent;

            if (inputAsset == null)
            {
                Debug.LogError("FPPCore: Assign an InputActionAsset containing a 'Player' map.", this);
                enabled = false;
                return;
            }

            if (playerCamera == null || cameraRoot == null)
                Debug.LogWarning("FPPCore: Camera or Camera Root was not found. Assign them if Look, Crouch, or Camera Effects are enabled.", this);

            playerMap = inputAsset.FindActionMap("Player", false);
            if (playerMap == null)
            {
                Debug.LogError("FPPCore: The Input Actions asset needs an action map named 'Player'.", this);
                enabled = false;
                return;
            }

            MoveAction = playerMap.FindAction("Move", false);
            LookAction = playerMap.FindAction("Look", false);
            JumpAction = playerMap.FindAction("Jump", false);
            SprintAction = playerMap.FindAction("Sprint", false);
            CrouchAction = playerMap.FindAction("Crouch", false);

            if (MoveAction == null || LookAction == null || JumpAction == null
                || SprintAction == null || CrouchAction == null)
            {
                Debug.LogError("FPPCore: The 'Player' map must contain Move, Look, Jump, Sprint, and Crouch actions (case-sensitive).", this);
                enabled = false;
                return;
            }

            strideLength = Mathf.Max(0.1f, strideLength);
            gravity = Mathf.Min(-0.01f, gravity);
            groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
            RebuildSurfaceCache();
            RefreshMotionState();
        }

        private void OnEnable()
        {
            playerMap?.Enable();
            if (lockCursorOnStart) SetCursorLock(true);
        }

        private void OnDisable() => playerMap?.Disable();

        // Maintain a sorted list at registration time, not by sorting during gameplay.
        public void Register(FPPModule module)
        {
            if (module == null || modules.Contains(module)) return;

            int index = modules.Count;
            while (index > 0 && modules[index - 1].Order > module.Order) index--;
            modules.Insert(index, module);
        }

        public void Unregister(FPPModule module)
        {
            if (module != null) modules.Remove(module);
        }

        private void Update()
        {
            HandleCursorLock();

            // ActualVelocity is refreshed after the previous CharacterController.Move call.
            LaunchedThisFrame = false;
            UpdateGroundState();

            float dt = Time.deltaTime;
            for (int i = 0; i < modules.Count; i++)
            {
                FPPModule module = modules[i];
                if (module != null && module.isActiveAndEnabled)
                    module.Tick(dt);
            }
        }

        // Call again immediately after CharacterController.Move so later modules share one fresh result.
        public void RefreshMotionState()
        {
            if (Controller == null) return;

            ActualVelocity = Controller.velocity;
            ActualHorizontalSpeed = new Vector2(ActualVelocity.x, ActualVelocity.z).magnitude;
        }

        private void UpdateGroundState()
        {
            CharacterController cc = Controller;
            float castRadius = cc.radius * 0.95f;
            float originHeight = cc.radius + 0.05f;
            Vector3 origin = transform.position + Vector3.up * originHeight;
            float bottomOffset = originHeight - castRadius;

            bool foundGround = Physics.SphereCast(
                origin, castRadius, Vector3.down, out RaycastHit sphereHit,
                bottomOffset + groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);

            // Ignore ground probes while rising; otherwise they can cancel a jump on its first frame.
            IsGrounded = foundGround && VerticalVelocity <= 0.1f;
            GroundNormal = Vector3.up;
            GroundCollider = null;
            GroundGap = 0f;

            if (IsGrounded)
            {
                GroundGap = Mathf.Max(0f, sphereHit.distance - bottomOffset);
                GroundCollider = sphereHit.collider;
                GroundNormal = sphereHit.normal;

                // Flat-ground sphere normals are already sufficient, so avoid a second physics query
                // there. Refine sloped/edge contacts with a ray and accept only the same collider.
                if (sphereHit.normal.y < 0.999f
                    && Physics.Raycast(sphereHit.point + Vector3.up * 0.1f, Vector3.down,
                        out RaycastHit rayHit, 0.3f, groundMask, QueryTriggerInteraction.Ignore)
                    && rayHit.collider == sphereHit.collider)
                {
                    GroundNormal = rayHit.normal;
                }
            }

            SlopeAngle = Vector3.Angle(GroundNormal, Vector3.up);
            float slideStartAngle = cc.slopeLimit - 0.5f;
            float slideStopAngle = cc.slopeLimit - 3f;
            IsSliding = IsGrounded && (IsSliding ? SlopeAngle > slideStopAngle : SlopeAngle > slideStartAngle);

            if (GroundCollider != null) lastGroundCollider = GroundCollider;
        }

        public void RebuildSurfaceCache()
        {
            Array.Clear(surfaceByLayer, 0, surfaceByLayer.Length);
            if (surfaces == null) return;

            // Preserve the original rule: the first surface entry matching a layer wins.
            for (int i = 0; i < surfaces.Length; i++)
            {
                SurfaceSound surface = surfaces[i];
                if (surface == null) continue;

                int layerBits = surface.layers.value;
                for (int layer = 0; layer < surfaceByLayer.Length; layer++)
                {
                    if (surfaceByLayer[layer] == null && (layerBits & (1 << layer)) != 0)
                        surfaceByLayer[layer] = surface;
                }
            }
        }

        public SurfaceSound GetCurrentSurface()
        {
            Collider surfaceCollider = GroundCollider != null ? GroundCollider : lastGroundCollider;
            if (surfaceCollider == null) return null;

            int layer = surfaceCollider.gameObject.layer;
            return layer >= 0 && layer < surfaceByLayer.Length ? surfaceByLayer[layer] : null;
        }

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
            if (clip == null || Audio == null) return;
            Audio.pitch = pitch;
            Audio.PlayOneShot(clip, volume);
        }
    }
}
