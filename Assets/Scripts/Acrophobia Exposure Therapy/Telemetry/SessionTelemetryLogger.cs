using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Acrophobia.AdaptiveFSM;
using Acrophobia.GazeGeometry;
using Acrophobia.SignalProcessing;

namespace Acrophobia.Telemetry
{
    /// <summary>
    /// Writes per-session exposure telemetry to CSV, incrementally, without allocating
    /// on the hot path.
    ///
    /// Two files per session, because continuous signals and discrete events have
    /// different shapes and want different analysis:
    ///   session_&lt;stamp&gt;_samples.csv  fixed-rate time series
    ///   session_&lt;stamp&gt;_events.csv   SUDS responses, tier changes, retreats, summary
    ///
    /// Sampling runs at a fixed rate (default 10 Hz) rather than per frame. At 72 FPS a
    /// per-frame row would be 7x the data for no extra information - postural sway sits
    /// below 1 Hz and tremor below 12 Hz, so 10 Hz is already generous for everything
    /// except the tremor envelope, which TremorBandFilter has itself integrated.
    ///
    /// All numbers are formatted through CsvLineBuffer, so a row costs zero managed
    /// allocations. Enum names are served from cached string tables for the same reason.
    ///
    /// This class only reads from the existing telemetry components. It never drives
    /// them, so it can be dropped into a scene without changing behaviour.
    /// </summary>
    public class SessionTelemetryLogger : MonoBehaviour
    {
        private const string TelemetryFolderName = "Telemetry";
        private const int LineCapacity = 2048;

        [SerializeField, Range(1f, 72f)] private float _sampleRateHz = 10f;

        [Tooltip("How often buffered rows are pushed to disk. Caps data lost to a crash.")]
        [SerializeField, Range(0.25f, 10f)] private float _flushIntervalSeconds = 2.0f;

        [Tooltip("World Y treated as ground level when computing height above ground.")]
        [SerializeField] private float _groundReferenceY = 0f;

        [Tooltip("Start logging as soon as the component wakes, rather than waiting for BeginSession().")]
        [SerializeField] private bool _autoBeginOnStart = true;

        [SerializeField] private bool _verboseConsoleLogging = false;

        [SerializeField] private Transform _headTransform;
        [SerializeField] private HeadPoseSampler _headPoseSampler;
        [SerializeField] private TremorBandFilter _tremorFilter;
        [SerializeField] private PosturalSwayAnalyzer _swayAnalyzer;
        [SerializeField] private EdgeProximityTracker _edgeTracker;
        [SerializeField] private GazeAvoidanceDetector _gazeDetector;
        [SerializeField] private AdaptiveExposureFSM _fsm;

        private CsvLineBuffer _line;
        private StreamWriter _sampleWriter;
        private StreamWriter _eventWriter;

        private bool _isLogging;
        private float _sessionStartTime;
        private float _sampleAccumulator;
        private float _flushAccumulator;
        private float _sampleInterval;

        private string _sessionId;
        private string _sampleFilePath;
        private string _eventFilePath;

        // Session aggregates, reported in the events file on close.
        private float _maxHeightAboveGround;
        private float _maxDistanceFromStart;
        private float _downwardGazeSeconds;
        private int _retreatEventCount;
        private int _sudsResponseCount;
        private int _samplesWritten;
        private Vector3 _sessionStartHeadPosition;

        // Index 0 is unused so a tier number indexes directly.
        private const int MaxTiers = 8;
        private readonly float[] _dwellSecondsPerTier = new float[MaxTiers + 1];
        private int _observedTier = 1;

        private float _calibratedEyeHeight = -1f;

        // Enum.ToString() allocates, so names are served from fixed tables instead.
        private static readonly string[] StateNames =
        {
            "AwaitingStart", "Exposing", "SafetyRecovery", "Completed"
        };

        public bool IsLogging => _isLogging;

        public string SessionId => _sessionId;

        public string SampleFilePath => _sampleFilePath;

