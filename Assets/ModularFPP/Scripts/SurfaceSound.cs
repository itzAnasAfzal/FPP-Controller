using UnityEngine;

namespace ModularFPP
{
    [System.Serializable]
    public class SurfaceSound
    {
        public string name = "New Surface";
        public LayerMask layers;                 // which layers count as this surface
        public AudioClip[] footstepClips;
        public AudioClip[] jumpClips;
        public AudioClip[] landClips;
        [Range(0f, 2f)] public float volumeMultiplier = 1f;
    }
}