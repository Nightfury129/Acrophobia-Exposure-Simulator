using System;
using UnityEngine;
using Acrophobia.SignalProcessing;

namespace Acrophobia.GazeGeometry
{
    /// <summary>
    /// Casts downward raycasts and forward edge probes to calculate distance to the nearest
    /// unguarded catwalk edge. When distance to the edge is under 0.6m, computes an aggregate
    /// HesitationIndex (0 to 100) combining GazeAvoidanceRatio, slow locomotion speed, and TremorIntensity.
    /// Exposes HesitationIndex and gaze angles as public properties for real-time tracking.
    /// </summary>
    public class EdgeProximityTracker : MonoBehaviour
    {
        [Header("Edge Probe Configuration")]
        [Tooltip("Maximum horizontal probe distance in meters to search for catwalk edges.")]
        [SerializeField, Range(1f, 6f)] private float _maxProbeDistance = 4.0f;

        [Tooltip("Horizontal step size in meters between downward probe raycasts.")]
        [SerializeField, Range(0.02f, 0.2f)] private float _probeStepDistance = 0.05f;

        [Tooltip("Distance threshold (0.6m) within which edge hesitation is actively monitored.")]
        [SerializeField] private float _nearEdgeThreshold = 0.6f;

        [Tooltip("Maximum vertical drop considered solid floor before classifying as open air/edge.")]
        [SerializeField, Range(0.3f, 3.0f)] private float _edgeDropThreshold = 0.8f;

        [Tooltip("Angular fan in degrees (+/- half-angle) for forward edge probes.")]
        [SerializeField, Range(15f, 90f)] private float _probeFanAngle = 60f;

        [Tooltip("Number of radial rays in the forward probe fan.")]
        [SerializeField, Range(5, 21)] private int _probeRayCount = 11;

        [Header("Hesitation Weights (Sum to 1.0)")]
        [SerializeField, Range(0f, 1f)] private float _weightGazeAvoidance = 0.40f;
        [SerializeField, Range(0f, 1f)] private float _weightLocomotionSlowness = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _weightTremor = 0.25f;

        [Tooltip("Locomotion speed in m/s below which the user is considered hesitating/crawling.")]
        [SerializeField, Range(0.2f, 1.5f)] private float _normalWalkSpeed = 0.8f;

        [Header("Component References")]
        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private GazeAvoidanceDetector _gazeDetector;
        [SerializeField] private TremorBandFilter _tremorFilter;
        [SerializeField] private HeadPoseSampler _headPoseSampler;

        [Header("Runtime Metrics")]
        [SerializeField] private float _distanceToEdge = 999f;
        [SerializeField] private bool _isNearEdge;
        [SerializeField] private float _hesitationIndex;
        [SerializeField] private float _currentSpeed;
        [SerializeField] private float _currentTremor;
        [SerializeField] private float _currentGazeAngle;
        [SerializeField] private float _currentAvoidanceRatio;

        /// <summary>
        /// Distance in meters to the nearest unguarded catwalk edge.
        /// </summary>
        public float DistanceToEdge => _distanceToEdge;

        /// <summary>
        /// True if the user is within the 0.6m near-edge hesitation zone.
        /// </summary>
        public bool IsNearEdge => _isNearEdge;

        /// <summary>
        /// Aggregate Behavioral Hesitation Index (0 to 100).
        /// Combines GazeAvoidanceRatio, slow locomotion speed, and TremorIntensity near the edge.
        /// </summary>
        public float HesitationIndex => _hesitationIndex;

        /// <summary>
        /// Gaze angle in degrees relative to the downward vertical vector (relayed from GazeAvoidanceDetector).
        /// </summary>
        public float GazeAngleWithDown => _gazeDetector != null ? _gazeDetector.GazeAngleWithDown : _currentGazeAngle;

        /// <summary>
        /// Fraction of time (0.0 to 1.0) the user actively avoids looking down while near the edge.
        /// </summary>
        public float GazeAvoidanceRatio => _gazeDetector != null ? _gazeDetector.GazeAvoidanceRatio : _currentAvoidanceRatio;

        /// <summary>
        /// Current head locomotion speed in m/s.
        /// </summary>
        public float LocomotionSpeed => _currentSpeed;

        /// <summary>
        /// Current normalized tremor intensity (0.0 to 1.0).
        /// </summary>
        public float TremorIntensity => _currentTremor;

        /// <summary>
        /// Event invoked whenever the HesitationIndex is updated.
        /// </summary>
        public event Action<float> OnHesitationIndexUpdated;

        /// <summary>
        /// Event invoked whenever the distance to the nearest edge is updated.
        /// </summary>
        public event Action<float> OnEdgeProximityUpdated;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (_centerEyeAnchor == null)
            {
                ResolveReferences();
                if (_centerEyeAnchor == null) return;
            }

            // 1. Probe distance to nearest unguarded edge
            _distanceToEdge = ComputeDistanceToNearestUnguardedEdge();
            _isNearEdge = _distanceToEdge < _nearEdgeThreshold;

            // Notify GazeAvoidanceDetector of near-edge state
            if (_gazeDetector != null)
            {
                _gazeDetector.IsNearEdge = _isNearEdge;
                _currentGazeAngle = _gazeDetector.GazeAngleWithDown;
                _currentAvoidanceRatio = _gazeDetector.GazeAvoidanceRatio;
            }

            // 2. Read kinematics and tremor
            _currentSpeed = _headPoseSampler != null ? _headPoseSampler.LatestSpeed : 0f;
            _currentTremor = _tremorFilter != null ? _tremorFilter.TremorIntensity : 0f;