        public string EventFilePath => _eventFilePath;

        public float MaxHeightAboveGround => _maxHeightAboveGround;

        public int RetreatEventCount => _retreatEventCount;

        public float DownwardGazeSeconds => _downwardGazeSeconds;

        /// <summary>
        /// Seconds since this session began logging. The time base for every row.
        /// </summary>
        public float SessionTime => _isLogging ? Time.time - _sessionStartTime : 0f;

        private void Awake()
        {
            EnsureBuffer();
            ResolveReferences();
        }

        /// <summary>
        /// The buffer is created on demand rather than only in Awake, so BeginSession()
        /// is safe to call from an editor tool or a bootstrapper that runs before the
        /// normal MonoBehaviour lifecycle.
        /// </summary>
        private void EnsureBuffer()
        {
            if (_line == null)
            {
                _line = new CsvLineBuffer(LineCapacity);
            }
        }

        private void Start()
        {
            ResolveReferences();

            if (_autoBeginOnStart && !_isLogging)
            {
                BeginSession();
            }
        }

        private void OnEnable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced += HandleTierAdvanced;
                _fsm.OnSafetyInterventionTriggered += HandleSafetyIntervention;
                _fsm.OnSessionCompleted += HandleSessionCompleted;
            }
        }

        private void OnDisable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced -= HandleTierAdvanced;
                _fsm.OnSafetyInterventionTriggered -= HandleSafetyIntervention;
                _fsm.OnSessionCompleted -= HandleSessionCompleted;
            }
        }

        private void OnDestroy()
        {
            EndSession("component_destroyed");
        }

        private void OnApplicationPause(bool paused)
        {
            // Quest backgrounds the app aggressively. Get bytes on disk before that.
            if (paused)
            {
                FlushNow();
            }
        }

        private void OnApplicationQuit()
        {
            EndSession("application_quit");
        }

        public void ResolveReferences()
        {
            if (_headPoseSampler == null) _headPoseSampler = FindAnyObjectByType<HeadPoseSampler>();
            if (_tremorFilter == null) _tremorFilter = FindAnyObjectByType<TremorBandFilter>();
            if (_swayAnalyzer == null) _swayAnalyzer = FindAnyObjectByType<PosturalSwayAnalyzer>();
            if (_edgeTracker == null) _edgeTracker = FindAnyObjectByType<EdgeProximityTracker>();
            if (_gazeDetector == null) _gazeDetector = FindAnyObjectByType<GazeAvoidanceDetector>();
            if (_fsm == null) _fsm = FindAnyObjectByType<AdaptiveExposureFSM>();

            if (_headTransform == null && _gazeDetector != null)
            {
                _headTransform = _gazeDetector.CenterEyeAnchor;
            }

            if (_headTransform == null)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    _headTransform = cam.transform;
                }
            }
        }

        /// <summary>
        /// Opens the CSV files and starts sampling. Safe to call twice; the second call
        /// is ignored rather than silently reopening and truncating.
        /// </summary>
        public void BeginSession()
        {
            if (_isLogging)
            {
                return;
            }

            EnsureBuffer();

            _sampleInterval = 1f / Mathf.Max(1f, _sampleRateHz);
            _sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

            string folder = Path.Combine(Application.persistentDataPath, TelemetryFolderName);

            try
            {
                Directory.CreateDirectory(folder);

                _sampleFilePath = Path.Combine(folder, "session_" + _sessionId + "_samples.csv");
                _eventFilePath = Path.Combine(folder, "session_" + _sessionId + "_events.csv");

                // autoFlush off: we flush on our own cadence so the hot path never
                // touches the filesystem.
                _sampleWriter = new StreamWriter(_sampleFilePath, false, new UTF8Encoding(false), 1 << 16)
                {
                    AutoFlush = false
                };
                _eventWriter = new StreamWriter(_eventFilePath, false, new UTF8Encoding(false), 1 << 12)
                {
                    AutoFlush = false
                };
            }
            catch (Exception e)
            {
                Debug.LogError("[SessionTelemetryLogger] Could not open telemetry files in " +
                               folder + " - logging disabled for this session. " + e.Message, this);
                CloseWriters();
                return;
            }

            WriteSampleHeader();
            WriteEventHeader();

            _isLogging = true;
            _sessionStartTime = Time.time;
            _sampleAccumulator = 0f;
            _flushAccumulator = 0f;

            _maxHeightAboveGround = 0f;
            _maxDistanceFromStart = 0f;
            _downwardGazeSeconds = 0f;
            _retreatEventCount = 0;
            _sudsResponseCount = 0;
            _samplesWritten = 0;
            Array.Clear(_dwellSecondsPerTier, 0, _dwellSecondsPerTier.Length);
            _observedTier = _fsm != null ? _fsm.CurrentTier : 1;

            _sessionStartHeadPosition = _headTransform != null ? _headTransform.position : Vector3.zero;

            // Seed the extremes from the starting pose. Without this, a session that ends
            // before its first Update would report a max height of zero despite the
            // participant having been on the roof the whole time.
            TrackExtremes();

            RecordEvent("session_start", _observedTier, "session_id", 0f);
            FlushNow();

            Debug.Log("[SessionTelemetryLogger] Session " + _sessionId + " logging to " + folder, this);
        }

        /// <summary>
        /// Writes the session summary, flushes and closes. Idempotent.
        /// </summary>
        public void EndSession(string reason = "ended")
        {
            if (!_isLogging)
            {
                return;
            }

            // Fold in the final tier's dwell before summarising.
            AccumulateDwell(Time.deltaTime);

            RecordEvent("max_height_reached", _observedTier, "metres", _maxHeightAboveGround);
            RecordEvent("max_distance_from_start", _observedTier, "metres", _maxDistanceFromStart);
            RecordEvent("downward_gaze_total", _observedTier, "seconds", _downwardGazeSeconds);
            RecordEvent("retreat_event_total", _observedTier, "count", _retreatEventCount);
            RecordEvent("suds_response_total", _observedTier, "count", _sudsResponseCount);
            RecordEvent("samples_written", _observedTier, "count", _samplesWritten);

            for (int tier = 1; tier <= MaxTiers; tier++)
            {
                if (_dwellSecondsPerTier[tier] > 0f)
                {
                    RecordEvent("dwell_tier_" + tier.ToString(CultureInfo.InvariantCulture),
                        tier, "seconds", _dwellSecondsPerTier[tier]);
                }
            }

            RecordEvent("session_end", _observedTier, reason, SessionTime);

            _isLogging = false;
            FlushNow();
            CloseWriters();
        }

        private void Update()
        {
            if (!_isLogging)
            {
                return;
            }

            float dt = Time.deltaTime;

            // Continuous accumulators must run every frame even though rows are written
            // at a lower rate, otherwise durations would be quantised to the sample tick.
            AccumulateDwell(dt);
            AccumulateGaze(dt);
            TrackExtremes();

            _sampleAccumulator += dt;
            if (_sampleAccumulator >= _sampleInterval)
            {
                // Subtract rather than zero, so sampling does not drift with frame time.
                _sampleAccumulator -= _sampleInterval;
                if (_sampleAccumulator > _sampleInterval)
                {
                    // A long hitch: resync instead of writing a burst of catch-up rows.
                    _sampleAccumulator = 0f;
                }

                WriteSampleRow();
            }

            _flushAccumulator += dt;
            if (_flushAccumulator >= _flushIntervalSeconds)
            {
                _flushAccumulator = 0f;
                FlushNow();
            }
        }

        private void AccumulateDwell(float dt)
        {
            int tier = _fsm != null ? _fsm.CurrentTier : _observedTier;
            if (tier < 1) tier = 1;
            if (tier > MaxTiers) tier = MaxTiers;

            _observedTier = tier;
            _dwellSecondsPerTier[tier] += dt;
        }

        private void AccumulateGaze(float dt)
        {
            if (_gazeDetector != null && _gazeDetector.IsLookingDown)
            {
                _downwardGazeSeconds += dt;
            }
        }

        private void TrackExtremes()
        {
            if (_headTransform == null)
            {
                return;
            }

            Vector3 pos = _headTransform.position;

            float height = pos.y - _groundReferenceY;
            if (height > _maxHeightAboveGround)
            {
                _maxHeightAboveGround = height;
            }

            float planarDistance = Vector2.Distance(
                new Vector2(pos.x, pos.z),
                new Vector2(_sessionStartHeadPosition.x, _sessionStartHeadPosition.z));

            if (planarDistance > _maxDistanceFromStart)
            {
                _maxDistanceFromStart = planarDistance;
            }
        }

        /// <summary>
        /// Records a discrete event. Pass only literals or cached strings for
        /// <paramref name="eventType"/> and <paramref name="detail"/> - a freshly built
        /// string moves the allocation to the call site.
        /// </summary>
        public void RecordEvent(string eventType, int tier, string detail, float value)
        {
            if (_eventWriter == null)
            {
                return;
            }

            _line.Clear();
            _line.AppendField(SessionTime, 3);
            _line.AppendField(eventType);
            _line.AppendField(tier);
            _line.AppendField(detail);
            _line.AppendField(value, 4);
            _line.EndLine();
            _line.WriteTo(_eventWriter);

            if (_verboseConsoleLogging)
            {
                Debug.Log("[Telemetry] " + eventType + " tier=" + tier + " " + detail, this);
            }
        }

        /// <summary>
        /// Called by the SUDS prompt when the participant reports a distress rating.
        /// </summary>
        public void RecordSudsResponse(int rating, string promptContext)
        {
            _sudsResponseCount++;
            RecordEvent("suds_response", _observedTier, promptContext, rating);

            // Pair the rating with a full signal snapshot at the same timestamp, so a
            // self-reported score can be read against the objective signals that
            // produced it without interpolating between sample ticks.
            CaptureSampleNow();

            FlushNow();
        }

        /// <summary>
        /// Writes one sample row immediately, outside the fixed-rate cadence. Used to
        /// anchor a full signal snapshot to a discrete moment such as a SUDS response.
        /// </summary>
        public void CaptureSampleNow()
        {
            if (!_isLogging)
            {
                return;
            }

            WriteSampleRow();
        }

        /// <summary>
        /// Called by the retreat detector when the participant withdraws from the edge.
        /// </summary>
        public void RecordRetreatEvent(float distanceGained, float fromDistanceToEdge)
        {
            _retreatEventCount++;
            RecordEvent("retreat", _observedTier, "distance_gained_m", distanceGained);
            RecordEvent("retreat_from_edge_m", _observedTier, "metres", fromDistanceToEdge);
        }

        /// <summary>
        /// Called by the eye-height calibrator once calibration completes.
        /// </summary>
        public void RecordEyeHeightCalibration(float eyeHeightMetres)
        {
            _calibratedEyeHeight = eyeHeightMetres;
            RecordEvent("eye_height_calibrated", _observedTier, "metres", eyeHeightMetres);
            FlushNow();
        }

        private void WriteSampleRow()
        {
            if (_sampleWriter == null)
            {
                return;
            }

            Vector3 pos = _headTransform != null ? _headTransform.position : Vector3.zero;
            Vector3 forward = _headTransform != null ? _headTransform.forward : Vector3.forward;

            // Signed pitch: positive looking up, negative looking down.
            float pitchDeg = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;

            int stateIndex = _fsm != null ? (int)_fsm.CurrentState : -1;
            string stateName = (stateIndex >= 0 && stateIndex < StateNames.Length)
                ? StateNames[stateIndex]
                : "Unknown";

            _line.Clear();
            _line.AppendField(SessionTime, 3);
            _line.AppendField(_observedTier);
            _line.AppendField(stateName);

            _line.AppendField(pos.x, 4);
            _line.AppendField(pos.y, 4);
            _line.AppendField(pos.z, 4);
            _line.AppendField(pos.y - _groundReferenceY, 4);

            _line.AppendField(_headPoseSampler != null ? _headPoseSampler.LatestSpeed : 0f, 4);
            _line.AppendField(pitchDeg, 3);

            _line.AppendField(_gazeDetector != null ? _gazeDetector.GazeAngleWithDown : 0f, 3);
            _line.AppendField(_gazeDetector != null && _gazeDetector.IsLookingDown);
            _line.AppendField(_gazeDetector != null ? _gazeDetector.GazeAvoidanceRatio : 0f, 4);
            _line.AppendField(_downwardGazeSeconds, 3);

            _line.AppendField(_edgeTracker != null ? _edgeTracker.DistanceToEdge : 0f, 4);
            _line.AppendField(_edgeTracker != null && _edgeTracker.IsNearEdge);
            _line.AppendField(_edgeTracker != null ? _edgeTracker.HesitationIndex : 0f, 3);

            _line.AppendField(_tremorFilter != null ? _tremorFilter.TremorIntensity : 0f, 4);
            _line.AppendField(_swayAnalyzer != null ? _swayAnalyzer.HorizontalSwayVelocity : 0f, 4);
            _line.AppendField(_swayAnalyzer != null ? _swayAnalyzer.CenterOfMassWander : 0f, 4);
            _line.AppendField(_swayAnalyzer != null && _swayAnalyzer.IsPosturalFrozen);

            _line.EndLine();
            _line.WriteTo(_sampleWriter);

            _samplesWritten++;
        }

        private void WriteSampleHeader()
        {
            _line.Clear();
            _line.AppendField("t_seconds");
            _line.AppendField("tier");
            _line.AppendField("fsm_state");
            _line.AppendField("head_x");
            _line.AppendField("head_y");
            _line.AppendField("head_z");
            _line.AppendField("height_above_ground_m");
            _line.AppendField("head_speed_mps");
            _line.AppendField("head_pitch_deg");
            _line.AppendField("gaze_angle_from_down_deg");
            _line.AppendField("is_looking_down");
            _line.AppendField("gaze_avoidance_ratio");
            _line.AppendField("downward_gaze_cumulative_s");
            _line.AppendField("distance_to_edge_m");
            _line.AppendField("is_near_edge");
            _line.AppendField("hesitation_index");
            _line.AppendField("tremor_intensity");
            _line.AppendField("sway_velocity_mps");
            _line.AppendField("com_wander_m");
            _line.AppendField("is_postural_frozen");
            _line.EndLine();
            _line.WriteTo(_sampleWriter);
        }

        private void WriteEventHeader()
        {
            _line.Clear();
            _line.AppendField("t_seconds");
            _line.AppendField("event_type");
            _line.AppendField("tier");
            _line.AppendField("detail");
            _line.AppendField("value");
            _line.EndLine();
            _line.WriteTo(_eventWriter);
        }

        public void FlushNow()
        {
            try
            {
                _sampleWriter?.Flush();
                _eventWriter?.Flush();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SessionTelemetryLogger] Flush failed: " + e.Message, this);
            }
        }

        private void CloseWriters()
        {
            try
            {
                _sampleWriter?.Dispose();
            }
            catch (Exception) { }

            try
            {
                _eventWriter?.Dispose();
            }
            catch (Exception) { }

            _sampleWriter = null;
            _eventWriter = null;
        }

        private void HandleTierAdvanced(int newTier)
        {
            RecordEvent("tier_advanced", newTier, "tier_entered", newTier);
            FlushNow();
        }

        private void HandleSafetyIntervention()
        {
            RecordEvent("safety_intervention", _observedTier, "reverted_to_tier_1", 0f);
            FlushNow();
        }

        private void HandleSessionCompleted()
        {
            RecordEvent("fsm_session_completed", _observedTier, "all_tiers_cleared", 0f);
            EndSession("fsm_completed");
        }
    }
}
