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

    public enum IdleFaceExpression
    {
        Neutral,
        Happy,
        Curious,
        Sleepy,
        Surprised,
        Grumpy
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
        [SerializeField] private Camera inputCamera;
        [Tooltip("Only the main character skin renderers should be tinted; candy and debris renderers are excluded.")]
        [SerializeField] private SpriteRenderer[] bodyRenderers = Array.Empty<SpriteRenderer>();

        [Header("Idle")]
        [Min(0f)] [SerializeField] private float idleDelay = 2.5f;
        [Min(.5f)] [SerializeField] private float minimumBlinkInterval = 2f;
        [Min(.5f)] [SerializeField] private float maximumBlinkInterval = 4.5f;
        [Min(.03f)] [SerializeField] private float blinkDuration = .12f;
        [Range(0f, .1f)] [SerializeField] private float idleGazeDistance = .04f;
        [Range(0f, .05f)] [SerializeField] private float idleFaceBob = .012f;
        [Min(.25f)] [SerializeField] private float minimumExpressionDuration = 1.5f;
        [Min(.25f)] [SerializeField] private float maximumExpressionDuration = 3.5f;
        [Min(0f)] [SerializeField] private float expressionBlendSpeed = 10f;
        [Range(15f, 60f)] [SerializeField] private float faceUpdateRate = 30f;

        [Header("Face Layout")]
        [Tooltip("Moves the complete procedural face in the head's local space.")]
        [SerializeField] private Vector2 facePositionOffset = Vector2.zero;
        [Tooltip("Scales the complete face without changing the head sprite.")]
        [SerializeField] private Vector2 faceScale = Vector2.one;
        [Range(.01f, .6f)] [SerializeField] private float eyeSpacing = .18f;
        [Range(-.5f, .5f)] [SerializeField] private float eyeVerticalOffset = .1f;
        [Range(.02f, .5f)] [SerializeField] private float eyeWidth = .18f;
        [SerializeField] private Vector2 pupilPositionOffset = Vector2.zero;
        [SerializeField] private Vector2 pupilScale = new Vector2(.38f, .45f);
        [SerializeField] private Vector2 mouthPositionOffset = new Vector2(0f, -.2f);
        [Range(.02f, .6f)] [SerializeField] private float mouthWidth = .22f;

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
        private IdleFaceExpression idleExpression;
        private float nextIdleExpressionTime;
        private float curiousDirection = 1f;
        private float nextFaceUpdateTime;
        private Color lastTintColor;
        private float lastTintBlend = -1f;

        public event Action<RagdollAnimationState> StateChanged;
        public event Action<RagdollPartHealth, float> DamageReactionPlayed;
        public event Action<DamageReceiver2D> DragReactionStarted;
        public event Action IdleAnimationStarted;
        public event Action<IdleFaceExpression> IdleExpressionChanged;

        public RagdollAnimationState CurrentState => state;

        private void Awake()
        {
            controller = GetComponent<RagdollController>();
            damageManager = GetComponent<RagdollDamageManager>();
            inputManager = GetComponent<RagdollInputManager>();
            if (inputCamera == null) inputCamera = Camera.main;

            RagdollLifeVisuals legacyVisuals = GetComponent<RagdollLifeVisuals>();
            if (legacyVisuals != null) legacyVisuals.enabled = false;

            head = FindChildContaining(transform, "head");
            if (head != null) CreateFaceIfNeeded();

            if (bodyRenderers == null || bodyRenderers.Length == 0)
                bodyRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            baseColors = new Color[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
                baseColors[i] = bodyRenderers[i] != null ? bodyRenderers[i].color : Color.white;

            lastInteractionTime = Time.time;
            ScheduleBlink();
            ScheduleIdleExpression();
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
            float now = Time.unscaledTime;
            if (now >= nextFaceUpdateTime)
            {
                float interval = 1f / Mathf.Max(1f, faceUpdateRate);
                nextFaceUpdateTime = now + interval;
                UpdateFace(interval);
            }
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
            lastTintBlend = -1f;

            if (animator != null)
            {
                animator.SetBool(IdleParameter, state == RagdollAnimationState.Idle);
                animator.SetBool(DraggingParameter, state == RagdollAnimationState.Dragged);
                animator.SetBool(DeadParameter, state == RagdollAnimationState.Dead);
                animator.SetBool(KnockedOutParameter, state == RagdollAnimationState.KnockedOut);
            }

            if (state == RagdollAnimationState.Idle)
            {
                SelectNextIdleExpression();
                IdleAnimationStarted?.Invoke();
            }
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
            lastTintBlend = -1f;

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

        private struct FacePose
        {
            public float LeftEyeHeight;
            public float RightEyeHeight;
            public float LeftEyeAngle;
            public float RightEyeAngle;
            public float MouthHeight;
            public float MouthWidthMultiplier;
            public float MouthAngle;
            public float FaceAngle;
            public Vector2 Gaze;
            public Vector2 FaceMotion;
        }

        private void UpdateFace(float deltaTime)
        {
            if (face == null || leftEye == null || rightEye == null ||
                leftPupil == null || rightPupil == null || mouth == null) return;

            FacePose pose = EvaluateFacePose();
            if ((state == RagdollAnimationState.Idle || state == RagdollAnimationState.Relaxed) && blinking)
            {
                pose.LeftEyeHeight = .05f;
                pose.RightEyeHeight = .05f;
            }

            float blend = expressionBlendSpeed <= 0f
                ? 1f
                : 1f - Mathf.Exp(-expressionBlendSpeed * deltaTime);

            Vector3 targetFacePosition = new Vector3(
                facePositionOffset.x + pose.FaceMotion.x,
                facePositionOffset.y + pose.FaceMotion.y,
                0f);
            Vector3 targetFaceScale = new Vector3(
                Mathf.Max(.01f, faceScale.x),
                Mathf.Max(.01f, faceScale.y),
                1f);
            face.localPosition = Vector3.Lerp(face.localPosition, targetFacePosition, blend);
            face.localScale = Vector3.Lerp(face.localScale, targetFaceScale, blend);
            face.localRotation = Quaternion.Lerp(
                face.localRotation,
                Quaternion.Euler(0f, 0f, pose.FaceAngle),
                blend);

            leftEye.localPosition = Vector3.Lerp(
                leftEye.localPosition,
                new Vector3(-eyeSpacing, eyeVerticalOffset, -.01f),
                blend);
            rightEye.localPosition = Vector3.Lerp(
                rightEye.localPosition,
                new Vector3(eyeSpacing, eyeVerticalOffset, -.01f),
                blend);
            leftEye.localScale = Vector3.Lerp(
                leftEye.localScale,
                new Vector3(eyeWidth, pose.LeftEyeHeight, 1f),
                blend);
            rightEye.localScale = Vector3.Lerp(
                rightEye.localScale,
                new Vector3(eyeWidth, pose.RightEyeHeight, 1f),
                blend);
            leftEye.localRotation = Quaternion.Lerp(
                leftEye.localRotation,
                Quaternion.Euler(0f, 0f, pose.LeftEyeAngle),
                blend);
            rightEye.localRotation = Quaternion.Lerp(
                rightEye.localRotation,
                Quaternion.Euler(0f, 0f, pose.RightEyeAngle),
                blend);

            Vector3 targetGaze = new Vector3(
                pose.Gaze.x + pupilPositionOffset.x,
                pose.Gaze.y + pupilPositionOffset.y,
                0f);
            Vector3 targetPupilScale = new Vector3(
                Mathf.Max(.01f, pupilScale.x),
                Mathf.Max(.01f, pupilScale.y),
                1f);
            leftPupil.localPosition = Vector3.Lerp(leftPupil.localPosition, targetGaze, blend);
            rightPupil.localPosition = Vector3.Lerp(rightPupil.localPosition, targetGaze, blend);
            leftPupil.localScale = Vector3.Lerp(leftPupil.localScale, targetPupilScale, blend);
            rightPupil.localScale = Vector3.Lerp(rightPupil.localScale, targetPupilScale, blend);

            mouth.localPosition = Vector3.Lerp(
                mouth.localPosition,
                new Vector3(mouthPositionOffset.x, mouthPositionOffset.y, -.01f),
                blend);
            mouth.localScale = Vector3.Lerp(
                mouth.localScale,
                new Vector3(mouthWidth * pose.MouthWidthMultiplier, pose.MouthHeight, 1f),
                blend);
            mouth.localRotation = Quaternion.Lerp(
                mouth.localRotation,
                Quaternion.Euler(0f, 0f, pose.MouthAngle),
                blend);
        }

        private FacePose EvaluateFacePose()
        {
            FacePose pose = new FacePose
            {
                LeftEyeHeight = .25f,
                RightEyeHeight = .25f,
                MouthHeight = .05f,
                MouthWidthMultiplier = 1f
            };

            switch (state)
            {
                case RagdollAnimationState.Idle:
                    UpdateBlink();
                    UpdateIdleExpression();
                    EvaluateIdleExpression(ref pose);
                    break;

                case RagdollAnimationState.Dragged:
                    pose.LeftEyeHeight = .32f;
                    pose.RightEyeHeight = .32f;
                    pose.Gaze = DirectionToLocalPoint(dragPoint) * .065f;
                    pose.MouthHeight = .1f;
                    pose.MouthWidthMultiplier = .8f;
                    break;

                case RagdollAnimationState.Hurt:
                    pose.LeftEyeHeight = Mathf.Lerp(.16f, .06f, reactionStrength);
                    pose.RightEyeHeight = pose.LeftEyeHeight;
                    pose.Gaze = new Vector2(0f, -.025f);
                    pose.MouthHeight = .035f;
                    pose.MouthAngle = 8f;
                    break;

                case RagdollAnimationState.KnockedOut:
                    pose.LeftEyeHeight = .045f;
                    pose.RightEyeHeight = .045f;
                    pose.MouthHeight = .025f;
                    pose.FaceAngle = 5f;
                    break;

                case RagdollAnimationState.Dead:
                    pose.LeftEyeHeight = .025f;
                    pose.RightEyeHeight = .025f;
                    pose.MouthHeight = .02f;
                    pose.MouthAngle = -6f;
                    break;

                default:
                    UpdateBlink();
                    pose.Gaze = DirectionToPointer() * idleGazeDistance;
                    break;
            }

            return pose;
        }

        private void EvaluateIdleExpression(ref FacePose pose)
        {
            float time = Time.time;
            float slowWave = Mathf.Sin(time * 1.5f);
            pose.FaceMotion.y = slowWave * idleFaceBob;

            switch (idleExpression)
            {
                case IdleFaceExpression.Happy:
                    pose.LeftEyeHeight = .17f;
                    pose.RightEyeHeight = .17f;
                    pose.Gaze = new Vector2(Mathf.Sin(time * .7f), .015f) * idleGazeDistance;
                    pose.MouthHeight = .085f + slowWave * .01f;
                    pose.MouthWidthMultiplier = 1.2f;
                    break;

                case IdleFaceExpression.Curious:
                    pose.LeftEyeHeight = curiousDirection < 0f ? .31f : .21f;
                    pose.RightEyeHeight = curiousDirection > 0f ? .31f : .21f;
                    pose.Gaze = new Vector2(curiousDirection * idleGazeDistance, .015f);
                    pose.MouthHeight = .04f;
                    pose.MouthWidthMultiplier = .75f;
                    pose.FaceAngle = curiousDirection * -4f;
                    break;

                case IdleFaceExpression.Sleepy:
                    pose.LeftEyeHeight = .085f;
                    pose.RightEyeHeight = .085f;
                    pose.Gaze = new Vector2(0f, -.02f);
                    pose.MouthHeight = .032f + Mathf.Max(0f, slowWave) * .015f;
                    pose.MouthWidthMultiplier = .85f;
                    break;

                case IdleFaceExpression.Surprised:
                    pose.LeftEyeHeight = .34f;
                    pose.RightEyeHeight = .34f;
                    pose.Gaze = Vector2.zero;
                    pose.MouthHeight = .14f;
                    pose.MouthWidthMultiplier = .7f;
                    pose.FaceMotion.y += .012f;
                    break;

                case IdleFaceExpression.Grumpy:
                    pose.LeftEyeHeight = .14f;
                    pose.RightEyeHeight = .14f;
                    pose.LeftEyeAngle = -10f;
                    pose.RightEyeAngle = 10f;
                    pose.Gaze = new Vector2(0f, -.018f);
                    pose.MouthHeight = .035f;
                    pose.MouthAngle = -5f;
                    pose.MouthWidthMultiplier = 1.15f;
                    break;

                default:
                    pose.LeftEyeHeight = .24f;
                    pose.RightEyeHeight = .24f;
                    pose.Gaze = new Vector2(Mathf.Sin(time * .8f), Mathf.Sin(time * .43f)) * idleGazeDistance;
                    pose.MouthHeight = .045f + slowWave * .008f;
                    break;
            }
        }

        private void UpdateIdleExpression()
        {
            if (Time.time >= nextIdleExpressionTime) SelectNextIdleExpression();
        }

        private void SelectNextIdleExpression()
        {
            const int expressionCount = 6;
            int current = (int)idleExpression;
            int next = (current + 1 + UnityEngine.Random.Range(0, expressionCount - 1)) % expressionCount;
            idleExpression = (IdleFaceExpression)next;
            curiousDirection = UnityEngine.Random.value < .5f ? -1f : 1f;
            ScheduleIdleExpression();
            IdleExpressionChanged?.Invoke(idleExpression);
        }

        private void ScheduleIdleExpression()
        {
            nextIdleExpressionTime = Time.time + UnityEngine.Random.Range(
                minimumExpressionDuration,
                maximumExpressionDuration);
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

            if (Mathf.Abs(lastTintBlend - blend) < .0001f && lastTintColor == target) return;
            lastTintBlend = blend;
            lastTintColor = target;

            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                SpriteRenderer renderer = bodyRenderers[i];
                if (renderer == null) continue;
                renderer.color = Color.Lerp(baseColors[i], target, blend);
            }
        }

        private Vector2 DirectionToPointer()
        {
            if (inputCamera == null || head == null) return Vector2.zero;
            Vector3 world = inputCamera.ScreenToWorldPoint(Input.mousePosition);
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
            lastTintBlend = -1f;
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
            minimumExpressionDuration = Mathf.Max(.25f, minimumExpressionDuration);
            maximumExpressionDuration = Mathf.Max(minimumExpressionDuration, maximumExpressionDuration);
            expressionBlendSpeed = Mathf.Max(0f, expressionBlendSpeed);
            faceUpdateRate = Mathf.Clamp(faceUpdateRate, 15f, 60f);
            faceScale.x = Mathf.Max(.01f, faceScale.x);
            faceScale.y = Mathf.Max(.01f, faceScale.y);
            pupilScale.x = Mathf.Max(.01f, pupilScale.x);
            pupilScale.y = Mathf.Max(.01f, pupilScale.y);
        }
    }
}
