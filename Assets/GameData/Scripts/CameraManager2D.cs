using Unity.VisualScripting;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class CameraManager2D : MonoBehaviour
{
    [Header("Reference")]
    public float referenceOrthoSize = 5f;    // the orthographic size that fits your game
    public float referenceWidth = 1920f;     // reference screen width
    public float referenceHeight = 1080f;    // reference screen height

    private Camera cam;
    private Vector2 previousScreen;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;
        AdjustCamera();
    }

    void AdjustCamera()
    {
        float targetAspect = referenceWidth / referenceHeight;
        float currentAspect = (float)Screen.width / Screen.height;

        // Scale camera orthographic size based on width or height
        if (currentAspect >= targetAspect)
        {
            // Screen is wider than reference → fit height
            cam.orthographicSize = referenceOrthoSize;
        }
        else
        {
            // Screen is taller → increase vertical size to fit width
            cam.orthographicSize = referenceOrthoSize * (targetAspect / currentAspect);
        }
    }
}
