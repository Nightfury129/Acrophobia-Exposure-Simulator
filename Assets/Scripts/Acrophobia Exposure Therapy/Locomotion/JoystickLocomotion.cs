using System;
using UnityEngine;

namespace Acrophobia.Locomotion
{
    /// <summary>
    /// Thumbstick (joystick) locomotion for classroom and seated/limited-space environments.
    ///
    /// Allows the participant or evaluator to traverse the scene using the VR controller
    /// thumbsticks (or WASD / arrow keys in simulator) without needing physical room-scale
    /// space to walk across distances.
    ///
    /// Movement is relative to where the participant is looking (head orientation).
    /// Works cooperatively with PlayerGroundingController:
    /// - CharacterController handles sliding against walls and railings.
    /// - Edge containment prevents walking off open ledges into open air.
    /// - Anti-clip prevents leaning through barriers.
    /// - Floor settling maintains accurate altitude.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class JoystickLocomotion : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Enable thumbstick/joystick translation.")]
        [SerializeField] private bool _enableMovement = true;

        [Tooltip("Calm walking speed in metres per second, matching a steady adult pace.")]
        [SerializeField, Range(0.5f, 5f)] private float _walkSpeed = 1.8f;

        [Tooltip("Fast walking / sprint speed (triggered by clicking the left thumbstick or Shift).")]
        [SerializeField, Range(1f, 8f)] private float _sprintSpeed = 3.0f;

        [Tooltip("Thumbstick deadzone to prevent drift.")]
        [SerializeField, Range(0.05f, 0.4f)] private float _deadzone = 0.15f;

        [Header("Rotation")]
        [Tooltip("Enable right thumbstick turning.")]
        [SerializeField] private bool _enableTurning = true;

        [Tooltip("Use snap turning (instant angle steps) to minimize motion sickness.")]
        [SerializeField] private bool _snapTurn = true;

        [Tooltip("Degrees per snap turn step.")]
        [SerializeField, Range(15f, 90f)] private float _snapTurnAngle = 45f;

        [Tooltip("Degrees per second when snap turn is disabled (smooth turn).")]
        [SerializeField, Range(30f, 180f)] private float _smoothTurnSpeed = 75f;

        [Header("References")]
        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private CharacterController _characterController;
        [SerializeField] private PlayerGroundingController _groundingController;
        [SerializeField] private Telemetry.SudsPromptController _suds;

        private bool _snapTurnDebounce;

        public bool EnableMovement
        {
            get => _enableMovement;
            set => _enableMovement = value;
        }

        public bool EnableTurning
        {
            get => _enableTurning;
            set => _enableTurning = value;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        public void ResolveReferences()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }

            if (_groundingController == null)
            {
                _groundingController = GetComponent<PlayerGroundingController>();
            }

            if (_suds == null)
            {
                _suds = FindAnyObjectByType<Telemetry.SudsPromptController>();
            }

