using UnityEngine;
using VRSlashGame.Input;

namespace VRSlashGame.Gameplay
{
    /// <summary>
    /// Attached to each controller hand anchor to detect and slice fruits.
    /// Uses kinematic Rigidbody and trigger Collider. Validates swing speed with VRInputManager.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class Blade : MonoBehaviour
    {
        [Header("Hand Configuration")]
        [Tooltip("Which hand controller this blade is attached to.")]
        [SerializeField] private ControllerHand _hand = ControllerHand.Right;

        [Header("Slash Speed Settings")]
        [Tooltip("Minimum velocity required to slice fruit in normal gameplay (m/s).")]
        [SerializeField] private float _minSlashVelocity = 1.8f;

        [Tooltip("Optional slash speed threshold override (m/s). If negative or zero, uses VRInputManager's global threshold.")]
        [SerializeField] private float _minSlashSpeedOverride = -1f;

        [Header("Animated Energy Flare Settings")]
        [Tooltip("Emission intensity when holding controller still (subtle glow).")]
        [SerializeField] private float _idleEmissionIntensity = 1.0f;

        [Tooltip("Emission intensity during fast swings (saturated neon flare).")]
        [SerializeField] private float _swingEmissionIntensity = 4.5f;

        [Tooltip("Controller speed that triggers maximum emission flare (m/s).")]
        [SerializeField] private float _maxFlareSpeed = 3.2f;

        [Tooltip("Speed of smoothing when transitioning emission flare.")]
        [SerializeField] private float _flareSmoothing = 12.0f;

        [Tooltip("Dynamic point light on blade to scale with swing flare.")]
        [SerializeField] private Light _bladeLight;

        [Header("Visual Feedback (Optional)")]
        [SerializeField] private TrailRenderer _trailRenderer;
        [SerializeField] private MeshRenderer _bladeGlowRenderer;

        private static readonly int EmissionIntensityID = Shader.PropertyToID("_EmissionIntensity");

        private Rigidbody _rb;
        private Collider _col;
        private float _currentEmissionIntensity = 1.0f;
        private MaterialPropertyBlock _propBlock;
        private Renderer[] _bladeRenderers;

        // Manual velocity tracking for Meta XR Simulator & Editor
        private Vector3 _lastPosition;
        private Vector3 _manualVelocity;

        public ControllerHand Hand => _hand;
        public float CurrentEmissionIntensity => _currentEmissionIntensity;
        public Light BladeLight => _bladeLight;
        public Vector3 ManualVelocity => _manualVelocity;

        public float EffectiveSlashThreshold
        {
            get
            {
#if UNITY_EDITOR
                return 0.5f;
#else
                if (_minSlashSpeedOverride >= 0f) return _minSlashSpeedOverride;
                if (VRInputManager.Instance != null) return VRInputManager.Instance.SlashSpeedThreshold;
                return _minSlashVelocity;
#endif
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;

            _col = GetComponent<Collider>();
            if (_col == null)
            {
                CapsuleCollider cap = gameObject.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 0f, 0.48f);
                cap.radius = 0.05f;
                cap.height = 0.96f;
                cap.direction = 2; // Z axis
                _col = cap;
            }
            _col.isTrigger = true;

            _propBlock = new MaterialPropertyBlock();
            _lastPosition = transform.position;
        }

        private void Start()
        {
            CacheBladeRenderers();
        }

        public void CacheBladeRenderers()
        {
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

            Transform visuals = transform.Find("Visuals");
            if (visuals != null)
            {
                var rendList = new System.Collections.Generic.List<Renderer>();
                for (int i = 0; i < visuals.childCount; i++)
                {
                    Transform c = visuals.GetChild(i);
                    if (c.name.StartsWith("EnergyBlade_"))
                    {
                        Renderer r = c.GetComponent<Renderer>();
                        if (r != null) rendList.Add(r);
                    }
                }
                _bladeRenderers = rendList.ToArray();

                if (_bladeLight == null)
                {
                    Transform lightT = visuals.Find("BladeEnergyLight");
                    if (lightT != null) _bladeLight = lightT.GetComponent<Light>();
                }
            }

            if (_bladeRenderers == null || _bladeRenderers.Length == 0)
            {
                _bladeRenderers = GetComponentsInChildren<Renderer>();
            }

            _currentEmissionIntensity = _idleEmissionIntensity;
            ApplyEmissionIntensity(_currentEmissionIntensity);
        }

        public void ApplyEmissionIntensity(float intensity)
        {
            if (_bladeRenderers == null || _bladeRenderers.Length == 0) return;
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

            _currentEmissionIntensity = intensity;
            _propBlock.SetFloat(EmissionIntensityID, intensity);

            foreach (var r in _bladeRenderers)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(_propBlock);
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt > 0.0001f)
            {
                _manualVelocity = (transform.position - _lastPosition) / dt;
                _lastPosition = transform.position;
            }

            // 1. Query controller velocity & slash state from VRInputManager
            float inputSpeed = 0f;
            bool isSlashing = false;

            if (VRInputManager.Instance != null)
            {
                inputSpeed = VRInputManager.Instance.GetSpeed(_hand);
                isSlashing = VRInputManager.Instance.IsSlashing(_hand);
            }

            float manualSpeed = _manualVelocity.magnitude;
            float speed = Mathf.Max(manualSpeed, inputSpeed);
            if (manualSpeed >= EffectiveSlashThreshold)
            {
                isSlashing = true;
            }

            // 2. Update trail emission based on whether player is slashing
            if (_trailRenderer != null)
            {
                _trailRenderer.emitting = isSlashing;
            }

            // 3. Connect shader emission intensity to controller velocity
            float speedFactor = Mathf.Clamp01(speed / Mathf.Max(0.1f, _maxFlareSpeed));
            float smoothFactor = Mathf.SmoothStep(0f, 1f, speedFactor);
            float targetIntensity = Mathf.Lerp(_idleEmissionIntensity, _swingEmissionIntensity, smoothFactor);

            _currentEmissionIntensity = Mathf.Lerp(_currentEmissionIntensity, targetIntensity, dt * _flareSmoothing);
            ApplyEmissionIntensity(_currentEmissionIntensity);

            // 4. Scale dynamic point light intensity and range
            if (_bladeLight != null)
            {
                _bladeLight.intensity = Mathf.Lerp(0.6f, 2.8f, smoothFactor);
                _bladeLight.range = Mathf.Lerp(1.8f, 3.2f, smoothFactor);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log("[Slash] Hit fruit: " + other.name);
            CheckAndSliceFruit(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // In case the blade entered slowly and then accelerated into a slash while still touching the fruit
            CheckAndSliceFruit(other);
        }

        public void CheckAndSliceFruit(Collider other)
        {
            if (other == null) return;

            Fruit fruit = other.GetComponent<Fruit>();
            if (fruit == null)
            {
                fruit = other.GetComponentInParent<Fruit>();
            }

            if (fruit == null || fruit.IsSliced) return;

            // Calculate manual velocity & query VRInputManager
            float manualSpeed = _manualVelocity.magnitude;
            Vector3 manualDir = _manualVelocity.sqrMagnitude > 0.01f ? _manualVelocity.normalized : transform.forward;

            float inputSpeed = 0f;
            Vector3 inputDir = transform.forward;

            if (VRInputManager.Instance != null)
            {
                inputSpeed = VRInputManager.Instance.GetSpeed(_hand);
                inputDir = VRInputManager.Instance.GetSlashDirection(_hand);
            }

            float currentSpeed = Mathf.Max(manualSpeed, inputSpeed);
            Vector3 slashDirection = (manualSpeed >= inputSpeed) ? manualDir : inputDir;

            float threshold = EffectiveSlashThreshold;

#if UNITY_EDITOR
            // In Editor / Meta XR Simulator, lower threshold to 0.5f and bypass so any physical touch slices
            bool canSlice = currentSpeed >= 0.5f || true;
#else
            bool canSlice = currentSpeed >= threshold;
#endif

            if (canSlice)
            {
                Vector3 contactPoint = other.ClosestPoint(transform.position);

                // Calculate the slicing plane normal from swing velocity and blade axis (transform.forward)
                Vector3 bladeAxis = transform.forward;
                Vector3 planeNormal = Vector3.Cross(slashDirection, bladeAxis);
                if (planeNormal.sqrMagnitude < 0.001f)
                {
                    planeNormal = Vector3.Cross(slashDirection, transform.up);
                    if (planeNormal.sqrMagnitude < 0.001f)
                    {
                        planeNormal = transform.up;
                    }
                }
                planeNormal.Normalize();

                bool sliced = fruit.Slice(slashDirection, contactPoint, planeNormal);
                if (sliced)
                {
                    Debug.Log($"[Blade] {_hand} blade sliced {fruit.FruitName}! Speed: {currentSpeed:F2} m/s (Threshold: {threshold:F2} m/s)");
                    TriggerHapticTap();
                }
            }
            else
            {
                Debug.Log($"[Blade] Swing too slow to slice {fruit.FruitName}. Speed: {currentSpeed:F2} m/s < {threshold:F2} m/s");
            }
        }

        /// <summary>
        /// Triggers high-frequency double-tap slice haptics via HapticsManager.
        /// </summary>
        public void TriggerHapticTap()
        {
            if (HapticsManager.Instance != null)
            {
                HapticsManager.Instance.TriggerSliceHaptic(_hand);
            }
            else
            {
                StartCoroutine(HapticFallbackRoutine(0.08f, 0.8f, 0.85f));
            }
        }

        private System.Collections.IEnumerator HapticFallbackRoutine(float duration, float frequency, float amplitude)
        {
            OVRInput.Controller controller = (_hand == ControllerHand.Left) 
                ? OVRInput.Controller.LTouch 
                : OVRInput.Controller.RTouch;

            try
            {
                OVRInput.SetControllerVibration(frequency, amplitude, controller);
            }
            catch { }

            yield return new WaitForSeconds(duration);

            try
            {
                OVRInput.SetControllerVibration(0f, 0f, controller);
            }
            catch { }
        }

        public void SetHand(ControllerHand hand)
        {
            _hand = hand;
        }
    }
}
