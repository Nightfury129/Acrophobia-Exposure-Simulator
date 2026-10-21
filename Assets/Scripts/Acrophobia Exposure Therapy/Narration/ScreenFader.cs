using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Acrophobia.Narration
{
    /// <summary>
    /// A full-screen fade used for interludes that happen off-camera.
    ///
    /// Separate from the teleport blink, which is a fast in-and-out with nothing held in
    /// between. Here the black is the point: it stands in for a whole scene - a bathroom,
    /// a kettle - that is never built, carried entirely by sound. Two audio clips replace
    /// a room.
    ///
    /// Holding black in VR is only tolerable if something is clearly happening. Silence
    /// behind a black screen reads as a crash, so an interlude with no audio still shows
    /// its subtitle, and the fade timings stay slow enough to read as deliberate.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 3f)] private float _defaultFadeSeconds = 0.8f;

        private Canvas _canvas;
        private Image _image;
        private float _alpha;

        // VR fade surface. A ScreenSpaceOverlay canvas renders only to the desktop Game
        // view and never reaches the headset, so the fader looked correct on the monitor
        // while every transition appeared abrupt in the HMD. A quad parented to the eye
        // and held just past the near plane works in both.
        private Transform _quad;
        private Renderer _quadRenderer;
        private Material _quadMaterial;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public bool IsBlack => _alpha > 0.99f;

        public float Alpha => _alpha;

        private void Awake()
        {
            Build();
        }

        private void Build()
        {
            if (_canvas != null) return;

            var canvasGo = new GameObject("FadeCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;   // above the teleport blink
            _canvas.enabled = false;

            var imageGo = new GameObject("Black");
            imageGo.transform.SetParent(canvasGo.transform, false);

            _image = imageGo.AddComponent<Image>();
            _image.color = new Color(0f, 0f, 0f, 0f);
            _image.raycastTarget = false;

            var rt = _image.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            BuildQuad();
        }

        /// <summary>
        /// The surface that actually blacks out a headset. Parented to the camera so it
        /// rides the head, sat just beyond the near clip plane, and oversized so no
        /// amount of FOV or IPD lets the edges creep into view.
        /// </summary>
        private void BuildQuad()
        {
            if (_quad != null) return;

            Camera cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam == null) return;   // nothing to parent to yet; retried on next Build

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "FadeQuad";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.02f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 2.0f;

            _quadRenderer = go.GetComponent<Renderer>();
            _quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _quadRenderer.receiveShadows = false;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _quadMaterial = new Material(shader);

            // Transparent, depth-ignoring, drawn last. Without ZTest Always the quad can
            // lose to anything the near plane is already intersecting.
            _quadMaterial.SetFloat("_Surface", 1f);
            _quadMaterial.SetFloat("_Blend", 0f);
            _quadMaterial.SetFloat("_ZWrite", 0f);
            _quadMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            _quadMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _quadMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _quadMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _quadMaterial.DisableKeyword("_ALPHATEST_ON");
            // Below the subtitle panel, which sits at 3600 and has to stay readable on
            // top of the black - the whole point of holding black is the line you show
            // during it. Still above ordinary transparent geometry.
            _quadMaterial.renderQueue = 3500;
            _quadMaterial.color = new Color(0f, 0f, 0f, 0f);
            if (_quadMaterial.HasProperty(BaseColorId))
                _quadMaterial.SetColor(BaseColorId, new Color(0f, 0f, 0f, 0f));

            _quadRenderer.sharedMaterial = _quadMaterial;
            _quadRenderer.enabled = false;

            _quad = go.transform;
        }

        public IEnumerator FadeTo(float target, float seconds = -1f)
        {
            Build();

            if (seconds < 0f) seconds = _defaultFadeSeconds;

            float from = _alpha;
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / seconds)));
                yield return null;
            }

            SetAlpha(target);
        }

        public void SetAlphaImmediate(float a)
        {
            Build();
            SetAlpha(a);
        }

        private void SetAlpha(float a)
        {
            _alpha = Mathf.Clamp01(a);

            if (_image != null)
            {
                Color c = _image.color;
                c.a = _alpha;
                _image.color = c;
            }

            if (_canvas != null)
            {
                _canvas.enabled = _alpha > 0.001f;
            }

            // The headset only ever sees this one.
            if (_quad == null) BuildQuad();

            if (_quadMaterial != null)
            {
                var c = new Color(0f, 0f, 0f, _alpha);
                _quadMaterial.color = c;
                if (_quadMaterial.HasProperty(BaseColorId)) _quadMaterial.SetColor(BaseColorId, c);
            }

            if (_quadRenderer != null)
            {
                _quadRenderer.enabled = _alpha > 0.001f;
            }
        }
    }
}
