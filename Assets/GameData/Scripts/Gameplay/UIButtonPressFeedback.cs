using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KickTheBuddy.Gameplay
{
    /// <summary>
    /// Allocation-free tactile scale feedback for touch UI controls.
    /// Gameplay commands and haptics remain owned by GameplayHUD.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Selectable))]
    public sealed class UIButtonPressFeedback : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField, Range(.85f, 1f)] private float pressedScale = .94f;

        private Vector3 restingScale;
        private Selectable selectable;

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
            restingScale = transform.localScale;
        }

        private void OnEnable()
        {
            if (selectable == null) selectable = GetComponent<Selectable>();
            if (restingScale == Vector3.zero) restingScale = transform.localScale;
            RestoreScale();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (selectable != null && selectable.IsInteractable())
                transform.localScale = restingScale * pressedScale;
        }

        public void OnPointerUp(PointerEventData eventData) => RestoreScale();

        public void OnPointerExit(PointerEventData eventData) => RestoreScale();

        private void OnDisable() => RestoreScale();

        private void RestoreScale()
        {
            if (restingScale != Vector3.zero) transform.localScale = restingScale;
        }
    }
}
