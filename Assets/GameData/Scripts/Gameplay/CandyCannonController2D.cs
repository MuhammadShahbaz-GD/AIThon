using System;
using KickTheBuddy.Physics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KickTheBuddy.Gameplay
{
    public enum CandyCannonSide { Left, Right }
    public enum CandyCannonTutorialPhase { AwaitingLeftHit, AwaitingRightHit, FreePlay, Complete }

    /// <summary>
    /// Level 3 input, tutorial and prewarmed projectile-pool owner. Damage is still calculated only
    /// by RagdollAttackManager2D and RagdollDamageManager on the body part actually struck.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CandyCannonController2D : MonoBehaviour
    {
        [Serializable]
        private sealed class CannonSlot
        {
            [SerializeField] private CandyCannonSide side;
            [SerializeField] private Collider2D pressCollider;
            [SerializeField] private Transform muzzle;
            [SerializeField] private Transform recoilVisual;
            [SerializeField] private ParticleSystem muzzleFlash;
            [SerializeField] private GameObject tutorialIndicator;

            [NonSerialized] public Vector3 RestLocalPosition;
            [NonSerialized] public Vector3 IndicatorRestScale;
            [NonSerialized] public float NextFireTime;
            [NonSerialized] public float NextHoldFireTime;
            [NonSerialized] public int HeldPointerId = NoPointer;

            public CandyCannonSide Side => side;
            public Collider2D PressCollider => pressCollider;
            public Transform Muzzle => muzzle;
            public Transform RecoilVisual => recoilVisual;
            public ParticleSystem MuzzleFlash => muzzleFlash;
            public GameObject TutorialIndicator => tutorialIndicator;

            public void CachePresentation()
            {
                RestLocalPosition = recoilVisual != null ? recoilVisual.localPosition : Vector3.zero;
                IndicatorRestScale = tutorialIndicator != null
                    ? tutorialIndicator.transform.localScale
                    : Vector3.one;
            }
        }

        [Serializable]
        private sealed class ProjectileSlot
        {
            [SerializeField] private Rigidbody2D body;
            [SerializeField] private Collider2D collider;
            [SerializeField] private SpriteRenderer renderer;
            [SerializeField] private TrailRenderer trail;
            [SerializeField] private RagdollAttackManager2D attack;

            [NonSerialized] public CandyCannonSide Side;
            [NonSerialized] public Rigidbody2D TargetBody;
            [NonSerialized] public float RemainingLifetime;
            [NonSerialized] public bool Active;
            [NonSerialized] public bool RecycleRequested;
            [NonSerialized] public bool Charged;

            public Rigidbody2D Body => body;
            public Collider2D Collider => collider;
            public SpriteRenderer Renderer => renderer;
            public TrailRenderer Trail => trail;
            public RagdollAttackManager2D Attack => attack;
        }

        [Header("Authored References")]
        [SerializeField] private Camera inputCamera;
        [SerializeField] private RagdollController ragdoll;
        [SerializeField] private RagdollAnimationController animationController;
        [SerializeField] private SoundManager soundManager;
        [Tooltip("Order: torso, head, arms, then legs. Tutorial shots always target element zero.")]
        [SerializeField] private Rigidbody2D[] aimBodies = Array.Empty<Rigidbody2D>();
        [Tooltip("Explicit colliders on the main ragdoll parts. A projectile ignores non-target parts so its authored shot cannot be intercepted by another limb.")]
        [SerializeField] private Collider2D[] ragdollPartColliders = Array.Empty<Collider2D>();
        [SerializeField] private CannonSlot leftCannon = new CannonSlot();
        [SerializeField] private CannonSlot rightCannon = new CannonSlot();
        [SerializeField] private ProjectileSlot[] projectilePool = Array.Empty<ProjectileSlot>();

        [Header("Input")]
        [SerializeField] private bool ignorePointerOverUI = true;
        [Tooltip("Minimum spacing between projectiles fired by the same cannon.")]
        [Min(.03f)] [SerializeField] private float perCannonCooldown = .11f;
        [Tooltip("Minimum spacing between either cannon. Keep this below the per-cannon cooldown.")]
        [Min(.02f)] [SerializeField] private float globalFireInterval = .055f;
        [Tooltip("Time a pointer must stay down before automatic fire begins.")]
        [Min(.05f)] [SerializeField] private float holdDelay = .28f;
        [Tooltip("Automatic-fire request interval while a cannon remains held.")]
        [Min(.03f)] [SerializeField] private float holdFireInterval = .11f;
        [Tooltip("Bounds buffered taps to the fixed projectile pool; no tap can create a runtime object.")]
        [Range(1, 12)] [SerializeField] private int maximumQueuedShots = 12;

        [Header("Projectile Feel")]
        [Min(1f)] [SerializeField] private float projectileSpeed = 14.5f;
        [Min(.25f)] [SerializeField] private float projectileLifetime = 3f;
        [Range(0f, .6f)] [SerializeField] private float targetLeadTime = .18f;
        [Range(.01f, .5f)] [SerializeField] private float chargedHealthRatio = .12f;
        [Range(1f, 2f)] [SerializeField] private float chargedSpeedMultiplier = 1.18f;
        [Tooltip("Point impulse applied to the exact living limb after a projectile successfully deals damage.")]
        [Min(0f)] [SerializeField] private float projectileImpactImpulse = 3.4f;
        [Tooltip("Extra physical kick for the low-health charged shot.")]
        [Range(1f, 2f)] [SerializeField] private float chargedImpactMultiplier = 1.35f;
        [SerializeField] private Color leftProjectileColor = new Color(1f, .35f, .62f);
        [SerializeField] private Color rightProjectileColor = new Color(.34f, .75f, 1f);

        [Header("Presentation")]
        [Min(0f)] [SerializeField] private float recoilDistance = .22f;
        [Min(.1f)] [SerializeField] private float recoilRecoverySpeed = 13f;
        [Range(0f, .3f)] [SerializeField] private float tutorialPulseAmount = .13f;
        [Min(.1f)] [SerializeField] private float tutorialPulseSpeed = 4.5f;
        [SerializeField] private bool useOpeningTutorial = true;

        private CandyCannonTutorialPhase tutorialPhase;
        private const int NoPointer = int.MinValue;
        private const int LeftProgrammaticPointer = int.MinValue + 1;
        private const int RightProgrammaticPointer = int.MinValue + 2;
        private float nextGlobalFireTime;
        private int completedShotCount;
        private bool inputEnabled;
        private int pendingLeftShots;
        private int pendingRightShots;
        private bool preferLeftShot = true;
        private bool tutorialProjectileInFlight;

        public bool InputEnabled => inputEnabled;
        public CandyCannonTutorialPhase TutorialPhase => tutorialPhase;
        public int PendingShotCount => pendingLeftShots + pendingRightShots;
        public int CompletedShotCount => completedShotCount;
        public float LastImpactImpulse { get; private set; }
        public int ActiveProjectileCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < projectilePool.Length; i++)
                    if (projectilePool[i] != null && projectilePool[i].Active) count++;
                return count;
            }
        }

        public event Action<bool> InputStateChanged;
        public event Action<CandyCannonTutorialPhase> TutorialPhaseChanged;
        public event Action<CandyCannonSide, Vector2, Vector2, bool> CannonFired;
        public event Action<CandyCannonSide, Rigidbody2D, float, Vector2> ProjectileHit;
        public event Action<CandyCannonSide, Rigidbody2D, Vector2, float, Vector2> ProjectileImpactApplied;
        public event Action<CandyCannonSide> ProjectileMissed;

        private void Awake()
        {
            leftCannon?.CachePresentation();
            rightCannon?.CachePresentation();
            ResetCannons();
        }

        private void OnEnable()
        {
            for (int i = 0; i < projectilePool.Length; i++)
                if (projectilePool[i]?.Attack != null)
                    projectilePool[i].Attack.DamageDealt += HandleDamageDealt;
            RefreshTutorialIndicators();
        }

        private void OnDisable()
        {
            for (int i = 0; i < projectilePool.Length; i++)
                if (projectilePool[i]?.Attack != null)
                    projectilePool[i].Attack.DamageDealt -= HandleDamageDealt;
            SetInputEnabled(false);
            RecycleAllProjectiles();
        }

        private void Update()
        {
            RecoverCannon(leftCannon);
            RecoverCannon(rightCannon);
            PulseTutorialIndicator(leftCannon);
            PulseTutorialIndicator(rightCannon);
            if (!inputEnabled) return;

            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began)
                        TryBeginPress(touch.position, touch.fingerId);
                    else if (touch.phase == TouchPhase.Stationary || touch.phase == TouchPhase.Moved)
                        ContinueHeldPointer(touch.fingerId);
                    else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                        EndHeldPointer(touch.fingerId);
                }
            }
            else
            {
                if (Input.GetMouseButtonDown(0)) TryBeginPress(Input.mousePosition, -1);
                else if (Input.GetMouseButton(0)) ContinueHeldPointer(-1);
                if (Input.GetMouseButtonUp(0)) EndHeldPointer(-1);
            }
            if (leftCannon != null && leftCannon.HeldPointerId == LeftProgrammaticPointer)
                ContinueHeldPointer(LeftProgrammaticPointer);
            if (rightCannon != null && rightCannon.HeldPointerId == RightProgrammaticPointer)
                ContinueHeldPointer(RightProgrammaticPointer);
        }

        private void FixedUpdate()
        {
            ProcessPendingShots();

            for (int i = 0; i < projectilePool.Length; i++)
            {
                ProjectileSlot slot = projectilePool[i];
                if (slot == null || !slot.Active) continue;
                slot.RemainingLifetime -= Time.fixedDeltaTime;
                if (!slot.RecycleRequested && slot.RemainingLifetime > 0f) continue;
                bool missed = !slot.RecycleRequested;
                CandyCannonSide side = slot.Side;
                RecycleProjectile(slot);
                if (!missed) continue;
                if (tutorialPhase != CandyCannonTutorialPhase.FreePlay) tutorialProjectileInFlight = false;
                ProjectileMissed?.Invoke(side);
            }
        }

        public void SetInputEnabled(bool value)
        {
            if (inputEnabled == value) return;
            inputEnabled = value;
            pendingLeftShots = pendingRightShots = 0;
            ClearHeldPointers();
            RefreshTutorialIndicators();
            InputStateChanged?.Invoke(value);
        }

        public void ResetCannons()
        {
            pendingLeftShots = pendingRightShots = 0;
            tutorialProjectileInFlight = false;
            preferLeftShot = true;
            ClearHeldPointers();
            nextGlobalFireTime = 0f;
            completedShotCount = 0;
            LastImpactImpulse = 0f;
            ResetCannon(leftCannon);
            ResetCannon(rightCannon);
            RecycleAllProjectiles();
            SetTutorialPhase(useOpeningTutorial
                ? CandyCannonTutorialPhase.AwaitingLeftHit
                : CandyCannonTutorialPhase.FreePlay);
        }

        /// <summary>
        /// Buffers exactly one physical projectile. Cooldowns are applied while draining the queue,
        /// so quick taps are preserved instead of silently discarded.
        /// </summary>
        public bool RequestFire(CandyCannonSide side)
        {
            if (!inputEnabled || tutorialProjectileInFlight) return false;
            CannonSlot cannon = side == CandyCannonSide.Left ? leftCannon : rightCannon;
            if (cannon == null || cannon.Muzzle == null) return false;
            if (tutorialPhase == CandyCannonTutorialPhase.AwaitingLeftHit && side != CandyCannonSide.Left)
                return false;
            if (tutorialPhase == CandyCannonTutorialPhase.AwaitingRightHit && side != CandyCannonSide.Right)
                return false;
            int queued = pendingLeftShots + pendingRightShots;
            if (queued >= maximumQueuedShots || queued >= CountAvailableProjectiles()) return false;

            if (side == CandyCannonSide.Left) pendingLeftShots++;
            else pendingRightShots++;
            tutorialProjectileInFlight = tutorialPhase != CandyCannonTutorialPhase.FreePlay;
            return true;
        }

        /// <summary>Starts a tap plus automatic repeat; useful for UI/EventTrigger integrations.</summary>
        public bool BeginContinuousFire(CandyCannonSide side)
        {
            CannonSlot cannon = side == CandyCannonSide.Left ? leftCannon : rightCannon;
            int pointerId = side == CandyCannonSide.Left
                ? LeftProgrammaticPointer
                : RightProgrammaticPointer;
            return BeginHeldCannon(cannon, pointerId);
        }

        public void EndContinuousFire(CandyCannonSide side)
        {
            CannonSlot cannon = side == CandyCannonSide.Left ? leftCannon : rightCannon;
            if (cannon == null) return;
            int expectedPointer = side == CandyCannonSide.Left
                ? LeftProgrammaticPointer
                : RightProgrammaticPointer;
            if (cannon.HeldPointerId == expectedPointer) cannon.HeldPointerId = NoPointer;
        }

        public void CompleteInteraction()
        {
            SetInputEnabled(false);
            SetTutorialPhase(CandyCannonTutorialPhase.Complete);
        }

        /// <summary>
        /// Rebinds the live persistent audio service after scene activation. This prevents a Level 3
        /// scene reference from retaining a bootstrap duplicate that Unity has destroyed.
        /// </summary>
        public void ConfigureAudio(SoundManager value) => soundManager = value;

        private void TryBeginPress(Vector2 screenPosition, int pointerId)
        {
            if (inputCamera == null) return;
            if (ignorePointerOverUI && EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject(pointerId)) return;
            Vector3 world3 = inputCamera.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, -inputCamera.transform.position.z));
            Vector2 world = world3;
            if (leftCannon?.PressCollider != null && leftCannon.PressCollider.OverlapPoint(world))
                BeginHeldCannon(leftCannon, pointerId);
            else if (rightCannon?.PressCollider != null && rightCannon.PressCollider.OverlapPoint(world))
                BeginHeldCannon(rightCannon, pointerId);
        }

        private bool BeginHeldCannon(CannonSlot cannon, int pointerId)
        {
            if (cannon == null || cannon.HeldPointerId != NoPointer) return false;
            if (!RequestFire(cannon.Side)) return false;
            cannon.HeldPointerId = pointerId;
            cannon.NextHoldFireTime = Time.unscaledTime + holdDelay;
            return true;
        }

        private void ContinueHeldPointer(int pointerId)
        {
            CannonSlot cannon = FindHeldCannon(pointerId);
            if (cannon == null || Time.unscaledTime < cannon.NextHoldFireTime) return;
            if (RequestFire(cannon.Side))
                cannon.NextHoldFireTime = Time.unscaledTime + holdFireInterval;
        }

        private void EndHeldPointer(int pointerId)
        {
            CannonSlot cannon = FindHeldCannon(pointerId);
            if (cannon != null) cannon.HeldPointerId = NoPointer;
        }

        private CannonSlot FindHeldCannon(int pointerId)
        {
            if (leftCannon != null && leftCannon.HeldPointerId == pointerId) return leftCannon;
            if (rightCannon != null && rightCannon.HeldPointerId == pointerId) return rightCannon;
            return null;
        }

        private void ClearHeldPointers()
        {
            if (leftCannon != null) leftCannon.HeldPointerId = NoPointer;
            if (rightCannon != null) rightCannon.HeldPointerId = NoPointer;
        }

        private void ProcessPendingShots()
        {
            if (Time.time < nextGlobalFireTime || FindAvailableProjectile() == null) return;
            bool leftReady = pendingLeftShots > 0 && leftCannon != null &&
                             Time.time >= leftCannon.NextFireTime;
            bool rightReady = pendingRightShots > 0 && rightCannon != null &&
                              Time.time >= rightCannon.NextFireTime;
            if (!leftReady && !rightReady) return;

            bool fireLeft = leftReady && (!rightReady || preferLeftShot);
            CannonSlot cannon = fireLeft ? leftCannon : rightCannon;
            if (fireLeft) pendingLeftShots--;
            else pendingRightShots--;
            Launch(cannon);
            cannon.NextFireTime = Time.time + perCannonCooldown;
            nextGlobalFireTime = Time.time + globalFireInterval;
            preferLeftShot = !fireLeft;
        }

        private void Launch(CannonSlot cannon)
        {
            ProjectileSlot projectile = FindAvailableProjectile();
            if (projectile == null || cannon?.Muzzle == null)
            {
                tutorialProjectileInFlight = false;
                return;
            }

            Rigidbody2D targetBody = ResolveAimBody();
            Vector2 origin = cannon.Muzzle.position;
            Vector2 target = targetBody != null
                ? targetBody.worldCenterOfMass + targetBody.velocity * targetLeadTime
                : Vector2.zero;
            Vector2 direction = target - origin;
            if (direction.sqrMagnitude < .001f)
                direction = cannon.Side == CandyCannonSide.Left ? Vector2.right : Vector2.left;
            direction.Normalize();

            bool charged = ragdoll != null && ragdoll.MaximumHealth > 0f &&
                           ragdoll.CurrentHealth / ragdoll.MaximumHealth <= chargedHealthRatio;
            float speed = projectileSpeed * (charged ? chargedSpeedMultiplier : 1f);
            ActivateProjectile(projectile, cannon.Side, targetBody, origin, direction, speed, charged);
            PlayCannonPresentation(cannon, direction, origin, charged);
            CannonFired?.Invoke(cannon.Side, origin, direction * speed, charged);
        }

        private void ActivateProjectile(ProjectileSlot projectile, CandyCannonSide side,
            Rigidbody2D targetBody, Vector2 origin, Vector2 direction, float speed, bool charged)
        {
            projectile.Side = side;
            projectile.TargetBody = targetBody;
            projectile.Active = true;
            projectile.RecycleRequested = false;
            projectile.Charged = charged;
            projectile.RemainingLifetime = projectileLifetime;
            projectile.Body.gameObject.SetActive(true);
            projectile.Body.simulated = true;
            projectile.Body.position = origin;
            projectile.Body.rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            projectile.Body.velocity = direction * speed;
            projectile.Body.angularVelocity = side == CandyCannonSide.Left ? 360f : -360f;
            if (projectile.Collider != null) projectile.Collider.enabled = true;
            ConfigureTargetCollisions(projectile, true);
            if (projectile.Renderer != null)
            {
                projectile.Renderer.enabled = true;
                projectile.Renderer.color = side == CandyCannonSide.Left
                    ? leftProjectileColor
                    : rightProjectileColor;
                projectile.Renderer.transform.localScale = Vector3.one * (charged ? 1.25f : 1f);
            }
            if (projectile.Trail != null)
            {
                projectile.Trail.Clear();
                projectile.Trail.enabled = true;
                projectile.Trail.startColor = side == CandyCannonSide.Left
                    ? leftProjectileColor
                    : rightProjectileColor;
            }
            projectile.Attack?.ResetAttack();
        }

        private void PlayCannonPresentation(CannonSlot cannon, Vector2 direction, Vector2 origin, bool charged)
        {
            if (cannon.RecoilVisual != null)
            {
                Vector3 localDirection = cannon.RecoilVisual.parent != null
                    ? cannon.RecoilVisual.parent.InverseTransformDirection(direction)
                    : (Vector3)direction;
                cannon.RecoilVisual.localPosition = cannon.RestLocalPosition - localDirection * recoilDistance;
            }
            if (charged) animationController?.PlayCryReaction(.8f);
            else if (cannon.Side == CandyCannonSide.Right &&
                     tutorialPhase == CandyCannonTutorialPhase.AwaitingRightHit)
                animationController?.PlayShockReaction(.7f);
        }

        private Rigidbody2D ResolveAimBody()
        {
            if (aimBodies == null || aimBodies.Length == 0) return null;
            if (tutorialPhase == CandyCannonTutorialPhase.AwaitingLeftHit ||
                tutorialPhase == CandyCannonTutorialPhase.AwaitingRightHit) return aimBodies[0];

            // Torso and head receive more shots; limbs still build local crack stages.
            int pattern = completedShotCount % 10;
            int index = pattern <= 2 ? 0 :
                pattern <= 5 ? Mathf.Min(1, aimBodies.Length - 1) :
                2 + (pattern - 6) % Mathf.Max(1, aimBodies.Length - 2);
            return aimBodies[Mathf.Clamp(index, 0, aimBodies.Length - 1)];
        }

        private void HandleDamageDealt(RagdollAttackManager2D attack, Rigidbody2D body,
            float damage, float speed, Vector2 point)
        {
            ProjectileSlot projectile = FindProjectile(attack);
            if (projectile == null || !projectile.Active || projectile.RecycleRequested) return;
            ApplyProjectileImpact(projectile, body, speed, point);
            projectile.RecycleRequested = true;
            completedShotCount++;
            tutorialProjectileInFlight = false;
            ProjectileHit?.Invoke(projectile.Side, body, damage, point);
            if (tutorialPhase == CandyCannonTutorialPhase.AwaitingLeftHit &&
                projectile.Side == CandyCannonSide.Left)
                SetTutorialPhase(CandyCannonTutorialPhase.AwaitingRightHit);
            else if (tutorialPhase == CandyCannonTutorialPhase.AwaitingRightHit &&
                     projectile.Side == CandyCannonSide.Right)
                SetTutorialPhase(CandyCannonTutorialPhase.FreePlay);
        }

        private void ApplyProjectileImpact(ProjectileSlot projectile, Rigidbody2D hitBody,
            float relativeSpeed, Vector2 point)
        {
            if (hitBody == null || !hitBody.simulated || projectileImpactImpulse <= 0f) return;

            Vector2 direction = projectile.Body != null ? projectile.Body.velocity : Vector2.zero;
            if (direction.sqrMagnitude <= .0001f)
                direction = projectile.Side == CandyCannonSide.Left ? Vector2.right : Vector2.left;
            else
                direction.Normalize();

            float speedRatio = projectileSpeed > .01f
                ? Mathf.Clamp(relativeSpeed / projectileSpeed, .65f, 1.5f)
                : 1f;
            float impulse = projectileImpactImpulse * speedRatio *
                            (projectile.Charged ? chargedImpactMultiplier : 1f);
            hitBody.AddForceAtPosition(direction * impulse, point, ForceMode2D.Impulse);
            LastImpactImpulse = impulse;
            ProjectileImpactApplied?.Invoke(projectile.Side, hitBody, direction, impulse, point);
        }

        private ProjectileSlot FindAvailableProjectile()
        {
            for (int i = 0; i < projectilePool.Length; i++)
                if (projectilePool[i] != null && !projectilePool[i].Active &&
                    projectilePool[i].Body != null) return projectilePool[i];
            return null;
        }

        private int CountAvailableProjectiles()
        {
            int count = 0;
            for (int i = 0; i < projectilePool.Length; i++)
                if (projectilePool[i] != null && !projectilePool[i].Active &&
                    projectilePool[i].Body != null) count++;
            return count;
        }

        private ProjectileSlot FindProjectile(RagdollAttackManager2D attack)
        {
            for (int i = 0; i < projectilePool.Length; i++)
                if (projectilePool[i] != null && projectilePool[i].Attack == attack)
                    return projectilePool[i];
            return null;
        }

        private void RecycleAllProjectiles()
        {
            for (int i = 0; i < projectilePool.Length; i++)
                if (projectilePool[i] != null) RecycleProjectile(projectilePool[i]);
        }

        private void RecycleProjectile(ProjectileSlot projectile)
        {
            ConfigureTargetCollisions(projectile, false);
            projectile.Active = projectile.RecycleRequested = false;
            projectile.Charged = false;
            projectile.TargetBody = null;
            projectile.RemainingLifetime = 0f;
            if (projectile.Trail != null) { projectile.Trail.Clear(); projectile.Trail.enabled = false; }
            if (projectile.Body != null)
            {
                projectile.Body.velocity = Vector2.zero;
                projectile.Body.angularVelocity = 0f;
                projectile.Body.simulated = false;
            }
            if (projectile.Collider != null) projectile.Collider.enabled = false;
            if (projectile.Renderer != null) projectile.Renderer.enabled = false;
            if (projectile.Body != null) projectile.Body.gameObject.SetActive(false);
        }

        private void ConfigureTargetCollisions(ProjectileSlot projectile, bool ignoreNonTargets)
        {
            if (projectile?.Collider == null || ragdollPartColliders == null) return;
            for (int i = 0; i < ragdollPartColliders.Length; i++)
            {
                Collider2D partCollider = ragdollPartColliders[i];
                if (partCollider == null) continue;
                bool ignore = ignoreNonTargets &&
                              partCollider.attachedRigidbody != projectile.TargetBody;
                Physics2D.IgnoreCollision(projectile.Collider, partCollider, ignore);
            }
        }

        private static void ResetCannon(CannonSlot cannon)
        {
            if (cannon == null) return;
            cannon.NextFireTime = 0f;
            cannon.NextHoldFireTime = 0f;
            cannon.HeldPointerId = NoPointer;
            if (cannon.RecoilVisual != null) cannon.RecoilVisual.localPosition = cannon.RestLocalPosition;
        }

        private void RecoverCannon(CannonSlot cannon)
        {
            if (cannon?.RecoilVisual == null) return;
            float blend = 1f - Mathf.Exp(-recoilRecoverySpeed * Time.deltaTime);
            cannon.RecoilVisual.localPosition =
                Vector3.Lerp(cannon.RecoilVisual.localPosition, cannon.RestLocalPosition, blend);
        }

        private void PulseTutorialIndicator(CannonSlot cannon)
        {
            if (cannon?.TutorialIndicator == null || !cannon.TutorialIndicator.activeSelf) return;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * tutorialPulseSpeed) * tutorialPulseAmount;
            cannon.TutorialIndicator.transform.localScale = cannon.IndicatorRestScale * pulse;
        }

        private void SetTutorialPhase(CandyCannonTutorialPhase phase)
        {
            bool changed = tutorialPhase != phase;
            tutorialPhase = phase;
            RefreshTutorialIndicators();
            if (changed) TutorialPhaseChanged?.Invoke(phase);
        }

        private void RefreshTutorialIndicators()
        {
            if (leftCannon?.TutorialIndicator != null)
                leftCannon.TutorialIndicator.SetActive(
                    inputEnabled && tutorialPhase == CandyCannonTutorialPhase.AwaitingLeftHit);
            if (rightCannon?.TutorialIndicator != null)
                rightCannon.TutorialIndicator.SetActive(
                    inputEnabled && tutorialPhase == CandyCannonTutorialPhase.AwaitingRightHit);
        }

        private void OnValidate()
        {
            perCannonCooldown = Mathf.Max(.03f, perCannonCooldown);
            globalFireInterval = Mathf.Clamp(globalFireInterval, .02f, perCannonCooldown);
            holdDelay = Mathf.Max(.05f, holdDelay);
            holdFireInterval = Mathf.Max(.03f, holdFireInterval);
            maximumQueuedShots = Mathf.Clamp(maximumQueuedShots, 1, 12);
            projectileSpeed = Mathf.Max(1f, projectileSpeed);
            projectileLifetime = Mathf.Max(.25f, projectileLifetime);
            targetLeadTime = Mathf.Clamp(targetLeadTime, 0f, .6f);
            chargedHealthRatio = Mathf.Clamp(chargedHealthRatio, .01f, .5f);
            chargedSpeedMultiplier = Mathf.Clamp(chargedSpeedMultiplier, 1f, 2f);
            projectileImpactImpulse = Mathf.Max(0f, projectileImpactImpulse);
            chargedImpactMultiplier = Mathf.Clamp(chargedImpactMultiplier, 1f, 2f);
            recoilDistance = Mathf.Max(0f, recoilDistance);
            recoilRecoverySpeed = Mathf.Max(.1f, recoilRecoverySpeed);
            tutorialPulseAmount = Mathf.Clamp(tutorialPulseAmount, 0f, .3f);
            tutorialPulseSpeed = Mathf.Max(.1f, tutorialPulseSpeed);
        }
    }
}
