using System.Collections;
using UnityEngine;

namespace Acrophobia.ProceduralEnvironment
{
    /// <summary>
    /// Applies gravity, physical collision, and falling mechanics to the VR player rig.
    /// Grounds the player on physical platforms/catwalks based on their actual tracked feet position.
    /// When walking off an edge into open air, gravity takes over and accelerates the player downward.
    /// Detects fall below threshold height (Y < 15m), fades the camera to black with a wind whoosh cue,
    /// and safely respawns the player back on the rooftop starting ledge.
    /// Includes full-body roomscale anti-clip to prevent walking or leaning through railing colliders.
    /// Supports both Touch controller thumbsticks (OVRInput) and keyboard (WASD).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerGravityFall : MonoBehaviour
    {
        [Header("Gravity & Physics")]
        [Tooltip("Downward gravitational acceleration in m/s^2.")]
        [SerializeField] private float gravity = -9.81f;

        [Tooltip("Terminal fall velocity in m/s.")]
        [SerializeField] private float terminalVelocity = -50f;

        [Tooltip("Downward velocity to stick player to ground colliders.")]
        [SerializeField] private float groundedStickVelocity = -2.0f;

        [Header("Ground Detection (Under Tracked Feet)")]
        [Tooltip("Maximum ground check distance beneath player feet.")]
        [SerializeField] private float groundCheckDistance = 0.45f;

        [Tooltip("Radius of spherecast beneath player feet.")]
        [SerializeField] private float groundCheckRadius = 0.20f;

        [Header("Fall Detection & Respawn")]
        [Tooltip("World Y height threshold below which fall blackout and respawn are triggered.")]
        [SerializeField] private float respawnThresholdY = 15f;

        [Tooltip("Safe respawn position on concrete rooftop.")]
        [SerializeField] private Vector3 respawnPosition = new Vector3(-13.0f, 104.2f, -13.0f);

        [Tooltip("Duration of screen fade to black.")]
        [SerializeField] private float fadeDuration = 0.6f;

        [Tooltip("Duration of pause at blackout before fading back in.")]
        [SerializeField] private float blackoutHoldTime = 0.5f;

        [Header("Audio & Sensory Cues")]
        [Tooltip("Wind whoosh audio clip played when falling.")]
        [SerializeField] private AudioClip windWhooshClip;

        [SerializeField] private AudioSource audioSource;

        [Header("Locomotion (Testing & VR)")]
        [Tooltip("Enable thumbstick and keyboard movement.")]
        [SerializeField] private bool enableLocomotion = true;

        [Tooltip("Horizontal movement speed in m/s.")]
        [SerializeField] private float moveSpeed = 2.5f;

        [Header("Roomscale Anti-Clip & Boundary Enforcement")]
        [Tooltip("Enable full-body collision check to prevent stepping or leaning through railings.")]
        [SerializeField] private bool enableBodyAntiClip = true;

        [Tooltip("Collision capsule radius for player body.")]
        [SerializeField] private float bodyRadius = 0.22f;

        [Header("Tracking References")]
        [SerializeField] private Transform centerEyeAnchor;

        private CharacterController characterController;
        private Vector3 velocity = Vector3.zero;
        private bool isRespawning = false;
        private bool isFalling = false;
        private bool windWhooshPlayed = false;
        private bool isGroundedUnderFeet = true;

        // Overlay fade canvas for blackout across Editor, Simulator, and Quest
        private Canvas fadeCanvas;
        private UnityEngine.UI.Image fadeImage;

        public bool IsGrounded => isGroundedUnderFeet;
        public bool IsFalling => isFalling;
        public Vector3 CurrentVelocity => velocity;
        public Vector3 RespawnPosition { get => respawnPosition; set => respawnPosition = value; }

        public event System.Action OnFallStarted;
        public event System.Action OnFallTriggered;
        public event System.Action OnPlayerRespawned;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            // Configure CharacterController
            characterController.radius = 0.25f;
            characterController.height = 1.8f;
            characterController.center = new Vector3(0f, 0.9f, 0f);
            characterController.skinWidth = 0.05f;
            characterController.stepOffset = 0.3f;
            characterController.minMoveDistance = 0.0f;

            if (centerEyeAnchor == null)
            {
                Transform found = transform.Find("TrackingSpace/CenterEyeAnchor");
                if (found != null) centerEyeAnchor = found;
                else
                {
                    Camera cam = Camera.main;
                    if (cam != null) centerEyeAnchor = cam.transform;
                }
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.playOnAwake = false;
                    audioSource.spatialBlend = 0.0f; // 2D stereo for maximum wind rush presence
                }
            }

