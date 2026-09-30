using System.Collections;
using UnityEngine;
using VRSlashGame.Events;

namespace VRSlashGame.Input
{
    /// <summary>
    /// Advanced Haptics Manager using the Meta XR SDK (OVRInput.SetControllerVibration).
    /// Provides distinct vibration patterns for Slicing (high-frequency double-tap),
    /// Misses (low-frequency rumble), Combo Milestones (3 ascending pulses),
    /// and Game Over (fading descending pulse train).
    /// Wrapped with editor safety checks and clean console debugging for simulator/editor workflows.
    /// </summary>
    public class HapticsManager : MonoBehaviour
    {
        public static HapticsManager Instance { get; private set; }

        [Header("Haptics Master Settings")]
        [Tooltip("Global toggle for VR controller haptics.")]
        [SerializeField] private bool _enableHaptics = true;

        [Tooltip("Master intensity multiplier for all vibration patterns.")]
        [Range(0f, 1f)]
        [SerializeField] private float _masterHapticsIntensity = 1.0f;

        [Tooltip("Enable formatted console debug logs when haptic patterns fire.")]
        [SerializeField] private bool _logHapticsInEditor = true;

        // Coroutine trackers per controller to prevent overlapping/conflicting patterns
        private Coroutine _leftHapticCoroutine;
        private Coroutine _rightHapticCoroutine;

        public bool EnableHaptics
        {
            get => _enableHaptics;
            set => _enableHaptics = value;
        }

        public float MasterHapticsIntensity
        {
            get => _masterHapticsIntensity;
            set => _masterHapticsIntensity = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            GameEvents.OnFruitMissed += HandleFruitMissed;
            GameEvents.OnComboChanged += HandleComboChanged;
            GameEvents.OnGameOver += HandleGameOver;
        }

        private void OnDisable()
        {
            GameEvents.OnFruitMissed -= HandleFruitMissed;
            GameEvents.OnComboChanged -= HandleComboChanged;
            GameEvents.OnGameOver -= HandleGameOver;
            StopAllHaptics();
        }

        #region Public Haptic Trigger API

        /// <summary>
        /// Slice Pattern: Quick, high-frequency double-tap targeting the controller that made the cut.
        /// </summary>
        public void TriggerSliceHaptic(ControllerHand hand)
        {
            if (!_enableHaptics) return;

            LogHaptics($"Triggered Slice Pattern on {hand} Hand");

            OVRInput.Controller controller = GetOVRController(hand);
            StartControllerRoutine(hand, SliceDoubleTapRoutine(controller));
        }

        /// <summary>
        /// Miss Pattern: Low-frequency, dull rumble across both controllers.
        /// </summary>
        public void TriggerMissHaptic()
        {
            if (!_enableHaptics) return;

            LogHaptics("Triggered Miss Pattern on Both Hands");

            StartBothControllersRoutine(MissRumbleRoutine());
        }

        /// <summary>
        /// Combo Milestone Pattern: 3 ascending pulses of escalating frequency and amplitude.
        /// </summary>
        public void TriggerComboMilestoneHaptic(int comboLevel = 5)
        {
            if (!_enableHaptics) return;

            LogHaptics($"Triggered Combo Milestone ({comboLevel}x) Pattern on Both Hands");

            StartBothControllersRoutine(ComboMilestoneAscendingRoutine());
        }

        /// <summary>
        /// Game Over Pattern: Fading, descending pulse train across both controllers.
        /// </summary>
        public void TriggerGameOverHaptic()
        {
            if (!_enableHaptics) return;

            LogHaptics("Triggered Game Over Pattern on Both Hands");

            StartBothControllersRoutine(GameOverDescendingRoutine());
        }

        /// <summary>
        /// Immediately silences vibration on both controllers.
        /// </summary>
        public void StopAllHaptics()
        {
            if (_leftHapticCoroutine != null)
            {
                StopCoroutine(_leftHapticCoroutine);
                _leftHapticCoroutine = null;
            }

            if (_rightHapticCoroutine != null)
            {
                StopCoroutine(_rightHapticCoroutine);
                _rightHapticCoroutine = null;
            }

            SetVibrationSafe(0f, 0f, OVRInput.Controller.LTouch);
            SetVibrationSafe(0f, 0f, OVRInput.Controller.RTouch);
        }

        #endregion

        #region Pattern Coroutines

        private IEnumerator SliceDoubleTapRoutine(OVRInput.Controller controller)
        {
            float intensity = _masterHapticsIntensity;

            // First rapid tap: 40ms @ high frequency
            SetVibrationSafe(0.9f, 0.85f * intensity, controller);
            yield return new WaitForSeconds(0.04f);

            // Inter-tap silent gap: 30ms
            SetVibrationSafe(0f, 0f, controller);
            yield return new WaitForSeconds(0.03f);

            // Second crisp punch: 50ms @ max frequency
            SetVibrationSafe(1.0f, 1.0f * intensity, controller);
            yield return new WaitForSeconds(0.05f);

            SetVibrationSafe(0f, 0f, controller);
        }

