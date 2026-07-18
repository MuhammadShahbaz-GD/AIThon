using System;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Releases an authored, pooled handful of loose candy when the draggable jar breaks.</summary>
    [DisallowMultipleComponent]
    public sealed class CandyJarBreakable2D : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D jarBody;
        [SerializeField] private Collider2D jarCollider;
        [SerializeField] private SpriteRenderer jarRenderer;
        [SerializeField] private Rigidbody2D[] containedCandy = Array.Empty<Rigidbody2D>();
        [Min(.1f)] [SerializeField] private float breakImpactSpeed = 8f;
        [Min(0f)] [SerializeField] private float burstImpulse = 2.8f;

        private bool broken;
        public bool IsBroken => broken;
        public event Action<CandyJarBreakable2D, Vector2> Broken;

        private void Awake() => ResetJar();

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (broken || collision.relativeVelocity.magnitude < breakImpactSpeed) return;
            Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point :
                (jarBody != null ? jarBody.worldCenterOfMass : (Vector2)transform.position);
            Break(point);
        }

        public void Break(Vector2 point)
        {
            if (broken) return;
            broken = true;
            Vector2 inheritedVelocity = jarBody != null ? jarBody.velocity : Vector2.zero;
            if (jarCollider != null) jarCollider.enabled = false;
            if (jarRenderer != null) jarRenderer.enabled = false;

            int count = containedCandy.Length;
            for (int i = 0; i < count; i++)
            {
                Rigidbody2D candy = containedCandy[i];
                if (candy == null) continue;
                float angle = count > 0 ? (i / (float)count) * Mathf.PI : 0f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Abs(Mathf.Sin(angle)) + .45f).normalized;
                candy.gameObject.SetActive(true);
                candy.transform.position = point + direction * .08f;
                candy.simulated = true;
                candy.velocity = inheritedVelocity * .35f;
                candy.AddForce(direction * burstImpulse, ForceMode2D.Impulse);
                candy.angularVelocity = (i & 1) == 0 ? 360f : -360f;
            }
            Broken?.Invoke(this, point);
        }

        public void ResetJar()
        {
            broken = false;
            if (jarCollider != null) jarCollider.enabled = true;
            if (jarRenderer != null) jarRenderer.enabled = true;
            for (int i = 0; i < containedCandy.Length; i++)
            {
                Rigidbody2D candy = containedCandy[i];
                if (candy == null) continue;
                candy.velocity = Vector2.zero;
                candy.angularVelocity = 0f;
                candy.simulated = false;
                candy.gameObject.SetActive(false);
            }
        }

        private void OnValidate()
        {
            breakImpactSpeed = Mathf.Max(.1f, breakImpactSpeed);
            burstImpulse = Mathf.Max(0f, burstImpulse);
            if (containedCandy == null) containedCandy = Array.Empty<Rigidbody2D>();
        }
    }
}
