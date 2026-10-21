using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Acrophobia.AdaptiveFSM;
using Acrophobia.Locomotion;
using Acrophobia.ProceduralEnvironment;
using Acrophobia.Telemetry;

namespace Acrophobia.Narration
{
    /// <summary>
    /// Runs a short ordinary routine before each exposure: wash, coffee, and only then
    /// the narrator noticing the light outside.
    ///
    /// This is therapeutically the right shape, not just atmosphere. An exposure session
    /// should not open on the feared stimulus - a few minutes of mundane, controllable
    /// activity gives the participant a settled baseline, which is also when the honest
    /// SUDS reading can be taken. The height then arrives into a grounded state rather
    /// than a cold start.
    ///
    /// Most beats cost almost nothing to build. An Interlude fades to black, plays a
    /// sound, and holds: a bathroom that is never modelled, a kettle that does not exist,
    /// each standing up on two audio clips. That is the cheapest scene in the project by
    /// a wide margin.
    ///
    /// Deliberately non-blocking. The balcony door stays usable throughout, so a
    /// participant who wants to walk straight out can, and the narrator simply notices.
    /// Gating the exposure behind a routine would take away the agency the whole design
    /// rests on, and would make the run tedious on the fifth repetition.
    /// </summary>
    public class RitualSequencer : MonoBehaviour
    {
        public enum BeatKind
        {
            Narrate,
            Interlude,
            WaitForApproach,
            SetProp,
            Pause,

            // Appended, never reordered: these map to serialized ints in the scene.
            // Holds until the participant looks at a marked point and clicks it. The
            // ritual interludes used to run straight into one another, which read as
            // the screen blacking out every few seconds for no reason the participant
            // could connect to anything they had done.
            WaitForClick
        }

        [Serializable]
        public class Beat
        {
            public BeatKind Kind = BeatKind.Narrate;

            [TextArea(2, 3)]
            public string Line = "";

            [Tooltip("Interlude audio. The beat still runs, silently, without one.")]
            public AudioClip Clip;

            [Tooltip("Seconds held. For an Interlude this is the time spent on black.")]
            public float Seconds = 3f;

            [Tooltip("WaitForApproach / WaitForClick: the place the participant has to " +
                     "reach, or the point they have to click.")]
            public Transform Marker;

            [Tooltip("Same thing by name, looked up in whichever tier scene is loaded. " +
                     "A Transform cannot be serialized across a scene boundary, so tiers " +
                     "living in their own scene have to name their points instead.")]
            public string MarkerName = "";

            [Tooltip("WaitForClick: clicking this point advances to the next tier. This is " +
                     "the way out of a level - the stairs, the door, the plank - so the " +
                     "one interaction in a room is the one that takes you somewhere.")]
            public bool AdvancesTierOnClick = false;

            [Tooltip("WaitForApproach: how close counts, in metres.")]
            public float Radius = 1.0f;

            [Tooltip("WaitForApproach: give up after this long so nobody gets stuck.")]
            public float TimeoutSeconds = 45f;

            [Tooltip("SetProp: object toggled.")]
            public GameObject Prop;

            public bool PropActive = true;

            [Tooltip("Hold the participant still for this beat. Teleport is disabled; " +
                     "they can still look around, and the session controls stay live.")]
            public bool LockMovement = false;
        }

        [Serializable]
        public class TierRitual
        {
            public string Label = "Tier";
            public Beat[] Beats = Array.Empty<Beat>();
        }

        [Tooltip("One ritual per tier, in order. Entry 0 is tier 1.")]
        [SerializeField] private TierRitual[] _rituals = new TierRitual[5];

        [Tooltip("Run the ritual automatically when a tier begins.")]
        [SerializeField] private bool _autoRunOnTier = true;

        [Tooltip("Seconds of black before the first fade-in, so a session opens on waking.")]
        [SerializeField, Range(0f, 4f)] private float _openingBlackSeconds = 1.2f;

