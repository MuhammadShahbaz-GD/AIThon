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
        Dead,
        Annoyed
    }

    /// <summary>The authored facial sequences available to gameplay and presentation systems.</summary>
    public enum RagdollFaceExpression
    {
        Smile,
        Laugh,
        Shock,
        Cry,
        Depressed,
        Hidden
    }

    /// <summary>
    /// Event-driven visual presenter for the active ragdoll. Physics and damage stay in their
    /// dedicated modules; this component only converts those signals into face animation,
    /// optional Animator parameters, and a short body-colour reaction.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollController), typeof(RagdollDamageManager), typeof(RagdollInputManager))]
    public sealed class RagdollAnimationController : MonoBehaviour
    {
        [Header("Optional Authored Animator")]
        [SerializeField] private Animator animator;
        [Tooltip("Assign only the six visible ragdoll-part renderers. Candy/debris and the face are excluded.")]
        [SerializeField] private SpriteRenderer[] bodyRenderers = Array.Empty<SpriteRenderer>();

        [Header("Authored Face Expressions")]
        [Tooltip("One explicitly authored overlay below the Head. Runtime code never searches for or creates it.")]
        [SerializeField] private SpriteRenderer faceRenderer;
        [SerializeField] private Sprite[] smileFrames = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] laughFrames = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] shockFrames = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] cryFrames = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] depressedFrames = Array.Empty<Sprite>();
        [Range(1f, 30f)] [SerializeField] private float faceFramesPerSecond = 24f;

        [Header("Face Layout")]
        [Tooltip("Local-space offset of the complete face overlay below the Head.")]
        [SerializeField] private Vector2 facePositionOffset = Vector2.zero;
        [Tooltip("Local-space scale of the complete face overlay.")]
        [SerializeField] private Vector2 faceScale = Vector2.one;

        [Header("Expression Feel")]
        [Min(0f)] [SerializeField] private float idleDelay = 2.5f;
        [Range(0f, 1f)] [SerializeField] private float idleLaughChancePerSmileLoop = .07f;
        [Min(.1f)] [SerializeField] private float normalHitShockDuration = 1.55f;
        [Min(.1f)] [SerializeField] private float comboCryDuration = 1.9f;
        [Min(.1f)] [SerializeField] private float maximumDamageCryDuration = 2.2f;
        [Min(.1f)] [SerializeField] private float limbBreakCryDuration = 2.7f;
        [Min(2)] [SerializeField] private int comboCryThreshold = 3;
        [Tooltip("Post-mitigation damage that counts as a maximum-strength single hit for this character balance.")]
        [Min(.01f)] [SerializeField] private float maximumDamageThreshold = 6f;
        [Tooltip("Fallback fraction of one part's maximum health removed by a single maximum-strength hit.")]
        [Range(.005f, .25f)] [SerializeField] private float maximumDamageHealthRatio = .02f;
        [Range(0f, 1f)] [SerializeField] private float depressedHealthThreshold = .3f;

        [Header("Body Damage Reaction")]
        [Min(.05f)] [SerializeField] private float damageReactionDuration = .4f;
        [SerializeField] private Color damageFlashColor = new Color(1f, .35f, .3f);
        [SerializeField] private Color deadTint = new Color(.35f, .35f, .38f);
        [Range(0f, 1f)] [SerializeField] private float tintStrength = .45f;

        private const int PriorityNone = 0;
        private const int PriorityNormalHit = 50;
        private const int PriorityComboOrMaximum = 70;
        private const int PriorityLimbBreak = 80;
        private const int PriorityKnockout = 90;
        private const int PriorityDeath = 100;

        private static readonly int IdleParameter = Animator.StringToHash("IsIdle");
        private static readonly int DraggingParameter = Animator.StringToHash("IsDragging");
        private static readonly int HurtTrigger = Animator.StringToHash("Damage");
        private static readonly int DeadParameter = Animator.StringToHash("IsDead");
        private static readonly int KnockedOutParameter = Animator.StringToHash("IsKnockedOut");
        private static readonly int ReactionStrengthParameter = Animator.StringToHash("ReactionStrength");

        private RagdollController controller;
        private RagdollDamageManager damageManager;
        private RagdollInputManager inputManager;
        private Color[] baseColors = Array.Empty<Color>();
        private Sprite[] activeFrames = Array.Empty<Sprite>();
        private RagdollAnimationState state;
        private RagdollFaceExpression currentExpression = RagdollFaceExpression.Hidden;
        private RagdollFaceExpression lockedExpression = RagdollFaceExpression.Smile;
        private int frameIndex;
        private int lockedPriority;
        private float nextFrameTime;
        private float lockedUntil;
        private float hurtUntil;
        private float annoyedUntil;
        private float lastInteractionTime;
        private float reactionStrength;
        private float annoyanceStrength;
        private Color lastTintColor;
        private float lastTintBlend = -1f;
        private bool dragging;
        private bool inputBound;
        private bool sequenceComplete;
        private bool facePlaying;
        private bool hasBrokenLimb;

        public event Action<RagdollAnimationState> StateChanged;
        public event Action<RagdollFaceExpression> FaceExpressionChanged;
        public event Action<RagdollFaceExpression, float> FaceReactionStarted;
        public event Action<RagdollFaceExpression> FaceSequenceCompleted;
        public event Action<RagdollPartHealth, float> DamageReactionPlayed;
        public event Action<DamageReceiver2D> DragReactionStarted;
        public event Action IdleAnimationStarted;
        public event Action<Rigidbody2D, float> AnnoyedReactionPlayed;

        public RagdollAnimationState CurrentState => state;
        public RagdollFaceExpression CurrentFaceExpression => currentExpression;
        public bool HasAuthoredFaceAnimation =>
            faceRenderer != null && HasFrames(smileFrames) && HasFrames(shockFrames) &&
            HasFrames(cryFrames) && HasFrames(depressedFrames);
        public bool IsAuthoredFacePlaying => facePlaying && faceRenderer != null && faceRenderer.enabled;

        // Compatibility aliases retained for existing validation code while the project migrates terminology.
        public bool HasAuthoredIdleFaceAnimation => HasAuthoredFaceAnimation;
        public bool IsAuthoredIdleFacePlaying => IsAuthoredFacePlaying;

        private void Awake()
        {
            controller = GetComponent<RagdollController>();
            damageManager = GetComponent<RagdollDamageManager>();
            inputManager = GetComponent<RagdollInputManager>();

            RagdollLifeVisuals legacyVisuals = GetComponent<RagdollLifeVisuals>();
            if (legacyVisuals != null) legacyVisuals.enabled = false;

            if (bodyRenderers == null) bodyRenderers = Array.Empty<SpriteRenderer>();
            baseColors = new Color[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
                baseColors[i] = bodyRenderers[i] != null ? bodyRenderers[i].color : Color.white;

            lastInteractionTime = Time.time;
            ApplyFaceLayout();
            SetFaceExpression(RagdollFaceExpression.Smile, true);
            ApplyAnimatorState();
        }

        private void Start() => BindInput();

        private void OnEnable()
        {
            SubscribeController();
            if (damageManager != null) damageManager.DamageCalculated += HandleDamage;
            BindInput();
            RefreshFaceExpression(true);
        }

        private void OnDisable()
        {
            UnsubscribeController();
            if (damageManager != null) damageManager.DamageCalculated -= HandleDamage;
            UnbindInput();
            RestoreBodyColors();
            facePlaying = false;
            if (faceRenderer != null) faceRenderer.enabled = false;
        }

        private void Update()
        {
            UpdateState();
            RefreshFaceExpression(false);
            AdvanceFaceAnimation();
            UpdateBodyTint();
        }

        /// <summary>Explicit presentation hook for non-damage gameplay that needs a normal shock.</summary>
        public void PlayShockReaction(float duration)
        {
            RequestTemporaryExpression(
                RagdollFaceExpression.Shock,
                duration > 0f ? duration : normalHitShockDuration,
                PriorityNormalHit);
        }

        /// <summary>Explicit presentation hook for scripted severe reactions.</summary>
        public void PlayCryReaction(float duration)
        {
            RequestTemporaryExpression(
                RagdollFaceExpression.Cry,
                duration > 0f ? duration : maximumDamageCryDuration,
                PriorityComboOrMaximum);
        }

        /// <summary>
        /// Plays a visual-only jelly nuisance reaction. It deliberately avoids health, combo,
        /// knockout, and physics paths used by real attacks.
        /// </summary>
        public void PlayAnnoyedReaction(
            Rigidbody2D contactedPart,
            Vector2 contactPoint,
            float intensity,
            float duration)
        {
            if (controller == null || controller.CurrentHealth <= 0f ||
                controller.CurrentState == RagdollState.KnockedOut) return;

            annoyanceStrength = Mathf.Clamp01(intensity);
            annoyedUntil = Mathf.Max(annoyedUntil, Time.time + Mathf.Max(.15f, duration));
            lastInteractionTime = Time.time;
            RefreshFaceExpression(true);
            AnnoyedReactionPlayed?.Invoke(contactedPart, annoyanceStrength);
        }

        private void SubscribeController()
        {
            if (controller == null) return;
            controller.OnCharacterKO += HandleKnockout;
            controller.OnCharacterDied += HandleDeath;
            controller.OnCharacterRevived += HandleRevived;
            controller.OnLimbBroken += HandleLimbBroken;
        }

        private void UnsubscribeController()
        {
            if (controller == null) return;
            controller.OnCharacterKO -= HandleKnockout;
            controller.OnCharacterDied -= HandleDeath;
            controller.OnCharacterRevived -= HandleRevived;
            controller.OnLimbBroken -= HandleLimbBroken;
        }

        private void BindInput()
        {
            if (inputBound || inputManager == null) return;
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
            else if (Time.time < annoyedUntil) next = RagdollAnimationState.Annoyed;
            else if (Time.time - lastInteractionTime >= idleDelay) next = RagdollAnimationState.Idle;
            else next = RagdollAnimationState.Relaxed;

            SetState(next);
        }

        private void SetState(RagdollAnimationState next)
        {
            if (state == next) return;
            state = next;
            lastTintBlend = -1f;
            ApplyAnimatorState();
            if (state == RagdollAnimationState.Idle) IdleAnimationStarted?.Invoke();
            StateChanged?.Invoke(state);
        }

        private void ApplyAnimatorState()
        {
            if (animator == null) return;
            animator.SetBool(IdleParameter, state == RagdollAnimationState.Idle);
            animator.SetBool(DraggingParameter, state == RagdollAnimationState.Dragged);
            animator.SetBool(DeadParameter, state == RagdollAnimationState.Dead);
            animator.SetBool(KnockedOutParameter, state == RagdollAnimationState.KnockedOut);
        }

        private void HandleDamage(
            Rigidbody2D body,
            RagdollPartHealth part,
            RagdollAttackManager2D attack,
            float damage,
            float impactSpeed,
            Vector2 point)
        {
            float partReaction = part != null ? part.DamageReactionStrength : 1f;
            reactionStrength = Mathf.Clamp01((damage / Mathf.Max(.01f, maximumDamageThreshold)) * partReaction);
            hurtUntil = Time.time + damageReactionDuration * Mathf.Lerp(.8f, 1.45f, reactionStrength);
            lastInteractionTime = Time.time;
            lastTintBlend = -1f;

            bool depleted = part != null && part.IsDepleted;
            bool comboHit = controller != null && controller.CurrentCombo >= comboCryThreshold;
            bool maximumHit = damage >= maximumDamageThreshold ||
                              (part != null && damage >= part.MaximumHealth * maximumDamageHealthRatio);

            if (depleted)
            {
                RequestTemporaryExpression(RagdollFaceExpression.Cry, limbBreakCryDuration, PriorityLimbBreak);
            }
            else if (comboHit || maximumHit)
            {
                float duration = maximumHit ? maximumDamageCryDuration : comboCryDuration;
                RequestTemporaryExpression(RagdollFaceExpression.Cry, duration, PriorityComboOrMaximum);
            }
            else
            {
                RequestTemporaryExpression(RagdollFaceExpression.Shock, normalHitShockDuration, PriorityNormalHit);
            }

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
            lastInteractionTime = Time.time;
            RefreshFaceExpression(true);
            DragReactionStarted?.Invoke(receiver);
        }

        private void HandleDragUpdated(DamageReceiver2D receiver, Vector2 point) =>
            lastInteractionTime = Time.time;

        private void HandleDragEnded(DamageReceiver2D receiver, Vector2 point)
        {
            dragging = false;
            lastInteractionTime = Time.time;
            RefreshFaceExpression(true);
        }

        private void HandleLimbBroken(Rigidbody2D body, Vector2 point)
        {
            hasBrokenLimb = true;
            lastInteractionTime = Time.time;
            RequestTemporaryExpression(RagdollFaceExpression.Cry, limbBreakCryDuration, PriorityLimbBreak);
        }

        private void HandleKnockout()
        {
            dragging = false;
            annoyedUntil = 0f;
            lastInteractionTime = Time.time;
            RequestPersistentExpression(RagdollFaceExpression.Depressed, PriorityKnockout);
        }

        private void HandleDeath(Vector2 point)
        {
            dragging = false;
            annoyedUntil = 0f;
            lastInteractionTime = Time.time;
            // Death VFX owns the final presentation. Hidden is also resolved every Update, so event
            // subscription order can never re-enable the face over the glass/candy explosion.
            RequestPersistentExpression(RagdollFaceExpression.Hidden, PriorityDeath);
        }

        private void HandleRevived()
        {
            reactionStrength = 0f;
            hurtUntil = 0f;
            annoyanceStrength = 0f;
            annoyedUntil = 0f;
            hasBrokenLimb = false;
            dragging = false;
            ClearExpressionLock();
            lastInteractionTime = Time.time;
            RestoreBodyColors();
            RefreshFaceExpression(true);
        }

        private void RequestTemporaryExpression(RagdollFaceExpression expression, float duration, int priority)
        {
            float now = Time.time;
            float expiry = now + Mathf.Max(.05f, duration);
            if (lockedPriority > priority && now < lockedUntil) return;

            if (lockedPriority == priority && lockedExpression == expression && now < lockedUntil)
            {
                lockedUntil = Mathf.Max(lockedUntil, expiry);
                return;
            }

            lockedExpression = expression;
            lockedPriority = priority;
            lockedUntil = expiry;
            SetFaceExpression(expression, true);
            FaceReactionStarted?.Invoke(expression, duration);
        }

        private void RequestPersistentExpression(RagdollFaceExpression expression, int priority)
        {
            if (lockedPriority > priority && Time.time < lockedUntil) return;
            bool changed = lockedPriority != priority || lockedExpression != expression ||
                           !float.IsPositiveInfinity(lockedUntil);
            lockedExpression = expression;
            lockedPriority = priority;
            lockedUntil = float.PositiveInfinity;
            SetFaceExpression(expression, changed);
            if (changed) FaceReactionStarted?.Invoke(expression, float.PositiveInfinity);
        }

        private void ClearExpressionLock()
        {
            lockedExpression = RagdollFaceExpression.Smile;
            lockedPriority = PriorityNone;
            lockedUntil = 0f;
        }

        private void RefreshFaceExpression(bool restart)
        {
            if (lockedPriority != PriorityNone && Time.time >= lockedUntil) ClearExpressionLock();
            RagdollFaceExpression desired = ResolveDesiredExpression();

            // Let the rare authored laugh finish instead of cutting back to Smile every Update.
            if (!restart && currentExpression == RagdollFaceExpression.Laugh && !sequenceComplete &&
                desired == RagdollFaceExpression.Smile) return;

            SetFaceExpression(desired, restart);
        }

        private RagdollFaceExpression ResolveDesiredExpression()
        {
            if (controller != null && controller.CurrentHealth <= 0f) return RagdollFaceExpression.Hidden;
            if (controller != null && controller.CurrentState == RagdollState.KnockedOut)
                return RagdollFaceExpression.Depressed;
            if (lockedPriority != PriorityNone && Time.time < lockedUntil) return lockedExpression;
            if (dragging) return RagdollFaceExpression.Shock;
            if (Time.time < annoyedUntil) return RagdollFaceExpression.Depressed;
            if (hasBrokenLimb || IsLowHealth()) return RagdollFaceExpression.Depressed;
            return RagdollFaceExpression.Smile;
        }

        private bool IsLowHealth()
        {
            if (controller == null || controller.MaximumHealth <= 0f) return false;
            return controller.CurrentHealth / controller.MaximumHealth <= depressedHealthThreshold;
        }

        private void SetFaceExpression(RagdollFaceExpression expression, bool restart)
        {
            if (expression == RagdollFaceExpression.Hidden)
            {
                bool changedToHidden = currentExpression != RagdollFaceExpression.Hidden;
                currentExpression = RagdollFaceExpression.Hidden;
                activeFrames = Array.Empty<Sprite>();
                frameIndex = 0;
                sequenceComplete = true;
                facePlaying = false;
                if (faceRenderer != null) faceRenderer.enabled = false;
                if (changedToHidden) FaceExpressionChanged?.Invoke(currentExpression);
                return;
            }

            Sprite[] frames = ResolveFrames(expression);
            if (!HasFrames(frames) && expression != RagdollFaceExpression.Smile)
            {
                expression = RagdollFaceExpression.Smile;
                frames = smileFrames;
            }

            if (faceRenderer == null || !HasFrames(frames))
            {
                facePlaying = false;
                currentExpression = RagdollFaceExpression.Hidden;
                activeFrames = Array.Empty<Sprite>();
                if (faceRenderer != null) faceRenderer.enabled = false;
                return;
            }

            if (!restart && currentExpression == expression && activeFrames == frames && facePlaying) return;

            bool expressionChanged = currentExpression != expression;
            currentExpression = expression;
            activeFrames = frames;
            frameIndex = 0;
            sequenceComplete = false;
            facePlaying = true;
            ApplyFaceLayout();
            faceRenderer.enabled = true;
            faceRenderer.sprite = activeFrames[0];
            nextFrameTime = Time.time + 1f / Mathf.Max(1f, faceFramesPerSecond);
            if (expressionChanged) FaceExpressionChanged?.Invoke(currentExpression);
        }

        private void AdvanceFaceAnimation()
        {
            if (!facePlaying || faceRenderer == null || !faceRenderer.enabled ||
                !HasFrames(activeFrames) || sequenceComplete || Time.time < nextFrameTime) return;

            if (frameIndex + 1 < activeFrames.Length)
            {
                frameIndex++;
                faceRenderer.sprite = activeFrames[frameIndex];
                ScheduleNextFaceFrame();
                return;
            }

            FaceSequenceCompleted?.Invoke(currentExpression);
            if (currentExpression == RagdollFaceExpression.Laugh)
            {
                sequenceComplete = true;
                SetFaceExpression(ResolveDesiredExpression(), true);
                return;
            }

            if (IsLooping(currentExpression))
            {
                if (currentExpression == RagdollFaceExpression.Smile &&
                    state == RagdollAnimationState.Idle && HasFrames(laughFrames) &&
                    UnityEngine.Random.value < idleLaughChancePerSmileLoop)
                {
                    SetFaceExpression(RagdollFaceExpression.Laugh, true);
                    return;
                }

                frameIndex = 0;
                faceRenderer.sprite = activeFrames[0];
                ScheduleNextFaceFrame();
            }
            else
            {
                // Shock holds its final authored frame until the timed request or drag ends.
                sequenceComplete = true;
            }
        }

        private Sprite[] ResolveFrames(RagdollFaceExpression expression)
        {
            switch (expression)
            {
                case RagdollFaceExpression.Laugh: return laughFrames;
                case RagdollFaceExpression.Shock: return shockFrames;
                case RagdollFaceExpression.Cry: return cryFrames;
                case RagdollFaceExpression.Depressed: return depressedFrames;
                case RagdollFaceExpression.Smile: return smileFrames;
                default: return Array.Empty<Sprite>();
            }
        }

        private static bool IsLooping(RagdollFaceExpression expression) =>
            expression == RagdollFaceExpression.Smile ||
            expression == RagdollFaceExpression.Cry ||
            expression == RagdollFaceExpression.Depressed;

        private static bool HasFrames(Sprite[] frames) => frames != null && frames.Length > 0;

        private void ScheduleNextFaceFrame()
        {
            float interval = 1f / Mathf.Max(1f, faceFramesPerSecond);
            // Increment the scheduled timestamp instead of using now+interval. This preserves an
            // average 24 FPS on both 30 Hz and 60 Hz displays instead of quantizing to 15/20 FPS.
            nextFrameTime += interval;
            if (Time.time - nextFrameTime > interval * 3f) nextFrameTime = Time.time + interval;
        }

        private void ApplyFaceLayout()
        {
            if (faceRenderer == null) return;
            Transform overlay = faceRenderer.transform;
            overlay.localPosition = new Vector3(facePositionOffset.x, facePositionOffset.y, -.02f);
            overlay.localScale = new Vector3(
                Mathf.Max(.01f, faceScale.x),
                Mathf.Max(.01f, faceScale.y),
                1f);
        }

        private void UpdateBodyTint()
        {
            Color target = Color.white;
            float blend = 0f;

            if (state == RagdollAnimationState.Hurt)
            {
                target = damageFlashColor;
                blend = tintStrength * Mathf.Max(.15f, reactionStrength);
            }
            else if (state == RagdollAnimationState.Dead)
            {
                target = deadTint;
                blend = tintStrength;
            }

            if (Mathf.Abs(lastTintBlend - blend) < .0001f && lastTintColor == target) return;
            lastTintBlend = blend;
            lastTintColor = target;

            int count = Mathf.Min(bodyRenderers.Length, baseColors.Length);
            for (int i = 0; i < count; i++)
            {
                SpriteRenderer renderer = bodyRenderers[i];
                if (renderer == null || renderer == faceRenderer) continue;
                renderer.color = Color.Lerp(baseColors[i], target, blend);
            }
        }

        private void RestoreBodyColors()
        {
            int count = Mathf.Min(bodyRenderers.Length, baseColors.Length);
            for (int i = 0; i < count; i++)
                if (bodyRenderers[i] != null && bodyRenderers[i] != faceRenderer)
                    bodyRenderers[i].color = baseColors[i];
            lastTintBlend = -1f;
        }

        private void OnValidate()
        {
            idleDelay = Mathf.Max(0f, idleDelay);
            faceFramesPerSecond = Mathf.Clamp(faceFramesPerSecond, 1f, 30f);
            normalHitShockDuration = Mathf.Max(.1f, normalHitShockDuration);
            comboCryDuration = Mathf.Max(.1f, comboCryDuration);
            maximumDamageCryDuration = Mathf.Max(.1f, maximumDamageCryDuration);
            limbBreakCryDuration = Mathf.Max(.1f, limbBreakCryDuration);
            comboCryThreshold = Mathf.Max(2, comboCryThreshold);
            maximumDamageThreshold = Mathf.Max(.01f, maximumDamageThreshold);
            damageReactionDuration = Mathf.Max(.05f, damageReactionDuration);
            faceScale.x = Mathf.Max(.01f, faceScale.x);
            faceScale.y = Mathf.Max(.01f, faceScale.y);
            ApplyFaceLayout();
        }
    }
}
