using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRSlashGame.Events;

namespace VRSlashGame.Gameplay
{
    /// <summary>
    /// Spawns fruits in front of the player in a wide arc at regular intervals.
    /// Calculates kinematic parabolic launch velocity so fruits arc up to arm height
    /// before falling back down with gravity.
    /// </summary>
    public class FruitSpawner : MonoBehaviour
    {
        [Header("Prefabs & Fallback")]
        [Tooltip("Fruit prefabs to spawn. If empty, colored primitive spheres will be spawned as fallback.")]
        [SerializeField] private GameObject[] _fruitPrefabs;

        [Header("Spawn Timing")]
        [Tooltip("Base time interval between fruit spawns (seconds).")]
        [SerializeField] private float _spawnInterval = 1.5f;

        [Tooltip("Minimum spawn interval reached at maximum difficulty (seconds).")]
        [SerializeField] private float _minSpawnInterval = 0.75f;

        [Tooltip("Time in seconds before multi-fruit waves begin spawning.")]
        [SerializeField] private float _multiFruitThresholdTime = 30.0f;

        [Tooltip("Rate at which spawn interval decreases per second of gameplay.")]
        [SerializeField] private float _difficultyScaleRate = 0.015f;

        [Tooltip("Whether to start spawning immediately when Play mode starts.")]
        [SerializeField] private bool _autoStartOnPlay = true;

        [Tooltip("Fruit lifetime before expiring as a miss.")]
        [SerializeField] private float _fruitLifetime = 5.0f;

        [Header("Arc & Trajectory Configuration")]
        [Tooltip("Player reference to spawn in front of (defaults to Camera.main or OVRCameraRig).")]
        [SerializeField] private Transform _playerTransform;

        [Tooltip("Horizontal distance in front of the player to spawn fruits (meters).")]
        [SerializeField] private Vector2 _spawnDistanceRange = new Vector2(1.3f, 1.7f);

        [Tooltip("Total horizontal arc angle in degrees centered on player forward.")]
        [SerializeField] private float _spawnArcAngle = 70f;

        [Tooltip("Vertical spawn height relative to player base (meters, below waist).")]
        [SerializeField] private float _spawnHeight = -0.4f;

        [Tooltip("Target apex height relative to player base (meters, arm/slash height).")]
        [SerializeField] private Vector2 _targetApexHeightRange = new Vector2(1.15f, 1.45f);

        [Tooltip("Max random lateral drift at apex (meters).")]
        [SerializeField] private float _lateralDriftVariation = 0.25f;

        [Tooltip("Random rotation speed (degrees per second).")]
        [SerializeField] private float _maxTumbleSpeed = 180f;

        [Header("Parent Container")]
        [SerializeField] private Transform _fruitsParent;

        private Coroutine _spawnCoroutine;
        private bool _isSpawning;
        private float _elapsedSessionTime;
        private readonly List<GameObject> _activeFruits = new List<GameObject>();

        public bool IsSpawning => _isSpawning;
        public float ElapsedSessionTime { get => _elapsedSessionTime; set => _elapsedSessionTime = value; }
        public float CurrentSpawnInterval => Mathf.Max(_minSpawnInterval, _spawnInterval - (_elapsedSessionTime * _difficultyScaleRate));

        private void Awake()
        {
            if (_playerTransform == null)
            {
                if (Camera.main != null)
                {
                    _playerTransform = Camera.main.transform;
                }
                else
                {
                    GameObject ovr = GameObject.Find("OVRCameraRig");
                    if (ovr != null) _playerTransform = ovr.transform;
                }
            }

            if (_fruitsParent == null)
            {
                GameObject container = new GameObject("ActiveFruits");
                container.transform.SetParent(transform);
                _fruitsParent = container.transform;
            }
        }

        private void OnEnable()
        {
            GameEvents.OnGameStarted += StartSpawning;
            GameEvents.OnGameOver += StopSpawningOnGameOver;
            GameEvents.OnGameRestarted += RestartSpawning;
        }

        private void OnDisable()
        {
            GameEvents.OnGameStarted -= StartSpawning;
            GameEvents.OnGameOver -= StopSpawningOnGameOver;
            GameEvents.OnGameRestarted -= RestartSpawning;
        }

        private void Start()
        {
            if (_fruitPrefabs != null && FruitPool.Instance != null)
            {
                foreach (var prefab in _fruitPrefabs)
                {
                    if (prefab != null)
                    {
                        FruitPool.Instance.PrewarmFruit(prefab, 4);
                    }
                }
            }

            if (_autoStartOnPlay && _spawnCoroutine == null)
            {
                StartSpawning();
            }
        }

        #region Spawner Control

        /// <summary>
        /// Starts the fruit spawning routine.
        /// </summary>
        public void StartSpawning()
        {
            if (_isSpawning) return;

            _elapsedSessionTime = 0f;
            _isSpawning = true;
            _spawnCoroutine = StartCoroutine(SpawnRoutine());
            Debug.Log("[FruitSpawner] Started spawning fruits.");
        }

