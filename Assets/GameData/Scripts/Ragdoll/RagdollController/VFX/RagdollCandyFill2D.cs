using System;
using UnityEngine;

namespace KickTheBuddy.Physics.VFX
{
    /// <summary>
    /// Authored candy-fill metadata for one glass ragdoll part. Candy visuals are lightweight child
    /// sprites; their equivalent ballast is baked into the existing Rigidbody2D by the editor tool.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class RagdollCandyFill2D : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private SpriteRenderer glassRenderer;
        [SerializeField] private GameObject fillRoot;
        [SerializeField] private GameObject[] candyVisuals = Array.Empty<GameObject>();
        [Min(0f)] [SerializeField] private float emptyPartMass;
        [Min(0f)] [SerializeField] private float candyBallastMass;
        [Min(0f)] [SerializeField] private float bakedPartMass;
        [Range(0f, 1f)] [SerializeField] private float visualFillRatio = .75f;

        public event Action<RagdollCandyFill2D, bool> VisibilityChanged;

        public Rigidbody2D Body => body;
        public SpriteRenderer GlassRenderer => glassRenderer;
        public int CandyCount => candyVisuals != null ? candyVisuals.Length : 0;
        public GameObject[] CandyVisuals => candyVisuals;
        public GameObject FillRoot => fillRoot;
        public float EmptyPartMass => emptyPartMass;
        public float CandyBallastMass => candyBallastMass;
        public float BakedPartMass => bakedPartMass;
        public float VisualFillRatio => visualFillRatio;

        public void SetCandyVisible(bool visible)
        {
            if (fillRoot == null || fillRoot.activeSelf == visible) return;
            fillRoot.SetActive(visible);
            VisibilityChanged?.Invoke(this, visible);
        }

        private void OnValidate()
        {
            emptyPartMass = Mathf.Max(0f, emptyPartMass);
            candyBallastMass = Mathf.Max(0f, candyBallastMass);
            bakedPartMass = Mathf.Max(.01f, bakedPartMass);
            visualFillRatio = Mathf.Clamp01(visualFillRatio);
        }
    }
}