            if (_centerEyeAnchor == null)
            {
                Transform found = transform.Find("TrackingSpace/CenterEyeAnchor");
                if (found != null)
                {
                    _centerEyeAnchor = found;
                }
                else
                {
                    Camera cam = Camera.main;
                    if (cam != null) _centerEyeAnchor = cam.transform;
                }
            }
        }

        private void Update()
        {
            if (_centerEyeAnchor == null || _characterController == null)
            {
                ResolveReferences();
                if (_centerEyeAnchor == null || _characterController == null) return;
            }

            // Pause locomotion when the SUDS distress rating prompt is open
            // so thumbstick input goes to dialing the distress value instead of moving.
            if (_suds != null && _suds.IsOpen)
            {
                return;
            }

            if (_enableTurning)
            {
                UpdateTurning();
            }

            if (_enableMovement)
            {
                UpdateMovement();
            }
        }

        private void UpdateMovement()
        {
            Vector2 input = ReadMoveInput();
            if (input.sqrMagnitude < (_deadzone * _deadzone))
            {
                return;
            }

            bool isSprinting = ReadSprintInput();
            float speed = isSprinting ? _sprintSpeed : _walkSpeed;

            // Movement direction follows horizontal head orientation
            Vector3 forward = _centerEyeAnchor.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 right = _centerEyeAnchor.right;
            right.y = 0f;
            right.Normalize();

            Vector3 moveDir = forward * input.y + right * input.x;
            if (moveDir.sqrMagnitude > 1f)
            {
                moveDir.Normalize();
            }

            Vector3 motion = moveDir * (speed * Time.deltaTime);

            if (_characterController != null && _characterController.enabled)
            {
                Vector3 step = motion;
                step.y -= 0.5f * Time.deltaTime;
                Vector3 before = transform.position;
                _characterController.Move(step);

                Vector3 delta = transform.position - before;
                delta.y = 0f;
                // If CharacterController caught on floor collider seams, apply direct motion fallback
                if (delta.sqrMagnitude < (motion.sqrMagnitude * 0.05f))
                {
                    transform.position = before + motion;
                    delta = motion;
                }

                _groundingController?.NotifyRigMoved(delta);
            }
            else
            {
                transform.position += motion;
                _groundingController?.NotifyRigMoved(motion);
            }
        }

        private void UpdateTurning()
        {
            float turnInput = ReadTurnInput();

            if (_snapTurn)
            {
                // Snap turn flick
                if (Mathf.Abs(turnInput) > 0.6f)
                {
                    if (!_snapTurnDebounce)
                    {
                        float angle = Mathf.Sign(turnInput) * _snapTurnAngle;
                        RotateAroundHead(angle);
                        _snapTurnDebounce = true;
                    }
                }
                else if (Mathf.Abs(turnInput) < 0.2f)
                {
                    _snapTurnDebounce = false;
                }
            }
            else
            {
                // Smooth turn
                if (Mathf.Abs(turnInput) > _deadzone)
                {
                    float angle = turnInput * _smoothTurnSpeed * Time.deltaTime;
                    RotateAroundHead(angle);
                }
            }
        }

        /// <summary>
        /// Rotates the rig around the tracked head position on the horizontal plane.
        /// Prevents orbital displacement when the participant's physical head is not at (0,0).
        /// </summary>
        private void RotateAroundHead(float angle)
        {
            if (_centerEyeAnchor != null)
            {
                Vector3 headPos = _centerEyeAnchor.position;
                Vector3 pivot = new Vector3(headPos.x, transform.position.y, headPos.z);
                transform.RotateAround(pivot, Vector3.up, angle);
            }
            else
            {
                transform.Rotate(0f, angle, 0f, Space.World);
            }
        }

        private Vector2 ReadMoveInput()
        {
            Vector2 stick = Vector2.zero;

            // 1. Right Thumbstick (Move forward/back/strafe)
            try
            {
                stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
                if (stick.sqrMagnitude < 0.001f)
                {
                    stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.RTouch);
                }
            }
            catch (Exception) { }

            // 1b. UnityEngine.XR CommonUsages fallback (Right controller)
            if (stick.sqrMagnitude < 0.001f)
            {
                try
                {
                    var rightDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                    UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                        UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller,
                        rightDevices);
                    if (rightDevices.Count > 0 && rightDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
                    {
                        stick = axis;
                    }
                }
                catch (Exception) { }
            }

            // 2. Keyboard fallback / Meta XR Simulator WASD & Arrows
#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) stick.y += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) stick.y -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) stick.x += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) stick.x -= 1f;
                }
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) stick.y += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) stick.y -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) stick.x += 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) stick.x -= 1f;
            }
            catch (Exception) { }
#endif

            return stick;
        }

        private float ReadTurnInput()
        {
            float turn = 0f;

            // 1. Left Thumbstick (Turn / look around left & right)
            try
            {
                turn = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).x;
                if (Mathf.Abs(turn) < 0.001f)
                {
                    turn = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch).x;
                }
            }
            catch (Exception) { }

            // 1b. UnityEngine.XR CommonUsages fallback (Left controller)
            if (Mathf.Abs(turn) < 0.001f)
            {
                try
                {
                    var leftDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                    UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                        UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller,
                        leftDevices);
                    if (leftDevices.Count > 0 && leftDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
                    {
                        turn = axis.x;
                    }
                }
                catch (Exception) { }
            }

            // 2. Keyboard turn fallback (Q / E keys)
#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.eKey.isPressed) turn += 1f;
                    if (kb.qKey.isPressed) turn -= 1f;
                }
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKey(KeyCode.E)) turn += 1f;
                if (Input.GetKey(KeyCode.Q)) turn -= 1f;
            }
            catch (Exception) { }
#endif

            return turn;
        }

        private bool ReadSprintInput()
        {
            try
            {
                if (OVRInput.Get(OVRInput.Button.SecondaryThumbstick) ||
                    OVRInput.Get(OVRInput.Button.PrimaryThumbstick))
                {
                    return true;
                }
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)) return true;
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return true;
            }
            catch (Exception) { }
#endif

            return false;
        }
    }
}
