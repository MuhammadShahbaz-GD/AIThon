using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>
    /// Presentation-only liveness. Animates face transforms without applying forces or changing
    /// Rigidbody2D state, so the simulation remains fully passive after player release.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RagdollLifeVisuals : MonoBehaviour
    {
        [SerializeField] private RagdollController controller;
        [SerializeField] private Transform head;
        [Min(0.5f)] [SerializeField] private float minimumBlinkInterval = 2.2f;
        [Min(0.5f)] [SerializeField] private float maximumBlinkInterval = 5.2f;
        [Min(0.03f)] [SerializeField] private float blinkDuration = 0.12f;
        [Range(0f, 0.12f)] [SerializeField] private float gazeDistance = 0.045f;

        private Camera inputCamera;
        private Transform leftEye;
        private Transform rightEye;
        private Transform leftPupil;
        private Transform rightPupil;
        private float nextBlinkTime;
        private float blinkEndTime;
        private bool blinking;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<RagdollController>();
            if (head == null) head = FindChildContaining(transform, "head");
            inputCamera = Camera.main;
            if (head != null) CreateFaceIfNeeded();
            ScheduleBlink();
        }

        private void Update()
        {
            if (head == null || leftEye == null) return;
            bool knockedOut = controller != null && controller.CurrentState == RagdollState.KnockedOut;
            UpdateBlink(knockedOut);
            if (!knockedOut && !blinking) UpdateGaze();
        }

        private void UpdateBlink(bool knockedOut)
        {
            if (knockedOut)
            {
                SetEyeHeight(0.08f);
                return;
            }

            if (!blinking && Time.time >= nextBlinkTime)
            {
                blinking = true;
                blinkEndTime = Time.time + blinkDuration;
            }

            if (blinking)
            {
                float half = blinkDuration * 0.5f;
                float distanceFromMiddle = Mathf.Abs(Time.time - (blinkEndTime - half));
                SetEyeHeight(Mathf.Lerp(0.08f, 0.25f, Mathf.Clamp01(distanceFromMiddle / half)));
                if (Time.time >= blinkEndTime)
                {
                    blinking = false;
                    SetEyeHeight(0.25f);
                    ScheduleBlink();
                }
            }
        }

        private void UpdateGaze()
        {
            if (inputCamera == null) return;
            Vector3 pointer = inputCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2 localDirection = head.InverseTransformDirection(pointer - head.position).normalized;
            Vector3 offset = new Vector3(localDirection.x, localDirection.y, 0f) * gazeDistance;
            leftPupil.localPosition = offset;
            rightPupil.localPosition = offset;
        }

        private void SetEyeHeight(float height)
        {
            leftEye.localScale = new Vector3(0.18f, height, 1f);
            rightEye.localScale = new Vector3(0.18f, height, 1f);
        }

        private void ScheduleBlink()
        {
            nextBlinkTime = Time.time + Random.Range(minimumBlinkInterval, maximumBlinkInterval);
        }

        private void CreateFaceIfNeeded()
        {
            leftEye = head.Find("Life Face/Left Eye");
            if (leftEye != null)
            {
                rightEye = head.Find("Life Face/Right Eye");
                leftPupil = leftEye.Find("Pupil");
                rightPupil = rightEye.Find("Pupil");
                return;
            }

            SpriteRenderer headRenderer = head.GetComponent<SpriteRenderer>();
            if (headRenderer == null || headRenderer.sprite == null) return;
            Transform face = new GameObject("Life Face").transform;
            face.SetParent(head, false);
            leftEye = CreateFeature(face, "Left Eye", new Vector3(-0.18f, 0.10f, -0.01f), new Vector3(0.18f, 0.25f, 1f), Color.white, headRenderer.sprite, 20);
            rightEye = CreateFeature(face, "Right Eye", new Vector3(0.18f, 0.10f, -0.01f), new Vector3(0.18f, 0.25f, 1f), Color.white, headRenderer.sprite, 20);
            leftPupil = CreateFeature(leftEye, "Pupil", Vector3.zero, new Vector3(0.38f, 0.45f, 1f), new Color(0.05f, 0.07f, 0.1f), headRenderer.sprite, 21);
            rightPupil = CreateFeature(rightEye, "Pupil", Vector3.zero, new Vector3(0.38f, 0.45f, 1f), new Color(0.05f, 0.07f, 0.1f), headRenderer.sprite, 21);
        }

        private static Transform CreateFeature(Transform parent, string name, Vector3 position, Vector3 scale, Color color, Sprite sprite, int order)
        {
            GameObject feature = new GameObject(name, typeof(SpriteRenderer));
            feature.transform.SetParent(parent, false);
            feature.transform.localPosition = position;
            feature.transform.localScale = scale;
            SpriteRenderer renderer = feature.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return feature.transform;
        }

        private static Transform FindChildContaining(Transform root, string value)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name.ToLowerInvariant().Contains(value)) return child;
            return null;
        }

        private void OnValidate()
        {
            minimumBlinkInterval = Mathf.Max(0.5f, minimumBlinkInterval);
            maximumBlinkInterval = Mathf.Max(minimumBlinkInterval, maximumBlinkInterval);
            blinkDuration = Mathf.Max(0.03f, blinkDuration);
        }
    }
}
