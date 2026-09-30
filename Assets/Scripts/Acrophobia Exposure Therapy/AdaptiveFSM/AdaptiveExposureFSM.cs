using System;
using UnityEngine;
using Acrophobia.UI;
using Acrophobia.ProceduralEnvironment;
using Acrophobia.SignalProcessing;
using Acrophobia.GazeGeometry;

namespace Acrophobia.AdaptiveFSM
{
    /// <summary>
    /// Adaptive Exposure Finite State Machine for Acrophobia Virtual Reality Therapy.
    /// 
    /// Coordinates real-time biometric and behavioral telemetry:
    /// - TremorIntensity from TremorBandFilter (micro-tremor envelope 8-12 Hz)
    /// - HesitationIndex from EdgeProximityTracker (edge proximity, speed, gaze avoidance)
    /// - Behavioral Freezing from PosturalSwayAnalyzer (acute postural immobility)
    /// - Downward gaze angles from GazeAvoidanceDetector
    /// 
    /// Adapts the 5-Tier dynamic physical platform (DynamicCatwalkController),
    /// floating HUD instructions and countdown (FloatingInstructionHUD),
    /// and immersive audio cues (ExposureAudioManager).
    /// </summary>
    public class AdaptiveExposureFSM : MonoBehaviour
    {
        public enum ExposureState
        {
            AwaitingStart,
            Exposing,
            SafetyRecovery,
            Completed
        }

        [Header("System References")]
        [SerializeField] private FloatingInstructionHUD _hud;
        [SerializeField] private DynamicCatwalkController _catwalkController;
        [SerializeField] private ExposureAudioManager _audioManager;

        [Header("Telemetry References")]
        [SerializeField] private TremorBandFilter _tremorFilter;
        [SerializeField] private PosturalSwayAnalyzer _swayAnalyzer;
        [SerializeField] private EdgeProximityTracker _edgeTracker;
        [SerializeField] private GazeAvoidanceDetector _gazeDetector;

        [Header("Adaptive Thresholds")]
        [Tooltip("Maximum micro-tremor intensity (0..1) to count as STEADY.")]
        [SerializeField, Range(0.1f, 0.5f)] private float _steadyTremorThreshold = 0.25f;

        [Tooltip("Maximum Behavioral Hesitation Index (0..100) to count as STEADY.")]
        [SerializeField, Range(15f, 60f)] private float _steadyHesitationThreshold = 35.0f;

        [Tooltip("Tremor threshold that triggers an emergency safety intervention.")]
        [SerializeField, Range(0.5f, 0.95f)] private float _panicTremorThreshold = 0.70f;

        [Tooltip("Seconds required in steady calm state to complete each tier.")]
        [SerializeField] private float _targetTierCalmSeconds = 30.0f;

        [Tooltip("Decay multiplier per second applied to calm timer when hesitation/tremor is detected.")]
        [SerializeField, Range(0.1f, 2.0f)] private float _calmDecayRate = 0.5f;

        [Tooltip("Seconds of steady calm required to recover from a safety intervention.")]
        [SerializeField] private float _safetyRecoverySeconds = 5.0f;

        [Header("Runtime Telemetry Monitor")]
        [SerializeField] private ExposureState _currentState = ExposureState.AwaitingStart;
        [SerializeField] private int _currentTier = 1;
        [SerializeField] private float _calmTimer = 0f;
        [SerializeField] private float _recoveryTimer = 0f;
        [SerializeField] private bool _isCurrentlySteady = true;
        [SerializeField] private bool _hasJerkedThisTier = false;

        // Current telemetry cache
        [SerializeField] private float _liveTremor = 0f;
        [SerializeField] private float _liveHesitation = 0f;
        [SerializeField] private bool _liveFrozen = false;
        [SerializeField] private float _liveGazeAvoidance = 0f;

