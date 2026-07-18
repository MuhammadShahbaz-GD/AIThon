using System;
using KickTheBuddy.Physics;
using KickTheBuddy.VFX;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KickTheBuddy.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class LevelFourPipeController2D : MonoBehaviour
    {
        [Serializable]
        private sealed class PipeStream
        {
            [SerializeField] private Collider2D buttonCollider;
            [SerializeField] private SpriteRenderer buttonRenderer;
            [SerializeField] private Transform muzzle;
            [SerializeField] private LevelFourPipeProjectile2D[] pool = Array.Empty<LevelFourPipeProjectile2D>();
            [SerializeField] private Color offColor = new Color(.65f, .65f, .65f, 1f);
            [SerializeField] private Color onColor = Color.white;
            [NonSerialized] public bool Active;
            [NonSerialized] public float NextShotTime;

            public Collider2D ButtonCollider => buttonCollider;
            public Transform Muzzle => muzzle;
            public LevelFourPipeProjectile2D[] Pool => pool;
            public void RefreshButton() { if (buttonRenderer != null) buttonRenderer.color = Active ? onColor : offColor; }
        }

        [SerializeField] private Camera inputCamera;
        [SerializeField] private RagdollController ragdoll;
        [SerializeField] private LevelFourPipeVFXController2D pipeVfx;
        [SerializeField] private PipeStream chocolateBombs = new PipeStream();
        [SerializeField] private PipeStream sodaCans = new PipeStream();
        [Header("Stream Timing")]
        [Min(.08f)] [SerializeField] private float bombInterval = .45f;
        [Min(.08f)] [SerializeField] private float sodaInterval = .3f;
        [Min(1f)] [SerializeField] private float projectileLifetime = 3f;
        [Header("Chocolate Bomb - High Damage + Blast")]
        [Min(1f)] [SerializeField] private float bombSpeed = 16f;
        [Min(0f)] [SerializeField] private float bombBaseDamage = 150f;
        [Min(0f)] [SerializeField] private float bombDamagePerSpeed = 0f;
        [Min(0f)] [SerializeField] private float bombMaximumDamage = 150f;
        [Min(0f)] [SerializeField] private float bombBlastDamage = 65f;
        [Min(0f)] [SerializeField] private float bombImpactImpulse = 28f;
        [Min(0f)] [SerializeField] private float bombWholeBodyVelocity = 9f;
        [Header("Soda Can - Medium Damage + Push")]
        [Min(1f)] [SerializeField] private float sodaSpeed = 14f;
        [Min(0f)] [SerializeField] private float sodaBaseDamage = 75f;
        [Min(0f)] [SerializeField] private float sodaDamagePerSpeed = 0f;
        [Min(0f)] [SerializeField] private float sodaMaximumDamage = 75f;
        [Min(0f)] [SerializeField] private float sodaImpactImpulse = 16f;
        [Min(0f)] [SerializeField] private float sodaWholeBodyVelocity = 5f;

        private bool inputEnabled;

        public event Action<bool, Vector2> ProjectileFired;
        public event Action<bool, Vector2> ProjectileImpacted;

        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
            if (!value)
            {
                chocolateBombs.Active = sodaCans.Active = false;
                chocolateBombs.RefreshButton();
                sodaCans.RefreshButton();
            }
        }

        public void ResetController() => ResetStreams();

        private void OnEnable()
        {
            inputEnabled = true;
            if (ragdoll != null) ragdoll.OnCharacterDied += HandleDeath;
            ResetStreams();
        }

        private void OnDisable()
        {
            if (ragdoll != null) ragdoll.OnCharacterDied -= HandleDeath;
            inputEnabled = false;
            RecycleAll();
        }

        private void Update()
        {
            if (!inputEnabled || Time.timeScale <= 0f || inputCamera == null) return;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                    if (Input.GetTouch(i).phase == TouchPhase.Began)
                        TryToggle(Input.GetTouch(i).position, Input.GetTouch(i).fingerId);
            }
            else if (Input.GetMouseButtonDown(0)) TryToggle(Input.mousePosition, -1);
        }

        private void FixedUpdate()
        {
            if (!inputEnabled || ragdoll == null || ragdoll.CurrentHealth <= 0f) return;
            TryFire(chocolateBombs, bombInterval, true);
            TryFire(sodaCans, sodaInterval, false);
        }

        private void TryToggle(Vector2 screen, int pointerId)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId)) return;
            Vector3 world3 = inputCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -inputCamera.transform.position.z));
            Vector2 world = world3;
            if (chocolateBombs.ButtonCollider != null && chocolateBombs.ButtonCollider.OverlapPoint(world))
                Toggle(chocolateBombs);
            else if (sodaCans.ButtonCollider != null && sodaCans.ButtonCollider.OverlapPoint(world))
                Toggle(sodaCans);
        }

        private static void Toggle(PipeStream stream)
        {
            stream.Active = !stream.Active;
            stream.NextShotTime = 0f;
            stream.RefreshButton();
        }

        private void TryFire(PipeStream stream, float interval, bool bomb)
        {
            if (!stream.Active || stream.Muzzle == null || Time.time < stream.NextShotTime) return;
            LevelFourPipeProjectile2D projectile = FindAvailable(stream.Pool);
            if (projectile == null) projectile = FindOldest(stream.Pool);
            if (projectile == null) return;
            projectile.Recycle();

            Rigidbody2D target = ResolveRandomTarget();
            Vector2 origin = stream.Muzzle.position;
            Vector2 targetPoint = target != null
                ? target.worldCenterOfMass + target.velocity * .12f
                : origin + (stream == chocolateBombs ? Vector2.right : Vector2.left) * 5f;
            Vector2 direction = targetPoint - origin;
            if (direction.sqrMagnitude < .0001f) direction = Vector2.down;
            direction.Normalize();
            pipeVfx?.PlayMuzzle(bomb, origin, direction);
            ProjectileFired?.Invoke(bomb, origin);
            if (bomb)
                projectile.Launch(origin, direction * bombSpeed, projectileLifetime, true,
                    bombBaseDamage, bombDamagePerSpeed, bombMaximumDamage, bombBlastDamage,
                    bombImpactImpulse, bombWholeBodyVelocity, pipeVfx, RecycleProjectile,
                    ReportProjectileImpact);
            else
                projectile.Launch(origin, direction * sodaSpeed, projectileLifetime, false,
                    sodaBaseDamage, sodaDamagePerSpeed, sodaMaximumDamage, 0f,
                    sodaImpactImpulse, sodaWholeBodyVelocity, pipeVfx, RecycleProjectile,
                    ReportProjectileImpact);
            stream.NextShotTime = Time.time + interval;
        }

        private Rigidbody2D ResolveRandomTarget()
        {
            if (ragdoll == null || ragdoll.Parts.Count == 0) return null;
            int start = UnityEngine.Random.Range(0, ragdoll.Parts.Count);
            for (int i = 0; i < ragdoll.Parts.Count; i++)
            {
                Rigidbody2D body = ragdoll.Parts[(start + i) % ragdoll.Parts.Count]?.Body;
                if (body != null && body.simulated) return body;
            }
            return null;
        }

        private static LevelFourPipeProjectile2D FindAvailable(LevelFourPipeProjectile2D[] pool)
        {
            for (int i = 0; i < pool.Length; i++) if (pool[i] != null && !pool[i].ActiveShot) return pool[i];
            return null;
        }

        private static LevelFourPipeProjectile2D FindOldest(LevelFourPipeProjectile2D[] pool)
        {
            LevelFourPipeProjectile2D oldest = null;
            float least = float.PositiveInfinity;
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null && pool[i].RemainingLifetime < least)
                { oldest = pool[i]; least = pool[i].RemainingLifetime; }
            return oldest;
        }

        private static void RecycleProjectile(LevelFourPipeProjectile2D projectile) => projectile?.Recycle();

        private void ReportProjectileImpact(bool bomb, Vector2 point) =>
            ProjectileImpacted?.Invoke(bomb, point);

        private void ResetStreams()
        {
            chocolateBombs.Active = sodaCans.Active = false;
            chocolateBombs.NextShotTime = sodaCans.NextShotTime = 0f;
            chocolateBombs.RefreshButton();
            sodaCans.RefreshButton();
            RecycleAll();
            pipeVfx?.ResetVFX();
        }

        private void RecycleAll()
        {
            RecyclePool(chocolateBombs.Pool);
            RecyclePool(sodaCans.Pool);
        }

        private static void RecyclePool(LevelFourPipeProjectile2D[] pool)
        {
            for (int i = 0; i < pool.Length; i++) pool[i]?.Recycle();
        }

        private void HandleDeath(Vector2 point)
        {
            inputEnabled = false;
            chocolateBombs.Active = sodaCans.Active = false;
            chocolateBombs.RefreshButton();
            sodaCans.RefreshButton();
        }
    }
}
