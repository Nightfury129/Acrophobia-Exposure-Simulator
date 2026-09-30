using UnityEngine;
using VRSlashGame.Events;

namespace VRSlashGame.Gameplay
{
    /// <summary>
    /// Represents an individual sliceable fruit.
    /// Uses a kinematic Rigidbody and trigger Collider for VR slash detection.
    /// Trajectory and gravity are simulated kinematically in FixedUpdate.
    /// Integrated with FruitPool for zero-allocation recycling and slicing.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class Fruit : MonoBehaviour
    {
        [Header("Fruit Properties")]
        [SerializeField] private string _fruitName = "Fruit";
        [SerializeField] private int _baseScorePoints = 100;
        [SerializeField] private float _lifetime = 5.0f;
        [Tooltip("Color of the particle burst spawned when sliced.")]
        [SerializeField] private Color _splashColor = Color.red;
        [Tooltip("Particle splash effect prefab to spawn upon being sliced.")]
        [SerializeField] private GameObject _splashEffectPrefab;

        [Header("Runtime Motion State (Read-Only)")]
        [SerializeField] private Vector3 _velocity;
        [SerializeField] private Vector3 _angularVelocity;
        [SerializeField] private bool _isSliced;
        [SerializeField] private float _age;

        private Rigidbody _rb;
        private Collider _col;
        private Transform _playerCameraTransform;
        private bool _hasTriggeredWhoosh = false;

        public string FruitName => _fruitName;
        public int BaseScorePoints => _baseScorePoints;
        public bool IsSliced => _isSliced;
        public Vector3 Velocity => _velocity;
        public Color SplashColor => _splashColor;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;

            _col = GetComponent<Collider>();
            if (_col == null) _col = GetComponentInChildren<Collider>();
            if (_col != null) _col.isTrigger = true;

            if (Camera.main != null)
            {
                _playerCameraTransform = Camera.main.transform;
            }
            else
            {
                GameObject centerEye = GameObject.Find("CenterEyeAnchor");
                if (centerEye != null) _playerCameraTransform = centerEye.transform;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isSliced) return;

            Blade blade = other.GetComponent<Blade>();
            if (blade == null) blade = other.GetComponentInParent<Blade>();

            if (blade != null)
            {
                Debug.Log("[Slash] Hit fruit: " + gameObject.name + " contacted by " + other.name);
                blade.CheckAndSliceFruit(GetComponent<Collider>());
            }
        }

        /// <summary>
        /// Initializes the fruit's flight trajectory, rotation drift, and lifetime.
        /// </summary>
        public void Launch(Vector3 initialVelocity, Vector3 rotationalDrift, float lifetime = 5.0f)
        {
            _velocity = initialVelocity;
            _angularVelocity = rotationalDrift;
            _lifetime = lifetime;
            _age = 0f;
            _isSliced = false;
            _hasTriggeredWhoosh = false;

            if (_col != null)
            {
                _col.enabled = true;
                _col.isTrigger = true;
            }
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
        }

        private void FixedUpdate()
        {
            if (_isSliced) return;

            float dt = Time.fixedDeltaTime;

            // Apply gravity kinematically
            _velocity += Physics.gravity * dt;

            // Update position
            Vector3 nextPos = _rb.position + _velocity * dt;
            _rb.MovePosition(nextPos);

            // Update rotational tumbling
            if (_angularVelocity.sqrMagnitude > 0.001f)
            {
                Quaternion deltaRot = Quaternion.Euler(_angularVelocity * dt);
                _rb.MoveRotation(_rb.rotation * deltaRot);
            }

            // Fly-by whoosh sound
            if (!_hasTriggeredWhoosh && _playerCameraTransform != null)
            {
                float distSq = (nextPos - _playerCameraTransform.position).sqrMagnitude;
                if (distSq < 1.96f) // < 1.4m
                {
                    _hasTriggeredWhoosh = true;
                    if (VRSlashGame.Audio.AudioManager.Instance != null)
                    {
                        VRSlashGame.Audio.AudioManager.Instance.PlayFlyByWhoosh(nextPos);
                    }
                }
            }
        }

        private void Update()
        {
            if (_isSliced) return;

            _age += Time.deltaTime;
            if (_age >= _lifetime)
            {
                Miss();
            }
        }

        [Header("Dissolve & Slicing Effects")]
        [Tooltip("Duration in seconds for the fruit halves to dissolve.")]
        [SerializeField] private float _dissolveDuration = 1.5f;

