using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Acrophobia.SignalProcessing;
using Acrophobia.GazeGeometry;

namespace Acrophobia.UI
{
    /// <summary>
    /// World-space floating HUD that guides the player through the acrophobia exposure session.
    /// Implements smooth lazy-follow behavior (1.8m in front of eyes, Y = -0.15m below eye level)
    /// using Slerp and Lerp for nausea-free visual stabilization.
    /// Features:
    /// - Start screen state locked until player presses [Space] or [A].
    /// - Title, Tier Banner, Step-by-Step Instruction text.
    /// - 30-Second Countdown progress bar.
    /// - Real-time Status Indicator ("Steady" / "High Hesitation Detected").
    /// </summary>
    public class FloatingInstructionHUD : MonoBehaviour
    {
        [Header("Lazy-Follow Configuration")]
        [Tooltip("Transform representing the player's head (CenterEyeAnchor).")]
        [SerializeField] private Transform _centerEyeAnchor;

        [Tooltip("Target distance in meters in front of the player's eyes.")]
        [SerializeField, Range(0.8f, 3.0f)] private float _followDistance = 1.3f;

        [Tooltip("Vertical offset relative to eye level (negative is slightly below eye level).")]
        [SerializeField, Range(-0.5f, 0.5f)] private float _verticalOffset = -0.15f;

        [Tooltip("Upward pitch tilt angle in degrees.")]
        [SerializeField, Range(-20f, 20f)] private float _upwardTiltAngle = 10.0f;

        [Tooltip("Position lerp smoothing speed.")]
        [SerializeField, Range(1.0f, 10.0f)] private float _positionFollowSpeed = 3.5f;

        [Tooltip("Rotation slerp smoothing speed.")]
        [SerializeField, Range(1.0f, 10.0f)] private float _rotationFollowSpeed = 4.0f;

