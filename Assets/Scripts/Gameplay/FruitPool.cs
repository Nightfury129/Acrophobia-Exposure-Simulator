using System.Collections.Generic;
using UnityEngine;

namespace VRSlashGame.Gameplay
{
    /// <summary>
    /// High-performance mobile object pool for Whole Fruits, Sliced Halves, and Particle Splash FX.
    /// Eliminates runtime GC allocation and CPU hitching caused by Instantiate() and Destroy().
    /// </summary>
    public class FruitPool : MonoBehaviour
    {
        private static FruitPool _instance;
        public static FruitPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<FruitPool>();
                    if (_instance == null)
                    {
                        GameObject poolObj = new GameObject("[FruitPool]");
                        _instance = poolObj.AddComponent<FruitPool>();
                    }
                }
                return _instance;
            }
        }

        [Header("Pool Capacities")]
        [SerializeField] private int _initialFruitPerPrefab = 4;
        [SerializeField] private int _initialSlicedHalfPairs = 8;
        [SerializeField] private int _initialSplashFXCount = 6;

        // Prefab-based whole fruit pools
        private readonly Dictionary<string, Queue<GameObject>> _fruitPools = new Dictionary<string, Queue<GameObject>>();
        private readonly Dictionary<GameObject, string> _fruitKeyMap = new Dictionary<GameObject, string>();

        // Reusable Sliced Halves pool
        private readonly Queue<DissolvingFruitHalf> _slicedHalfPool = new Queue<DissolvingFruitHalf>();

        // Reusable Splash FX pool
        private readonly Queue<FruitSplashFX> _splashFXPool = new Queue<FruitSplashFX>();

        private Transform _poolContainer;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            if (_poolContainer == null)
            {
                GameObject container = new GameObject("PooledObjects");
                container.transform.SetParent(transform);
                _poolContainer = container.transform;
            }

            PrewarmSlicedHalves();
        }

        #region Sliced Halves Pooling

        private void PrewarmSlicedHalves()
        {
            for (int i = 0; i < _initialSlicedHalfPairs * 2; i++)
            {
                DissolvingFruitHalf half = CreateNewPooledHalf(i);
                half.gameObject.SetActive(false);
                _slicedHalfPool.Enqueue(half);
            }
        }

        private DissolvingFruitHalf CreateNewPooledHalf(int index)
        {
            GameObject go = new GameObject($"PooledHalf_{index}");
            go.transform.SetParent(_poolContainer);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = $"PooledHalfMesh_{index}" };

            go.AddComponent<MeshRenderer>();
            DissolvingFruitHalf comp = go.AddComponent<DissolvingFruitHalf>();
            return comp;
        }

        /// <summary>
        /// Borrows two pre-allocated dissolving fruit half GameObjects and their reusable meshes.
        /// </summary>
        public bool TryGetHalfPair(out DissolvingFruitHalf halfA, out DissolvingFruitHalf halfB)
        {
            halfA = GetOrCreateHalf();
            halfB = GetOrCreateHalf();
            return halfA != null && halfB != null;
        }

        private DissolvingFruitHalf GetOrCreateHalf()
        {
            DissolvingFruitHalf half = null;
            while (_slicedHalfPool.Count > 0 && half == null)
            {
                half = _slicedHalfPool.Dequeue();
            }

            if (half == null)
            {
                half = CreateNewPooledHalf(_slicedHalfPool.Count + 100);
            }

            half.gameObject.SetActive(true);
            return half;
        }

        /// <summary>
        /// Recycles a dissolving fruit half back into the pool.
        /// </summary>
        public void ReturnHalf(DissolvingFruitHalf half)
        {
            if (half == null) return;

            half.gameObject.SetActive(false);
            half.transform.SetParent(_poolContainer);
            _slicedHalfPool.Enqueue(half);
        }

        #endregion

        #region Whole Fruit Pooling

        /// <summary>
        /// Prewarms fruit instances for the specified prefab.
        /// </summary>
        public void PrewarmFruit(GameObject prefab, int count = -1)
        {
            if (prefab == null) return;
            string key = prefab.name;

            if (!_fruitPools.ContainsKey(key))
            {
                _fruitPools[key] = new Queue<GameObject>();
            }

            int spawnCount = count > 0 ? count : _initialFruitPerPrefab;
            for (int i = 0; i < spawnCount; i++)
            {
                GameObject obj = Instantiate(prefab, _poolContainer);
                obj.name = prefab.name;
                obj.SetActive(false);
                _fruitKeyMap[obj] = key;
                _fruitPools[key].Enqueue(obj);
            }
        }

        /// <summary>
        /// Retrieves a pooled fruit instance matching the given prefab, or spawns one if empty.
        /// </summary>
        public GameObject GetFruit(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;
            string key = prefab.name;

            if (!_fruitPools.ContainsKey(key))
            {
                _fruitPools[key] = new Queue<GameObject>();
            }

            GameObject fruitObj = null;
            Queue<GameObject> pool = _fruitPools[key];

            while (pool.Count > 0 && fruitObj == null)
            {
                fruitObj = pool.Dequeue();
            }

            if (fruitObj == null)
            {
                fruitObj = Instantiate(prefab, parent);
                fruitObj.name = prefab.name;
                _fruitKeyMap[fruitObj] = key;
            }
            else
            {
                fruitObj.transform.SetParent(parent);
            }

            fruitObj.SetActive(true);
            return fruitObj;
        }

        /// <summary>
        /// Recycles a fruit back to its respective prefab pool.
        /// </summary>
        public void ReturnFruit(Fruit fruit)
        {
            if (fruit == null) return;
            GameObject go = fruit.gameObject;

            go.SetActive(false);
            go.transform.SetParent(_poolContainer);

            if (_fruitKeyMap.TryGetValue(go, out string key) && _fruitPools.ContainsKey(key))
            {
                _fruitPools[key].Enqueue(go);
            }
            else
            {
                // Unmapped or fallback fruit: pool by GameObject name
                string nameKey = go.name;
                if (!_fruitPools.ContainsKey(nameKey))
                {
                    _fruitPools[nameKey] = new Queue<GameObject>();
                }
                _fruitKeyMap[go] = nameKey;
                _fruitPools[nameKey].Enqueue(go);
            }
        }

        #endregion

        #region Splash FX Pooling

        /// <summary>
        /// Retrieves or creates a pooled particle splash effect.
        /// </summary>
        public FruitSplashFX GetSplashFX(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            FruitSplashFX fx = null;

            while (_splashFXPool.Count > 0 && fx == null)
            {
                fx = _splashFXPool.Dequeue();
            }

            if (fx == null)
            {
                GameObject obj = prefab != null ? Instantiate(prefab, position, rotation) : new GameObject("SplashFX");
                fx = obj.GetComponent<FruitSplashFX>();
                if (fx == null) fx = obj.AddComponent<FruitSplashFX>();
            }
            else
            {
                fx.transform.position = position;
                fx.transform.rotation = rotation;
            }

            fx.gameObject.SetActive(true);
            return fx;
        }

        /// <summary>
        /// Recycles a particle splash effect back into the pool.
        /// </summary>
        public void ReturnSplashFX(FruitSplashFX fx)
        {
            if (fx == null) return;

            fx.gameObject.SetActive(false);
            fx.transform.SetParent(_poolContainer);
            _splashFXPool.Enqueue(fx);
        }

        #endregion

        /// <summary>
        /// Cleans up or returns all active elements upon game restart.
        /// </summary>
        public void ResetAll()
        {
            // Halves or active fruits can be recovered if needed
        }
    }
}