        [Tooltip("Silence every other audio source while an interlude plays, so the " +
                 "street does not bleed through the black. The interlude itself is exempt.")]
        [SerializeField] private bool _cutAmbienceDuringInterlude = true;

        [Tooltip("Rig moved when an interlude beat carries a Marker. Resolved from the " +
                 "teleport rig when left empty.")]
        [SerializeField] private Transform _playerRig;

        [Tooltip("Swaps the per-tier environment scene during the blackout. Optional - " +
                 "without it every tier uses the environment built into this scene.")]
        [SerializeField] private EnvironmentSceneLoader _envLoader;

        [SerializeField] private ScreenFader _fader;
        [SerializeField] private NarratorSubtitleView _subtitles;
        [SerializeField] private AudioSource _interludeSource;
        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private AdaptiveExposureFSM _fsm;
        [SerializeField] private SessionTelemetryLogger _logger;
        [SerializeField] private TeleportLocomotion _teleport;

        private Coroutine _running;
        private int _runningTier = -1;
        private bool _firstRitualDone;
        private AdaptiveExposureFSM.ExposureState _lastState = AdaptiveExposureFSM.ExposureState.AwaitingStart;

        // Volumes held while an interlude is playing, so ambience comes back exactly as
        // it was rather than at some assumed default.
        private readonly Dictionary<AudioSource, float> _silenced = new Dictionary<AudioSource, float>();

        public bool IsRunning => _running != null;

        public int RunningTier => _runningTier;

        public event Action<int> OnRitualStarted;

