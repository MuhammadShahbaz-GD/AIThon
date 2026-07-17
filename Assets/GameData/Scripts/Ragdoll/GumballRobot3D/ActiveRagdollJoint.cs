using UnityEngine;

namespace KickTheBuddy.Ragdoll3D
{
    /// <summary>
    /// Drives one physical bone toward the relative pose of its matching animated bone.
    /// Set both angular drive modes on the ConfigurableJoint; this component only supplies
    /// target rotation and runtime strength changes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(ConfigurableJoint))]
    public sealed class ActiveRagdollJoint : MonoBehaviour
    {
        [Header("Animated Pose Source")]
        [Tooltip("Matching bone in the animated, non-physics skeleton.")]
        public Transform targetBone;

        [Header("Drive")]
        [Tooltip("Maximum torque available to each angular drive after SetJointStrength is called.")]
        [Min(0f)] [SerializeField] private float maximumDriveForce = 10000f;

        private Transform cachedTransform;
        private Rigidbody boneRigidbody;
        private ConfigurableJoint configurableJoint;
        private Quaternion initialRotation;
        private Quaternion initialWorldRotation;
        private Quaternion initialTargetLocalRotation;
        private Quaternion initialTargetWorldRotation;
        private Quaternion jointSpace;
        private Quaternion inverseJointSpace;
        private bool initialized;

        public Quaternion InitialRotation => initialRotation;
        public float CurrentSpring { get; private set; }
        public float CurrentDamper { get; private set; }

        private void Awake()
        {
            cachedTransform = transform;
            boneRigidbody = GetComponent<Rigidbody>();
            configurableJoint = GetComponent<ConfigurableJoint>();
            CacheJointSpace();
        }

        private void Start()
        {
            InitializePose();
        }

        private void FixedUpdate()
        {
            if (!initialized || targetBone == null) return;

            Quaternion desiredRotation;
            Quaternion driveRotation;

            if (configurableJoint.configuredInWorldSpace)
            {
                // The target's world-space delta includes animation inherited from its parents.
                Quaternion targetDelta = targetBone.rotation * Quaternion.Inverse(initialTargetWorldRotation);
                desiredRotation = targetDelta * initialWorldRotation;
                driveRotation = inverseJointSpace *
                                (initialWorldRotation * Quaternion.Inverse(desiredRotation)) *
                                jointSpace;
            }
            else
            {
                // Apply the animated local delta to the physical bone's authored rest rotation.
                Quaternion targetDelta = targetBone.localRotation * Quaternion.Inverse(initialTargetLocalRotation);
                desiredRotation = targetDelta * initialRotation;
                driveRotation = inverseJointSpace *
                                (Quaternion.Inverse(desiredRotation) * initialRotation) *
                                jointSpace;
            }

            configurableJoint.targetRotation = driveRotation;
        }

        /// <summary>
        /// Updates every angular drive without replacing joint limits or motion constraints.
        /// Use zero spring/damper for a limp body, and restore authored values to stiffen it.
        /// </summary>
        public void SetJointStrength(float spring, float damper)
        {
            if (configurableJoint == null) configurableJoint = GetComponent<ConfigurableJoint>();
            if (configurableJoint == null) return;

            CurrentSpring = Mathf.Max(0f, spring);
            CurrentDamper = Mathf.Max(0f, damper);

            JointDrive xDrive = configurableJoint.angularXDrive;
            SetDriveValues(ref xDrive);
            configurableJoint.angularXDrive = xDrive;

            JointDrive yzDrive = configurableJoint.angularYZDrive;
            SetDriveValues(ref yzDrive);
            configurableJoint.angularYZDrive = yzDrive;

            JointDrive slerpDrive = configurableJoint.slerpDrive;
            SetDriveValues(ref slerpDrive);
            configurableJoint.slerpDrive = slerpDrive;
        }

        /// <summary>Re-caches rest rotations after replacing or deliberately reposing the rig.</summary>
        public void ReinitializePose()
        {
            CacheJointSpace();
            InitializePose();
        }

        private void InitializePose()
        {
            initialized = false;
            if (cachedTransform == null) cachedTransform = transform;
            if (boneRigidbody == null) boneRigidbody = GetComponent<Rigidbody>();
            if (configurableJoint == null) configurableJoint = GetComponent<ConfigurableJoint>();
            if (targetBone == null || boneRigidbody == null || configurableJoint == null) return;

            Rigidbody connectedBody = configurableJoint.connectedBody;
            initialRotation = connectedBody != null
                ? Quaternion.Inverse(connectedBody.rotation) * boneRigidbody.rotation
                : boneRigidbody.rotation;
            initialWorldRotation = cachedTransform.rotation;
            initialTargetLocalRotation = targetBone.localRotation;
            initialTargetWorldRotation = targetBone.rotation;
            initialized = true;
        }

        private void CacheJointSpace()
        {
            if (configurableJoint == null) return;

            Vector3 right = configurableJoint.axis;
            if (right.sqrMagnitude < .000001f) right = Vector3.right;
            right.Normalize();

            Vector3 upReference = configurableJoint.secondaryAxis;
            if (upReference.sqrMagnitude < .000001f) upReference = Vector3.up;
            upReference.Normalize();

            Vector3 forward = Vector3.Cross(right, upReference);
            if (forward.sqrMagnitude < .000001f)
            {
                upReference = Mathf.Abs(Vector3.Dot(right, Vector3.up)) < .99f
                    ? Vector3.up
                    : Vector3.forward;
                forward = Vector3.Cross(right, upReference);
            }
            forward.Normalize();
            Vector3 up = Vector3.Cross(forward, right).normalized;

            jointSpace = Quaternion.LookRotation(forward, up);
            inverseJointSpace = Quaternion.Inverse(jointSpace);
        }

        private void SetDriveValues(ref JointDrive drive)
        {
            drive.positionSpring = CurrentSpring;
            drive.positionDamper = CurrentDamper;
            drive.maximumForce = maximumDriveForce;
        }

        private void OnValidate()
        {
            maximumDriveForce = Mathf.Max(0f, maximumDriveForce);
        }
    }
}