        private IEnumerator MissRumbleRoutine()
        {
            float intensity = _masterHapticsIntensity;

            // Low frequency dull rumble: 220ms @ 0.25 frequency
            SetVibrationSafe(0.25f, 0.55f * intensity, OVRInput.Controller.LTouch);
            SetVibrationSafe(0.25f, 0.55f * intensity, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.22f);

            SetVibrationSafe(0f, 0f, OVRInput.Controller.LTouch);
            SetVibrationSafe(0f, 0f, OVRInput.Controller.RTouch);
        }

        private IEnumerator ComboMilestoneAscendingRoutine()
        {
            float intensity = _masterHapticsIntensity;

            // Pulse 1 (Low-mid): 60ms
            SetVibrationSafe(0.40f, 0.45f * intensity, OVRInput.Controller.LTouch);
            SetVibrationSafe(0.40f, 0.45f * intensity, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.06f);

            SetVibrationSafe(0f, 0f, OVRInput.Controller.LTouch);
            SetVibrationSafe(0f, 0f, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.05f);

            // Pulse 2 (Mid-high): 60ms
            SetVibrationSafe(0.70f, 0.70f * intensity, OVRInput.Controller.LTouch);
            SetVibrationSafe(0.70f, 0.70f * intensity, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.06f);

            SetVibrationSafe(0f, 0f, OVRInput.Controller.LTouch);
            SetVibrationSafe(0f, 0f, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.05f);

            // Pulse 3 (Crescendo): 90ms
            SetVibrationSafe(1.00f, 0.95f * intensity, OVRInput.Controller.LTouch);
            SetVibrationSafe(1.00f, 0.95f * intensity, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.09f);

            SetVibrationSafe(0f, 0f, OVRInput.Controller.LTouch);
            SetVibrationSafe(0f, 0f, OVRInput.Controller.RTouch);
        }

        private IEnumerator GameOverDescendingRoutine()
        {
            float intensity = _masterHapticsIntensity;

            float[] freqs = new float[] { 0.85f, 0.65f, 0.45f, 0.25f };
            float[] amps = new float[] { 0.85f, 0.60f, 0.40f, 0.20f };
            float[] durations = new float[] { 0.09f, 0.09f, 0.10f, 0.12f };
            float[] gaps = new float[] { 0.06f, 0.06f, 0.07f, 0f };

            for (int i = 0; i < freqs.Length; i++)
            {
                SetVibrationSafe(freqs[i], amps[i] * intensity, OVRInput.Controller.LTouch);
                SetVibrationSafe(freqs[i], amps[i] * intensity, OVRInput.Controller.RTouch);
                yield return new WaitForSeconds(durations[i]);

                SetVibrationSafe(0f, 0f, OVRInput.Controller.LTouch);
                SetVibrationSafe(0f, 0f, OVRInput.Controller.RTouch);

                if (gaps[i] > 0f)
                {
                    yield return new WaitForSeconds(gaps[i]);
                }
            }
        }

        #endregion

        #region Event Callbacks

        private void HandleFruitMissed()
        {
            TriggerMissHaptic();
        }

        private void HandleComboChanged(int comboCount)
        {
            if (comboCount == 5 || comboCount == 10)
            {
                TriggerComboMilestoneHaptic(comboCount);
            }
        }

        private void HandleGameOver(int finalScore)
        {
            TriggerGameOverHaptic();
        }

        #endregion

        #region Safe OVRInput Execution & Helpers

        /// <summary>
        /// Safe wrapper around OVRInput.SetControllerVibration that prevents null reference
        /// exceptions in Meta XR Simulator or non-VR mock environments while providing editor feedback.
        /// </summary>
        private void SetVibrationSafe(float frequency, float amplitude, OVRInput.Controller controller)
        {
            try
            {
#if UNITY_EDITOR
                // In Unity Editor / Simulator, verify OVRManager before hardware vibration
                if (OVRManager.instance != null)
                {
                    OVRInput.SetControllerVibration(frequency, amplitude, controller);
                }
#else
                OVRInput.SetControllerVibration(frequency, amplitude, controller);
#endif
            }
            catch (System.Exception)
            {
                // Silently absorb simulator/mock hardware disconnect exceptions
            }
        }

        private void StartControllerRoutine(ControllerHand hand, IEnumerator routine)
        {
            if (hand == ControllerHand.Left)
            {
                if (_leftHapticCoroutine != null) StopCoroutine(_leftHapticCoroutine);
                _leftHapticCoroutine = StartCoroutine(routine);
            }
            else
            {
                if (_rightHapticCoroutine != null) StopCoroutine(_rightHapticCoroutine);
                _rightHapticCoroutine = StartCoroutine(routine);
            }
        }

        private void StartBothControllersRoutine(IEnumerator routine)
        {
            if (_leftHapticCoroutine != null) StopCoroutine(_leftHapticCoroutine);
            if (_rightHapticCoroutine != null) StopCoroutine(_rightHapticCoroutine);

            _leftHapticCoroutine = StartCoroutine(routine);
            _rightHapticCoroutine = null;
        }

        private static OVRInput.Controller GetOVRController(ControllerHand hand)
        {
            return hand == ControllerHand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        }

        private void LogHaptics(string message)
        {
#if UNITY_EDITOR
            if (_logHapticsInEditor)
            {
                Debug.Log($"<color=#FF9800>[Haptics]</color> {message}");
            }
#endif
        }

        #endregion
    }
}
