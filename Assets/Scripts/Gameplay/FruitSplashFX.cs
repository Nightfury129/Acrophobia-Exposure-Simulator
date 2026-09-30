using System.Collections;
using UnityEngine;

namespace VRSlashGame.Gameplay
{
    /// <summary>
    /// Controls a juicy particle splash effect spawned when a fruit is sliced.
    /// Clamps particle burst counts and max particles to prevent mobile VR overdraw stalls,
    /// and recycles through FruitPool with zero GC allocation.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class FruitSplashFX : MonoBehaviour
    {
        [Header("Mobile VR Optimization")]
        [Tooltip("Clamps total maximum particles per emitter to protect mobile fill rate.")]
        [SerializeField] private int _maxParticlesMobile = 25;
        [Tooltip("Clamps maximum burst count per emission impulse to prevent TBDR overdraw.")]
        [SerializeField] private short _maxBurstCountMobile = 20;

        private ParticleSystem _particleSystem;
        private ParticleSystem[] _allParticleSystems;
        private Coroutine _recycleCoroutine;

        private void Awake()
        {
            EnsureSystems();
        }

        private void EnsureSystems()
        {
            if (_allParticleSystems == null || _allParticleSystems.Length == 0)
            {
                _allParticleSystems = GetComponentsInChildren<ParticleSystem>(true);
                if (_allParticleSystems == null || _allParticleSystems.Length == 0)
                {
                    _particleSystem = GetComponent<ParticleSystem>();
                    if (_particleSystem != null) _allParticleSystems = new ParticleSystem[] { _particleSystem };
                }
                else
                {
                    _particleSystem = _allParticleSystems[0];
                }
            }
        }

        /// <summary>
        /// Initializes the particle burst with clamped counts, fruit color, and directional alignment.
        /// </summary>
        public void Initialize(Color juiceColor, Vector3 sliceDirection)
        {
            EnsureSystems();

            if (sliceDirection.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(sliceDirection);
            }

            float maxDuration = 0.4f;

            if (_allParticleSystems != null)
            {
                foreach (var ps in _allParticleSystems)
                {
                    if (ps == null) continue;

                    // 1. Clamp Max Particles for Mobile Fill Rate
                    var main = ps.main;
                    main.startColor = juiceColor;
                    if (main.maxParticles > _maxParticlesMobile)
                    {
                        main.maxParticles = _maxParticlesMobile;
                    }

                    // 2. Clamp Emission Bursts
                    var emission = ps.emission;
                    int burstCount = emission.burstCount;
                    if (burstCount > 0)
                    {
                        ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[burstCount];
                        emission.GetBursts(bursts);
                        bool changed = false;
                        for (int b = 0; b < bursts.Length; b++)
                        {
                            if (bursts[b].minCount > _maxBurstCountMobile)
                            {
                                bursts[b].minCount = _maxBurstCountMobile;
                                changed = true;
                            }
                            if (bursts[b].maxCount > _maxBurstCountMobile)
                            {
                                bursts[b].maxCount = _maxBurstCountMobile;
                                changed = true;
                            }
                        }
                        if (changed) emission.SetBursts(bursts);
                    }

                    float psDuration = main.duration + main.startLifetime.constantMax;
                    if (psDuration > maxDuration) maxDuration = psDuration;

                    ps.Clear(true);
                    ps.Play();
                }
            }

            if (_recycleCoroutine != null)
            {
                StopCoroutine(_recycleCoroutine);
            }
            _recycleCoroutine = StartCoroutine(RecycleRoutine(maxDuration + 0.1f));
        }

        private IEnumerator RecycleRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);

            if (FruitPool.Instance != null)
            {
                FruitPool.Instance.ReturnSplashFX(this);
            }
            else
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
                    Destroy(gameObject);
                else
                    DestroyImmediate(gameObject);
#else
                Destroy(gameObject);
#endif
            }
        }
    }
}
