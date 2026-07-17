using System.Collections.Generic;
using UnityEngine;

namespace KickTheBuddy.Ragdoll3D
{
    /// <summary>
    /// Converts sharp container acceleration into small internal gumball impulses and explicitly
    /// sleeps settled contents. Constant gravity is removed from the acceleration measurement so
    /// normal free-fall does not continuously shake the gumballs.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class InternalGumballShaker : MonoBehaviour
    {
        [Header("Authored References")]
        [SerializeField] private Rigidbody containerRigidbody;
        public List<Rigidbody> gumballRigidbodies = new List<Rigidbody>(32);

        [Header("Shake Response")]
        [Min(0f)] public float shakeForceFactor = .06f;
        [Min(0f)] public float accelerationThreshold = 8f;
        [Min(0f)] [SerializeField] private float maximumImpulse = .75f;
        [Range(0f, 1f)] [SerializeField] private float directionRandomness = .35f;
        [Min(0f)] [SerializeField] private float rollTorqueFactor = .2f;

        [Header("Rest And Sleep")]
        [Min(0f)] [SerializeField] private float restSleepDelay = .35f;
        [Min(.02f)] [SerializeField] private float sleepCheckInterval = .12f;
        [Min(0f)] [SerializeField] private float containerRestSpeed = .05f;
        [Min(0f)] [SerializeField] private float containerRestAngularSpeed = .1f;
        [Min(0f)] [SerializeField] private float gumballSleepSpeed = .08f;
        [Min(0f)] [SerializeField] private float gumballSleepAngularSpeed = .2f;

        private Vector3 previousVelocity;
        private float restTime;
        private float nextSleepCheckTime;
        private bool contentsSleeping;

        public float LastMeasuredAcceleration { get; private set; }
        public bool ContentsSleeping => contentsSleeping;

        private void Awake()
        {
            if (containerRigidbody == null) containerRigidbody = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            previousVelocity = containerRigidbody != null ? containerRigidbody.velocity : Vector3.zero;
            LastMeasuredAcceleration = 0f;
            restTime = 0f;
            nextSleepCheckTime = 0f;
            contentsSleeping = false;
        }

        private void FixedUpdate()
        {
            if (containerRigidbody == null) return;

            float fixedDeltaTime = Time.fixedDeltaTime;
            Vector3 currentVelocity = containerRigidbody.velocity;
            Vector3 priorVelocity = previousVelocity;
            previousVelocity = currentVelocity;

            float restSpeedSquared = containerRestSpeed * containerRestSpeed;
            float restAngularSpeedSquared = containerRestAngularSpeed * containerRestAngularSpeed;
            // Requiring both samples to be slow avoids discarding the large deceleration on the
            // frame where a moving container hits the floor and comes to rest.
            bool containerAtRest = containerRigidbody.IsSleeping() ||
                                   (currentVelocity.sqrMagnitude <= restSpeedSquared &&
                                    priorVelocity.sqrMagnitude <= restSpeedSquared &&
                                    containerRigidbody.angularVelocity.sqrMagnitude <= restAngularSpeedSquared);
            if (containerAtRest)
            {
                LastMeasuredAcceleration = 0f;
                restTime += fixedDeltaTime;
                if (contentsSleeping || restTime < restSleepDelay || Time.fixedTime < nextSleepCheckTime) return;

                nextSleepCheckTime = Time.fixedTime + sleepCheckInterval;
                SleepSettledGumballs();
                return;
            }

            restTime = 0f;
            contentsSleeping = false;
            Vector3 measuredAcceleration = (currentVelocity - priorVelocity) / fixedDeltaTime;
            if (containerRigidbody.useGravity && !containerRigidbody.isKinematic)
            {
                Vector3 gravity = UnityEngine.Physics.gravity;
                float gravitySquared = gravity.sqrMagnitude;
                // Remove gravity only when the measured velocity delta actually contains gravity.
                // A supported body moving at constant speed has zero delta and must not report 1g.
                if (gravitySquared > .000001f &&
                    Vector3.Dot(measuredAcceleration, gravity) / gravitySquared > .5f)
                    measuredAcceleration -= gravity;
            }

            float accelerationMagnitude = measuredAcceleration.magnitude;
            LastMeasuredAcceleration = accelerationMagnitude;
            if (accelerationMagnitude > accelerationThreshold)
                ApplyShakeImpulse(measuredAcceleration, accelerationMagnitude, fixedDeltaTime);
        }

        /// <summary>Immediately wakes all valid dynamic gumballs.</summary>
        public void WakeGumballs()
        {
            int count = gumballRigidbodies != null ? gumballRigidbodies.Count : 0;
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = gumballRigidbodies[i];
                if (!CanSimulate(body)) continue;
                body.WakeUp();
            }
            contentsSleeping = false;
            restTime = 0f;
        }

