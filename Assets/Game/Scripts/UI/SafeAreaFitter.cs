using UnityEngine;

namespace MonsterLogic.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private Rect _last;
        private Rect _lastCamRect;

        private void OnEnable() => Apply();

        private void Update()
        {
            var cam = Camera.main;
            Rect camRect = cam != null ? cam.rect : new Rect(0f, 0f, 1f, 1f);
            if (_last != Screen.safeArea || _lastCamRect != camRect) Apply();
        }

        private void Apply()
        {
            _last = Screen.safeArea;
            var rect = (RectTransform)transform;
            Vector2 screenMin = _last.position, screenMax = _last.position + _last.size;
            float safeMinX = screenMin.x / Screen.width;
            float safeMaxX = screenMax.x / Screen.width;
            float safeMinY = screenMin.y / Screen.height;
            float safeMaxY = screenMax.y / Screen.height;

            var cam = Camera.main;
            Rect camRect = (cam != null && cam.rect.width > 0f && cam.rect.height > 0f) ? cam.rect : new Rect(0f, 0f, 1f, 1f);
            _lastCamRect = camRect;

            float minX = Mathf.Max(camRect.x, safeMinX);
            float maxX = Mathf.Min(camRect.x + camRect.width, safeMaxX);
            float minY = Mathf.Max(camRect.y, safeMinY);
            float maxY = Mathf.Min(camRect.y + camRect.height, safeMaxY);

            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
