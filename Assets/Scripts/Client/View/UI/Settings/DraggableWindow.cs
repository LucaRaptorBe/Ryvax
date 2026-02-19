using UnityEngine;
using UnityEngine.EventSystems;

namespace MOBANet.Client.Settings
{
    /// <summary>
    /// Makes a UI element draggable within its parent canvas bounds.
    /// Attach to the drag handle area of a window.
    /// </summary>
    public class DraggableWindow : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform windowTransform;

        private Vector2 _dragOffset;
        private Canvas _parentCanvas;
        private RectTransform _canvasRect;

        void Awake()
        {
            if (windowTransform == null)
                windowTransform = transform.parent as RectTransform;
        }

        void Start()
        {
            _parentCanvas = GetComponentInParent<Canvas>();
            if (_parentCanvas != null)
                _canvasRect = _parentCanvas.GetComponent<RectTransform>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (windowTransform == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                windowTransform, eventData.position, eventData.pressEventCamera, out _dragOffset);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (windowTransform == null || _canvasRect == null) return;

            Vector2 localPoint;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, eventData.position, eventData.pressEventCamera, out localPoint))
                return;

            windowTransform.localPosition = localPoint - _dragOffset;
            ClampToCanvas();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // Nothing special needed
        }

        private void ClampToCanvas()
        {
            if (_canvasRect == null || windowTransform == null) return;

            Vector3 pos = windowTransform.localPosition;
            Vector2 canvasSize = _canvasRect.rect.size;
            Vector2 windowSize = windowTransform.rect.size;
            Vector2 pivot = windowTransform.pivot;

            float minX = -canvasSize.x * 0.5f + windowSize.x * pivot.x;
            float maxX = canvasSize.x * 0.5f - windowSize.x * (1f - pivot.x);
            float minY = -canvasSize.y * 0.5f + windowSize.y * pivot.y;
            float maxY = canvasSize.y * 0.5f - windowSize.y * (1f - pivot.y);

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            windowTransform.localPosition = pos;
        }
    }
}
