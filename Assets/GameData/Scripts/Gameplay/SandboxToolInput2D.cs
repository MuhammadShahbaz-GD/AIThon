using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Owns mouse/touch selection for explicitly authored Level 2 sandbox tools.</summary>
    [DisallowMultipleComponent]
    public sealed class SandboxToolInput2D : MonoBehaviour
    {
        [SerializeField] private Camera inputCamera;
        [SerializeField] private SandboxTool2D[] tools = Array.Empty<SandboxTool2D>();
        [SerializeField] private LayerMask toolLayers = ~0;
        [SerializeField] private bool ignorePointerOverUI = true;
        [Header("Tap Recognition")]
        [Min(.01f)] [SerializeField] private float maximumTapWorldDistance = .18f;
        [Min(.05f)] [SerializeField] private float maximumTapDuration = .32f;

        private SandboxTool2D activeTool;
        private int activeFingerId = -1;
        private bool inputEnabled = true;
        private Vector2 pressWorldPoint;
        private float pressTime;

        public IReadOnlyList<SandboxTool2D> Tools => tools;
        public bool InputEnabled => inputEnabled;

        public event Action<SandboxTool2D, Vector2> DragStarted;
        public event Action<SandboxTool2D, Vector2> DragUpdated;
        public event Action<SandboxTool2D, Vector2> DragEnded;

        private void Update()
        {
            if (!inputEnabled || inputCamera == null) return;
            if (Input.touchCount > 0) ReadTouch();
            else ReadMouse();
        }

        private void ReadMouse()
        {
            if (Input.GetMouseButtonDown(0)) TryBegin(Input.mousePosition, -1);
            if (activeTool != null && Input.GetMouseButton(0)) UpdateActive(Input.mousePosition);
            if (activeTool != null && Input.GetMouseButtonUp(0)) EndActive(Input.mousePosition);
        }

        private void ReadTouch()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (activeFingerId >= 0 && touch.fingerId != activeFingerId) continue;
                if (touch.phase == TouchPhase.Began) TryBegin(touch.position, touch.fingerId);
                else if (activeTool != null &&
                         (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary))
                    UpdateActive(touch.position);
                else if (activeTool != null &&
                         (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled))
                    EndActive(touch.position);
            }
        }

        private void TryBegin(Vector2 screenPoint, int fingerId)
        {
            if (ignorePointerOverUI && EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject(fingerId)) return;

            Vector2 world = ScreenToWorld(screenPoint);
            Collider2D hit = Physics2D.OverlapPoint(world, toolLayers);
            SandboxTool2D selected = ResolveTool(hit);
            if (selected == null || !selected.BeginDrag(world)) return;
            activeTool = selected;
            activeFingerId = fingerId;
            pressWorldPoint = world;
            pressTime = Time.unscaledTime;
            DragStarted?.Invoke(selected, world);
        }

        private void UpdateActive(Vector2 screenPoint)
        {
            Vector2 world = ScreenToWorld(screenPoint);
            activeTool.UpdateDragTarget(world);
            DragUpdated?.Invoke(activeTool, world);
        }

        private void EndActive(Vector2 screenPoint)
        {
            Vector2 world = ScreenToWorld(screenPoint);
            SandboxTool2D released = activeTool;
            released.EndDrag();
            bool wasTap = Time.unscaledTime - pressTime <= maximumTapDuration &&
                          (world - pressWorldPoint).sqrMagnitude <=
                          maximumTapWorldDistance * maximumTapWorldDistance;
            activeTool = null;
            activeFingerId = -1;
            DragEnded?.Invoke(released, world);
            if (wasTap) released.NotifyTap(world);
        }

        public void SetInputEnabled(bool value)
        {
            if (inputEnabled == value) return;
            inputEnabled = value;
            if (!inputEnabled) ReleaseActive();
        }

        public void ResetTools()
        {
            ReleaseActive();
            for (int i = 0; i < tools.Length; i++)
                if (tools[i] != null) tools[i].ResetToSpawn();
        }

        private SandboxTool2D ResolveTool(Collider2D hit)
        {
            if (hit == null) return null;
            for (int i = 0; i < tools.Length; i++)
                if (tools[i] != null && tools[i].OwnsCollider(hit)) return tools[i];
            return null;
        }

        private Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 world = inputCamera.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, -inputCamera.transform.position.z));
            return new Vector2(world.x, world.y);
        }

        private void ReleaseActive()
        {
            if (activeTool != null) activeTool.EndDrag();
            activeTool = null;
            activeFingerId = -1;
        }

        private void OnDisable() => ReleaseActive();

        private void OnValidate()
        {
            maximumTapWorldDistance = Mathf.Max(.01f, maximumTapWorldDistance);
            maximumTapDuration = Mathf.Max(.05f, maximumTapDuration);
            if (tools == null) tools = Array.Empty<SandboxTool2D>();
        }
    }
}
