using System;
using UnityEngine;

namespace KickTheBuddy.Ragdoll3D
{
    /// <summary>Renders a non-allocating helical coil between two moving bones.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class ProceduralSpringRenderer : MonoBehaviour
    {
        [Header("Endpoints")]
        public Transform startBone;
        public Transform endBone;
        public LineRenderer lineRenderer;

        [Header("Spring Style")]
        [Range(8, 256)] public int segmentsCount = 64;
        [Min(0f)] public float radius = .08f;
        [Tooltip("Number of complete coils per world-space unit.")]
        [Min(0f)] public float coilDensity = 6f;
        [Min(.001f)] public float wireThickness = .035f;

        [Header("Stretch Response")]
        [Tooltip("Minimum and maximum multipliers applied to wire thickness while stretched/compressed.")]
        [SerializeField] private Vector2 thicknessScaleLimits = new Vector2(.55f, 1.4f);

        private Vector3[] positions = Array.Empty<Vector3>();
        private float restLength = 1f;
        private bool initialized;

        private void Awake()
        {
            if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
            RebuildPositionBuffer();
        }

        private void Start()
        {
            initialized = startBone != null && endBone != null && lineRenderer != null && positions.Length >= 2;
            if (!initialized)
            {
                enabled = false;
                return;
            }

            restLength = Mathf.Max(.0001f, Vector3.Distance(startBone.position, endBone.position));
            lineRenderer.useWorldSpace = true;
        }

        private void Update()
        {
            if (!initialized) return;

            Vector3 start = startBone.position;
            Vector3 delta = endBone.position - start;
            float distance = delta.magnitude;
            if (distance <= .0001f)
            {
                for (int i = 0; i < positions.Length; i++) positions[i] = start;
                SetDynamicWidth(.0001f);
                lineRenderer.SetPositions(positions);
                return;
            }

            Vector3 direction = delta / distance;
            // Project a bone-relative reference onto the spring plane. This prevents visible basis
            // flips as the limb rotates close to a world axis.
            Vector3 normal = Vector3.ProjectOnPlane(startBone.up, direction);
            if (normal.sqrMagnitude < .000001f)
                normal = Vector3.ProjectOnPlane(startBone.right, direction);
            if (normal.sqrMagnitude < .000001f)
                normal = Vector3.ProjectOnPlane(Vector3.up, direction);
            normal.Normalize();
            Vector3 binormal = Vector3.Cross(direction, normal).normalized;

            float turns = distance * coilDensity;
            float phaseScale = turns * Mathf.PI * 2f;
            float inverseLastIndex = 1f / (positions.Length - 1);
            for (int i = 0; i < positions.Length; i++)
            {
                float normalizedDistance = i * inverseLastIndex;
                float phase = normalizedDistance * phaseScale;
                // The sine envelope pulls the first/last samples into the bone anchors.
                float endpointEnvelope = Mathf.Sin(normalizedDistance * Mathf.PI);
                Vector3 radialOffset =
                    (normal * Mathf.Cos(phase) + binormal * Mathf.Sin(phase)) *
                    (radius * endpointEnvelope);
                positions[i] = start + delta * normalizedDistance + radialOffset;
            }

            SetDynamicWidth(distance);
            lineRenderer.SetPositions(positions);
        }

        /// <summary>
        /// Changes spring resolution. Allocation happens only when explicitly reconfiguring,
        /// never during the regular Update path.
        /// </summary>
        public void SetSegmentsCount(int value)
        {
            int clamped = Mathf.Clamp(value, 8, 256);
            if (segmentsCount == clamped && positions.Length == clamped) return;
            segmentsCount = clamped;
            RebuildPositionBuffer();
        }

        private void RebuildPositionBuffer()
        {
            segmentsCount = Mathf.Clamp(segmentsCount, 8, 256);
            positions = new Vector3[segmentsCount];
            if (lineRenderer != null)
            {
                lineRenderer.positionCount = positions.Length;
                lineRenderer.useWorldSpace = true;
            }
        }

        private void SetDynamicWidth(float currentLength)
        {
            // sqrt(rest/current) approximately preserves apparent wire volume while stretching.
            float stretchRatio = Mathf.Sqrt(restLength / Mathf.Max(.0001f, currentLength));
            float multiplier = Mathf.Clamp(
                stretchRatio,
                thicknessScaleLimits.x,
                thicknessScaleLimits.y);
            float width = wireThickness * multiplier;
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;
        }

        private void OnValidate()
        {
            segmentsCount = Mathf.Clamp(segmentsCount, 8, 256);
            radius = Mathf.Max(0f, radius);
            coilDensity = Mathf.Max(0f, coilDensity);
            wireThickness = Mathf.Max(.001f, wireThickness);
            thicknessScaleLimits.x = Mathf.Max(.05f, thicknessScaleLimits.x);
            thicknessScaleLimits.y = Mathf.Max(thicknessScaleLimits.x, thicknessScaleLimits.y);
            if (Application.isPlaying && lineRenderer != null && positions.Length != segmentsCount)
                RebuildPositionBuffer();
        }
    }
}
