using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KickTheBuddy.Physics
{
    /// <summary>Owns all mouse/touch sampling and selects one draggable ragdoll part at a time.</summary>
    [DisallowMultipleComponent]
    public sealed class RagdollInputManager : MonoBehaviour
    {
        [SerializeField] private Camera inputCamera;
        [SerializeField] private LayerMask draggableLayers = ~0;
        [SerializeField] private bool ignorePointerOverUI = true;
        private DamageReceiver2D activeReceiver;
        private int activeFingerId = -1;
        private bool inputEnabled = true;
        public event Action<DamageReceiver2D, Vector2> DragStarted;
        public event Action<DamageReceiver2D, Vector2> DragUpdated;
        public event Action<DamageReceiver2D, Vector2> DragEnded;
        private void Awake() { if (inputCamera == null) inputCamera = Camera.main; }
        private void Update()
        {
            if (!inputEnabled || inputCamera == null) return;
            if (Input.touchCount > 0) ReadTouch(); else ReadMouse();
        }
        private void ReadMouse()
        {
            if (Input.GetMouseButtonDown(0)) TryBegin(Input.mousePosition, -1);
            if (activeReceiver != null && Input.GetMouseButton(0)) UpdateActive(Input.mousePosition);
            if (activeReceiver != null && Input.GetMouseButtonUp(0)) EndActive(Input.mousePosition);
        }
        private void ReadTouch()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i); if (activeFingerId >= 0 && touch.fingerId != activeFingerId) continue;
                if (touch.phase == TouchPhase.Began) TryBegin(touch.position, touch.fingerId);
                else if (activeReceiver != null && (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)) UpdateActive(touch.position);
                else if (activeReceiver != null && (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)) EndActive(touch.position);
            }
        }
        private void TryBegin(Vector2 screenPoint, int fingerId)
        {
            if (ignorePointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(fingerId)) return;
            Vector2 world = ScreenToWorld(screenPoint); Collider2D hit = Physics2D.OverlapPoint(world, draggableLayers);
            DamageReceiver2D receiver = hit != null ? hit.GetComponentInParent<DamageReceiver2D>() : null;
            if (receiver == null || !receiver.BeginDrag(world)) return; activeReceiver = receiver; activeFingerId = fingerId; DragStarted?.Invoke(receiver, world);
        }
        private void UpdateActive(Vector2 screenPoint) { Vector2 world = ScreenToWorld(screenPoint); activeReceiver.UpdateDragTarget(world); DragUpdated?.Invoke(activeReceiver, world); }
        private void EndActive(Vector2 screenPoint) { Vector2 world = ScreenToWorld(screenPoint); DamageReceiver2D released = activeReceiver; released.EndDrag(); activeReceiver = null; activeFingerId = -1; DragEnded?.Invoke(released, world); }
        private Vector2 ScreenToWorld(Vector2 screen) { Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -inputCamera.transform.position.z)); return new Vector2(world.x, world.y); }
        /// <summary>Enables or disables gameplay input and safely releases any active grabbed limb.</summary>
        public void SetInputEnabled(bool value)
        {
            if (inputEnabled == value) return;
            inputEnabled = value;
            if (!inputEnabled && activeReceiver != null) activeReceiver.EndDrag();
            activeReceiver = null; activeFingerId = -1;
        }
        public bool InputEnabled => inputEnabled;
        private void OnDisable() { if (activeReceiver != null) activeReceiver.EndDrag(); activeReceiver = null; activeFingerId = -1; }
    }
}