        [Tooltip("Separation speed of the two fruit halves outward from the cut plane (m/s).")]
        [SerializeField] private float _separationSpeed = 1.5f;

        /// <summary>
        /// Slices the fruit, spawning colorful juice particles, awarding points, and firing OnFruitSliced event.
        /// Returns true if successfully sliced, false if already sliced (prevents double-hits).
        /// </summary>
        public bool Slice(Vector3 sliceDirection, Vector3 hitPoint, Vector3? customSlicePlaneNormal = null)
        {
            if (_isSliced) return false;
            _isSliced = true;

            if (_col != null) _col.enabled = false;

            // 1. Determine world slice plane normal
            Vector3 sliceNorm;
            if (customSlicePlaneNormal.HasValue && customSlicePlaneNormal.Value.sqrMagnitude > 0.001f)
            {
                sliceNorm = customSlicePlaneNormal.Value.normalized;
            }
            else
            {
                sliceNorm = Vector3.Cross(sliceDirection.normalized, transform.up);
                if (sliceNorm.sqrMagnitude < 0.01f)
                {
                    sliceNorm = Vector3.Cross(sliceDirection.normalized, Vector3.forward);
                    if (sliceNorm.sqrMagnitude < 0.01f) sliceNorm = Vector3.right;
                }
                sliceNorm.Normalize();
            }

            // 2. Spawn particle burst matching fruit color (pooled zero-allocation)
            SpawnSplashParticles(sliceDirection, hitPoint);

            // 3. Play distinct 3D spatial fruit slicing sound effect at hit point
            if (VRSlashGame.Audio.AudioManager.Instance != null)
            {
                VRSlashGame.Audio.AudioManager.Instance.PlayFruitSliceSound(_fruitName, _splashColor, hitPoint);
            }

            // 4. Dispatch event
            GameEvents.TriggerFruitSliced(new FruitSliceData(hitPoint, sliceDirection, _baseScorePoints, 0, _fruitName));

            // 5. Perform runtime mesh slicing into 2 pooled GameObject halves
            SpawnSlicedHalves(hitPoint, sliceNorm);

            // 6. Return fruit to pool
            RecycleFruit();
            return true;
        }

        private void SpawnSlicedHalves(Vector3 hitPoint, Vector3 worldSliceNormal)
        {
            MeshFilter originalFilter = GetComponentInChildren<MeshFilter>();
            if (originalFilter == null || originalFilter.sharedMesh == null) return;

            // Compute outward impulse velocities & rotational tumbling for both halves
            Vector3 velA = _velocity + (worldSliceNormal * _separationSpeed);
            Vector3 velB = _velocity - (worldSliceNormal * _separationSpeed);
            Vector3 tumbleA = _angularVelocity + new Vector3(Random.Range(-120f, 120f), Random.Range(-120f, 120f), Random.Range(-120f, 120f));
            Vector3 tumbleB = _angularVelocity + new Vector3(Random.Range(-120f, 120f), Random.Range(-120f, 120f), Random.Range(-120f, 120f));

            Vector3 localOrigin = originalFilter.transform.InverseTransformPoint(hitPoint);
            Vector3 localNorm = originalFilter.transform.InverseTransformDirection(worldSliceNormal).normalized;
            Plane localPlane = new Plane(localNorm, localOrigin);

            // Use pooled halves with pre-allocated Mesh instances for 0 B GC allocation
            if (FruitPool.Instance != null && FruitPool.Instance.TryGetHalfPair(out DissolvingFruitHalf halfA, out DissolvingFruitHalf halfB))
            {
                halfA.transform.position = originalFilter.transform.position;
                halfA.transform.rotation = originalFilter.transform.rotation;
                halfA.transform.localScale = originalFilter.transform.lossyScale;

                halfB.transform.position = originalFilter.transform.position;
                halfB.transform.rotation = originalFilter.transform.rotation;
                halfB.transform.localScale = originalFilter.transform.lossyScale;

                bool sliced = MeshSlicer.Slice(originalFilter.sharedMesh, localPlane, halfA.PooledMesh, halfB.PooledMesh);

                MeshRenderer origRend = originalFilter.GetComponent<MeshRenderer>();
                Material[] origMats = origRend != null ? origRend.sharedMaterials : null;

                if (origMats != null && origMats.Length > 0)
                {
                    halfA.Renderer.sharedMaterials = origMats;
                    halfB.Renderer.sharedMaterials = origMats;
                }

                float clipSideA = sliced ? 0f : 1.0f;
                float clipSideB = sliced ? 0f : -1.0f;

                halfA.Initialize(velA, tumbleA, localOrigin, localNorm, clipSideA, _dissolveDuration);
                halfB.Initialize(velB, tumbleB, localOrigin, localNorm, clipSideB, _dissolveDuration);
            }
            else
            {
                // Fallback: instantiate if pooling is unavailable
                SpawnSlicedHalvesFallback(hitPoint, worldSliceNormal, velA, velB, tumbleA, tumbleB, localOrigin, localNorm);
            }
        }