        // Visual Palette matching FloatingInstructionHUD
        private static readonly Color ColorSteady = new Color(0.00f, 0.90f, 0.46f, 1f);      // Emerald Green (#00E676)
        private static readonly Color ColorHesitation = new Color(0.95f, 0.61f, 0.07f, 1f);   // Orange/Amber (#F39C12)
        private static readonly Color ColorSafety = new Color(1.00f, 0.24f, 0.00f, 1f);       // Bright Safety Alert (#FF3D00)
        private static readonly Color ColorComplete = new Color(0.00f, 0.82f, 0.83f, 1f);     // Cyan (#00D2D3)

        // Tier Instruction Descriptions
        private static readonly string[] TierNames = new string[]
        {
            "ENCLOSED LEDGE",
            "OPEN EDGE",
            "CRANE SUSPENSION",
            "WIDTH CONTRACTION",
            "RAZOR PLANK & GLASS FLOOR"
        };

        private static readonly string[] TierInstructions = new string[]
        {
            "Take deep breaths. Look down past the railing at the street below.",
            "Railing lowered. Step to the edge and look down at the street.",
            "Step onto the suspended crane platform. Maintain steady balance.",
            "Platform width is narrowing. Keep your footing steady.",
            "Narrow glass plank. Look straight down through the glass."
        };

        // Events
        public event Action<int> OnTierAdvanced;
        public event Action OnSafetyInterventionTriggered;
        public event Action OnSessionCompleted;

        public ExposureState CurrentState => _currentState;
        public int CurrentTier => _currentTier;
        public float CalmTimer => _calmTimer;
        public float TargetTierCalmSeconds => _targetTierCalmSeconds;
        public bool IsCurrentlySteady => _isCurrentlySteady;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();

            if (_hud != null)
            {
                _hud.ExternalFsmControlled = true;
                _hud.OnSessionStarted += HandleHudSessionStarted;
            }

            // Sync initial state
            _currentState = ExposureState.AwaitingStart;
            _currentTier = 1;
            _calmTimer = 0f;
            _hasJerkedThisTier = false;

            if (_catwalkController != null)
            {
                _catwalkController.SetTier(1, true);
            }
        }

        private void OnDestroy()
        {
            if (_hud != null)
            {
                _hud.OnSessionStarted -= HandleHudSessionStarted;
            }
        }

        private void Update()
        {
            PollTelemetry();

            switch (_currentState)
            {
                case ExposureState.AwaitingStart:
                    UpdateAwaitingStart();
                    break;

                case ExposureState.Exposing:
                    UpdateExposing();
                    break;

                case ExposureState.SafetyRecovery:
                    UpdateSafetyRecovery();
                    break;

                case ExposureState.Completed:
                    UpdateCompleted();
                    break;
            }

            HandleDebugShortcuts();
        }

        /// <summary>
        /// Automatically locates all connected subsystems and sensors if unassigned.
        /// </summary>
        public void ResolveReferences()
        {
            if (_hud == null) _hud = FindAnyObjectByType<FloatingInstructionHUD>();
            if (_catwalkController == null) _catwalkController = FindAnyObjectByType<DynamicCatwalkController>();
            if (_audioManager == null) _audioManager = FindAnyObjectByType<ExposureAudioManager>();

            if (_tremorFilter == null) _tremorFilter = FindAnyObjectByType<TremorBandFilter>();
            if (_swayAnalyzer == null) _swayAnalyzer = FindAnyObjectByType<PosturalSwayAnalyzer>();
            if (_edgeTracker == null) _edgeTracker = FindAnyObjectByType<EdgeProximityTracker>();
            if (_gazeDetector == null) _gazeDetector = FindAnyObjectByType<GazeAvoidanceDetector>();
        }

        private void PollTelemetry()
        {
            _liveTremor = _tremorFilter != null ? _tremorFilter.TremorIntensity : 0f;
            _liveHesitation = _edgeTracker != null ? _edgeTracker.HesitationIndex : 0f;
            _liveFrozen = _swayAnalyzer != null && _swayAnalyzer.IsPosturalFrozen;
            _liveGazeAvoidance = _gazeDetector != null ? _gazeDetector.GazeAvoidanceRatio : 0f;
        }

