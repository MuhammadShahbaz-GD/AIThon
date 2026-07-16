using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    public enum RagdollAnimationState
    {
        Relaxed,
        Idle,
        Dragged,
        Hurt,
        KnockedOut,
        Dead
    }

    /// <summary>
    /// Coordinates procedural face reactions and an optional Animator without applying physics forces.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollController), typeof(RagdollDamageManager))]
    public sealed class RagdollAnimationController : MonoBehaviour
    {
        [Header("Optional Authored Animator")]
        [SerializeField] private Animator animator;

        [Header("Idle")]
        [Min(0f)] [SerializeField] private float idleDelay = 2.5f;
        [Min(.5f)] [SerializeField] private float minimumBlinkInterval = 2f;
        [Min(.5f)] [SerializeField] private float maximumBlinkInterval = 4.5f;
        [Min(.03f)] [SerializeField] private float blinkDuration = .12f;
        [Range(0f, .1f)] [SerializeField] private float idleGazeDistance = .04f;
        [Range(0f, .05f)] [SerializeField] private float idleFaceBob = .012f;

        [Header("Reactions")]
        [Min(.05f)] [SerializeField] private float damageReactionDuration = .28f;
        [SerializeField] private Color damageFlashColor = new Color(1f, .35f, .3f);
        [SerializeField] private Color deadTint = new Color(.35f, .35f, .38f);
        [Range(0f, 1f)] [SerializeField] private float tintStrength = .45f;

        private static readonly int IdleParameter = Animator.StringToHash("IsIdle");
        private static readonly int DraggingParameter = Animator.StringToHash("IsDragging");
        private static readonly int HurtTrigger = Animator.StringToHash("Damage");
        private static readonly int DeadParameter = Animator.StringToHash("IsDead");
        private static readonly int KnockedOutParameter = Animator.StringToHash("IsKnockedOut");
        private static readonly int ReactionStrengthParameter = Animator.StringToHash("ReactionStrength");

        private RagdollController controller;
        private RagdollDamageManager damageManager;
        private RagdollInputManager inputManager;
        private Transform head;
        private Transform face;
        private Transform leftEye;
        private Transform rightEye;
        private Transform leftPupil;
        private Transform rightPupil;
        private Transform mouth;
        private SpriteRenderer[] bodyRenderers = Array.Empty<SpriteRenderer>();
        private Color[] baseColors = Array.Empty<Color>();
        private RagdollAnimationState state;
        private Vector2 dragPoint;
        private float reactionStrength;
        private float hurtUntil;
        private float lastInteractionTime;
        private float nextBlinkTime;
        private float blinkEndTime;
        private bool blinking;
        private bool dragging;
        private bool inputBound;

        public event Action<RagdollAnimationState> StateChanged;
        public event Action<RagdollPartHealth, float> DamageReactionPlayed;
        public event Action<DamageReceiver2D> DragReactionStarted;
        public event Action IdleAnimationStarted;

        public RagdollAnimationState CurrentState => state;

        private void Awake()
        {
            controller = GetComponent<RagdollController>();
            damageManager = GetComponent<RagdollDamageManager>();
            inputManager = GetComponent<RagdollInputManager>();

            RagdollLifeVisuals legacyVisuals = GetComponent<RagdollLifeVisuals>();
            if (legacyVisuals != null) legacyVisuals.enabled = false;

            head = FindChildContaining(transform, "head");
            if (head != null) CreateFaceIfNeeded();

            bodyRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            baseColors = new Color[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
                baseColors[i] = bodyRenderers[i] != null ? bodyRenderers[i].color : Color.white;

            lastInteractionTime = Time.time;
            ScheduleBlink();
        }

        private void Start()
        {
            BindInput();
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.OnCharacterKO += HandleKnockout;
                controller.OnCharacterRevived += HandleRevived;
            }

            if (damageManager != null)
                damageManager.DamageCalculated += HandleDamage;

            BindInput();
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnCharacterKO -= HandleKnockout;
                controller.OnCharacterRevived -= HandleRevived;
            }

            if (damageManager != null)
                damageManager.DamageCalculated -= HandleDamage;

            UnbindInput();
            RestoreBodyColors();
        }

        private void Update()
        {
            UpdateState();
            UpdateFace();
            UpdateBodyTint();
        }

        private void BindInput()
        {
            if (inputBound) return;
            if (inputManager == null) inputManager = GetComponent<RagdollInputManager>();
            if (inputManager == null) return;

            inputManager.DragStarted += HandleDragStarted;
            inputManager.DragUpdated += HandleDragUpdated;
            inputManager.DragEnded += HandleDragEnded;
            inputBound = true;
        }

        private void UnbindInput()
        {
            if (!inputBound || inputManager == null) return;
            inputManager.DragStarted -= HandleDragStarted;
            inputManager.DragUpdated -= HandleDragUpdated;
            inputManager.DragEnded -= HandleDragEnded;
            inputBound = false;
        }

        private void UpdateState()
        {
            RagdollAnimationState next;
            if (controller == null) next = RagdollAnimationState.Relaxed;
            else if (controller.CurrentHealth <= 0f) next = RagdollAnimationState.Dead;
            else if (controller.CurrentState == RagdollState.KnockedOut) next = RagdollAnimationState.KnockedOut;
            else if (dragging) next = RagdollAnimationState.Dragged;
            else if (Time.time < hurtUntil) next = RagdollAnimationState.Hurt;
            else if (Time.time - lastInteractionTime >= idleDelay) next = RagdollAnimationState.Idle;
            else next = RagdollAnimationState.Relaxed;

            SetState(next);
        }

        private void SetState(RagdollAnimationState next)
        {
            if (state == next) return;
            state = next;

            if (animator != null)
            {
                animator.SetBool(IdleParameter, state == RagdollAnimationState.Idle);
                animator.SetBool(DraggingParameter, state == RagdollAnimationState.Dragged);
                animator.SetBool(DeadParameter, state == RagdollAnimationState.Dead);
                animator.SetBool(KnockedOutParameter, state == RagdollAnimationState.KnockedOut);
            }

            if (state == RagdollAnimationState.Idle) IdleAnimationStarted?.Invoke();
            StateChanged?.Invoke(state);
        }

        private void HandleDamage(
            Rigidbody2D body,
            RagdollPartHealth part,
            RagdollAttackManager2D attack,
            float damage,
            float impactSpeed,
            Vector2 point)
        {
            reactionStrength = Mathf.Clamp01((damage / 25f) * (part != null ? part.DamageReactionStrength : 1f));
            hurtUntil = Time.time + damageReactionDuration * Mathf.Lerp(.75f, 1.5f, reactionStrength);
            lastInteractionTime = Time.time;

            if (animator != null)
            {
                animator.SetFloat(ReactionStrengthParameter, reactionStrength);
                animator.SetTrigger(HurtTrigger);
            }

            DamageReactionPlayed?.Invoke(part, reactionStrength);
        }

        private void HandleDragStarted(DamageReceiver2D receiver, Vector2 point)
        {
            dragging = true;
            dragPoint = point;
            lastInteractionTime = Time.time;
            DragReactionStarted?.Invoke(receiver);
        }

        private void HandleDragUpdated(DamageReceiver2D receiver, Vector2 point)
        {
            dragPoint = point;
            lastInteractionTime = Time.time;
        }

        private void HandleDragEnded(DamageReceiver2D receiver, Vector2 point)
        {
            dragging = false;
            dragPoint = point;
            lastInteractionTime = Time.time;
        }

        private void HandleKnockout()
        {
            dragging = false;
            lastInteractionTime = Time.time;
        }

        private void HandleRevived()
        {
            reactionStrength = 0f;
            hurtUntil = 0f;
            lastInteractionTime = Time.time;
            RestoreBodyColors();
        }

        private void UpdateFace()
        {
            if (face == null || leftEye == null || rightEye == null) return;

            float eyeHeight = .25f;
            Vector2 gaze = Vector2.zero;
            float mouthHeight = .05f;
            Vector3 facePosition = Vector3.zero;

            switch (state)
            {
                case RagdollAnimationState.Idle:
                    UpdateBlink();
                    eyeHeight = blinking ? .05f : .24f;
                    float time = Time.time;
                    gaze = new Vector2(Mathf.Sin(time * .8f), Mathf.Sin(time * .43f)) * idleGazeDistance;
                    facePosition.y = Mathf.Sin(time * 1.5f) * idleFaceBob;
                    mouthHeight = .045f + Mathf.Sin(time * 1.5f) * .008f;
                    break;

                case RagdollAnimationState.Dragged:
                    eyeHeight = .32f;
                    gaze = DirectionToLocalPoint(dragPoint) * .065f;
                    mouthHeight = .1f;
                    break;

                case RagdollAnimationState.Hurt:
                    eyeHeight = Mathf.Lerp(.16f, .06f, reactionStrength);
                    gaze = new Vector2(0f, -.025f);
                    mouthHeight = .035f;
                    break;

                case RagdollAnimationState.KnockedOut:
                    eyeHeight = .045f;
                    mouthHeight = .025f;
                    break;

                case RagdollAnimationState.Dead:
                    eyeHeight = .025f;
                    gaze = Vector2.zero;
                    mouthHeight = .02f;
                    break;

                default:
                    UpdateBlink();
                    eyeHeight = blinking ? .05f : .25f;
                    gaze = DirectionToPointer() * idleGazeDistance;
                    break;
            }

            face.localPosition = facePosition;
            SetEyeHeight(eyeHeight);
            leftPupil.localPosition = gaze;
            rightPupil.localPosition = gaze;
            mouth.localScale = new Vector3(.22f, mouthHeight, 1f);
        }

        private void UpdateBlink()
        {
            if (!blinking && Time.time >= nextBlinkTime)
            {
                blinking = true;
                blinkEndTime = Time.time + blinkDuration;
            }

            if (blinking && Time.time >= blinkEndTime)
            {
                blinking = false;
                ScheduleBlink();
            }
        }

        private void UpdateBodyTint()
        {
            Color target = Color.white;
            float blend = 0f;

            if (state == RagdollAnimationState.Hurt)
            {
                target = damageFlashColor;
                blend = tintStrength * reactionStrength;
            }
            else if (state == RagdollAnimationState.Dead)
            {
                target = deadTint;
                blend = tintStrength;
            }

            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                SpriteRenderer renderer = bodyRenderers[i];
                if (renderer == null) continue;
                renderer.color = Color.Lerp(baseColors[i], target, blend);
            }
        }

        private Vector2 DirectionToPointer()
        {
            Camera camera = Camera.main;
            if (camera == null || head == null) return Vector2.zero;
            Vector3 world = camera.ScreenToWorldPoint(Input.mousePosition);
            return head.InverseTransformDirection(world - head.position).normalized;
        }

        private Vector2 DirectionToLocalPoint(Vector2 worldPoint)
        {
            if (head == null) return Vector2.zero;
            return head.InverseTransformDirection(worldPoint - (Vector2)head.position).normalized;
        }

        private void SetEyeHeight(float height)
        {
            leftEye.localScale = new Vector3(.18f, height, 1f);
            rightEye.localScale = new Vector3(.18f, height, 1f);
        }

        private void RestoreBodyColors()
        {
            for (int i = 0; i < bodyRenderers.Length; i++)
                if (bodyRenderers[i] != null) bodyRenderers[i].color = baseColors[i];
        }

        private void ScheduleBlink()
        {
            nextBlinkTime = Time.time + UnityEngine.Random.Range(minimumBlinkInterval, maximumBlinkInterval);
        }

        private void CreateFaceIfNeeded()
        {
            face = head.Find("Life Face");
            if (face != null)
            {
                leftEye = face.Find("Left Eye");
                rightEye = face.Find("Right Eye");
                if (leftEye != null) leftPupil = leftEye.Find("Pupil");
                if (rightEye != null) rightPupil = rightEye.Find("Pupil");
                mouth = face.Find("Mouth");
            }

            SpriteRenderer headRenderer = head.GetComponent<SpriteRenderer>();
            if (headRenderer == null || headRenderer.sprite == null) return;

            if (face == null)
            {
                face = new GameObject("Life Face").transform;
                face.SetParent(head, false);
            }

            if (leftEye == null)
                leftEye = CreateFeature(face, "Left Eye", new Vector3(-.18f, .1f, -.01f), new Vector3(.18f, .25f, 1f), Color.white, headRenderer.sprite, 20);
            if (rightEye == null)
                rightEye = CreateFeature(face, "Right Eye", new Vector3(.18f, .1f, -.01f), new Vector3(.18f, .25f, 1f), Color.white, headRenderer.sprite, 20);
            if (leftPupil == null)
                leftPupil = CreateFeature(leftEye, "Pupil", Vector3.zero, new Vector3(.38f, .45f, 1f), new Color(.05f, .07f, .1f), headRenderer.sprite, 21);
            if (rightPupil == null)
                rightPupil = CreateFeature(rightEye, "Pupil", Vector3.zero, new Vector3(.38f, .45f, 1f), new Color(.05f, .07f, .1f), headRenderer.sprite, 21);
            if (mouth == null)
                mouth = CreateFeature(face, "Mouth", new Vector3(0f, -.2f, -.01f), new Vector3(.22f, .05f, 1f), new Color(.25f, .04f, .04f), headRenderer.sprite, 20);
        }

        private static Transform CreateFeature(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 scale,
            Color color,
            Sprite sprite,
            int sortingOrder)
        {
            GameObject feature = new GameObject(name, typeof(SpriteRenderer));
            feature.transform.SetParent(parent, false);
            feature.transform.localPosition = position;
            feature.transform.localScale = scale;
            SpriteRenderer renderer = feature.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return feature.transform;
        }

        private static Transform FindChildContaining(Transform root, string value)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
                if (children[i].name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    return children[i];
            return null;
        }

        private void OnValidate()
        {
            idleDelay = Mathf.Max(0f, idleDelay);
            minimumBlinkInterval = Mathf.Max(.5f, minimumBlinkInterval);
            maximumBlinkInterval = Mathf.Max(minimumBlinkInterval, maximumBlinkInterval);
            blinkDuration = Mathf.Max(.03f, blinkDuration);
            damageReactionDuration = Mathf.Max(.05f, damageReactionDuration);
        }
    }
}
