using UnityEngine;

namespace MonsterLogic.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class BannerAwareContentLayout : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float separationPixels = 16f;

        private Canvas _canvas;
        private RectTransform _rect;
        private float _bannerHeightPixels;
        private int _lastWidth;
        private int _lastHeight;
        private Rect _lastSafeArea;
        private float _lastScaleFactor;

        public float BannerHeightPixels => _bannerHeightPixels;

        public void Configure(Canvas canvas)
        {
            _canvas = canvas;
            _rect = (RectTransform)transform;
            Apply();
        }

        public void SetBannerHeightPixels(float pixels)
        {
            pixels = Mathf.Max(0f, pixels);
            if (Mathf.Approximately(_bannerHeightPixels, pixels)) return;
            _bannerHeightPixels = pixels;
            Apply();
        }

        private void OnEnable()
        {
            _rect = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            float scale = _canvas == null ? 1f : Mathf.Max(.0001f, _canvas.scaleFactor);
            if (_lastWidth != Screen.width || _lastHeight != Screen.height || _lastSafeArea != Screen.safeArea || !Mathf.Approximately(_lastScaleFactor, scale))
                Apply();
        }

        private void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            float scale = _canvas == null ? 1f : Mathf.Max(.0001f, _canvas.scaleFactor);
            float reservedPixels = _bannerHeightPixels > 0f ? _bannerHeightPixels + separationPixels : 0f;
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = new Vector2(0f, reservedPixels / scale);
            _rect.offsetMax = Vector2.zero;
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            _lastSafeArea = Screen.safeArea;
            _lastScaleFactor = scale;
        }
    }
}
