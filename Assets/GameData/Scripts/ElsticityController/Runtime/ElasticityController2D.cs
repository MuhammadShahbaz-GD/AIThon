using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    public enum ElasticitySpriteAxis
    {
        Horizontal,
        Vertical
    }

    /// <summary>
    /// Presentation-only connector that positions, rotates, stretches, and compresses a sprite
    /// between two Transform endpoints. It never applies forces or modifies Rigidbody2D state.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ElasticityController2D : MonoBehaviour
    {
        [Header("Connection Points")]
        [SerializeField] private Transform startPoint;
        [SerializeField] private Transform endPoint;
        [SerializeField] private Vector2 startWorldOffset;
        [SerializeField] private Vector2 endWorldOffset;
        [Min(0f)] [SerializeField] private float startPadding;
        [Min(0f)] [SerializeField] private float endPadding;

        [Header("Sprite Stretch")]
        [SerializeField] private SpriteRenderer targetRenderer;
        [Tooltip("Select the direction in which the original sprite artwork is drawn.")]
        [SerializeField] private ElasticitySpriteAxis stretchAxis = ElasticitySpriteAxis.Vertical;
        [Tooltip("Thickness perpendicular to the stretching direction.")]
        [Min(.01f)] [SerializeField] private float widthMultiplier = 1f;
        [Tooltip("Additional length multiplier after fitting the two endpoints.")]
        [Min(.01f)] [SerializeField] private float lengthMultiplier = 1f;
        [Tooltip("Limits compression relative to the sprite's original world-space length.")]
        [Min(.01f)] [SerializeField] private float minimumLengthMultiplier = .1f;
        [Tooltip("Limits expansion relative to the sprite's original world-space length.")]
        [Min(.01f)] [SerializeField] private float maximumLengthMultiplier = 10f;
        [Tooltip("Rotates the fitted sprite after it is aligned with the endpoints.")]
        [SerializeField] private float rotationOffset;

        [Header("Following")]
        [Tooltip("Zero snaps exactly to both points. Higher values smoothly follow moving physics parts.")]
        [Min(0f)] [SerializeField] private float followSmoothness;
        [Tooltip("Disables the sprite when either endpoint or the sprite is missing.")]
        [SerializeField] private bool hideWhenInvalid = true;
        [Tooltip("Keep the connector active when its endpoints become nearly coincident.")]
        [SerializeField] private bool showAtZeroLength;
        [Min(.0001f)] [SerializeField] private float zeroLengthThreshold = .001f;

        [Header("Diagnostics")]
        [SerializeField] private bool drawConnectionGizmos = true;
        [SerializeField] private Color gizmoColor = new Color(.2f, .9f, 1f, .9f);

        private Vector2 displayedStart;
        private Vector2 displayedEnd;
        private bool hasDisplayedPose;
        private float nativeLength = 1f;
        private float lastDistance = -1f;
        private bool initialized;

        public event Action<ElasticityController2D, float, float> ConnectionChanged;
        public event Action<ElasticityController2D, bool> VisibilityChanged;

        public Transform StartPoint => startPoint;
        public Transform EndPoint => endPoint;
        public ElasticitySpriteAxis StretchAxis => stretchAxis;
        public float CurrentDistance { get; private set; }
        public float CurrentStretchRatio { get; private set; } = 1f;
        public bool HasValidConnection => startPoint != null && endPoint != null &&
                                          targetRenderer != null && targetRenderer.sprite != null;

        private void Awake()
        {
            CacheRenderer();
            RefreshSpriteMetrics();
            SnapToConnection();
            initialized = true;
        }

        private void OnEnable()
        {
            CacheRenderer();
            RefreshSpriteMetrics();
            SnapToConnection();
            initialized = true;
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            UpdateConnection(Application.isPlaying && followSmoothness > 0f);
        }

        /// <summary>Assigns both endpoints and immediately updates the connector.</summary>
        public void SetEndpoints(Transform start, Transform end)
        {
            startPoint = start;
            endPoint = end;
            SnapToConnection();
        }

        public void SetStartPoint(Transform value)
        {
            startPoint = value;
            SnapToConnection();
        }

        public void SetEndPoint(Transform value)
        {
            endPoint = value;
            SnapToConnection();
        }

        /// <summary>Refreshes sprite bounds after replacing the SpriteRenderer sprite at runtime.</summary>
        public void RefreshSpriteMetrics()
        {
            CacheRenderer();
            if (targetRenderer == null || targetRenderer.sprite == null)
            {
                nativeLength = 1f;
                return;
            }

            Vector2 size = targetRenderer.sprite.bounds.size;
            nativeLength = Mathf.Max(.0001f,
                stretchAxis == ElasticitySpriteAxis.Horizontal ? size.x : size.y);
        }

        [ContextMenu("Snap To Connection")]
        public void SnapToConnection()
        {
            hasDisplayedPose = false;
            UpdateConnection(false);
        }

        private void UpdateConnection(bool smooth)
        {
            CacheRenderer();
            if (!HasValidConnection)
            {
                SetRendererVisible(!hideWhenInvalid);
                return;
            }

            Vector2 requestedStart = (Vector2)startPoint.position + startWorldOffset;
            Vector2 requestedEnd = (Vector2)endPoint.position + endWorldOffset;
            Vector2 requestedDirection = requestedEnd - requestedStart;
            float requestedDistance = requestedDirection.magnitude;

            if (requestedDistance <= zeroLengthThreshold && !showAtZeroLength)
            {
                SetRendererVisible(false);
                return;
            }

            SetRendererVisible(true);
            if (requestedDistance > zeroLengthThreshold)
            {
                Vector2 direction = requestedDirection / requestedDistance;
                float totalPadding = Mathf.Min(startPadding + endPadding, requestedDistance);
                float paddingScale = totalPadding > 0f
                    ? Mathf.Min(1f, requestedDistance / totalPadding)
                    : 1f;
                requestedStart += direction * startPadding * paddingScale;
                requestedEnd -= direction * endPadding * paddingScale;
            }

            if (!hasDisplayedPose || !smooth)
            {
                displayedStart = requestedStart;
                displayedEnd = requestedEnd;
                hasDisplayedPose = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-followSmoothness * Time.deltaTime);
                displayedStart = Vector2.Lerp(displayedStart, requestedStart, blend);
                displayedEnd = Vector2.Lerp(displayedEnd, requestedEnd, blend);
            }

            ApplyVisual(displayedStart, displayedEnd);
        }

        private void ApplyVisual(Vector2 start, Vector2 end)
        {
            Vector2 difference = end - start;
            float distance = difference.magnitude;
            Vector2 direction = distance > zeroLengthThreshold ? difference / distance : Vector2.up;
            float unclampedScale = distance / nativeLength * lengthMultiplier;
            float lengthScale = Mathf.Clamp(
                unclampedScale,
                minimumLengthMultiplier,
                Mathf.Max(minimumLengthMultiplier, maximumLengthMultiplier));

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (stretchAxis == ElasticitySpriteAxis.Vertical) angle -= 90f;
            Quaternion rotation = Quaternion.Euler(0f, 0f, angle + rotationOffset);

            // Sprite bounds are pivot-relative. Positioning the scaled minimum bound at the start point
            // makes the visual connect both endpoints even when the imported sprite pivot is not centered.
            Bounds bounds = targetRenderer.sprite.bounds;
            Vector3 scale = stretchAxis == ElasticitySpriteAxis.Horizontal
                ? new Vector3(lengthScale, widthMultiplier, 1f)
                : new Vector3(widthMultiplier, lengthScale, 1f);
            float minimumAxis = stretchAxis == ElasticitySpriteAxis.Horizontal
                ? bounds.min.x
                : bounds.min.y;
            Vector3 localStart = stretchAxis == ElasticitySpriteAxis.Horizontal
                ? new Vector3(minimumAxis * scale.x, 0f, 0f)
                : new Vector3(0f, minimumAxis * scale.y, 0f);

            float preservedZ = transform.position.z;
            transform.rotation = rotation;
            transform.localScale = scale;
            Vector3 unscaledLocalStart = stretchAxis == ElasticitySpriteAxis.Horizontal
                ? new Vector3(minimumAxis, 0f, 0f)
                : new Vector3(0f, minimumAxis, 0f);
            Vector3 worldStartOffset = transform.TransformVector(unscaledLocalStart);
            transform.position = new Vector3(
                start.x - worldStartOffset.x,
                start.y - worldStartOffset.y,
                preservedZ);

            CurrentDistance = distance;
            CurrentStretchRatio = lengthScale;
            if (Mathf.Abs(lastDistance - distance) > .0001f)
            {
                lastDistance = distance;
                ConnectionChanged?.Invoke(this, CurrentDistance, CurrentStretchRatio);
            }
        }

        private void CacheRenderer()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<SpriteRenderer>();
        }

        private void SetRendererVisible(bool value)
        {
            if (targetRenderer == null || targetRenderer.enabled == value) return;
            targetRenderer.enabled = value;
            VisibilityChanged?.Invoke(this, value);
        }

        private void OnValidate()
        {
            widthMultiplier = Mathf.Max(.01f, widthMultiplier);
            lengthMultiplier = Mathf.Max(.01f, lengthMultiplier);
            minimumLengthMultiplier = Mathf.Max(.01f, minimumLengthMultiplier);
            maximumLengthMultiplier = Mathf.Max(minimumLengthMultiplier, maximumLengthMultiplier);
            followSmoothness = Mathf.Max(0f, followSmoothness);
            zeroLengthThreshold = Mathf.Max(.0001f, zeroLengthThreshold);
            startPadding = Mathf.Max(0f, startPadding);
            endPadding = Mathf.Max(0f, endPadding);
            CacheRenderer();
            RefreshSpriteMetrics();
            if (!Application.isPlaying) SnapToConnection();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawConnectionGizmos || startPoint == null || endPoint == null) return;
            Vector3 start = startPoint.position + (Vector3)startWorldOffset;
            Vector3 end = endPoint.position + (Vector3)endWorldOffset;
            Gizmos.color = gizmoColor;
            Gizmos.DrawLine(start, end);
            float radius = Mathf.Max(.02f, Vector3.Distance(start, end) * .025f);
            Gizmos.DrawWireSphere(start, radius);
            Gizmos.DrawWireSphere(end, radius);
        }
    }
}