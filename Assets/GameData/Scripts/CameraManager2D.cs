using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class CameraManager2D : MonoBehaviour
{
    [Header("Reference")]
    [Min(0.01f)] public float referenceOrthoSize = 5f;
    [Min(1f)] public float referenceWidth = 1920f;
    [Min(1f)] public float referenceHeight = 1080f;

    private Camera cam;
    private Vector2Int previousViewportSize;
    private Vector3 previousReferenceSettings;

    private void Awake()
    {
        CacheCamera();
        RefreshCamera(true);
    }

    private void OnEnable()
    {
        CacheCamera();
        RefreshCamera(true);
    }

    private void LateUpdate()
    {
        // Keep the framing authoritative in Play Mode without interfering with
        // camera position and rotation effects such as shake.
        RefreshCamera(false);
    }

    private void OnValidate()
    {
        referenceOrthoSize = Mathf.Max(0.01f, referenceOrthoSize);
        referenceWidth = Mathf.Max(1f, referenceWidth);
        referenceHeight = Mathf.Max(1f, referenceHeight);

        CacheCamera();
        RefreshCamera(true);
    }

    private void CacheCamera()
    {
        if (cam == null)
            cam = GetComponent<Camera>();
    }

    private void RefreshCamera(bool force)
    {
        if (cam == null)
            return;

        int viewportWidth = Mathf.Max(1, cam.pixelWidth);
        int viewportHeight = Mathf.Max(1, cam.pixelHeight);
        Vector2Int viewportSize = new Vector2Int(viewportWidth, viewportHeight);
        Vector3 referenceSettings = new Vector3(referenceOrthoSize, referenceWidth, referenceHeight);
        float requiredSize = CalculateOrthographicSize(viewportWidth, viewportHeight);

        bool settingsChanged = previousReferenceSettings != referenceSettings;
        bool viewportChanged = previousViewportSize != viewportSize;
        bool cameraChanged = !cam.orthographic || !Mathf.Approximately(cam.orthographicSize, requiredSize);
        if (!force && !settingsChanged && !viewportChanged && !cameraChanged)
            return;

        cam.orthographic = true;
        cam.orthographicSize = requiredSize;
        previousViewportSize = viewportSize;
        previousReferenceSettings = referenceSettings;
    }

    private float CalculateOrthographicSize(int viewportWidth, int viewportHeight)
    {
        float targetAspect = referenceWidth / referenceHeight;
        float currentAspect = (float)viewportWidth / viewportHeight;

        if (currentAspect >= targetAspect)
            return referenceOrthoSize;

        return referenceOrthoSize * (targetAspect / currentAspect);
    }
}