        public event Action<int> OnRitualCompleted;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_fsm != null && _autoRunOnTier)
            {
                _fsm.OnTierAdvanced += HandleTier;
            }
        }

        private void OnDisable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced -= HandleTier;
            }
        }

        public void ResolveReferences()
        {
            if (_fsm == null) _fsm = FindAnyObjectByType<AdaptiveExposureFSM>();
            if (_teleport == null) _teleport = FindAnyObjectByType<TeleportLocomotion>();
            if (_logger == null) _logger = FindAnyObjectByType<SessionTelemetryLogger>();
            if (_subtitles == null) _subtitles = FindAnyObjectByType<NarratorSubtitleView>();

            // Same rig ExposureHeightController drives, so a placement and a tier offset
            // never disagree about what "the player" is.
            if (_playerRig == null && _teleport != null) _playerRig = _teleport.transform;

            if (_fader == null)
            {
                _fader = FindAnyObjectByType<ScreenFader>();
                if (_fader == null) _fader = gameObject.AddComponent<ScreenFader>();
            }

            if (_interludeSource == null)
            {
                _interludeSource = GetComponent<AudioSource>();
                if (_interludeSource == null)
                {
                    _interludeSource = gameObject.AddComponent<AudioSource>();
                    _interludeSource.playOnAwake = false;
                    _interludeSource.spatialBlend = 0f;
                }
            }

            if (_centerEyeAnchor == null)
            {
                var found = GameObject.Find("CenterEyeAnchor");
                if (found != null) _centerEyeAnchor = found.transform;
                else if (Camera.main != null) _centerEyeAnchor = Camera.main.transform;
            }
        }

        public void RunRitual(int tier)
        {
            if (_rituals == null || _rituals.Length == 0) return;
            if (_running != null) StopCoroutine(_running);

            int index = Mathf.Clamp(tier - 1, 0, _rituals.Length - 1);
            _running = StartCoroutine(Play(index, tier));
        }

        public void SkipRitual()
        {
            if (_running == null) return;

            StopCoroutine(_running);
            _running = null;

            _subtitles?.Hide();
            _fader?.SetAlphaImmediate(0f);

            // A skip can land mid-locked-beat, so movement is restored unconditionally.
            if (_teleport != null) _teleport.enabled = true;

            _logger?.RecordEvent("ritual_skipped", _runningTier, "participant_or_system", 0f);
            _runningTier = -1;
        }

        public void SetRituals(TierRitual[] rituals)
        {
            _rituals = rituals;
        }

        public int RitualCount => _rituals != null ? _rituals.Length : 0;

        private IEnumerator Play(int index, int tier)
        {
            _runningTier = tier;
            TierRitual ritual = _rituals[index];

            _logger?.RecordEvent("ritual_started", tier,
                ritual != null ? ritual.Label : "unnamed", 0f);
            OnRitualStarted?.Invoke(tier);

            // Open on black so the session begins at the moment of waking - but only when
            // the ritual does not already start with an interlude. Every tier now opens
            // with a transition interlude that runs its own black, and fading in here
            // first showed the participant one second of the new tier height with no
            // environment loaded yet: hanging in the air, then black again, then placed.
            bool startsWithInterlude = ritual != null && ritual.Beats != null
                                       && ritual.Beats.Length > 0
                                       && ritual.Beats[0].Kind == BeatKind.Interlude;

            if (_openingBlackSeconds > 0f && _fader != null && !startsWithInterlude)
            {
                _fader.SetAlphaImmediate(1f);
                yield return new WaitForSeconds(_openingBlackSeconds);
                yield return _fader.FadeTo(0f, 1.2f);
            }
            else if (startsWithInterlude && _fader != null)
            {
                // Hold whatever black the FSM already raised, so there is no gap at all
                // between the tier changing and the interlude taking over.
                _fader.SetAlphaImmediate(1f);
            }

            if (ritual != null && ritual.Beats != null)
            {
                for (int i = 0; i < ritual.Beats.Length; i++)
                {
                    yield return RunBeat(ritual.Beats[i]);
                }
            }

            _subtitles?.Hide();
            _logger?.RecordEvent("ritual_completed", tier,
                ritual != null ? ritual.Label : "unnamed", 0f);
            OnRitualCompleted?.Invoke(tier);

            _running = null;
            _runningTier = -1;
        }

        private IEnumerator RunBeat(Beat beat)
        {
            if (beat == null) yield break;

            // Movement lock is scoped to the beat and restored in every exit path,
            // including a skip, so a participant can never be left unable to move.
            // Only deliberate travel is withheld: looking around still works, and the
            // step-back and end-session controls stay live throughout, which the brief
            // requires unconditionally.
            bool lockedHere = beat.LockMovement && _teleport != null && _teleport.enabled;
            if (lockedHere)
            {
                _teleport.enabled = false;
                _logger?.RecordEvent("movement_locked", _runningTier, "ritual_beat", 0f);
            }

            switch (beat.Kind)
            {
                case BeatKind.Narrate:
                    _subtitles?.Show(beat.Line);
                    yield return new WaitForSeconds(Mathf.Max(0.5f, beat.Seconds));
                    _subtitles?.Hide();
                    break;

                case BeatKind.Interlude:
                    // The whole trick: a scene implied by sound, never built. The black is
                    // also the only safe moment to move the participant - they cannot see
                    // the world change, so a reposition reads as elapsed time rather than
                    // teleportation, and nothing can strand them where the floor just was.
                    if (_fader != null) yield return _fader.FadeTo(1f, 0.7f);

                    if (_cutAmbienceDuringInterlude) SilenceAmbience();

                    // Swap the environment while nothing can be seen. A load takes as long
                    // as it takes; the black is the only place that is free.
                    if (_envLoader != null) yield return _envLoader.ApplyTier(_runningTier);

                    // A Marker on an interlude means "you end up here", and only beats
                    // that say so move anybody. The washing and coffee interludes carry
                    // no marker: they are time passing in the same room, and re-placing
                    // the participant on each one teleported them back to a spawn point
                    // they were already standing on.
                    bool relocates = beat.Marker != null || !string.IsNullOrEmpty(beat.MarkerName);
                    if (relocates)
                    {
                        // A loaded tier scene carries its own spawn point and wins, since
                        // the marker wired here belongs to an environment just hidden.
                        Transform placeAt = (_envLoader != null && _envLoader.CurrentAnchor != null)
                            ? _envLoader.CurrentAnchor
                            : ResolveMarker(beat);
                        if (placeAt != null) PlaceRigAt(placeAt);
                    }

                    if (!string.IsNullOrEmpty(beat.Line)) _subtitles?.Show(beat.Line);

                    if (beat.Clip != null && _interludeSource != null)
                    {
                        _interludeSource.clip = beat.Clip;
                        _interludeSource.Play();
                    }

                    yield return new WaitForSeconds(Mathf.Max(0.5f, beat.Seconds));

                    if (_interludeSource != null && _interludeSource.isPlaying)
                    {
                        _interludeSource.Stop();
                    }

                    _subtitles?.Hide();
                    RestoreAmbience();
                    if (_fader != null) yield return _fader.FadeTo(0f, 0.9f);
                    break;

                case BeatKind.WaitForClick:
                {
                    Transform target = ResolveMarker(beat);
                    if (target == null)
                    {
                        // Nothing to point at. Better to carry on than to strand the
                        // participant in front of an invisible requirement.
                        Debug.LogWarning("[Ritual] WaitForClick with no marker - skipping.", this);
                        break;
                    }

                    if (!string.IsNullOrEmpty(beat.Line)) _subtitles?.Show(beat.Line);

                    GameObject pip = SpawnIndicator(target.position);
                    float held = 0f;
                    float limit = Mathf.Max(5f, beat.TimeoutSeconds);
                    bool clicked = false;

                    while (!clicked && held < limit)
                    {
                        held += Time.deltaTime;
                        if (pip != null) PulseIndicator(pip, held);
                        if (ClickPressed() && IsLookingAt(target.position)) clicked = true;
                        yield return null;
                    }

                    if (pip != null) Destroy(pip);
                    _subtitles?.Hide();
                    _logger?.RecordEvent(clicked ? "ritual_point_clicked" : "ritual_point_timeout",
                                         _runningTier, target.name, held);

                    // The way out. Clicking it is the participant choosing to go up, which
                    // is the only route to a higher tier the brief allows.
                    if (clicked && beat.AdvancesTierOnClick && _fsm != null)
                    {
                        _fsm.RequestAdvanceTier();
                    }
                    break;
                }

                case BeatKind.WaitForApproach:
                    if (!string.IsNullOrEmpty(beat.Line)) _subtitles?.Show(beat.Line);

                    float waited = 0f;
                    while (waited < beat.TimeoutSeconds)
                    {
                        if (beat.Marker == null || _centerEyeAnchor == null) break;

                        Vector3 a = _centerEyeAnchor.position;
                        Vector3 b = beat.Marker.position;
                        float dx = a.x - b.x, dz = a.z - b.z;
                        if (Mathf.Sqrt(dx * dx + dz * dz) <= beat.Radius) break;

                        waited += Time.deltaTime;
                        yield return null;
                    }

                    _subtitles?.Hide();
                    break;

                case BeatKind.SetProp:
                    if (beat.Prop != null) beat.Prop.SetActive(beat.PropActive);
                    break;

                case BeatKind.Pause:
                    yield return new WaitForSeconds(Mathf.Max(0.1f, beat.Seconds));
                    break;
            }

            if (lockedHere)
            {
                _teleport.enabled = true;
                _logger?.RecordEvent("movement_unlocked", _runningTier, "ritual_beat", 0f);
            }
        }

        /// <summary>
        /// A beat's target, whether it was wired directly or has to be found by name in
        /// whichever tier scene is currently loaded.
        /// </summary>
        private Transform ResolveMarker(Beat beat)
        {
            if (beat.Marker != null) return beat.Marker;
            if (string.IsNullOrEmpty(beat.MarkerName)) return null;

            var go = GameObject.Find(beat.MarkerName);
            return go != null ? go.transform : null;
        }

        /// <summary>
        /// The thing the participant is meant to click. Deliberately conspicuous: an
        /// unmarked interaction in VR is an interaction nobody finds.
        /// </summary>
        private GameObject SpawnIndicator(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "__RitualPoint";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.14f;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            var warm = new Color(1.00f, 0.86f, 0.45f, 1f);
            mat.color = warm;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", warm);
            // Over the top of the room so it never hides inside a wall or a worktop.
            mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            mat.renderQueue = 3550;

            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            return go;
        }

        private static void PulseIndicator(GameObject pip, float t)
        {
            float s = 0.12f + Mathf.Sin(t * 4.5f) * 0.025f;
            pip.transform.localScale = Vector3.one * s;
        }

        /// <summary>
        /// Angle-based rather than a physics raycast: the indicator has no collider, and
        /// a cone is far more forgiving than a ray when aiming with a tracked head.
        /// </summary>
        private bool IsLookingAt(Vector3 position)
        {
            if (_centerEyeAnchor == null) return true;

            Vector3 toPoint = position - _centerEyeAnchor.position;
            if (toPoint.sqrMagnitude > 400f) return false;          // beyond 20 m, no

            float angle = Vector3.Angle(_centerEyeAnchor.forward, toPoint);
            return angle <= 14f;
        }

        private static bool ClickPressed()
        {
            // Trigger on a headset. Button.One is the tier advance, so it is kept clear.
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger)) return true;
                if (OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger)) return true;
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.eKey.wasPressedThisFrame) return true;
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetMouseButtonDown(0)) return true;
                if (Input.GetKeyDown(KeyCode.E)) return true;
            }
            catch (Exception) { }
