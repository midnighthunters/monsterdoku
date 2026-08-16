using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterLogic.UI
{
    public sealed class CellView : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        public int CellIndex { get; private set; }
        public Image Background { get; private set; }
        public Image EmptyIcon { get; private set; }
        public TMP_Text Mark { get; private set; }
        public Image Monster { get; private set; }
        public TMP_Text RegionSymbol { get; private set; }
        public float BaseFontSize { get; private set; }
        public System.Action<int, bool> Activated;
        public float doubleTapDelay = .24f;
        public float longPressDelay = .55f;
        private Coroutine _singleTap;
        private float _pressedAt;
        private bool _longPressFired;

        public void Configure(int index, Image background, Image emptyIcon, TMP_Text mark, Image monster, TMP_Text regionSymbol)
        { CellIndex = index; Background = background; EmptyIcon = emptyIcon; Mark = mark; Monster = monster; RegionSymbol = regionSymbol; BaseFontSize = mark.fontSize; }

        public void OnPointerDown(PointerEventData eventData) { _pressedAt = Time.unscaledTime; _longPressFired = false; }
        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_longPressFired && Time.unscaledTime - _pressedAt >= longPressDelay)
            { _longPressFired = true; if (_singleTap != null) StopCoroutine(_singleTap); Activated?.Invoke(CellIndex, true); }
        }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_longPressFired) return;
            if (eventData.clickCount >= 2)
            {
                if (_singleTap != null) { StopCoroutine(_singleTap); _singleTap = null; }
                Activated?.Invoke(CellIndex, true);
            }
            else { if (_singleTap != null) StopCoroutine(_singleTap); _singleTap = StartCoroutine(ResolveSingle()); }
        }
        private IEnumerator ResolveSingle() { yield return new WaitForSecondsRealtime(doubleTapDelay); _singleTap = null; Activated?.Invoke(CellIndex, false); }
    }
}
