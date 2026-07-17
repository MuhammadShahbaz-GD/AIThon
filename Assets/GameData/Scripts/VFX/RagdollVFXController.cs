using System;
using System.Collections;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using UnityEngine;

namespace KickTheBuddy.VFX
{
    /// <summary>Pooled presenter for impacts, combo/KO bursts, death blast, candy debris, and glass shards.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollController))]
    public sealed class RagdollVFXController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RagdollController controller;
        [SerializeField] private RagdollVFXProfile profile;
        [SerializeField] private RagdollCandyFill2D[] candyFills = Array.Empty<RagdollCandyFill2D>();
        [SerializeField] private SpriteRenderer[] characterRenderers = Array.Empty<SpriteRenderer>();

        [Header("Physical Death Pools")]
        [SerializeField] private Rigidbody2D[] candyDebrisBodies = Array.Empty<Rigidbody2D>();
        [SerializeField] private SpriteRenderer[] candyDebrisRenderers = Array.Empty<SpriteRenderer>();
        [SerializeField] private Rigidbody2D[] glassShardBodies = Array.Empty<Rigidbody2D>();
        [SerializeField] private SpriteRenderer[] glassShardRenderers = Array.Empty<SpriteRenderer>();
        [Range(4, 24)] [SerializeField] private int maximumActiveCandyDebris = 24;
        [Range(4, 20)] [SerializeField] private int maximumActiveGlassShards = 16;
        [Min(1f)] [SerializeField] private float debrisLifetime = 10f;

        [Header("Torso-Mapped Death Burst")]
        [Tooltip("All pooled debris is packed around this body before the death burst.")]
        [SerializeField] private Rigidbody2D deathDebrisOriginBody;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private Vector2 torsoPackingSize = new Vector2(1.55f, 1.55f);
        [SerializeField] private float floorWorldY = -3.95f;
        [Min(0f)] [SerializeField] private float screenEdgePadding = .55f;
        [SerializeField] private Vector2 candyFlightTimeRange = new Vector2(1.08f, 1.36f);
        [SerializeField] private Vector2 glassFlightTimeRange = new Vector2(.92f, 1.22f);
        [Min(1f)] [SerializeField] private float maximumDebrisSpeed = 12f;
        [SerializeField] private Vector2 angularVelocityRange = new Vector2(220f, 520f);

        [Header("Impact")]
        [SerializeField] private Color lightHitColor = new Color(1f, .85f, .25f);
        [SerializeField] private Color heavyHitColor = new Color(1f, .18f, .12f);
        [Min(.01f)] [SerializeField] private float speedForMaximumStrength = 18f;
        [Range(1, 12)] [SerializeField] private int minimumHitParticles = 4;
        [Range(1, 16)] [SerializeField] private int maximumHitParticles = 11;

        private ParticleSystem hitParticle;
        private ParticleSystem comboParticle;
        private ParticleSystem knockoutParticle;
        private ParticleSystem deathParticle;
        private ParticleSystem collisionFumeParticle;
        private Color profileColor = Color.white;
        private bool deathPlayed;
        private Coroutine debrisCleanup;
        private bool[] authoredRendererEnabled = Array.Empty<bool>();

        public event Action<Vector2, float> ImpactEffectPlayed;
        public event Action<int, Vector2> ComboEffectPlayed;
        public event Action<Vector2> KnockoutEffectPlayed;
        public event Action<Vector2> DeathEffectPlayed;

