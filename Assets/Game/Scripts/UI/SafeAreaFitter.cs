using UnityEngine;

namespace MonsterLogic.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private Rect _last;
        private void OnEnable() => Apply();
        private void Update() { if (_last != Screen.safeArea) Apply(); }
        private void Apply()
        {
            _last = Screen.safeArea; var rect = (RectTransform)transform;
            Vector2 min = _last.position, max = _last.position + _last.size;
            min.x /= Screen.width; min.y /= Screen.height; max.x /= Screen.width; max.y /= Screen.height;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
