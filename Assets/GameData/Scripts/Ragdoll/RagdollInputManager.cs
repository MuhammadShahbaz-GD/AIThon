using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KickTheBuddy.Physics
{
    /// <summary>Owns raw pointer input, drag selection, and the character-wide drag feel.</summary>
    [DisallowMultipleComponent]
    public sealed class RagdollInputManager : MonoBehaviour
    {
        [Serializable]
        public sealed class DragConfiguration
        {
            [Header("Responsiveness")]
            [Tooltip("Higher values make the grabbed limb follow the pointer faster.")]
            [Range(.5f, 12f)] [SerializeField] private float frequency = 5f;
            [Tooltip("Higher values remove spring wobble. Around .9 stays smooth and responsive.")]
            [Range(0f, 1f)] [SerializeField] private float dampingRatio = .9f;
            [Tooltip("Maximum TargetJoint force. Heavy ragdolls need more force.")]
            [Min(0f)] [SerializeField] private float maximumForce = 1900f;
            [Tooltip("Makes the joint target use the exact mouse/touch world position without smoothing delay.")]
            [SerializeField] private bool directPointerTracking = true;
            [Tooltip("Minimum joint frequency used by direct pointer tracking.")]
            [Range(1f, 40f)] [SerializeField] private float directTrackingFrequency = 22f;
            [Tooltip("Minimum direct-follow force per unit of grabbed Rigidbody mass.")]
            [Min(0f)] [SerializeField] private float directTrackingForcePerMass = 4000f;
            [Tooltip("Lower values reduce pointer delay. Avoid zero because it can expose touch jitter.")]
            [Range(.01f, .25f)] [SerializeField] private float targetSmoothTime = .03f;
            [Tooltip("Maximum world-space speed of the smoothed pointer target.")]
            [Min(1f)] [SerializeField] private float maximumTargetSpeed = 75f;

            [Header("Elastic Limits")]
            [Min(.05f)] [SerializeField] private float headStretchLimit = .55f;
            [Min(.05f)] [SerializeField] private float armStretchLimit = 1.1f;
            [Min(.05f)] [SerializeField] private float legStretchLimit = .85f;
            [Min(.05f)] [SerializeField] private float defaultStretchLimit = .65f;
            [Tooltip("Frequency retained after a limb exceeds its elastic limit.")]
            [Range(.1f, 1f)] [SerializeField] private float stretchedFrequencyMultiplier = .9f;

            [Header("Head Drag Assist")]
            [Tooltip("The head must pull the full body through the neck, so it receives extra force.")]
            [Range(1f, 3f)] [SerializeField] private float headForceMultiplier = 1.8f;
            [Range(1f, 2f)] [SerializeField] private float headFrequencyMultiplier = 1.3f;
            [Tooltip("Lower values let the grabbed head bounce around the pointer like a spring.")]
            [Range(.15f, 1f)] [SerializeField] private float headDampingMultiplier = .55f;

            public float Frequency => frequency;
            public float DampingRatio => dampingRatio;
            public float MaximumForce => maximumForce;
            public bool DirectPointerTracking => directPointerTracking;
            public float DirectTrackingFrequency => directTrackingFrequency;
            public float DirectTrackingForcePerMass => directTrackingForcePerMass;
            public float TargetSmoothTime => targetSmoothTime;
            public float MaximumTargetSpeed => maximumTargetSpeed;
            public float HeadStretchLimit => headStretchLimit;
            public float ArmStretchLimit => armStretchLimit;
            public float LegStretchLimit => legStretchLimit;
            public float DefaultStretchLimit => defaultStretchLimit;
            public float StretchedFrequencyMultiplier => stretchedFrequencyMultiplier;
            public float HeadForceMultiplier => headForceMultiplier;
            public float HeadFrequencyMultiplier => headFrequencyMultiplier;
            public float HeadDampingMultiplier => headDampingMultiplier;

            internal void Validate()
            {
                frequency = Mathf.Clamp(frequency, .5f, 12f);
                dampingRatio = Mathf.Clamp01(dampingRatio);
                maximumForce = Mathf.Max(0f, maximumForce);
                directTrackingFrequency = Mathf.Clamp(directTrackingFrequency, 1f, 40f);
                directTrackingForcePerMass = Mathf.Max(0f, directTrackingForcePerMass);
                targetSmoothTime = Mathf.Clamp(targetSmoothTime, .01f, .25f);
                maximumTargetSpeed = Mathf.Max(1f, maximumTargetSpeed);
                headStretchLimit = Mathf.Max(.05f, headStretchLimit);
                armStretchLimit = Mathf.Max(.05f, armStretchLimit);
                legStretchLimit = Mathf.Max(.05f, legStretchLimit);
                defaultStretchLimit = Mathf.Max(.05f, defaultStretchLimit);
                stretchedFrequencyMultiplier = Mathf.Clamp(stretchedFrequencyMultiplier, .1f, 1f);
                headForceMultiplier = Mathf.Clamp(headForceMultiplier, 1f, 3f);
                headFrequencyMultiplier = Mathf.Clamp(headFrequencyMultiplier, 1f, 2f);
                headDampingMultiplier = Mathf.Clamp(headDampingMultiplier, .15f, 1f);
            }
        }

        [Header("Input")]
        [SerializeField] private Camera inputCamera;
        [Tooltip("Explicit DamageReceiver2D references for Head, Belly, Arms, and Legs only.")]
        [SerializeField] private DamageReceiver2D[] receivers = Array.Empty<DamageReceiver2D>();
        [SerializeField] private LayerMask draggableLayers = ~0;
        [SerializeField] private bool ignorePointerOverUI = true;

        [Header("Common Drag Feel")]
        [SerializeField] private DragConfiguration drag = new DragConfiguration();

        private DamageReceiver2D activeReceiver;
        private int activeFingerId = -1;
        private bool inputEnabled = true;

        public event Action<DamageReceiver2D, Vector2> DragStarted;
        public event Action<DamageReceiver2D, Vector2> DragUpdated;
        public event Action<DamageReceiver2D, Vector2> DragEnded;

        public bool InputEnabled => inputEnabled;
        public DragConfiguration Drag => drag;

        private void Awake() => ApplyDragSettingsToAllParts();

        private void Update()
        {
            if (!inputEnabled || inputCamera == null) return;
            if (Input.touchCount > 0) ReadTouch();
            else ReadMouse();
        }

        private void ReadMouse()
        {
            if (Input.GetMouseButtonDown(0)) TryBegin(Input.mousePosition, -1);
            if (activeReceiver != null && Input.GetMouseButton(0)) UpdateActive(Input.mousePosition);
            if (activeReceiver != null && Input.GetMouseButtonUp(0)) EndActive(Input.mousePosition);
        }

        private void ReadTouch()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (activeFingerId >= 0 && touch.fingerId != activeFingerId) continue;

                if (touch.phase == TouchPhase.Began) TryBegin(touch.position, touch.fingerId);
                else if (activeReceiver != null &&
                         (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary))
                    UpdateActive(touch.position);
                else if (activeReceiver != null &&
                         (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled))
                    EndActive(touch.position);
            }
        }

        private void TryBegin(Vector2 screenPoint, int fingerId)
        {
            if (ignorePointerOverUI && EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject(fingerId)) return;

            Vector2 world = ScreenToWorld(screenPoint);
            Collider2D hit = Physics2D.OverlapPoint(world, draggableLayers);
            DamageReceiver2D receiver = ResolveReceiver(hit);
            if (receiver == null) return;

            // Applying immediately before BeginDrag makes Play Mode Inspector tuning affect the next grab.
            receiver.ApplyDragConfiguration(drag);
            if (!receiver.BeginDrag(world)) return;

            activeReceiver = receiver;
            activeFingerId = fingerId;
            DragStarted?.Invoke(receiver, world);
        }

        private void UpdateActive(Vector2 screenPoint)
        {
            Vector2 world = ScreenToWorld(screenPoint);
            activeReceiver.UpdateDragTarget(world);
            DragUpdated?.Invoke(activeReceiver, world);
        }

        private void EndActive(Vector2 screenPoint)
        {
            Vector2 world = ScreenToWorld(screenPoint);
            DamageReceiver2D released = activeReceiver;
            released.EndDrag();
            activeReceiver = null;
            activeFingerId = -1;
            DragEnded?.Invoke(released, world);
        }

        private Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 world = inputCamera.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, -inputCamera.transform.position.z));
            return new Vector2(world.x, world.y);
        }

        /// <summary>Pushes the common Inspector drag feel to every currently wired ragdoll limb.</summary>
        public void ApplyDragSettingsToAllParts()
        {
            for (int i = 0; i < receivers.Length; i++)
                if (receivers[i] != null) receivers[i].ApplyDragConfiguration(drag);
        }

        private DamageReceiver2D ResolveReceiver(Collider2D hit)
        {
            if (hit == null) return null;
            Rigidbody2D hitBody = hit.attachedRigidbody;
            for (int i = 0; i < receivers.Length; i++)
                if (receivers[i] != null && receivers[i].Body == hitBody) return receivers[i];
            return null;
        }

        /// <summary>Enables or disables gameplay input and safely releases any active grabbed limb.</summary>
        public void SetInputEnabled(bool value)
        {
            if (inputEnabled == value) return;
            inputEnabled = value;
            if (!inputEnabled && activeReceiver != null) activeReceiver.EndDrag();
            activeReceiver = null;
            activeFingerId = -1;
        }

        private void OnDisable()
        {
            if (activeReceiver != null) activeReceiver.EndDrag();
            activeReceiver = null;
            activeFingerId = -1;
        }

        private void OnValidate()
        {
            if (drag == null) drag = new DragConfiguration();
            drag.Validate();
        }
    }
}
