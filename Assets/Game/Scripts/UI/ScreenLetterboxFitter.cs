using UnityEngine;
using UnityEngine.UI;

namespace MonsterLogic.UI
{
    /// <summary>
    /// Ensures that on iPad and screens wider than the target iPhone portrait aspect ratio (9:16),
    /// the game maintains the exact iPhone screen size without stretching, pillarboxing with black bars
    /// on the sides.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ScreenLetterboxFitter : MonoBehaviour
    {
        [Header("Target Aspect Ratio (iPhone 9:16)")]
        [SerializeField] private float targetWidth = 1080f;
        [SerializeField] private float targetHeight = 1920f;
        [SerializeField] private bool letterboxTallerScreens = false;

        [Header("Scene References")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Canvas canvas;
        [SerializeField] private CanvasScaler canvasScaler;

        private Camera _backgroundCamera;
        private int _lastScreenWidth = -1;
        private int _lastScreenHeight = -1;
        private Rect _lastCameraRect = Rect.zero;

        public float TargetAspectRatio => targetHeight > 0f ? targetWidth / targetHeight : 9f / 16f;
        public Camera BackgroundCamera => _backgroundCamera;
        public Camera MainCamera => mainCamera;

        public static ScreenLetterboxFitter Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            ResolveReferences();
            EnsureBackgroundCamera();
            Apply();
        }

        private void OnEnable()
        {
            Instance = this;
            ResolveReferences();
            EnsureBackgroundCamera();
            Apply();
        }

        private void Update()
        {
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                Apply();
            }
        }

        private void LateUpdate()
        {
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                Apply();
            }
        }

        public void Configure(Camera cam, Canvas targetCanvas)
        {
            if (cam != null) mainCamera = cam;
            if (targetCanvas != null)
            {
                canvas = targetCanvas;
                canvasScaler = targetCanvas.GetComponent<CanvasScaler>();
            }
            ResolveReferences();
            EnsureBackgroundCamera();
            Apply();
        }

        public void Apply()
        {
            ResolveReferences();
            if (mainCamera == null) return;

            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            if (screenWidth <= 0 || screenHeight <= 0) return;

            float targetAspect = TargetAspectRatio;
            float screenAspect = (float)screenWidth / screenHeight;

            EnsureBackgroundCamera();

            if (screenAspect > targetAspect + 0.001f)
            {
                // Screen is wider than target iPhone (e.g. iPad 4:3 / 3:4, landscape)
                // Pillarbox: Keep full screen height, constrain width, black bars on sides
                float insetWidth = targetAspect / screenAspect;
                float insetX = (1f - insetWidth) * 0.5f;
                mainCamera.rect = new Rect(insetX, 0f, insetWidth, 1f);

                if (canvasScaler != null)
                {
                    canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    canvasScaler.referenceResolution = new Vector2(targetWidth, targetHeight);
                    canvasScaler.matchWidthOrHeight = 1f; // Match height (1920) so logical width is exactly 1080
                }

                if (_backgroundCamera != null)
                {
                    _backgroundCamera.gameObject.SetActive(true);
                }
            }
            else if (letterboxTallerScreens && screenAspect < targetAspect - 0.001f)
            {
                // Screen is taller than target iPhone
                float insetHeight = screenAspect / targetAspect;
                float insetY = (1f - insetHeight) * 0.5f;
                mainCamera.rect = new Rect(0f, insetY, 1f, insetHeight);

                if (canvasScaler != null)
                {
                    canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    canvasScaler.referenceResolution = new Vector2(targetWidth, targetHeight);
                    canvasScaler.matchWidthOrHeight = 0f; // Match width (1080)
                }

                if (_backgroundCamera != null)
                {
                    _backgroundCamera.gameObject.SetActive(true);
                }
            }
            else
            {
                // Screen aspect matches target iPhone (e.g. 9:16), or taller screen without letterbox
                mainCamera.rect = new Rect(0f, 0f, 1f, 1f);

                if (canvasScaler != null)
                {
                    canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    canvasScaler.referenceResolution = new Vector2(targetWidth, targetHeight);
                    canvasScaler.matchWidthOrHeight = 0f; // Match width (1080) so elements don't get clipped horizontally
                }

                if (_backgroundCamera != null)
                {
                    _backgroundCamera.gameObject.SetActive(false);
                }
            }

            _lastScreenWidth = screenWidth;
            _lastScreenHeight = screenHeight;
            _lastCameraRect = mainCamera.rect;
        }

        private void ResolveReferences()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) mainCamera = FindFirstObjectByType<Camera>();

            if (canvas == null)
            {
                var canvasGo = GameObject.Find("MonsterLogicCanvas");
                if (canvasGo != null) canvas = canvasGo.GetComponent<Canvas>();
                if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
            }

            if (canvas != null && canvasScaler == null)
            {
                canvasScaler = canvas.GetComponent<CanvasScaler>();
            }
        }

        private void EnsureBackgroundCamera()
        {
            if (_backgroundCamera != null) return;

            var existing = GameObject.Find("LetterboxBackgroundCamera");
            if (existing != null)
            {
                _backgroundCamera = existing.GetComponent<Camera>();
                if (_backgroundCamera != null) return;
            }

            var go = new GameObject("LetterboxBackgroundCamera");
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(go);
            }
            else
            {
                go.hideFlags = HideFlags.HideAndDontSave;
            }

            _backgroundCamera = go.AddComponent<Camera>();
            _backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
            _backgroundCamera.backgroundColor = Color.black;
            _backgroundCamera.cullingMask = 0; // Nothing
            _backgroundCamera.depth = (mainCamera != null ? mainCamera.depth : -1f) - 100f; // Render before main camera
            _backgroundCamera.rect = new Rect(0f, 0f, 1f, 1f); // Full screen
            _backgroundCamera.allowHDR = false;
            _backgroundCamera.allowMSAA = false;

            var listener = go.GetComponent<AudioListener>();
            if (listener != null)
            {
                if (Application.isPlaying) Destroy(listener);
                else DestroyImmediate(listener);
            }
        }

        private void OnDisable()
        {
            if (!Application.isPlaying && _backgroundCamera != null)
            {
                DestroyImmediate(_backgroundCamera.gameObject);
                _backgroundCamera = null;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_backgroundCamera != null)
            {
                if (Application.isPlaying) Destroy(_backgroundCamera.gameObject);
                else DestroyImmediate(_backgroundCamera.gameObject);
                _backgroundCamera = null;
            }
        }
    }
}
