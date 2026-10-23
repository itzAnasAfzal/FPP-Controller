using UnityEngine;
using UnityEngine.InputSystem;

namespace ModularFPP
{
    public class FPPLook : FPPModule
    {
        public override int Order => 0;

        [Header("Look Settings")]
        [SerializeField] protected float mouseSensitivity = 0.1f;
        [SerializeField] protected float gamepadSensitivity = 140f;
        [SerializeField] protected bool invertY = false;

        [Header("Vertical Limits")]
        [SerializeField, Range(0f, 90f)] protected float maxLookUp = 85f;
        [SerializeField, Range(0f, 90f)] protected float maxLookDown = 85f;

        [Header("Smoothing")]
        [SerializeField] protected bool useSmoothing = true;
        [SerializeField, Range(0.01f, 0.2f)] protected float smoothTime = 0.04f;

        protected float targetYaw, targetPitch, currentYaw, currentPitch;

        protected override void Awake()
        {
            base.Awake();
            if (core.CameraRoot == null)
            {
                Debug.LogError("FPPLook: assign a Camera Root on FPPCore before enabling look.", this);
                enabled = false;
                return;
            }

            targetYaw = currentYaw = transform.eulerAngles.y;
            targetPitch = currentPitch = Mathf.DeltaAngle(0f, core.CameraRoot.localEulerAngles.x);
        }

        public override void Tick(float dt)
        {
            if (!core.CursorIsLocked) return;

            Vector2 input = core.LookAction.ReadValue<Vector2>();

            bool usingGamepad = core.LookAction.activeControl != null
                                && core.LookAction.activeControl.device is Gamepad;
            Vector2 delta = usingGamepad
                ? input * gamepadSensitivity * dt
                : input * mouseSensitivity;

            targetYaw += delta.x;
            targetPitch += invertY ? delta.y : -delta.y;
            targetPitch = Mathf.Clamp(targetPitch, -maxLookUp, maxLookDown);

            if (useSmoothing)
            {
                float t = 1f - Mathf.Exp(-dt / smoothTime);
                currentYaw = Mathf.Lerp(currentYaw, targetYaw, t);
                currentPitch = Mathf.Lerp(currentPitch, targetPitch, t);
            }
            else
            {
                currentYaw = targetYaw;
                currentPitch = targetPitch;
            }

            transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
            core.CameraRoot.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
        }
    }
}