        /// <summary>
        /// Stops spawning and cleans up any remaining active fruits.
        /// </summary>
        public void StopSpawning()
        {
            if (!_isSpawning) return;

            _isSpawning = false;
            if (_spawnCoroutine != null)
            {
                StopCoroutine(_spawnCoroutine);
                _spawnCoroutine = null;
            }

            ClearActiveFruits();
            Debug.Log("[FruitSpawner] Stopped spawning fruits.");
        }

        private void StopSpawningOnGameOver(int finalScore)
        {
            StopSpawning();
        }

        private void RestartSpawning()
        {
            StopSpawning();
            StartSpawning();
        }

        private void ClearActiveFruits()
        {
            for (int i = _activeFruits.Count - 1; i >= 0; i--)
            {
                if (_activeFruits[i] != null)
                {
                    Fruit f = _activeFruits[i].GetComponent<Fruit>();
                    if (f != null && FruitPool.Instance != null)
                    {
                        FruitPool.Instance.ReturnFruit(f);
                    }
                    else
                    {
#if UNITY_EDITOR
                        if (!Application.isPlaying)
                            DestroyImmediate(_activeFruits[i]);
                        else
                            Destroy(_activeFruits[i]);
#else
                        Destroy(_activeFruits[i]);
#endif
                    }
                }
            }
            _activeFruits.Clear();
        }

        #endregion

        #region Spawning Logic

        private IEnumerator SpawnRoutine()
        {
            // Initial short grace delay before the first fruit launches
            yield return new WaitForSeconds(0.5f);

            while (_isSpawning)
            {
                float currentInterval = CurrentSpawnInterval;

                if (_elapsedSessionTime >= _multiFruitThresholdTime)
                {
                    // Multi-fruit wave: launch 2 fruits across left and right sectors of the arc
                    SpawnFruitInArcRange(-_spawnArcAngle * 0.5f, -5f);
                    SpawnFruitInArcRange(5f, _spawnArcAngle * 0.5f);
                }
                else
                {
                    SpawnSingleFruit();
                }

                _elapsedSessionTime += currentInterval;
                yield return new WaitForSeconds(currentInterval);
            }
        }

        /// <summary>
        /// Spawns a single fruit with randomized arc trajectory across the full arc.
        /// </summary>
        public GameObject SpawnSingleFruit()
        {
            return SpawnFruitInArcRange(-_spawnArcAngle * 0.5f, _spawnArcAngle * 0.5f);
        }

        /// <summary>
        /// Spawns a single fruit constrained to a specific sub-arc (minAngle to maxAngle degrees).
        /// </summary>
        public GameObject SpawnFruitInArcRange(float minAngle, float maxAngle)
        {
            GameObject fruitInstance = CreateFruitInstance();
            if (fruitInstance == null) return null;

            _activeFruits.Add(fruitInstance);

            // Clean null references from active list
            _activeFruits.RemoveAll(item => item == null);

            // 1. Determine player reference position & forward heading
            Vector3 playerPos = Vector3.zero;
            Vector3 playerForward = Vector3.forward;

            if (_playerTransform != null)
            {
                playerPos = _playerTransform.position;
                playerForward = _playerTransform.forward;
                playerForward.y = 0;
                if (playerForward.sqrMagnitude < 0.001f) playerForward = Vector3.forward;
                playerForward.Normalize();
            }

            Vector3 playerRight = Vector3.Cross(Vector3.up, playerForward).normalized;

            // 2. Calculate arc spawn origin within the specified range
            float randomAngle = Random.Range(minAngle, maxAngle);
            float randomDistance = Random.Range(_spawnDistanceRange.x, _spawnDistanceRange.y);

            Vector3 arcDir = Quaternion.Euler(0, randomAngle, 0) * playerForward;
            Vector3 spawnOrigin = playerPos + arcDir * randomDistance;
            spawnOrigin.y = playerPos.y + _spawnHeight;

            fruitInstance.transform.position = spawnOrigin;
            fruitInstance.transform.rotation = Random.rotation;

            // 3. Calculate target apex (arm height) with slight lateral drift
            float targetApexHeight = Random.Range(_targetApexHeightRange.x, _targetApexHeightRange.y);
            float verticalRise = (playerPos.y + targetApexHeight) - spawnOrigin.y;
            if (verticalRise < 0.2f) verticalRise = 0.2f;

            float gravity = Mathf.Abs(Physics.gravity.y);
            if (gravity < 0.001f) gravity = 9.81f;

            // Vertical launch velocity: v_y = sqrt(2 * g * h)
            float launchVy = Mathf.Sqrt(2f * gravity * verticalRise);
            float timeToApex = launchVy / gravity;

            // Target apex horizontal position
            float lateralOffset = Random.Range(-_lateralDriftVariation, _lateralDriftVariation);
            Vector3 targetApexPos = playerPos + (playerForward * (randomDistance * 0.85f)) + (playerRight * (randomAngle * 0.01f + lateralOffset));
            targetApexPos.y = playerPos.y + targetApexHeight;

            // Horizontal launch velocity to arrive at target apex at peak time
            Vector3 horizontalDisp = targetApexPos - spawnOrigin;
            horizontalDisp.y = 0;
            Vector3 launchVxz = horizontalDisp / Mathf.Max(0.01f, timeToApex);

            Vector3 totalLaunchVelocity = new Vector3(launchVxz.x, launchVy, launchVxz.z);

            // 4. Random rotational tumbling
            Vector3 randomTumble = new Vector3(
                Random.Range(-_maxTumbleSpeed, _maxTumbleSpeed),
                Random.Range(-_maxTumbleSpeed, _maxTumbleSpeed),
                Random.Range(-_maxTumbleSpeed, _maxTumbleSpeed)
            );

            // 5. Initialize Fruit component
            Fruit fruitComponent = fruitInstance.GetComponent<Fruit>();
            if (fruitComponent == null)
            {
                fruitComponent = fruitInstance.AddComponent<Fruit>();
            }

            fruitComponent.Launch(totalLaunchVelocity, randomTumble, _fruitLifetime);

            // 6. Emit 3D directional spawn chime cue at spawn origin
            if (VRSlashGame.Audio.AudioManager.Instance != null)
            {
                VRSlashGame.Audio.AudioManager.Instance.PlaySpawnCue(spawnOrigin);
            }

            return fruitInstance;
        }