        private void SpawnSlicedHalvesFallback(
            Vector3 hitPoint, Vector3 worldSliceNormal, 
            Vector3 velA, Vector3 velB, Vector3 tumbleA, Vector3 tumbleB, 
            Vector3 localOrigin, Vector3 localNorm)
        {
            MeshFilter mf = GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            GameObject halfObjA = new GameObject($"{name}_SlicedHalfA");
            halfObjA.transform.position = transform.position;
            halfObjA.transform.rotation = transform.rotation;
            halfObjA.transform.localScale = transform.localScale;

            GameObject halfObjB = new GameObject($"{name}_SlicedHalfB");
            halfObjB.transform.position = transform.position;
            halfObjB.transform.rotation = transform.rotation;
            halfObjB.transform.localScale = transform.localScale;

            Vector3 childLocalHitPoint = mf.transform.InverseTransformPoint(hitPoint);
            Vector3 childLocalNormal = mf.transform.InverseTransformDirection(worldSliceNormal).normalized;
            Plane localPlane = new Plane(childLocalNormal, childLocalHitPoint);

            MeshRenderer origRend = mf.GetComponent<MeshRenderer>();
            Material[] origMats = origRend != null ? origRend.sharedMaterials : null;

            if (MeshSlicer.Slice(mf.sharedMesh, localPlane, out Mesh posMesh, out Mesh negMesh))
            {
                CreateChildMesh(halfObjA.transform, mf.transform, posMesh, origMats);
                CreateChildMesh(halfObjB.transform, mf.transform, negMesh, origMats);
            }
            else
            {
                CreateChildMesh(halfObjA.transform, mf.transform, mf.sharedMesh, origMats);
                CreateChildMesh(halfObjB.transform, mf.transform, mf.sharedMesh, origMats);
            }

            DissolvingFruitHalf compA = halfObjA.AddComponent<DissolvingFruitHalf>();
            compA.Initialize(velA, tumbleA, localOrigin, localNorm, 0f, _dissolveDuration);

            DissolvingFruitHalf compB = halfObjB.AddComponent<DissolvingFruitHalf>();
            compB.Initialize(velB, tumbleB, localOrigin, localNorm, 0f, _dissolveDuration);
        }

        private static void CreateChildMesh(Transform parent, Transform originalTransform, Mesh mesh, Material[] materials)
        {
            GameObject child = new GameObject(mesh.name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = originalTransform.localPosition;
            child.transform.localRotation = originalTransform.localRotation;
            child.transform.localScale = originalTransform.localScale;

            MeshFilter mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            if (materials != null && materials.Length > 0)
            {
                MeshRenderer mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
            }
        }

        private void SpawnSplashParticles(Vector3 sliceDirection, Vector3 hitPoint)
        {
            if (_splashEffectPrefab != null && FruitPool.Instance != null)
            {
                FruitSplashFX splashComp = FruitPool.Instance.GetSplashFX(_splashEffectPrefab, hitPoint, Quaternion.identity);
                if (splashComp != null)
                {
                    splashComp.Initialize(_splashColor, sliceDirection);
                }
            }
            else if (_splashEffectPrefab != null)
            {
                GameObject fx = Instantiate(_splashEffectPrefab, hitPoint, Quaternion.identity);
                FruitSplashFX splashComp = fx.GetComponent<FruitSplashFX>();
                if (splashComp != null)
                {
                    splashComp.Initialize(_splashColor, sliceDirection);
                }
            }
        }

        /// <summary>
        /// Triggered when the fruit falls below the play space or expires unsliced.
        /// </summary>
        public void Miss()
        {
            if (_isSliced) return;
            _isSliced = true;

            GameEvents.TriggerFruitMissed();
            RecycleFruit();
        }

        private void RecycleFruit()
        {
            if (FruitPool.Instance != null)
            {
                FruitPool.Instance.ReturnFruit(this);
            }
            else
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(gameObject);
                else
                    Destroy(gameObject);
#else
                Destroy(gameObject);
#endif
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(transform.position, _velocity * 0.2f);
        }
    }
}