            SetupFadeOverlay();
        }

        private void Start()
        {
            if (windWhooshClip == null)
            {
#if UNITY_EDITOR
                windWhooshClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/Audio/Acrophobia Exposure Therapy/Wind_gust_strong.mp3");
#endif
            }
        }

        private void Update()
        {
            if (isRespawning) return;

            // 1. Keep CharacterController aligned with player's tracked physical head & height
            SynchronizeCharacterControllerWithHMD();

            // 2. Full-Body Roomscale Anti-Clip: Prevent physical stepping or leaning through railings
            if (enableBodyAntiClip && centerEyeAnchor != null)
            {
                PerformBodyAntiClip();
            }

            // 3. Ground Detection directly beneath player's tracked feet
            isGroundedUnderFeet = CheckGroundUnderFeet();

            // 4. Horizontal Locomotion (WASD / Quest Touch thumbstick)
            Vector3 horizontalVelocity = Vector3.zero;
            if (enableLocomotion)
            {
                horizontalVelocity = CalculateInputVelocity();
            }

            // 5. Continuous Downward Gravity & Grounding
            if (isGroundedUnderFeet && characterController.isGrounded)
            {
                velocity.y = groundedStickVelocity;
                if (isFalling)
                {
                    isFalling = false;
                    windWhooshPlayed = false;
                }
            }
            else
            {
                // Not grounded beneath feet: apply downward gravity acceleration!
                velocity.y += gravity * Time.deltaTime;
                if (velocity.y < terminalVelocity)
                {
                    velocity.y = terminalVelocity;
                }

                if (!isFalling && velocity.y < -3.0f)
                {
                    isFalling = true;
                    OnFallStarted?.Invoke();
                }

                // If falling, smoothly center the rig root under the player's head so camera drops with the body
                if (centerEyeAnchor != null)
                {
                    Vector3 feetWorld = new Vector3(centerEyeAnchor.position.x, transform.position.y, centerEyeAnchor.position.z);
                    Vector3 delta = (feetWorld - transform.position);
                    delta.y = 0f;
                    transform.position += delta * Mathf.Clamp01(6f * Time.deltaTime);
                }

                // Play rushing wind cue when falling past altitude
                if (transform.position.y < 85f && !windWhooshPlayed)
                {
                    windWhooshPlayed = true;
                    if (audioSource != null && windWhooshClip != null)
                    {
                        audioSource.PlayOneShot(windWhooshClip, 1.0f);
                    }
                }
            }

            // 6. Combine horizontal locomotion + vertical gravity into single movement
            velocity.x = horizontalVelocity.x;
            velocity.z = horizontalVelocity.z;

            // Move via CharacterController for continuous sweep collision detection
            characterController.Move(velocity * Time.deltaTime);

            // 7. Check fall threshold height (Y < 15m) -> Trigger blackout and respawn
            if (transform.position.y < respawnThresholdY)
            {
                StartCoroutine(RespawnSequence());
            }
        }

        /// <summary>
        /// Adjusts the CharacterController height and horizontal center to match the HMD's local position.
        /// Ensures physical collision matches where the player's physical body is in the room.
        /// </summary>
        private void SynchronizeCharacterControllerWithHMD()
        {
            if (centerEyeAnchor == null) return;

            Vector3 localHead = centerEyeAnchor.localPosition;
            float playerHeight = Mathf.Clamp(localHead.y, 1.0f, 2.2f);

            characterController.height = playerHeight;
            characterController.center = new Vector3(localHead.x, playerHeight * 0.5f, localHead.z);
        }

        /// <summary>
        /// Detects if there is solid physical ground directly beneath the player's tracked feet.
        /// This ensures the player cannot float if they step out past the edge of the catwalk.
        /// </summary>
        private bool CheckGroundUnderFeet()
        {
            Vector3 feetOrigin;
            if (centerEyeAnchor != null)
            {
                feetOrigin = new Vector3(centerEyeAnchor.position.x, transform.position.y + 0.25f, centerEyeAnchor.position.z);
            }
            else
            {
                feetOrigin = transform.position + Vector3.up * 0.25f;
            }

            // Spherecast downwards to detect solid ground
            RaycastHit[] hits = Physics.SphereCastAll(feetOrigin, groundCheckRadius, Vector3.down, groundCheckDistance, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider == characterController) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.isTrigger) continue;

                // Found solid ground beneath feet
                return true;
            }

            return false;
        }

        /// <summary>
        /// Prevents physical roomscale walking or leaning through railing colliders.
        /// Checks a full-body vertical capsule from feet to head.
        /// </summary>
        private void PerformBodyAntiClip()
        {
            Vector3 feetPos = new Vector3(centerEyeAnchor.position.x, transform.position.y + 0.25f, centerEyeAnchor.position.z);
            Vector3 headPos = new Vector3(centerEyeAnchor.position.x, centerEyeAnchor.position.y - 0.15f, centerEyeAnchor.position.z);

            if (headPos.y <= feetPos.y) headPos.y = feetPos.y + 0.5f;

            Collider[] overlaps = Physics.OverlapCapsule(feetPos, headPos, bodyRadius, ~0, QueryTriggerInteraction.Ignore);

            foreach (var col in overlaps)
            {
                // Ignore rig's own CharacterController and child colliders
                if (col == characterController || col.transform.IsChildOf(transform)) continue;
                if (col.isTrigger) continue;

                // Find closest point on penetrated collider
                Vector3 closest = col.ClosestPoint(centerEyeAnchor.position);
                Vector3 pushDir = centerEyeAnchor.position - closest;
                pushDir.y = 0f;

                if (pushDir.sqrMagnitude < 0.0001f)
                {
                    pushDir = -centerEyeAnchor.forward;
                    pushDir.y = 0f;
                }
                pushDir.Normalize();

                // Push the rig back so the player cannot penetrate railings
                float penetration = bodyRadius - Vector3.Distance(new Vector3(centerEyeAnchor.position.x, closest.y, centerEyeAnchor.position.z), closest);
                float pushDistance = Mathf.Max(0.04f, penetration + 0.03f);

                characterController.Move(pushDir * pushDistance);
                break;
            }
        }

        /// <summary>
        /// Reads keyboard (WASD / Arrows) and Meta Quest Touch Controller thumbsticks.
        /// Compatible with both New Input System and Legacy Input.
        /// </summary>
        private Vector3 CalculateInputVelocity()
        {
            float horizontal = 0f;
            float vertical = 0f;

            // 1. Keyboard Input via New Input System
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) vertical += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) vertical -= 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) horizontal -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) horizontal += 1f;
            }
