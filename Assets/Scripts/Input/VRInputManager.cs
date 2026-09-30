using System;
using UnityEngine;

namespace VRSlashGame.Input
{
    /// <summary>
    /// Tracks VR controller velocity, angular speed, and calculates slash states for both hands.
    /// Combines Meta XR SDK hardware velocity with smoothed frame-delta velocity tracking
    /// to ensure 100% reliable slash detection in both headset hardware and Meta XR Simulator.
    /// </summary>
    public class VRInputManager : MonoBehaviour
    {
        private static VRInputManager _instance;
        public static VRInputManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<VRInputManager>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Controller Tracked Transforms")]
        [Tooltip("Transform for the Left Controller Anchor.")]
        [SerializeField] private Transform _leftHandTransform;

        [Tooltip("Transform for the Right Controller Anchor.")]
        [SerializeField] private Transform _rightHandTransform;

        [Header("Slash Detection Parameters")]
        [Tooltip("Minimum velocity (in m/s) required to consider a controller motion as a valid slash.")]
        [SerializeField] private float _slashSpeedThreshold = 1.8f;

        [Tooltip("Smoothing response (0 = instant raw delta, 1 = maximum smoothing).")]
        [Range(0.01f, 0.99f)]
        [SerializeField] private float _velocitySmoothing = 0.35f;

        [Header("Debug")]
        [SerializeField] private bool _drawDebugGizmos = true;
        [SerializeField] private float _gizmoVectorScale = 0.3f;

        [Header("Editor Fallback (Mouse Swing)")]
        [Tooltip("Enables mouse-driven blade swinging in Unity Editor when holding Left Mouse Button.")]
        [SerializeField] private bool _enableEditorMouseFallback = true;

        [Tooltip("Frequency/speed of the sword swing sweeps (sweeps per second).")]
        [SerializeField] private float _editorSwingSpeed = 3.2f;

        [Tooltip("Resting local position for the right hand blade in Editor when not swinging.")]
        [SerializeField] private Vector3 _editorRestLocalPos = new Vector3(0.35f, 1.15f, 0.75f);

        [Tooltip("Resting local rotation for the right hand blade in Editor.")]
        [SerializeField] private Vector3 _editorRestLocalEuler = new Vector3(20f, -15f, 0f);

        // Velocity tracking states
        private Vector3 _prevLeftPos;
        private Vector3 _prevRightPos;
        private Vector3 _smoothedLeftVelocity;
        private Vector3 _smoothedRightVelocity;

        private bool _isLeftSlashing;
        private bool _isRightSlashing;

        // Editor fallback runtime states
        private float _editorSwingTimer = 0f;
        private bool _isEditorMouseSwinging = false;

        public event Action<ControllerHand, Vector3> OnSlashStarted;
        public event Action<ControllerHand> OnSlashEnded;

        public float SlashSpeedThreshold
        {
            get => _slashSpeedThreshold;
            set => _slashSpeedThreshold = value;
        }

        public Transform LeftHandTransform => _leftHandTransform;
        public Transform RightHandTransform => _rightHandTransform;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            AutoFindAnchorsIfUnassigned();
        }

        [Header("Camera Eye Level (Editor Fallback)")]
        [Tooltip("Transform for CenterEyeAnchor. If unassigned, auto-found at runtime.")]
        [SerializeField] private Transform _centerEyeTransform;
        [SerializeField] private float _editorEyeHeight = 1.65f;

        public Transform CenterEyeTransform => _centerEyeTransform;

        private void Start()
        {
            InitializeAnchorPositions();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_enableEditorMouseFallback && _rightHandTransform != null)
            {
                if (_rightHandTransform.localPosition == Vector3.zero)
                {
                    _rightHandTransform.localPosition = _editorRestLocalPos;
                    _rightHandTransform.localRotation = Quaternion.Euler(_editorRestLocalEuler);
                    _prevRightPos = _rightHandTransform.position;
                }
            }

            if (_leftHandTransform != null)
            {
                _leftHandTransform.localPosition = Vector3.zero;
                _leftHandTransform.localRotation = Quaternion.identity;
                _prevLeftPos = _leftHandTransform.position;
            }