        private void Awake()
        {
            hitParticle = CreateShared(profile != null ? profile.HitPrefab : null, "Shared Hit");
            comboParticle = CreateShared(profile != null ? profile.ComboPrefab : null, "Shared Combo");
            knockoutParticle = CreateShared(profile != null ? profile.KnockoutPrefab : null, "Shared Knockout");
            deathParticle = CreateShared(profile != null ? profile.DeathPrefab : null, "Shared Death");
            collisionFumeParticle = CreateShared(profile != null ? profile.CollisionFumePrefab : null, "Shared Collision Fumes");
            authoredRendererEnabled = new bool[characterRenderers.Length];
            for (int i = 0; i < characterRenderers.Length; i++)
                authoredRendererEnabled[i] = characterRenderers[i] != null && characterRenderers[i].enabled;
            ResetDebris();
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.OnImpactResolved += HandleImpact;
            controller.OnProfileDamageEffect += HandleProfileColor;
            controller.OnComboAdvanced += HandleCombo;
            controller.OnCharacterKO += HandleKnockout;
            controller.OnCharacterDied += HandleDeath;
            controller.OnCharacterRevived += HandleRevived;
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnImpactResolved -= HandleImpact;
                controller.OnProfileDamageEffect -= HandleProfileColor;
                controller.OnComboAdvanced -= HandleCombo;
                controller.OnCharacterKO -= HandleKnockout;
                controller.OnCharacterDied -= HandleDeath;
                controller.OnCharacterRevived -= HandleRevived;
            }
            StopParticles();
        }

        private ParticleSystem CreateShared(ParticleSystem prefab, string objectName)
        {
            if (prefab == null) return null;
            ParticleSystem instance = Instantiate(prefab, transform);
            instance.gameObject.name = objectName;
            ParticleSystem.MainModule main = instance.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return instance;
        }

        private void HandleImpact(float damage, float impactSpeed, Vector2 point)
        {
            if (deathPlayed) return;
            float strength = Mathf.Clamp01(impactSpeed / speedForMaximumStrength);
            int count = Mathf.RoundToInt(Mathf.Lerp(minimumHitParticles, maximumHitParticles, strength));
            if (hitParticle != null)
                Emit(hitParticle, point, Color.Lerp(Color.Lerp(lightHitColor, heavyHitColor, strength), profileColor, .25f),
                    Mathf.Lerp(.04f, .09f, strength), count);
            if (collisionFumeParticle != null)
                Emit(collisionFumeParticle, point, new Color(1f, 1f, 1f, Mathf.Lerp(.35f, .68f, strength)),
                    Mathf.Lerp(.12f, .24f, strength), Mathf.RoundToInt(Mathf.Lerp(3f, 7f, strength)));
            ImpactEffectPlayed?.Invoke(point, strength);
        }

        private void HandleProfileColor(RagdollProfileType type, Color color, Vector2 point) => profileColor = color;

        private void HandleCombo(int combo, float damage, Vector2 point)
        {
            if (deathPlayed || combo < 3 || combo % 3 != 0 || comboParticle == null) return;
            PlayBurst(comboParticle, point);
            ComboEffectPlayed?.Invoke(combo, point);
        }

        private void HandleKnockout()
        {
            if (deathPlayed || controller == null || controller.CurrentHealth <= 0f || knockoutParticle == null) return;
            Vector2 point = ResolveCenter();
            PlayBurst(knockoutParticle, point);
            KnockoutEffectPlayed?.Invoke(point);
        }

        private void HandleDeath(Vector2 point)
        {
            if (deathPlayed) return;
            deathPlayed = true;
            Vector2 debrisOrigin = ResolveDebrisOrigin(point);
            if (deathParticle != null) PlayBurst(deathParticle, debrisOrigin);
            ReleaseCandyDebris(debrisOrigin);
            ReleaseGlassShards(debrisOrigin);
            SetCharacterRenderers(false);
            if (debrisCleanup != null) StopCoroutine(debrisCleanup);
            debrisCleanup = StartCoroutine(ResetDebrisAfterDelay());
            DeathEffectPlayed?.Invoke(debrisOrigin);
        }

        private void HandleRevived()
        {
            deathPlayed = false;
            if (debrisCleanup != null) StopCoroutine(debrisCleanup);
            debrisCleanup = null;
            ResetDebris();
            SetCharacterRenderers(true);
            for (int i = 0; i < candyFills.Length; i++)
                if (candyFills[i] != null) candyFills[i].SetCandyVisible(true);
            StopParticles();
        }

        private void ReleaseCandyDebris(Vector2 origin)
        {
            int candyLimit = Mathf.Min(maximumActiveCandyDebris, candyDebrisBodies.Length);
            for (int index = 0; index < candyLimit; index++)
            {
                Vector2 position = ResolvePackedPosition(index, candyLimit, origin, 0f);
                Vector2 landingPoint = ResolveLandingPoint(index, candyLimit, .11f);
                ActivateDebris(candyDebrisBodies[index],
                    index < candyDebrisRenderers.Length ? candyDebrisRenderers[index] : null,
                    position, Quaternion.Euler(0f, 0f, Hash01(index, .27f) * 360f),
                    landingPoint, ResolveFlightTime(index, candyFlightTimeRange, .47f), index, .31f);
            }

            // Authored fill visuals are presentation-only. Hide them even when an optimized scene
            // intentionally removed their individual serialized candy references.
            for (int fillIndex = 0; fillIndex < candyFills.Length; fillIndex++)
            {
                RagdollCandyFill2D fill = candyFills[fillIndex];
                if (fill == null) continue;
                fill.SetCandyVisible(false);
            }
        }

        private void ReleaseGlassShards(Vector2 origin)
        {
            int shardLimit = Mathf.Min(maximumActiveGlassShards, glassShardBodies.Length);
            for (int index = 0; index < shardLimit; index++)
            {
                Vector2 position = ResolvePackedPosition(index, shardLimit, origin, 74f);
                Vector2 landingPoint = ResolveLandingPoint(index, shardLimit, .59f);
                ActivateDebris(glassShardBodies[index],
                    index < glassShardRenderers.Length ? glassShardRenderers[index] : null,
                    position, Quaternion.Euler(0f, 0f, Hash01(index, .73f) * 360f),
                    landingPoint, ResolveFlightTime(index, glassFlightTimeRange, .83f), index, .67f);
            }
        }

        private void ActivateDebris(Rigidbody2D body, SpriteRenderer renderer, Vector2 position,
            Quaternion rotation, Vector2 landingPoint, float flightTime, int index, float spinPhase)
        {
            if (body == null) return;
            body.gameObject.SetActive(true);
            body.transform.SetPositionAndRotation(position, rotation);
            if (renderer != null)
            {
                renderer.enabled = true;
            }
            body.simulated = true;
            body.velocity = ResolveBallisticVelocity(body, position, landingPoint, flightTime);
            float spin = Mathf.Lerp(angularVelocityRange.x, angularVelocityRange.y, Hash01(index, spinPhase));
            body.angularVelocity = (index & 1) == 0 ? spin : -spin;
        }

        private void ResetDebris()
        {
            ResetPool(candyDebrisBodies, 0f);
            ResetPool(glassShardBodies, 74f);
        }

        private IEnumerator ResetDebrisAfterDelay()
        {
            float fastFlightDuration = Mathf.Min(1.75f, debrisLifetime);
            yield return new WaitForSecondsRealtime(fastFlightDuration);
            SetCollisionMode(candyDebrisBodies, CollisionDetectionMode2D.Discrete);
            SetCollisionMode(glassShardBodies, CollisionDetectionMode2D.Discrete);
            float remainingLifetime = debrisLifetime - fastFlightDuration;
            if (remainingLifetime > 0f) yield return new WaitForSecondsRealtime(remainingLifetime);
            debrisCleanup = null;
            ResetDebris();
        }

        private static void SetCollisionMode(Rigidbody2D[] pool, CollisionDetectionMode2D mode)
        {
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null && pool[i].simulated)
                    pool[i].collisionDetectionMode = mode;
        }

        private void ResetPool(Rigidbody2D[] pool, float phaseDegrees)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == null) continue;
                pool[i].velocity = Vector2.zero;
                pool[i].angularVelocity = 0f;
                pool[i].simulated = false;
                pool[i].collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                Vector2 fallback = deathDebrisOriginBody != null ? deathDebrisOriginBody.worldCenterOfMass : (Vector2)transform.position;
                pool[i].transform.position = ResolvePackedPosition(i, pool.Length, fallback, phaseDegrees);
                pool[i].gameObject.SetActive(false);
            }
        }

        private Vector2 ResolveDebrisOrigin(Vector2 fallback) => deathDebrisOriginBody != null
            ? deathDebrisOriginBody.worldCenterOfMass
            : fallback;

        private Vector2 ResolvePackedPosition(int index, int count, Vector2 origin, float phaseDegrees)
        {
            if (count <= 0) return origin;
            float radius = Mathf.Sqrt((index + .5f) / count);
            float angle = (index * 137.50777f + phaseDegrees) * Mathf.Deg2Rad;
            Vector2 localOffset = new Vector2(
                Mathf.Cos(angle) * torsoPackingSize.x * .5f * radius,
                Mathf.Sin(angle) * torsoPackingSize.y * .5f * radius);
            float bodyAngle = deathDebrisOriginBody != null ? deathDebrisOriginBody.rotation : 0f;
            return origin + (Vector2)(Quaternion.Euler(0f, 0f, bodyAngle) * localOffset);
        }

        private Vector2 ResolveLandingPoint(int index, int count, float phase)
        {
            float centerX = deathDebrisOriginBody != null ? deathDebrisOriginBody.worldCenterOfMass.x : transform.position.x;
            float halfWidth = 3.5f;
            if (gameplayCamera != null && gameplayCamera.orthographic)
            {
                centerX = gameplayCamera.transform.position.x;
                halfWidth = gameplayCamera.orthographicSize * gameplayCamera.aspect;
            }

            float left = centerX - halfWidth + screenEdgePadding;
            float right = centerX + halfWidth - screenEdgePadding;
            if (right < left) right = left;
            float normalizedX = count > 1 ? Hash01(index, phase) : .5f;
            // The center offset keeps the Rigidbody center above the floor collider at touchdown.
            return new Vector2(Mathf.Lerp(left, right, normalizedX), floorWorldY + .14f);
        }

        private static float ResolveFlightTime(int index, Vector2 range, float phase) =>
            Mathf.Lerp(range.x, range.y, Hash01(index, phase));

        private Vector2 ResolveBallisticVelocity(Rigidbody2D body, Vector2 position, Vector2 target, float flightTime)
        {
            float time = Mathf.Max(.1f, flightTime);
            Vector2 displacement = target - position;
            float gravity = Physics2D.gravity.y * body.gravityScale;
            // s = v*t + 0.5*g*t^2, solved for v so each pooled body lands inside the camera view.
            Vector2 velocity = new Vector2(
                displacement.x / time,
                (displacement.y - .5f * gravity * time * time) / time);
            return Vector2.ClampMagnitude(velocity, maximumDebrisSpeed);
        }

        private static float Hash01(int index, float phase) => Mathf.Repeat(index * .61803398875f + phase, 1f);

        private void SetCharacterRenderers(bool restore)
        {
            for (int i = 0; i < characterRenderers.Length; i++)
                if (characterRenderers[i] != null)
                    characterRenderers[i].enabled = restore && i < authoredRendererEnabled.Length
                        ? authoredRendererEnabled[i]
                        : false;
        }
        private Vector2 ResolveCenter()
        {
            if (controller != null && controller.Parts.Count > 0 && controller.Parts[0].Body != null)
                return controller.Parts[0].Body.worldCenterOfMass;
            return transform.position;
        }

        private static void PlayBurst(ParticleSystem system, Vector2 point)
        {
            system.transform.position = point;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play(true);
        }

        private static void Emit(ParticleSystem system, Vector2 point, Color color, float size, int count)
        {
            ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
            {
                position = point,
                startColor = color,
                startSize = size
            };
            system.Emit(parameters, count);
        }

        private void StopParticles()
        {
            StopParticle(hitParticle);
            StopParticle(comboParticle);
            StopParticle(knockoutParticle);
            StopParticle(deathParticle);
            StopParticle(collisionFumeParticle);
        }

        private static void StopParticle(ParticleSystem system)
        {
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnValidate()
        {
            speedForMaximumStrength = Mathf.Max(.01f, speedForMaximumStrength);
            maximumHitParticles = Mathf.Max(minimumHitParticles, maximumHitParticles);
            debrisLifetime = Mathf.Max(1f, debrisLifetime);
            maximumActiveCandyDebris = Mathf.Clamp(maximumActiveCandyDebris, 4, 24);
            maximumActiveGlassShards = Mathf.Clamp(maximumActiveGlassShards, 4, 20);
            torsoPackingSize.x = Mathf.Max(.1f, torsoPackingSize.x);
            torsoPackingSize.y = Mathf.Max(.1f, torsoPackingSize.y);
            screenEdgePadding = Mathf.Max(0f, screenEdgePadding);
            candyFlightTimeRange = SortPositiveRange(candyFlightTimeRange);
            glassFlightTimeRange = SortPositiveRange(glassFlightTimeRange);
            angularVelocityRange = SortPositiveRange(angularVelocityRange);
            maximumDebrisSpeed = Mathf.Max(1f, maximumDebrisSpeed);
        }

        private static Vector2 SortPositiveRange(Vector2 range)
        {
            float minimum = Mathf.Max(.1f, Mathf.Min(range.x, range.y));
            return new Vector2(minimum, Mathf.Max(minimum, Mathf.Max(range.x, range.y)));
        }
    }
}




