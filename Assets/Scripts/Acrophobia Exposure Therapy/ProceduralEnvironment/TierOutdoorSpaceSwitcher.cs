using System;
using UnityEngine;
using Acrophobia.AdaptiveFSM;

namespace Acrophobia.ProceduralEnvironment
{
    /// <summary>
    /// Shows one outdoor space per tier, and only one at a time.
    ///
    /// These cannot live inside the tier dressing groups the way the interior props
    /// do. TierEnvironmentDresser parents its groups under Apartment_Interior, and
    /// ExposureHeightController hides that whole root at the top tier, so anything
    /// parented there vanishes exactly when the terrace is supposed to appear. They
    /// sit in a sibling root instead, switched from the same FSM events.
    ///
    /// A catwalk was never defensible on a rooftop. Each tier gets the outdoor space
    /// the room behind it would actually have, and the barrier erodes on its own
    /// because of what each place plausibly is:
    ///
    ///   1  apartment    homely balcony, solid parapet at chest height
    ///   2  office       service balcony, waist rail and a condenser unit
    ///   3  plant room   maintenance gantry, grated deck, handrail and mid-rail
    ///   4  stairwell    fire-escape landing, open grating, one rail
    ///   5  terrace      bare roof, a low fence to lean over
    /// </summary>
    public class TierOutdoorSpaceSwitcher : MonoBehaviour
    {
        [Tooltip("One root per tier, in order. Entry 0 is tier 1.")]
        [SerializeField] private GameObject[] _spaces = new GameObject[5];

        [SerializeField] private AdaptiveExposureFSM _fsm;

        private int _appliedTier = -1;

        public int AppliedTier => _appliedTier;

        public event Action<int> OnOutdoorSpaceApplied;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced += Apply;
                _fsm.OnTierSteppedBack += Apply;
            }
        }

        private void OnDisable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced -= Apply;
                _fsm.OnTierSteppedBack -= Apply;
            }
        }

        private void Start()
        {
            Apply(_fsm != null ? _fsm.CurrentTier : 1);
        }

        public void ResolveReferences()
        {
            if (_fsm == null)
                _fsm = FindFirstObjectByType<AdaptiveExposureFSM>();
        }

        /// <summary>
        /// Switches to the given tier's outdoor space. Safe to call repeatedly, and
        /// safe when an entry is still unassigned - a missing space leaves the player
        /// with no balcony rather than throwing every frame.
        /// </summary>
        public void Apply(int tier)
        {
            if (_spaces == null || _spaces.Length == 0) return;

            int index = Mathf.Clamp(tier - 1, 0, _spaces.Length - 1);

            for (int i = 0; i < _spaces.Length; i++)
            {
                if (_spaces[i] == null) continue;
                bool shouldBeActive = i == index;
                if (_spaces[i].activeSelf != shouldBeActive)
                    _spaces[i].SetActive(shouldBeActive);
            }

            _appliedTier = tier;
            OnOutdoorSpaceApplied?.Invoke(tier);
        }
    }
}