            if (_centerEyeTransform != null)
            {
                _centerEyeTransform.localPosition = Vector3.zero;
                _centerEyeTransform.localRotation = Quaternion.identity;
            }
#endif
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0.0001f) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UpdateEditorMouseFallback(dt);
#endif

            UpdateHandVelocity(ControllerHand.Left, _leftHandTransform, ref _prevLeftPos, ref _smoothedLeftVelocity, ref _isLeftSlashing, dt);
            UpdateHandVelocity(ControllerHand.Right, _rightHandTransform, ref _prevRightPos, ref _smoothedRightVelocity, ref _isRightSlashing, dt);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void LateUpdate()
        {
            if (_centerEyeTransform == null)
            {
                Camera mainCam = Camera.main;
                if (mainCam != null) _centerEyeTransform = mainCam.transform;
            }

            // Ensure CenterEyeAnchor stays at local (0,0,0) relative to OVRCameraRig (world pos 0, 0.40, 0)
            // matching Screenshot 157.
            if (_centerEyeTransform != null && _centerEyeTransform.localPosition != Vector3.zero)
            {
                _centerEyeTransform.localPosition = Vector3.zero;
                _centerEyeTransform.localRotation = Quaternion.identity;
            }

            // Ensure left hand anchor maintains its resting stance at local (0,0,0) matching Screenshot 157
            if (_leftHandTransform != null && _leftHandTransform.localPosition != Vector3.zero)
            {
                _leftHandTransform.localPosition = Vector3.zero;
                _leftHandTransform.localRotation = Quaternion.identity;
            }

            // Ensure right hand anchor maintains resting stance in Editor when not swinging
            if (!_isEditorMouseSwinging && _rightHandTransform != null && _rightHandTransform.localPosition.sqrMagnitude < 0.01f)
            {
                _rightHandTransform.localPosition = _editorRestLocalPos;
                _rightHandTransform.localRotation = Quaternion.Euler(_editorRestLocalEuler);
            }
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void UpdateEditorMouseFallback(float dt)
        {
            if (!_enableEditorMouseFallback || _rightHandTransform == null) return;

            bool isLeftMouseHeld = false;
            try
            {
                if (UnityEngine.InputSystem.Mouse.current != null &&
                    UnityEngine.InputSystem.Mouse.current.leftButton.isPressed)
                {
                    isLeftMouseHeld = true;
                }
            }
            catch { }

            if (isLeftMouseHeld)
            {
                _isEditorMouseSwinging = true;
                _editorSwingTimer += dt * _editorSwingSpeed;

                // Ping-pong across the screen: 0 = right side, 1 = left side
                float t = Mathf.PingPong(_editorSwingTimer, 1.0f);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                // Right to left sweep across the fruit flight zone in front of player
                Vector3 rightStart = new Vector3(0.65f, 1.35f, 1.25f);
                Vector3 leftEnd = new Vector3(-0.65f, 1.05f, 1.25f);

                Vector3 swingPos = Vector3.Lerp(rightStart, leftEnd, smoothT);

                // Dynamic rotation: blade tilts along cutting trajectory
                bool swingingLeft = Mathf.Repeat(_editorSwingTimer, 2.0f) < 1.0f;
                Quaternion cutRot = swingingLeft 
                    ? Quaternion.Euler(25f, -55f, 40f) 
                    : Quaternion.Euler(25f, 55f, -40f);

                _rightHandTransform.localPosition = swingPos;
                _rightHandTransform.localRotation = cutRot;
            }
            else if (_isEditorMouseSwinging)
            {
                // Smoothly return right hand back to ready resting stance
                _editorSwingTimer = 0f;
                _rightHandTransform.localPosition = Vector3.Lerp(_rightHandTransform.localPosition, _editorRestLocalPos, dt * 10f);
                _rightHandTransform.localRotation = Quaternion.Slerp(_rightHandTransform.localRotation, Quaternion.Euler(_editorRestLocalEuler), dt * 10f);

                if (Vector3.Distance(_rightHandTransform.localPosition, _editorRestLocalPos) < 0.01f)
                {
                    _rightHandTransform.localPosition = _editorRestLocalPos;
                    _rightHandTransform.localRotation = Quaternion.Euler(_editorRestLocalEuler);
                    _isEditorMouseSwinging = false;
                }
            }
        }
#endif

        private void InitializeAnchorPositions()
        {
            if (_leftHandTransform != null) _prevLeftPos = _leftHandTransform.position;
            if (_rightHandTransform != null) _prevRightPos = _rightHandTransform.position;
        }

        private void AutoFindAnchorsIfUnassigned()
        {
            if (_leftHandTransform == null)
            {
                GameObject leftAnchor = GameObject.Find("LeftHandAnchor");
                if (leftAnchor != null) _leftHandTransform = leftAnchor.transform;
            }

            if (_rightHandTransform == null)
            {
                GameObject rightAnchor = GameObject.Find("RightHandAnchor");
                if (rightAnchor != null) _rightHandTransform = rightAnchor.transform;
            }

            if (_centerEyeTransform == null)
            {
                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    _centerEyeTransform = mainCam.transform;
                }
                else
                {
                    GameObject centerEye = GameObject.Find("CenterEyeAnchor");
                    if (centerEye != null) _centerEyeTransform = centerEye.transform;
                }
            }
        }

        private void UpdateHandVelocity(
            ControllerHand hand,
            Transform handTransform,
            ref Vector3 prevPos,
            ref Vector3 smoothedVelocity,
            ref bool isSlashingState,
            float dt)
        {
            if (handTransform == null) return;

            // 1. Calculate delta-position velocity
            Vector3 currentPos = handTransform.position;
            Vector3 deltaVelocity = (currentPos - prevPos) / dt;
            prevPos = currentPos;

            // 2. Poll hardware velocity from Meta XR SDK if available
            Vector3 hardwareVelocity = Vector3.zero;
            OVRInput.Controller ovrController = (hand == ControllerHand.Left) ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            hardwareVelocity = OVRInput.GetLocalControllerVelocity(ovrController);

            // Transform local hardware velocity to world space if anchor has parent tracking space
            if (hardwareVelocity.sqrMagnitude > 0.001f && handTransform.parent != null)
            {
                hardwareVelocity = handTransform.parent.TransformDirection(hardwareVelocity);
            }

            // 3. Choose the most active velocity (delta vs hardware)
            // In Meta XR Simulator, hardware IMU velocity might be 0 while transform moves, so we take the max
            Vector3 activeVelocity = deltaVelocity.sqrMagnitude > hardwareVelocity.sqrMagnitude ? deltaVelocity : hardwareVelocity;

            // 4. Low-pass filter smoothing
            smoothedVelocity = Vector3.Lerp(activeVelocity, smoothedVelocity, _velocitySmoothing);

            // 5. Slash threshold evaluation
            float currentSpeed = smoothedVelocity.magnitude;
            bool wasSlashing = isSlashingState;
            isSlashingState = currentSpeed >= _slashSpeedThreshold;

            if (!wasSlashing && isSlashingState)
            {
                OnSlashStarted?.Invoke(hand, smoothedVelocity);
            }
            else if (wasSlashing && !isSlashingState)
            {
                OnSlashEnded?.Invoke(hand);
            }
        }

        #region Public Query API

        /// <summary>
        /// Returns the current world-space velocity vector for the specified hand.
        /// </summary>
        public Vector3 GetVelocity(ControllerHand hand)
        {
            return hand == ControllerHand.Left ? _smoothedLeftVelocity : _smoothedRightVelocity;
        }

        /// <summary>
        /// Returns the current speed in m/s for the specified hand.
        /// </summary>
        public float GetSpeed(ControllerHand hand)
        {
            return GetVelocity(hand).magnitude;
        }

        /// <summary>
        /// Checks whether the specified hand is currently slashing above the speed threshold.
        /// </summary>
        public bool IsSlashing(ControllerHand hand)
        {
            return hand == ControllerHand.Left ? _isLeftSlashing : _isRightSlashing;
        }

        /// <summary>
        /// Returns normalized slash cut direction for the specified hand.
        /// </summary>
        public Vector3 GetSlashDirection(ControllerHand hand)
        {
            Vector3 vel = GetVelocity(hand);
            return vel.sqrMagnitude > 0.001f ? vel.normalized : Vector3.forward;
        }

        /// <summary>
        /// Manually binds the controller anchors.
        /// </summary>
        public void SetHandAnchors(Transform left, Transform right)
        {
            _leftHandTransform = left;
            _rightHandTransform = right;
            InitializeAnchorPositions();
        }

        #endregion

        #region Debug Visualization

        private void OnDrawGizmos()
        {
            if (!_drawDebugGizmos) return;

            DrawHandGizmo(_leftHandTransform, _smoothedLeftVelocity, _isLeftSlashing);
            DrawHandGizmo(_rightHandTransform, _smoothedRightVelocity, _isRightSlashing);
        }

        private void DrawHandGizmo(Transform handTransform, Vector3 velocity, bool isSlashing)
        {
            if (handTransform == null) return;

            Gizmos.color = isSlashing ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(handTransform.position, 0.05f);
            Gizmos.DrawRay(handTransform.position, velocity * _gizmoVectorScale);
        }

        #endregion
    }
}
