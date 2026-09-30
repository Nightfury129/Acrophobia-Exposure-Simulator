using UnityEngine;
using VRSlashGame.Events;

namespace VRSlashGame.Audio
{
    /// <summary>
    /// Central audio manager that listens to GameEvents and plays procedural 3D spatial sound effects.
    /// Handles heavy squishes, crisp cracks, soft slices, directional spawn cues, fly-by whooshes,
    /// and revolving stereo combo milestone chimes.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Audio Settings")]
        [Range(0f, 1f)]
        [SerializeField] private float _masterVolume = 0.85f;
        [SerializeField] private Vector2 _pitchVariation = new Vector2(0.92f, 1.08f);

        [Header("3D Spatial Rolloff Settings")]
        [SerializeField] private float _spatialMinDistance = 0.5f;
        [SerializeField] private float _spatialMaxDistance = 18.0f;
        [SerializeField] private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;

        private AudioSource _stereo2DAudioSource;
        private AudioClip _watermelonSquishClip;
        private AudioClip _coconutCrackClip;
        private AudioClip _bananaSliceClip;
        private AudioClip _spawnChimeClip;
        private AudioClip _flyByWhooshClip;
        private AudioClip _combo5xClip;
        private AudioClip _combo10xClip;
        private AudioClip _missClip;

        // Pool of 3D spatial audio sources
        private const int SpatialPoolSize = 10;
        private readonly System.Collections.Generic.List<AudioSource> _spatialPool = new System.Collections.Generic.List<AudioSource>();
        private int _poolIndex = 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _stereo2DAudioSource = GetComponent<AudioSource>();
            _stereo2DAudioSource.playOnAwake = false;
            _stereo2DAudioSource.spatialBlend = 0.0f; // 2D stereo for UI/milestones

            // Initialize 3D Spatial Audio Pool
            for (int i = 0; i < SpatialPoolSize; i++)
            {
                GameObject poolChild = new GameObject($"SpatialAudioSource_{i}");
                poolChild.transform.SetParent(transform);
                AudioSource src = poolChild.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1.0f; // 100% 3D spatial positioning
                src.rolloffMode = _rolloffMode;
                src.minDistance = _spatialMinDistance;
                src.maxDistance = _spatialMaxDistance;
                src.dopplerLevel = 0.8f;
                _spatialPool.Add(src);
            }

            // Generate procedural clips
            _watermelonSquishClip = SlashAudioSynthesizer.CreateHeavySquishClip();
            _coconutCrackClip = SlashAudioSynthesizer.CreateHardCrackClip();
            _bananaSliceClip = SlashAudioSynthesizer.CreateSoftFleshySliceClip();
            _spawnChimeClip = SlashAudioSynthesizer.CreateSpawnChimeClip();
            _flyByWhooshClip = SlashAudioSynthesizer.CreateFlyByWhooshClip();
            _combo5xClip = SlashAudioSynthesizer.CreateRevolvingMilestoneClip(5, "Combo_5xRevolving");
            _combo10xClip = SlashAudioSynthesizer.CreateRevolvingMilestoneClip(10, "Combo_10xRevolving");
            _missClip = SlashAudioSynthesizer.CreateFlyByWhooshClip("Fruit_Miss_Thud");
        }

        private void OnEnable()
        {
            GameEvents.OnFruitSliced += HandleFruitSliced;
            GameEvents.OnFruitMissed += HandleFruitMissed;
            GameEvents.OnComboChanged += HandleComboChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnFruitSliced -= HandleFruitSliced;
            GameEvents.OnFruitMissed -= HandleFruitMissed;
            GameEvents.OnComboChanged -= HandleComboChanged;
        }

        public void PlaySpawnCue(Vector3 spawnPosition)
        {
            PlaySpatialClip(_spawnChimeClip, spawnPosition, _masterVolume * 0.75f, Random.Range(0.96f, 1.04f));
        }

        public void PlayFlyByWhoosh(Vector3 passPosition)
        {
            PlaySpatialClip(_flyByWhooshClip, passPosition, _masterVolume * 0.85f, Random.Range(0.95f, 1.05f));
        }

        public void PlayFruitSliceSound(string fruitName, Color splashColor, Vector3 hitPosition)
        {
            AudioClip clipToPlay = _watermelonSquishClip;
            string lowerName = fruitName.ToLowerInvariant();

            if (lowerName.Contains("watermelon") || splashColor.r > 0.6f && splashColor.g < 0.4f)
            {
                clipToPlay = _watermelonSquishClip; // Heavy wet squish/splash
            }
            else if (lowerName.Contains("avocado") || lowerName.Contains("coconut") || splashColor.g > 0.5f && splashColor.r < 0.5f)
            {
                clipToPlay = _coconutCrackClip; // Hard crisp crack
            }
            else if (lowerName.Contains("banana") || lowerName.Contains("orange") || splashColor.r > 0.6f && splashColor.g > 0.4f)
            {
                clipToPlay = _bananaSliceClip; // Soft fleshy slice
            }

            float pitch = Random.Range(_pitchVariation.x, _pitchVariation.y);
            PlaySpatialClip(clipToPlay, hitPosition, _masterVolume, pitch);
        }

        private void HandleFruitSliced(FruitSliceData sliceData)
        {
            // Default fallback if not routed directly
            PlayFruitSliceSound(sliceData.FruitType, Color.red, sliceData.SlicePosition);
        }

        private void HandleFruitMissed()
        {
            Play2DSound(_missClip, _masterVolume * 0.55f, 0.85f);
        }

        private void HandleComboChanged(int comboCount)
        {
            if (comboCount == 5)
            {
                Play2DSound(_combo5xClip, _masterVolume * 1.0f, 1.0f);
            }
            else if (comboCount == 10)
            {
                Play2DSound(_combo10xClip, _masterVolume * 1.1f, 1.0f);
            }
        }

        public void PlaySpatialClip(AudioClip clip, Vector3 worldPos, float volume, float pitch)
        {
            if (clip == null || _spatialPool.Count == 0) return;

            AudioSource src = _spatialPool[_poolIndex];
            _poolIndex = (_poolIndex + 1) % _spatialPool.Count;

            src.transform.position = worldPos;
            src.pitch = pitch;
            src.PlayOneShot(clip, volume);
        }

        public void Play2DSound(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || _stereo2DAudioSource == null) return;

            _stereo2DAudioSource.pitch = pitch;
            _stereo2DAudioSource.PlayOneShot(clip, volume);
        }
    }
}
