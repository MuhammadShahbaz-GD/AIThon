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
            if (deathParticle != null) PlayBurst(deathParticle, point);
            ReleaseCandyDebris(point);
            ReleaseGlassShards(point);
            SetCharacterRenderers(false);
            if (debrisCleanup != null) StopCoroutine(debrisCleanup);
            debrisCleanup = StartCoroutine(ResetDebrisAfterDelay());
            DeathEffectPlayed?.Invoke(point);
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
            int partCount = controller != null ? controller.Parts.Count : 0;
            for (int index = 0; index < candyLimit; index++)
            {
                Rigidbody2D partBody = partCount > 0 ? controller.Parts[index % partCount].Body : null;
                Vector2 center = partBody != null ? partBody.worldCenterOfMass : origin;
                ActivateDebris(candyDebrisBodies[index],
                    index < candyDebrisRenderers.Length ? candyDebrisRenderers[index] : null,
                    null, center + UnityEngine.Random.insideUnitCircle * .16f,
                    Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f)),
                    origin, 3f, 7f, 1.8f);
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
            int partCount = controller != null ? controller.Parts.Count : 0;
            for (int index = 0; index < shardLimit; index++)
            {
                Rigidbody2D partBody = partCount > 0 ? controller.Parts[index % partCount].Body : null;
                Vector2 center = partBody != null ? partBody.worldCenterOfMass : origin;
                ActivateDebris(glassShardBodies[index],
                    index < glassShardRenderers.Length ? glassShardRenderers[index] : null,
                    null, center + UnityEngine.Random.insideUnitCircle * .14f,
                    Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f)),
                    origin, 5f, 9f, 2.8f);
            }
        }

        private static void ActivateDebris(Rigidbody2D body, SpriteRenderer renderer, Sprite sprite,
            Vector2 position, Quaternion rotation, Vector2 origin, float minimumForce, float maximumForce, float lift)
        {
            if (body == null) return;
            body.gameObject.SetActive(true);
            body.transform.SetPositionAndRotation(position, rotation);
            if (renderer != null)
            {
                if (sprite != null) renderer.sprite = sprite;
                renderer.enabled = true;
            }
            Collider2D collider = body.GetComponent<Collider2D>();
            if (collider != null) collider.enabled = true;
            body.simulated = true;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            Vector2 direction = position - origin;
            if (direction.sqrMagnitude < .001f) direction = UnityEngine.Random.insideUnitCircle;
            direction = (direction.normalized + Vector2.up * .55f).normalized;
            body.AddForce(direction * UnityEngine.Random.Range(minimumForce, maximumForce) + Vector2.up * lift,
                ForceMode2D.Impulse);
            body.AddTorque(UnityEngine.Random.Range(-1.2f, 1.2f), ForceMode2D.Impulse);
        }

        private void ResetDebris()
        {
            ResetPool(candyDebrisBodies);
            ResetPool(glassShardBodies);
        }

        private IEnumerator ResetDebrisAfterDelay()
        {
            yield return new WaitForSecondsRealtime(debrisLifetime);
            debrisCleanup = null;
            ResetDebris();
        }

        private static void ResetPool(Rigidbody2D[] pool)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == null) continue;
                pool[i].velocity = Vector2.zero;
                pool[i].angularVelocity = 0f;
                pool[i].simulated = false;
                pool[i].gameObject.SetActive(false);
            }
        }

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
        }
    }
}