        [Header("UI Elements (Auto-Generated if unassigned)")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _mainPanel;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _tierBannerText;
        [SerializeField] private TextMeshProUGUI _instructionText;
        [SerializeField] private TextMeshProUGUI _countdownText;
        [SerializeField] private Image _progressBarFill;
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private Image _statusIcon;

        [Header("Distress Telemetry Integration")]
        [SerializeField] private Acrophobia.Telemetry.SudsPromptController _sudsPrompt;
        [SerializeField] private TextMeshProUGUI _distressPromptHint;
        private Coroutine _distressBannerCoroutine;

        [Header("Runtime State")]
        [SerializeField] private bool _hasSessionStarted = false;
        [SerializeField] private int _currentTier = 1;
        [SerializeField] private float _currentProgressSeconds = 0f;
        [SerializeField] private float _targetProgressSeconds = 30f;
        [SerializeField] private bool _isHesitating = false;

        // Visual Colors - High-contrast palette
        private static readonly Color ColorSteady = new Color(0.00f, 0.90f, 0.46f, 1f);      // Emerald Green (#00E676)
        private static readonly Color ColorHesitation = new Color(0.95f, 0.61f, 0.07f, 1f);   // Orange (#F39C12)
        private static readonly Color ColorAwaiting = new Color(0.40f, 0.78f, 1.00f, 1f);     // High-contrast Sky Blue (#66C7FF)
        private static readonly Color ColorComplete = new Color(0.00f, 0.82f, 0.83f, 1f);     // Cyan (#00D2D3)
        private static readonly Color ColorCyanAccent = new Color(0.00f, 0.82f, 0.83f, 1f);   // Bright Cyan
        private static readonly Color ColorDarkBg = new Color(0.08f, 0.10f, 0.14f, 0.85f);    // Translucent dark slate
        private static readonly Color ColorTroughBg = new Color(0.12f, 0.15f, 0.20f, 0.90f);  // Progress trough
        private static readonly Color ColorBorderCyan = new Color(0.00f, 0.82f, 0.83f, 0.50f);// Outline border

        // Connected telemetry (optional auto-binding)
        private EdgeProximityTracker _edgeTracker;
        private PosturalSwayAnalyzer _swayAnalyzer;

        public bool HasSessionStarted => _hasSessionStarted;
        public int CurrentTier => _currentTier;
        public float ProgressSeconds => _currentProgressSeconds;
        public float TargetProgressSeconds => _targetProgressSeconds;
        public bool IsHesitating => _isHesitating;
        public bool ExternalFsmControlled { get; set; } = false;

        public event Action OnSessionStarted;
        public event Action OnExposureCompleted;

        private void Awake()
        {
            ResolveTarget();
            EnsureUIHierarchy();
            TryBindSudsPrompt();
            ShowStartScreen();
        }

        private void Start()
        {
            ResolveTarget();
            SnapToTargetPosition();
            TryBindTelemetry();
            TryBindSudsPrompt();
        }

        private void OnDestroy()
        {
            if (_sudsPrompt != null)
            {
                _sudsPrompt.OnSudsSubmitted -= HandleSudsSubmitted;
            }
        }

        private void Update()
        {
            // 1. Smooth lazy-follow movement
            UpdateLazyFollow();

            // 2. Start screen initiation check
            if (!_hasSessionStarted)
            {
                CheckStartInput();
            }
            else
            {
                // 3. Autonomous telemetry polling if present
                PollTelemetry();
            }
        }

        /// <summary>
        /// Smoothly glides the HUD to maintain 1.3m distance and -0.15m vertical offset
        /// relative to the player's head, facing the camera with a 10-degree upward tilt.
        /// </summary>
        private void UpdateLazyFollow()
        {
            if (_centerEyeAnchor == null) return;

            Vector3 headPos = _centerEyeAnchor.position;
            Vector3 forward = _centerEyeAnchor.forward;

            // Project forward onto horizontal plane to keep HUD upright
            Vector3 forwardHoriz = new Vector3(forward.x, 0f, forward.z);
            if (forwardHoriz.sqrMagnitude < 0.01f) forwardHoriz = forward;
            forwardHoriz.Normalize();

            Vector3 targetPosition = headPos + forwardHoriz * _followDistance + Vector3.up * _verticalOffset;
            Vector3 lookDirection = transform.position - headPos;

            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion baseRotation = Quaternion.LookRotation(lookDirection);
                Quaternion tilt = Quaternion.Euler(-_upwardTiltAngle, 0f, 0f);
                Quaternion targetRotation = baseRotation * tilt;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * _rotationFollowSpeed);
            }

            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * _positionFollowSpeed);
        }

        /// <summary>
        /// Instantly places the HUD at its ideal target position without smoothing.
        /// Useful during initialization and scene load.
        /// </summary>
        public void SnapToTargetPosition()
        {
            if (_centerEyeAnchor == null) return;

            Vector3 headPos = _centerEyeAnchor.position;
            Vector3 forwardHoriz = new Vector3(_centerEyeAnchor.forward.x, 0f, _centerEyeAnchor.forward.z).normalized;
            if (forwardHoriz.sqrMagnitude < 0.01f) forwardHoriz = Vector3.forward;

            transform.position = headPos + forwardHoriz * _followDistance + Vector3.up * _verticalOffset;
            Vector3 lookDirection = transform.position - headPos;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion baseRotation = Quaternion.LookRotation(lookDirection);
                Quaternion tilt = Quaternion.Euler(-_upwardTiltAngle, 0f, 0f);
                transform.rotation = baseRotation * tilt;
            }
        }

        /// <summary>
        /// Displays the initial locked Start Screen state.
        /// </summary>
        public void ShowStartScreen()
        {
            _hasSessionStarted = false;
            _currentProgressSeconds = 0f;

            if (_titleText != null) _titleText.text = "[ TIER 1: ENCLOSED LEDGE ]";
            if (_tierBannerText != null) _tierBannerText.text = "[ TIER 1: ENCLOSED LEDGE ]";
            if (_instructionText != null)
            {
                _instructionText.text = "Stand comfortably on the platform.\nPress [Space] or [A] to Start.";
            }

            if (_countdownText != null) _countdownText.text = "Calm Exposure: 0.0s / 30.0s";
            if (_progressBarFill != null) _progressBarFill.fillAmount = 0f;

            UpdateStatusDisplay(false, "STATUS: AWAITING START", ColorAwaiting);
        }

        /// <summary>
        /// Initiates the exposure session and unlocks Tier 1 guidance.
        /// </summary>
        public void StartSession()
        {
            if (_hasSessionStarted) return;

            _hasSessionStarted = true;
            _currentTier = 1;
            SetTier(1, "ENCLOSED LEDGE");
            SetInstruction("Take deep breaths. Look down past the railing at the street below.");
            UpdateProgress(0f, 30f);
            SetStatus(false, "STATUS: STEADY");

            OnSessionStarted?.Invoke();
        }

        /// <summary>
        /// Sets the current exposure tier banner.
        /// </summary>
        public void SetTier(int tierNumber, string tierName)
        {
            _currentTier = tierNumber;
            if (_tierBannerText != null)
            {
                _tierBannerText.text = $"[ TIER {tierNumber}: {tierName.ToUpperInvariant()} ]";
            }
            if (_titleText != null)
            {
                _titleText.text = $"[ TIER {tierNumber}: {tierName.ToUpperInvariant()} ]";
            }
        }

        /// <summary>
        /// Updates the main dynamic step-by-step guidance instruction.
        /// </summary>
        public void SetInstruction(string instruction)
        {
            if (_instructionText != null)
            {
                _instructionText.text = instruction;
            }
        }

        /// <summary>
        /// Updates the 30-second progress bar and text counter.
        /// </summary>
        public void UpdateProgress(float currentSeconds, float targetSeconds = 30f)
        {
            _currentProgressSeconds = Mathf.Clamp(currentSeconds, 0f, targetSeconds);
            _targetProgressSeconds = targetSeconds;

            float fill = _targetProgressSeconds > 0f ? _currentProgressSeconds / _targetProgressSeconds : 0f;
            if (_progressBarFill != null)
            {
                _progressBarFill.fillAmount = fill;
            }

            if (_countdownText != null)
            {
                if (_currentProgressSeconds >= _targetProgressSeconds)
                {
                    _countdownText.text = $"Exposure Complete! ({_targetProgressSeconds:F1}s / {_targetProgressSeconds:F1}s)";
                    UpdateStatusDisplay(false, "STATUS: TIER COMPLETE", ColorComplete);
                    OnExposureCompleted?.Invoke();
                }
                else
                {
                    _countdownText.text = $"Calm Exposure: {_currentProgressSeconds:F1}s / {_targetProgressSeconds:F1}s";
                }
            }
        }

        /// <summary>
        /// Overload accepting normalized progress (0..1), current seconds, and target seconds.
        /// Matches FloatingInstructionHUD.UpdateProgress(calmTimer / 30.0f, calmTimer, 30.0f).
        /// </summary>
        public void UpdateProgress(float progressNormalized, float currentSeconds, float targetSeconds)
        {
            UpdateProgress(currentSeconds, targetSeconds);
        }

        /// <summary>
        /// Updates the real-time Status Indicator between Steady (green) and Hesitation Detected (orange).
        /// </summary>
        public void SetStatus(bool isHesitating, string customStatus = null)
        {
            _isHesitating = isHesitating;
            string label = customStatus ?? (isHesitating ? "HESITATION DETECTED" : "STATUS: STEADY");
            Color color = isHesitating ? ColorHesitation : ColorSteady;

            UpdateStatusDisplay(isHesitating, label, color);
        }

        /// <summary>
        /// Updates the real-time Status Indicator with explicit label and color.
        /// </summary>
        public void SetStatus(bool isHesitating, string customStatus, Color customColor)
        {
            _isHesitating = isHesitating;
            UpdateStatusDisplay(isHesitating, customStatus, customColor);
        }

        private void UpdateStatusDisplay(bool isHesitating, string label, Color color)
        {
            if (_statusText != null)
            {
                _statusText.text = label;
                _statusText.color = color;
            }

            if (_statusIcon != null)
            {
                _statusIcon.color = color;
            }
        }

        private void CheckStartInput()
        {
            bool startTriggered = false;

            // 1. Keyboard Input via New Input System
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                {
                    startTriggered = true;
                }
            }
