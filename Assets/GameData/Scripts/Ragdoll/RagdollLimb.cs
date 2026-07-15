using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Single-purpose collision adapter owned by a ragdoll body.</summary>
    [DisallowMultipleComponent]
    public sealed class RagdollLimb : MonoBehaviour
    {
        private IRagdollCollisionReceiver owner;
        private Rigidbody2D body;

        internal void Initialize(IRagdollCollisionReceiver controller, Rigidbody2D rigidbody)
        {
            owner = controller;
            body = rigidbody;
        }

        private void Awake() { if (body == null) body = GetComponent<Rigidbody2D>(); }
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (owner != null) owner.ReportCollision(body, collision);
        }
    }
}