#else
            try
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal += 1f;
            }
            catch (System.InvalidOperationException)
            {
                // New Input System is active in PlayerSettings
            }
#endif

            // 2. Meta Quest Primary Touch Thumbstick (Left controller)
            try
            {
                Vector2 touchThumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
                if (touchThumbstick.sqrMagnitude > 0.01f)
                {
                    horizontal += touchThumbstick.x;
                    vertical += touchThumbstick.y;
                }

                // 3. Optional Secondary Thumbstick (Right controller) support
                Vector2 secondaryThumbstick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
                if (secondaryThumbstick.sqrMagnitude > 0.01f)
                {
                    horizontal += secondaryThumbstick.x;
                    vertical += secondaryThumbstick.y;
                }
            }
            catch (System.Exception)
            {
                // OVR runtime not active in non-VR test
            }

            Vector3 forward = centerEyeAnchor != null ? centerEyeAnchor.forward : transform.forward;
            Vector3 right = centerEyeAnchor != null ? centerEyeAnchor.right : transform.right;

            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            Vector3 moveDir = (forward * vertical + right * horizontal);
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            return moveDir * moveSpeed;
        }

        private IEnumerator RespawnSequence()
        {
            isRespawning = true;
            OnFallTriggered?.Invoke();

            if (!windWhooshPlayed && audioSource != null && windWhooshClip != null)
            {
                audioSource.PlayOneShot(windWhooshClip, 1.0f);
                windWhooshPlayed = true;
            }

            // Fade to black
            yield return StartCoroutine(FadeRoutine(0f, 1f, fadeDuration));

            yield return new WaitForSeconds(blackoutHoldTime);

            // Teleport safely back to rooftop start
            characterController.enabled = false;
            transform.position = respawnPosition;
            velocity = new Vector3(0f, groundedStickVelocity, 0f);
            characterController.enabled = true;

            // Fade back in
            yield return StartCoroutine(FadeRoutine(1f, 0f, fadeDuration));

            isFalling = false;
            windWhooshPlayed = false;
            isRespawning = false;
            OnPlayerRespawned?.Invoke();
        }

        private void SetupFadeOverlay()
        {
            if (centerEyeAnchor == null) return;

            GameObject canvasObj = new GameObject("PlayerFallFadeCanvas");
            canvasObj.transform.SetParent(centerEyeAnchor, false);
            canvasObj.transform.localPosition = new Vector3(0f, 0f, 0.25f);
            canvasObj.transform.localRotation = Quaternion.identity;
            canvasObj.transform.localScale = Vector3.one * 0.001f;

            fadeCanvas = canvasObj.AddComponent<Canvas>();
            fadeCanvas.renderMode = RenderMode.WorldSpace;
            fadeCanvas.sortingOrder = 32767;

            RectTransform rt = canvasObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000f, 1000f);

            GameObject imageObj = new GameObject("BlackoutImage");
            imageObj.transform.SetParent(canvasObj.transform, false);
            RectTransform imgRt = imageObj.AddComponent<RectTransform>();
            imgRt.anchorMin = Vector2.zero;
            imgRt.anchorMax = Vector2.one;
            imgRt.sizeDelta = Vector2.zero;

            fadeImage = imageObj.AddComponent<UnityEngine.UI.Image>();
            fadeImage.color = new Color(0f, 0f, 0f, 0f);
            fadeImage.raycastTarget = false;
        }

        private IEnumerator FadeRoutine(float fromAlpha, float toAlpha, float duration)
        {
            if (fadeImage == null) yield break;

            float elapsed = 0f;
            Color c = Color.black;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                c.a = Mathf.Lerp(fromAlpha, toAlpha, t);
                fadeImage.color = c;
                yield return null;
            }
            c.a = toAlpha;
            fadeImage.color = c;
        }
    }
}
