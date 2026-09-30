using UnityEngine;

namespace VRSlashGame.Gameplay
{
    /// <summary>
    /// Attached to a sliced fruit half. Simulates independent physics momentum,
    /// gravity drift, and rotational tumbling while driving a 1.5-second dissolve
    /// animation outward from the blade slice point via MaterialPropertyBlock.
    /// Integrated with FruitPool for zero-allocation recycling.
    /// </summary>
    public class DissolvingFruitHalf : MonoBehaviour
    {
        private static readonly int DissolveProgressID = Shader.PropertyToID("_DissolveProgress");
        private static readonly int SliceOriginID = Shader.PropertyToID("_SliceOrigin");
        private static readonly int SliceNormalID = Shader.PropertyToID("_SliceNormal");
        private static readonly int ClipSideID = Shader.PropertyToID("_ClipSide");
        private static readonly int DissolveMaxRadiusID = Shader.PropertyToID("_DissolveMaxRadius");

        [SerializeField] private float _dissolveDuration = 1.5f;
        [SerializeField] private Vector3 _velocity;
        [SerializeField] private Vector3 _angularVelocity;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private MaterialPropertyBlock _propBlock;
        private float _elapsedTime;
        private bool _isInitialized;

        public MeshFilter Filter => _meshFilter;
        public MeshRenderer Renderer => _meshRenderer;
        public Mesh PooledMesh => _meshFilter != null ? _meshFilter.sharedMesh : null;
        public float DissolveProgress => Mathf.Clamp01(_elapsedTime / Mathf.Max(0.01f, _dissolveDuration));
        public Vector3 Velocity => _velocity;

        private void Awake()
        {
            EnsureComponents();
        }

        private void EnsureComponents()
        {
            if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
            if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Manually sets the dissolve progress for testing and visual previews.
        /// </summary>
        public void SetSimulatedProgress(float progress)
        {
            EnsureComponents();
            _elapsedTime = Mathf.Clamp01(progress) * _dissolveDuration;
            if (_meshRenderer != null && _propBlock != null)
            {
                _meshRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetFloat(DissolveProgressID, progress);
                _meshRenderer.SetPropertyBlock(_propBlock);
            }
        }

        /// <summary>
        /// Initializes the dissolving half with kinematic momentum, slice plane clipping, and dissolve parameters.
        /// </summary>
        public void Initialize(
            Vector3 initialVelocity,
            Vector3 angularVelocity,
            Vector3 localSliceOrigin,
            Vector3 localSliceNormal,
            float clipSide,
            float duration = 1.5f,
            float maxRadius = 0.65f)
        {
            EnsureComponents();

            _velocity = initialVelocity;
            _angularVelocity = angularVelocity;
            _dissolveDuration = duration;
            _elapsedTime = 0f;

            if (_meshRenderer != null)
            {
                _meshRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetFloat(ClipSideID, clipSide);
                _propBlock.SetVector(SliceOriginID, new Vector4(localSliceOrigin.x, localSliceOrigin.y, localSliceOrigin.z, 0f));
                _propBlock.SetVector(SliceNormalID, new Vector4(localSliceNormal.x, localSliceNormal.y, localSliceNormal.z, 0f));
                _propBlock.SetFloat(DissolveMaxRadiusID, maxRadius);
                _propBlock.SetFloat(DissolveProgressID, 0f);
                _meshRenderer.SetPropertyBlock(_propBlock);
            }

            _isInitialized = true;
        }

        private void FixedUpdate()
        {
            if (!_isInitialized) return;

            float dt = Time.fixedDeltaTime;

            // Apply gravity kinematically to match fruit projectile physics
            _velocity += Physics.gravity * dt;
            transform.position += _velocity * dt;

            // Rotational tumbling
            if (_angularVelocity.sqrMagnitude > 0.001f)
            {
                transform.rotation *= Quaternion.Euler(_angularVelocity * dt);
            }
        }

        private void Update()
        {
            if (!_isInitialized) return;

            _elapsedTime += Time.deltaTime;
            float progress = DissolveProgress;

            if (_meshRenderer != null && _propBlock != null)
            {
                _meshRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetFloat(DissolveProgressID, progress);
                _meshRenderer.SetPropertyBlock(_propBlock);
            }

            if (_elapsedTime >= _dissolveDuration)
            {
                Recycle();
            }
        }

        private void Recycle()
        {
            _isInitialized = false;

            if (FruitPool.Instance != null)
            {
                FruitPool.Instance.ReturnHalf(this);
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
    }
}