        private void HandleHudSessionStarted()
        {
            if (_currentState == ExposureState.AwaitingStart)
            {
                StartExposureSession();
            }
        }

        /// <summary>
        /// Begins the exposure protocol at Tier 1.
        /// </summary>
        public void StartExposureSession()
        {
            _currentState = ExposureState.Exposing;
            _currentTier = 1;
            _calmTimer = 0f;
            _hasJerkedThisTier = false;

            if (_catwalkController != null)
            {
                _catwalkController.SetTier(1);
            }

            if (_audioManager != null)
            {
                _audioManager.StartAmbientLoops();
                _audioManager.SetTierWindIntensity(1);
            }

            ApplyTierHUD(_currentTier);
        }

        private void UpdateAwaitingStart()
        {
            // If the user presses space, enter, or VR button, start the session
            bool startPressed = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                (UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame ||
                 UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame))
            {
                startPressed = true;
            }
#endif
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One) ||
                    OVRInput.GetDown(OVRInput.RawButton.A) ||
                    OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger))
                {
                    startPressed = true;
                }
            }
            catch (Exception) { }

            if (startPressed)
            {
                if (_hud != null) _hud.StartSession();
                StartExposureSession();
            }
        }

        private void UpdateExposing()
        {
            // 1. Safety Check: Acute Panic / Freeze Intervention
            if (_liveTremor > _panicTremorThreshold || _liveFrozen)
            {
                TriggerSafetyIntervention();
                return;
            }

            // 2. Steady / Hesitation Evaluation
            _isCurrentlySteady = (_liveTremor < _steadyTremorThreshold) && (_liveHesitation < _steadyHesitationThreshold);

            if (_isCurrentlySteady)
            {
                // Accumulate calm exposure time
                _calmTimer += Time.deltaTime;
                if (_hud != null)
                {
                    _hud.SetStatus(false, "STATUS: STEADY", ColorSteady);
                    _hud.UpdateProgress(_calmTimer / _targetTierCalmSeconds, _calmTimer, _targetTierCalmSeconds);
                }
            }
            else
            {
                // Hesitation / Tremor detected: decay or pause timer
                _calmTimer = Mathf.Max(0f, _calmTimer - Time.deltaTime * _calmDecayRate);
                if (_hud != null)
                {
                    _hud.SetStatus(true, "HESITATION DETECTED", ColorHesitation);
                    _hud.UpdateProgress(_calmTimer / _targetTierCalmSeconds, _calmTimer, _targetTierCalmSeconds);
                }
            }

            // 3. Stability Perturbation (Cable Jerk) at 15.0s in Tiers 3, 4, 5
            if (_currentTier >= 3 && !_hasJerkedThisTier && _calmTimer >= 15.0f)
            {
                _hasJerkedThisTier = true;
                if (_catwalkController != null)
                {
                    _catwalkController.TriggerCableJerk(1.0f);
                }
                if (_audioManager != null)
                {
                    _audioManager.PlayCableCreak(1.0f);
                    _audioManager.PlayWindGust(1.0f);
                }
            }

            // 4. Tier Progression Check
            if (_calmTimer >= _targetTierCalmSeconds)
            {
                AdvanceToNextTier();
            }
        }

        private void AdvanceToNextTier()
        {
            _calmTimer = 0f;
            _hasJerkedThisTier = false;

            // Play completion chime
            if (_audioManager != null)
            {
                _audioManager.PlayChime();
            }

            if (_currentTier < 5)
            {
                _currentTier++;
                if (_catwalkController != null)
                {
                    _catwalkController.SetTier(_currentTier);
                }
                if (_audioManager != null)
                {
                    _audioManager.SetTierWindIntensity(_currentTier);
                }

                ApplyTierHUD(_currentTier);
                OnTierAdvanced?.Invoke(_currentTier);
            }
            else
            {
                // Completed Tier 5 - Session Finished!
                _currentState = ExposureState.Completed;
                if (_hud != null)
                {
                    _hud.SetTier(5, "COMPLETE");
                    _hud.SetInstruction("Session Complete! Outstanding physiological composure and courage demonstrated.");
                    _hud.SetStatus(false, "STATUS: COMPLETE", ColorComplete);
                    _hud.UpdateProgress(1.0f, _targetTierCalmSeconds, _targetTierCalmSeconds);
                }
                OnSessionCompleted?.Invoke();
            }
        }

        private void TriggerSafetyIntervention()
        {
            _currentState = ExposureState.SafetyRecovery;
            _recoveryTimer = 0f;

            // 1. Revert environment immediately to Tier 1 safety
            if (_catwalkController != null)
            {
                _catwalkController.SetTier(1);
            }

            // 2. Play warning alert
            if (_audioManager != null)
            {
                _audioManager.PlayWarningBeep();
            }

            // 3. Update HUD with safety guidance
            if (_hud != null)
            {
                _hud.SetInstruction("SAFETY RECOVERY ACTIVATED - Focus on slow, deep breaths.\nRailing raised.");
                _hud.SetStatus(true, "SAFETY INTERVENTION", ColorSafety);
            }

            OnSafetyInterventionTriggered?.Invoke();
        }

        private void UpdateSafetyRecovery()
        {
            // Evaluate whether user is calm enough to resume
            bool isRecovering = (_liveTremor < (_steadyTremorThreshold + 0.05f)) && !_liveFrozen;

            if (isRecovering)
            {
                _recoveryTimer += Time.deltaTime;
                if (_hud != null)
                {
                    _hud.SetStatus(false, $"RECOVERING: {_recoveryTimer:F0}s / {_safetyRecoverySeconds:F0}s", ColorSteady);
                }

                if (_recoveryTimer >= _safetyRecoverySeconds)
                {
                    // Resume current tier
                    _currentState = ExposureState.Exposing;
                    _calmTimer = 0f;
                    _hasJerkedThisTier = false;

                    if (_catwalkController != null)
                    {
                        _catwalkController.SetTier(_currentTier);
                    }
                    ApplyTierHUD(_currentTier);
                }
            }
            else
            {
                _recoveryTimer = Mathf.Max(0f, _recoveryTimer - Time.deltaTime);
                if (_hud != null)
                {
                    _hud.SetStatus(true, "HIGH ANXIETY - BREATHE SLOWLY", ColorSafety);
                }
            }
        }

        private void UpdateCompleted()
        {
            // Maintain completion display
            if (_hud != null)
            {
                _hud.UpdateProgress(1.0f, _targetTierCalmSeconds, _targetTierCalmSeconds);
            }
        }

        private void ApplyTierHUD(int tier)
        {
            if (_hud == null) return;

            int index = Mathf.Clamp(tier - 1, 0, TierNames.Length - 1);
            _hud.SetTier(tier, TierNames[index]);
            _hud.SetInstruction(TierInstructions[index]);
            _hud.SetStatus(false, "STATUS: STEADY", ColorSteady);
            _hud.UpdateProgress(0f, _targetTierCalmSeconds);
        }

        private void HandleDebugShortcuts()
        {
            // Debug key [T]: Advance tier manually
            if (Input.GetKeyDown(KeyCode.T))
            {
                AdvanceToNextTier();
            }

            // Debug key [J]: Trigger Cable Jerk
            if (Input.GetKeyDown(KeyCode.J))
            {
                if (_catwalkController != null) _catwalkController.TriggerCableJerk(1.0f);
                if (_audioManager != null) _audioManager.PlayCableCreak();
            }

            // Debug key [K]: Force Safety Intervention
            if (Input.GetKeyDown(KeyCode.K))
            {
                TriggerSafetyIntervention();
            }
        }
    }
}
