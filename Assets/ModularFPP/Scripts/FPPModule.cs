using UnityEngine;

namespace ModularFPP
{
    [RequireComponent(typeof(FPPCore))]      // adding any feature auto-adds the Core
    public abstract class FPPModule : MonoBehaviour
    {
        protected FPPCore core;

        // Lower number runs earlier each frame
        public virtual int Order => 100;

        // Each feature does its work here (instead of Update)
        public abstract void Tick(float dt);

        protected virtual void Awake() => core = GetComponent<FPPCore>();
        protected virtual void OnEnable() => core.Register(this);
        protected virtual void OnDisable() { if (core != null) core.Unregister(this); }

        protected static bool HasClips(AudioClip[] clips) => clips != null && clips.Length > 0;
    }
}