        private GameObject CreateFruitInstance()
        {
            if (_fruitPrefabs != null && _fruitPrefabs.Length > 0)
            {
                // Filter out any accidentally null prefab slots
                List<GameObject> validPrefabs = new List<GameObject>();
                foreach (var p in _fruitPrefabs)
                {
                    if (p != null) validPrefabs.Add(p);
                }

                if (validPrefabs.Count > 0)
                {
                    GameObject chosen = validPrefabs[Random.Range(0, validPrefabs.Count)];
                    if (FruitPool.Instance != null)
                    {
                        return FruitPool.Instance.GetFruit(chosen, _fruitsParent);
                    }
                    GameObject instance = Instantiate(chosen, _fruitsParent);
                    instance.name = chosen.name;
                    return instance;
                }
            }

            // Fallback: Primitive sphere if no prefabs configured
            return CreateFallbackSphere();
        }

        private GameObject CreateFallbackSphere()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "FallbackFruit_Sphere";
            sphere.transform.SetParent(_fruitsParent);
            sphere.transform.localScale = Vector3.one * 0.2f;

            // Ensure collider is trigger
            Collider col = sphere.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            // Ensure kinematic Rigidbody
            Rigidbody rb = sphere.GetComponent<Rigidbody>();
            if (rb == null) rb = sphere.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // Ensure FruitDissolve material
            Renderer rend = sphere.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = null;
#if UNITY_EDITOR
                Shader dissolveShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/FruitDissolve.shader");
                if (dissolveShader != null)
                {
                    mat = new Material(dissolveShader);
                }
#endif
                if (mat == null)
                {
                    Material dissolveMatAsset = Resources.Load<Material>("Mat_FruitDissolve");
                    if (dissolveMatAsset != null) mat = new Material(dissolveMatAsset);
                }

                if (mat != null)
                {
                    Color[] fruitColors = new Color[] { Color.red, Color.green, Color.yellow, new Color(1f, 0.5f, 0f) };
                    mat.color = fruitColors[Random.Range(0, fruitColors.Length)];
                    rend.sharedMaterial = mat;
                }
            }

            sphere.AddComponent<Fruit>();
            return sphere;
        }

        #endregion

        #region Debug Visualization

        private void OnDrawGizmosSelected()
        {
            Vector3 origin = _playerTransform != null ? _playerTransform.position : transform.position;
            Vector3 forward = _playerTransform != null ? _playerTransform.forward : Vector3.forward;
            forward.y = 0;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            Gizmos.color = Color.cyan;
            // Draw spawn arc preview
            int segments = 16;
            Vector3 prevPt = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = Mathf.Lerp(-_spawnArcAngle * 0.5f, _spawnArcAngle * 0.5f, t);
                Vector3 dir = Quaternion.Euler(0, angle, 0) * forward;
                Vector3 pt = origin + dir * _spawnDistanceRange.x;
                pt.y += _spawnHeight;

                if (i > 0)
                {
                    Gizmos.DrawLine(prevPt, pt);
                }
                prevPt = pt;
            }

            // Draw target arm height line
            Gizmos.color = Color.green;
            Vector3 apexCenter = origin + forward * 1.4f;
            apexCenter.y = origin.y + (_targetApexHeightRange.x + _targetApexHeightRange.y) * 0.5f;
            Gizmos.DrawWireCube(apexCenter, new Vector3(1.2f, 0.3f, 0.4f));
        }

        #endregion
    }
}
