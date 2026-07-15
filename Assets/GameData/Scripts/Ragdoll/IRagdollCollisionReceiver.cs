using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Small dependency-inversion boundary between limb callbacks and game logic.</summary>
    internal interface IRagdollCollisionReceiver
    {
        void ReportCollision(Rigidbody2D source, Collision2D collision);
    }
}