        /// <summary>Forces valid dynamic gumballs to sleep, useful while disabling the character.</summary>
        public void SleepAllGumballs()
        {
            int count = gumballRigidbodies != null ? gumballRigidbodies.Count : 0;
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = gumballRigidbodies[i];
                if (!CanSimulate(body)) continue;
                body.Sleep();
            }
            contentsSleeping = true;
        }

        private void ApplyShakeImpulse(Vector3 acceleration, float accelerationMagnitude, float fixedDeltaTime)
        {
            float excessAcceleration = accelerationMagnitude - accelerationThreshold;
            float impulseMagnitude = Mathf.Min(
                maximumImpulse,
                excessAcceleration * shakeForceFactor * fixedDeltaTime);
            if (impulseMagnitude <= 0f) return;

            Vector3 reactionDirection = -acceleration / accelerationMagnitude;
            int count = gumballRigidbodies != null ? gumballRigidbodies.Count : 0;
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = gumballRigidbodies[i];
                if (!CanSimulate(body)) continue;

                Vector3 randomDirection = Random.insideUnitSphere;
                if (randomDirection.sqrMagnitude < .000001f) randomDirection = Vector3.up;
                else randomDirection.Normalize();

                Vector3 direction = Vector3.LerpUnclamped(
                    reactionDirection,
                    randomDirection,
                    directionRandomness);
                if (direction.sqrMagnitude < .000001f) direction = reactionDirection;
                else direction.Normalize();

                float variation = Random.Range(.8f, 1.2f);
                float variedImpulse = impulseMagnitude * variation;
                body.AddForce(direction * variedImpulse, ForceMode.Impulse);
                if (rollTorqueFactor > 0f)
                    body.AddTorque(randomDirection * (variedImpulse * rollTorqueFactor), ForceMode.Impulse);
            }
        }

        private void SleepSettledGumballs()
        {
            float speedSquared = gumballSleepSpeed * gumballSleepSpeed;
            float angularSpeedSquared = gumballSleepAngularSpeed * gumballSleepAngularSpeed;
            bool allSettled = true;
            int count = gumballRigidbodies != null ? gumballRigidbodies.Count : 0;
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = gumballRigidbodies[i];
                if (!CanSimulate(body) || body.IsSleeping()) continue;

                if (body.velocity.sqrMagnitude <= speedSquared &&
                    body.angularVelocity.sqrMagnitude <= angularSpeedSquared)
                {
                    body.Sleep();
                }
                else
                {
                    allSettled = false;
                }
            }
            contentsSleeping = allSettled;
        }

        private bool CanSimulate(Rigidbody body)
        {
            return body != null && body != containerRigidbody && body.gameObject.activeInHierarchy && !body.isKinematic;
        }

        private void OnValidate()
        {
            shakeForceFactor = Mathf.Max(0f, shakeForceFactor);
            accelerationThreshold = Mathf.Max(0f, accelerationThreshold);
            maximumImpulse = Mathf.Max(0f, maximumImpulse);
            restSleepDelay = Mathf.Max(0f, restSleepDelay);
            sleepCheckInterval = Mathf.Max(.02f, sleepCheckInterval);
            containerRestSpeed = Mathf.Max(0f, containerRestSpeed);
            containerRestAngularSpeed = Mathf.Max(0f, containerRestAngularSpeed);
            gumballSleepSpeed = Mathf.Max(0f, gumballSleepSpeed);
            gumballSleepAngularSpeed = Mathf.Max(0f, gumballSleepAngularSpeed);
        }
    }
}