            // 3. Compute HesitationIndex (0 to 100) when within 0.6m of edge
            if (_isNearEdge)
            {
                _hesitationIndex = ComputeHesitationIndex(_currentAvoidanceRatio, _currentSpeed, _currentTremor, _distanceToEdge);
            }
            else
            {
                _hesitationIndex = 0f;
            }

            OnEdgeProximityUpdated?.Invoke(_distanceToEdge);
            OnHesitationIndexUpdated?.Invoke(_hesitationIndex);
        }

        /// <summary>
        /// Casts downward raycasts and forward edge probes across a horizontal fan
        /// to determine distance to the nearest unguarded catwalk edge.
        /// </summary>
        public float ComputeDistanceToNearestUnguardedEdge()
        {
            Transform headTransform = _centerEyeAnchor != null ? _centerEyeAnchor : transform;
            Vector3 headPos = headTransform.position;

            // Find current ground/floor surface below player's feet
            float floorY = headPos.y - 1.6f;
            if (Physics.Raycast(headPos + Vector3.up * 0.1f, Vector3.down, out RaycastHit groundHit, 3.0f, ~0, QueryTriggerInteraction.Ignore))
            {
                floorY = groundHit.point.y;
            }

            Vector3 feetOrigin = new Vector3(headPos.x, floorY + 0.05f, headPos.z);
            Vector3 forward = headTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            float minEdgeDistance = _maxProbeDistance;
            int rayCount = Mathf.Max(3, _probeRayCount);
            float startAngle = -_probeFanAngle;
            float angleStep = (_probeFanAngle * 2f) / (rayCount - 1);

            for (int r = 0; r < rayCount; r++)
            {
                float angle = startAngle + r * angleStep;
                Quaternion rot = Quaternion.Euler(0f, angle, 0f);
                Vector3 probeDir = rot * forward;

                // Step outward horizontally from feet
                for (float dist = _probeStepDistance; dist <= _maxProbeDistance; dist += _probeStepDistance)
                {
                    Vector3 probePos = feetOrigin + probeDir * dist;

                    // Check if there is an impenetrable guard railing blocking this path
                    if (Physics.Raycast(feetOrigin + Vector3.up * 0.7f, probeDir, out RaycastHit barrierHit, dist, ~0, QueryTriggerInteraction.Ignore))
                    {
                        // If blocked by a railing collider, this ray path is safely guarded!
                        break;
                    }

                    // Cast downward from probe position to check for catwalk surface
                    Vector3 rayStart = probePos + Vector3.up * 0.3f;
                    if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit probeHit, _edgeDropThreshold + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        // Solid catwalk surface is still beneath this probe point
                        float drop = floorY - probeHit.point.y;
                        if (drop > _edgeDropThreshold)
                        {
                            // Floor drops significantly -> edge found
                            if (dist < minEdgeDistance) minEdgeDistance = dist;
                            break;
                        }
                    }
                    else
                    {
                        // No solid ground within edgeDropThreshold -> open air precipice found!
                        if (dist < minEdgeDistance) minEdgeDistance = dist;
                        break;
                    }
                }
            }

            return minEdgeDistance;
        }

        /// <summary>
        /// Computes the aggregate HesitationIndex (0 to 100) combining GazeAvoidanceRatio,
        /// slow locomotion speed, and TremorIntensity.
        /// </summary>
        public float ComputeHesitationIndex(float gazeAvoidanceRatio, float speed, float tremorIntensity, float distanceToEdge)
        {
            // 1. Gaze avoidance factor: [0, 1]
            float gazeFactor = Mathf.Clamp01(gazeAvoidanceRatio);

            // 2. Locomotion slowness factor: [0, 1] (1.0 = completely frozen/stopped, 0.0 = normal walking)
            float slownessFactor = 1.0f - Mathf.Clamp01(speed / Mathf.Max(0.1f, _normalWalkSpeed));

            // 3. Physiological tremor factor: [0, 1]
            float tremorFactor = Mathf.Clamp01(tremorIntensity);

            // Combine factors with configured weights
            float rawHesitation = _weightGazeAvoidance * gazeFactor +
                                  _weightLocomotionSlowness * slownessFactor +
                                  _weightTremor * tremorFactor;

            // Proximity weighting: increases as player gets closer to 0.0m edge
            float proximityScale = Mathf.Clamp01((_nearEdgeThreshold - distanceToEdge) / _nearEdgeThreshold);
            float scaledHesitation = rawHesitation * (0.6f + 0.4f * proximityScale);

            return Mathf.Clamp(scaledHesitation * 100.0f, 0f, 100f);
        }

        private void ResolveReferences()
        {
            if (_centerEyeAnchor == null)
            {
                if (name.Contains("CenterEyeAnchor") || GetComponent<Camera>() != null)
                {
                    _centerEyeAnchor = transform;
                }
                else
                {
                    var centerEye = GameObject.Find("CenterEyeAnchor");
                    if (centerEye != null) _centerEyeAnchor = centerEye.transform;
                    else if (Camera.main != null) _centerEyeAnchor = Camera.main.transform;
                }
            }

            if (_gazeDetector == null)
            {
                _gazeDetector = GetComponent<GazeAvoidanceDetector>();
                if (_gazeDetector == null) _gazeDetector = FindAnyObjectByType<GazeAvoidanceDetector>();
            }

            if (_tremorFilter == null)
            {
                _tremorFilter = GetComponent<TremorBandFilter>();
                if (_tremorFilter == null) _tremorFilter = FindAnyObjectByType<TremorBandFilter>();
            }

            if (_headPoseSampler == null)
            {
                _headPoseSampler = GetComponent<HeadPoseSampler>();
                if (_headPoseSampler == null) _headPoseSampler = FindAnyObjectByType<HeadPoseSampler>();
            }
        }
    }
}