#endif
            return false;
        }

        /// <summary>
        /// Mutes every audio source except the interlude itself, remembering what each
        /// was set to. AudioListener.volume would be simpler but would also silence the
        /// interlude clip, which is the one thing that has to be heard.
        /// </summary>
        private void SilenceAmbience()
        {
            if (_silenced.Count > 0) return;   // already down; never stack

            foreach (var src in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (src == null || src == _interludeSource) continue;
                _silenced[src] = src.volume;
                src.volume = 0f;
            }
        }

        private void RestoreAmbience()
        {
            foreach (var kv in _silenced)
            {
                if (kv.Key != null) kv.Key.volume = kv.Value;
            }
            _silenced.Clear();
        }

        /// <summary>
        /// Puts the participant at a marker. The rig is moved rather than the camera,
        /// because the camera is driven by head tracking - so the rig is offset by the
        /// gap between where the head currently is and where the marker wants it.
        /// </summary>
        private void PlaceRigAt(Transform marker)
        {
            if (marker == null || _playerRig == null) return;

            Vector3 head = _centerEyeAnchor != null ? _centerEyeAnchor.position : _playerRig.position;

            Vector3 delta = marker.position - head;
            delta.y = 0f;                                   // horizontal correction only

            Vector3 placed = _playerRig.position + delta;
            placed.y = marker.position.y;                   // rig root sits at floor level
            _playerRig.position = placed;

            _logger?.RecordEvent("rig_placed", _runningTier, marker.name, 0f);
        }

        private void HandleTier(int tier)
        {
            RunRitual(tier);
        }

        /// <summary>
        /// OnTierAdvanced only fires moving from one tier to the next, so tier 1 - the
        /// first and most important ritual - would never have run off it. The session
        /// starting is its own trigger.
        /// </summary>
        private void Update()
        {
            if (_fsm == null || !_autoRunOnTier) return;

            var state = _fsm.CurrentState;
            if (state != _lastState)
            {
                if (_lastState == AdaptiveExposureFSM.ExposureState.AwaitingStart
                    && state == AdaptiveExposureFSM.ExposureState.Exposing
                    && !_firstRitualDone)
                {
                    _firstRitualDone = true;
                    RunRitual(_fsm.CurrentTier);
                }

                _lastState = state;
            }
        }
    }
}