#else
            try
            {
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                {
                    startTriggered = true;
                }
            }
            catch (System.InvalidOperationException) { }
#endif

            // 2. Meta Quest Touch Controller Input
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One) ||           // A button
                    OVRInput.GetDown(OVRInput.Button.Three) ||         // X button
                    OVRInput.GetDown(OVRInput.RawButton.A) ||
                    OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) ||
                    OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
                {
                    startTriggered = true;
                }
            }
            catch (System.Exception) { }

            if (startTriggered)
            {
                StartSession();
            }
        }

        private void PollTelemetry()
        {
            if (ExternalFsmControlled) return;

            if (_edgeTracker != null)
            {
                // If within edge zone and hesitation index is elevated (> 45), flag hesitation
                bool highHesitation = _edgeTracker.IsNearEdge && _edgeTracker.HesitationIndex > 45f;
                if (highHesitation != _isHesitating)
                {
                    SetStatus(highHesitation);
                }
            }
        }

        private void TryBindTelemetry()
        {
            if (_edgeTracker == null) _edgeTracker = FindAnyObjectByType<EdgeProximityTracker>();
            if (_swayAnalyzer == null) _swayAnalyzer = FindAnyObjectByType<PosturalSwayAnalyzer>();
        }

        private void TryBindSudsPrompt()
        {
            if (_sudsPrompt == null)
            {
                _sudsPrompt = FindAnyObjectByType<Acrophobia.Telemetry.SudsPromptController>();
            }

            if (_sudsPrompt != null)
            {
                _sudsPrompt.OnSudsSubmitted -= HandleSudsSubmitted;
                _sudsPrompt.OnSudsSubmitted += HandleSudsSubmitted;
            }
        }

        private void HandleSudsSubmitted(int distressRating, string context)
        {
            ShowDistressLoggedConfirmation(distressRating);
        }

        /// <summary>
        /// Displays transient visual feedback on the HUD that distress was recorded.
        /// </summary>
        public void ShowDistressLoggedConfirmation(int distressValue)
        {
            if (_distressBannerCoroutine != null)
            {
                StopCoroutine(_distressBannerCoroutine);
            }
            _distressBannerCoroutine = StartCoroutine(DistressConfirmationRoutine(distressValue));
        }

        private System.Collections.IEnumerator DistressConfirmationRoutine(int distressValue)
        {
            UpdateStatusDisplay(false, $"DISTRESS: {distressValue}/100 LOGGED", ColorComplete);

            yield return new WaitForSeconds(3.5f);

            if (_hasSessionStarted)
            {
                SetStatus(_isHesitating);
            }
            else
            {
                UpdateStatusDisplay(false, "STATUS: AWAITING START", ColorAwaiting);
            }

            _distressBannerCoroutine = null;
        }

        private void ResolveTarget()
        {
            if (_centerEyeAnchor != null) return;

            var centerEye = GameObject.Find("CenterEyeAnchor");
            if (centerEye != null) _centerEyeAnchor = centerEye.transform;
            else if (Camera.main != null) _centerEyeAnchor = Camera.main.transform;
        }

        /// <summary>
        /// Builds the entire ergonomic World-Space Canvas and visual hierarchy programmatically
        /// if components are not already wired in the Inspector.
        /// Configured for 850 x 350 at 0.001 scale (0.85m x 0.35m physical) with 2px cyan outline.
        /// </summary>
        public void EnsureUIHierarchy()
        {
            // 1. Ensure Canvas
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            RectTransform canvasRt = GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(850f, 350f);
            canvasRt.localScale = Vector3.one * 0.001f; // 0.85m x 0.35m physical size

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;

            GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster == null) gameObject.AddComponent<GraphicRaycaster>();

            // 2. Main Background Panel (0.85m x 0.35m dark slate with 2px cyan outline)
            Transform panelTrans = transform.Find("MainPanel");
            GameObject panelObj;
            if (panelTrans == null)
            {
                panelObj = new GameObject("MainPanel");
                panelObj.transform.SetParent(transform, false);
            }
            else
            {
                panelObj = panelTrans.gameObject;
            }

            _mainPanel = panelObj.GetComponent<RectTransform>();
            if (_mainPanel == null) _mainPanel = panelObj.AddComponent<RectTransform>();
            _mainPanel.anchorMin = Vector2.zero;
            _mainPanel.anchorMax = Vector2.one;
            _mainPanel.sizeDelta = Vector2.zero;

            Image bg = panelObj.GetComponent<Image>();
            if (bg == null) bg = panelObj.AddComponent<Image>();
            bg.color = ColorDarkBg;

            // 2px Subtle Cyan/Steel Outline
            Outline outline = panelObj.GetComponent<Outline>();
            if (outline == null) outline = panelObj.AddComponent<Outline>();
            outline.effectColor = ColorBorderCyan;
            outline.effectDistance = new Vector2(2f, -2f);

            // 3. Header / Tier Banner: Font size 32, Bold, bright cyan/white: [ TIER 1: ENCLOSED LEDGE ]
            _titleText = EnsureTextComponent(_mainPanel.transform, "TitleText", 
                "[ TIER 1: ENCLOSED LEDGE ]", 32f, FontStyles.Bold, ColorCyanAccent,
                new Vector2(0f, 115f), new Vector2(800f, 42f), TextAlignmentOptions.Center);
            _tierBannerText = _titleText;

            // 4. Instruction Text: Font size 26, Bold, crisp white, centered
            _instructionText = EnsureTextComponent(_mainPanel.transform, "InstructionText",
                "Stand comfortably on the platform.\nPress [Space] or [A] to Start.", 26f, FontStyles.Bold, Color.white,
                new Vector2(0f, 42f), new Vector2(780f, 75f), TextAlignmentOptions.Center);

            // 5. 30-Second Countdown Text: Font size 20, Bold, Calm Exposure: 0.0s / 30.0s
            _countdownText = EnsureTextComponent(_mainPanel.transform, "CountdownText",
                "Calm Exposure: 0.0s / 30.0s", 20f, FontStyles.Bold, new Color(0.82f, 0.88f, 0.94f, 1f),
                new Vector2(0f, -28f), new Vector2(700f, 26f), TextAlignmentOptions.Center);

            // 6. Progress Bar (Trough + Emerald Fill): Prominent horizontal bar (Height = 16px, Width = 700px)
            Transform troughTrans = _mainPanel.transform.Find("ProgressBarTrough");
            GameObject troughObj;
            if (troughTrans == null)
            {
                troughObj = new GameObject("ProgressBarTrough");
                troughObj.transform.SetParent(_mainPanel.transform, false);
            }
            else
            {
                troughObj = troughTrans.gameObject;
            }

            RectTransform trRt = troughObj.GetComponent<RectTransform>();
            if (trRt == null) trRt = troughObj.AddComponent<RectTransform>();
            trRt.anchoredPosition = new Vector2(0f, -60f);
            trRt.sizeDelta = new Vector2(700f, 16f);

            Image trImg = troughObj.GetComponent<Image>();
            if (trImg == null) trImg = troughObj.AddComponent<Image>();
            trImg.color = ColorTroughBg;

            Transform fillTrans = troughObj.transform.Find("ProgressBarFill");
            GameObject fillObj;
            if (fillTrans == null)
            {
                fillObj = new GameObject("ProgressBarFill");
                fillObj.transform.SetParent(troughObj.transform, false);
            }
            else
            {
                fillObj = fillTrans.gameObject;
            }

            RectTransform fillRt = fillObj.GetComponent<RectTransform>();
            if (fillRt == null) fillRt = fillObj.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;

            _progressBarFill = fillObj.GetComponent<Image>();
            if (_progressBarFill == null) _progressBarFill = fillObj.AddComponent<Image>();
            _progressBarFill.type = Image.Type.Filled;
            _progressBarFill.fillMethod = Image.FillMethod.Horizontal;
            _progressBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _progressBarFill.fillAmount = 0f;
            _progressBarFill.color = ColorSteady;

            // 7. Status Indicator Pill Badge: STATUS: AWAITING START / STATUS: STEADY / HESITATION DETECTED
            Transform statusTrans = _mainPanel.transform.Find("StatusIndicator");
            GameObject statusObj;
            if (statusTrans == null)
            {
                statusObj = new GameObject("StatusIndicator");
                statusObj.transform.SetParent(_mainPanel.transform, false);
            }
            else
            {
                statusObj = statusTrans.gameObject;
            }

            RectTransform statusRt = statusObj.GetComponent<RectTransform>();
            if (statusRt == null) statusRt = statusObj.AddComponent<RectTransform>();
            statusRt.anchoredPosition = new Vector2(0f, -112f);
            statusRt.sizeDelta = new Vector2(360f, 34f);

            Image statusBg = statusObj.GetComponent<Image>();
            if (statusBg == null) statusBg = statusObj.AddComponent<Image>();
            statusBg.color = new Color(0.12f, 0.16f, 0.22f, 0.90f);

            Outline statusOutline = statusObj.GetComponent<Outline>();
            if (statusOutline == null) statusOutline = statusObj.AddComponent<Outline>();
            statusOutline.effectColor = new Color(0.25f, 0.35f, 0.45f, 0.50f);
            statusOutline.effectDistance = new Vector2(1f, -1f);

            // Status Icon Circle
            Transform iconTrans = statusObj.transform.Find("StatusIcon");
            GameObject iconObj;
            if (iconTrans == null)
            {
                iconObj = new GameObject("StatusIcon");
                iconObj.transform.SetParent(statusObj.transform, false);
            }
            else
            {
                iconObj = iconTrans.gameObject;
            }

            RectTransform iconRt = iconObj.GetComponent<RectTransform>();
            if (iconRt == null) iconRt = iconObj.AddComponent<RectTransform>();
            iconRt.anchoredPosition = new Vector2(-120f, 0f);
            iconRt.sizeDelta = new Vector2(14f, 14f);

            _statusIcon = iconObj.GetComponent<Image>();
            if (_statusIcon == null) _statusIcon = iconObj.AddComponent<Image>();
            _statusIcon.color = ColorAwaiting;

            // Status Text
            _statusText = EnsureTextComponent(statusObj.transform, "StatusText",
                "STATUS: AWAITING START", 18f, FontStyles.Bold, ColorAwaiting,
                new Vector2(10f, 0f), new Vector2(270f, 30f), TextAlignmentOptions.Center);
            _statusText.textWrappingMode = TextWrappingModes.NoWrap;

            // 8. Distress rating prompt callout at bottom of panel
            _distressPromptHint = EnsureTextComponent(_mainPanel.transform, "DistressPromptHint",
                "Press [Y] or [U] to Rate Distress (0-100)", 16f, FontStyles.Normal,
                new Color(0.65f, 0.75f, 0.85f, 0.85f),
                new Vector2(0f, -148f), new Vector2(500f, 24f), TextAlignmentOptions.Center);
        }

        private TextMeshProUGUI EnsureTextComponent(Transform parent, string name, string initialText,
            float fontSize, FontStyles style, Color color, Vector2 anchoredPos, Vector2 sizeDelta, TextAlignmentOptions alignment)
        {
            Transform existing = parent.Find(name);
            GameObject textObj;
            if (existing == null)
            {
                textObj = new GameObject(name);
                textObj.transform.SetParent(parent, false);
            }
            else
            {
                textObj = existing.gameObject;
            }

            RectTransform rt = textObj.GetComponent<RectTransform>();
            if (rt == null) rt = textObj.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = textObj.AddComponent<TextMeshProUGUI>();

            tmp.text = initialText;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;

            return tmp;
        }
    }
}
