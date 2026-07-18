using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraManager2D : MonoBehaviour
{
    public enum FramingMode
    {
        ShowEntireReference,
        FitWidth,
        FitHeight
    }

    [Header("Reference Composition")]
    [Tooltip("Orthographic size used at the authored reference resolution.")]
    [Min(0.01f)] public float referenceOrthoSize = 5f;
    [Min(1f)] public float referenceWidth = 1920f;
    [Min(1f)] public float referenceHeight = 1080f;

    [Header("Responsive Framing")]
    [Tooltip("Show Entire Reference prevents gameplay cropping on every aspect ratio.")]
    [SerializeField] private FramingMode framingMode = FramingMode.ShowEntireReference;
    [Tooltip("Adds a small border around the authored gameplay area. Zero keeps the exact composition.")]
    [Range(0f, .25f)] [SerializeField] private float overscan;
    [Tooltip("Frames gameplay inside the usable display area on phones with notches or rounded corners.")]
    [SerializeField] private bool accountForSafeArea = true;

    private Camera cam;
    private Vector2Int previousViewportSize;
    private Rect previousSafeArea;
    private Vector4 previousSettings;
    private FramingMode previousFramingMode;
    private bool previousSafeAreaSetting;

    public FramingMode CurrentFramingMode => framingMode;
    public float CurrentOrthographicSize => cam != null ? cam.orthographicSize : referenceOrthoSize;

    private void Awake()
    {
        CacheCamera();
        RefreshNow();
    }

    private void OnEnable()
    {
        CacheCamera();
        RefreshNow();
    }

    private void LateUpdate()
    {
        // Resolution, orientation, split-screen viewport, and Play Mode Inspector
        // changes can occur at runtime, so framing is checked after normal updates.
        RefreshCamera(false);
    }

    private void OnValidate()
    {
        referenceOrthoSize = Mathf.Max(0.01f, referenceOrthoSize);
        referenceWidth = Mathf.Max(1f, referenceWidth);
        referenceHeight = Mathf.Max(1f, referenceHeight);
        overscan = Mathf.Clamp(overscan, 0f, .25f);

        CacheCamera();
        RefreshNow();
    }

    /// <summary>Immediately reapplies responsive framing using the current viewport.</summary>
    public void RefreshNow()
    {
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

        Rect safeArea = Screen.safeArea;
        Vector2Int viewportSize = GetEffectiveViewportSize(safeArea);
        Vector4 settings = new Vector4(referenceOrthoSize, referenceWidth, referenceHeight, overscan);
        float requiredSize = CalculateOrthographicSize(viewportSize.x, viewportSize.y);

        bool settingsChanged = previousSettings != settings ||
                               previousFramingMode != framingMode ||
                               previousSafeAreaSetting != accountForSafeArea;
        bool viewportChanged = previousViewportSize != viewportSize || previousSafeArea != safeArea;
        bool cameraChanged = !cam.orthographic || !Mathf.Approximately(cam.orthographicSize, requiredSize);
        if (!force && !settingsChanged && !viewportChanged && !cameraChanged)
            return;

        cam.orthographic = true;
        cam.orthographicSize = requiredSize;
        previousViewportSize = viewportSize;
        previousSafeArea = safeArea;
        previousSettings = settings;
        previousFramingMode = framingMode;
        previousSafeAreaSetting = accountForSafeArea;
    }

    private Vector2Int GetEffectiveViewportSize(Rect safeArea)
    {
        int viewportWidth = Mathf.Max(1, cam.pixelWidth);
        int viewportHeight = Mathf.Max(1, cam.pixelHeight);

        bool usesScreenViewport = cam.targetTexture == null &&
                                  Mathf.Approximately(cam.rect.x, 0f) &&
                                  Mathf.Approximately(cam.rect.y, 0f) &&
                                  Mathf.Approximately(cam.rect.width, 1f) &&
                                  Mathf.Approximately(cam.rect.height, 1f);
        if (!accountForSafeArea || !usesScreenViewport || Screen.width <= 0 || Screen.height <= 0)
            return new Vector2Int(viewportWidth, viewportHeight);

        float widthScale = viewportWidth / (float)Screen.width;
        float heightScale = viewportHeight / (float)Screen.height;
        int safeWidth = Mathf.Max(1, Mathf.RoundToInt(safeArea.width * widthScale));
        int safeHeight = Mathf.Max(1, Mathf.RoundToInt(safeArea.height * heightScale));
        return new Vector2Int(safeWidth, safeHeight);
    }

    internal float CalculateOrthographicSize(int viewportWidth, int viewportHeight)
    {
        viewportWidth = Mathf.Max(1, viewportWidth);
        viewportHeight = Mathf.Max(1, viewportHeight);

        float referenceAspect = referenceWidth / referenceHeight;
        float viewportAspect = viewportWidth / (float)viewportHeight;
        float fitHeightSize = referenceOrthoSize;
        float fitWidthSize = referenceOrthoSize * (referenceAspect / viewportAspect);

        float size;
        switch (framingMode)
        {
            case FramingMode.FitWidth:
                size = fitWidthSize;
                break;
            case FramingMode.FitHeight:
                size = fitHeightSize;
                break;
            default:
                size = Mathf.Max(fitHeightSize, fitWidthSize);
                break;
        }

        return size * (1f + overscan);
    }
}
