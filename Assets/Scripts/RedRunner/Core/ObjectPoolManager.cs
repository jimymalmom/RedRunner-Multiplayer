using System.Collections.Generic;
using UnityEngine;

namespace RedRunner.Core
{
    /// <summary>
    /// High-performance object pooling system
    /// Reduces GC pressure and improves performance for frequently spawned objects
    /// </summary>
    public class ObjectPoolManager : MonoBehaviour
    {
        private static ObjectPoolManager instance;
        public static ObjectPoolManager Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("ObjectPoolManager");
                    instance = go.AddComponent<ObjectPoolManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }
        
        [System.Serializable]
        public class PoolConfig
        {
            public string poolKey;
            public GameObject prefab;
            public int initialSize = 10;
            public int maxSize = 100;
            public bool autoExpand = true;
        }
        
        [SerializeField] private List<PoolConfig> poolConfigs = new List<PoolConfig>();
        private Dictionary<string, ObjectPool> pools = new Dictionary<string, ObjectPool>();
        
        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                InitializePools();
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }
        
        private void InitializePools()
        {
            foreach (var config in poolConfigs)
            {
                CreatePool(config.poolKey, config.prefab, config.initialSize, config.maxSize, config.autoExpand);
            }
        }
        
        public void CreatePool(string poolKey, GameObject prefab, int initialSize = 10, int maxSize = 100, bool autoExpand = true)
        {
            if (pools.ContainsKey(poolKey))
            {
                Debug.LogWarning($"Pool with key '{poolKey}' already exists!");
                return;
            }
            
            var poolParent = new GameObject($"Pool_{poolKey}");
            poolParent.transform.SetParent(transform);
            
            var pool = new ObjectPool(prefab, poolParent.transform, initialSize, maxSize, autoExpand);
            pools[poolKey] = pool;
        }
        
        public T Get<T>(string poolKey) where T : Component
        {
            if (!pools.TryGetValue(poolKey, out var pool))
            {
                Debug.LogError($"Pool with key '{poolKey}' not found!");
                return null;
            }
            
            var obj = pool.Get();
            return obj?.GetComponent<T>();
        }
        
        public GameObject Get(string poolKey)
        {
            if (!pools.TryGetValue(poolKey, out var pool))
            {
                Debug.LogError($"Pool with key '{poolKey}' not found!");
                return null;
            }
            
            return pool.Get();
        }
        
        public void Return(string poolKey, GameObject obj)
        {
            if (!pools.TryGetValue(poolKey, out var pool))
            {
                Debug.LogError($"Pool with key '{poolKey}' not found!");
                Destroy(obj);
                return;
            }
            
            pool.Return(obj);
        }
        
        public void Return<T>(T component) where T : Component
        {
            if (component == null) return;
            
            var pooledObject = component.GetComponent<PooledObject>();
            if (pooledObject != null)
            {
                Return(pooledObject.PoolKey, component.gameObject);
            }
            else
            {
                Debug.LogWarning($"Object {component.name} doesn't have PooledObject component. Destroying instead.");
                Destroy(component.gameObject);
            }
        }
        
        public void PrewarmPool(string poolKey, int count)
        {
            if (pools.TryGetValue(poolKey, out var pool))
            {
                pool.Prewarm(count);
            }
        }
        
        public void ClearPool(string poolKey)
        {
            if (pools.TryGetValue(poolKey, out var pool))
            {
                pool.Clear();
            }
        }
        
        public void ClearAllPools()
        {
            foreach (var pool in pools.Values)
            {
                pool.Clear();
            }
        }
        
        public PoolStats GetPoolStats(string poolKey)
        {
            if (pools.TryGetValue(poolKey, out var pool))
            {
                return pool.GetStats();
            }
            return default;
        }
        
        public Dictionary<string, PoolStats> GetAllPoolStats()
        {
            var stats = new Dictionary<string, PoolStats>();
            foreach (var kvp in pools)
            {
                stats[kvp.Key] = kvp.Value.GetStats();
            }
            return stats;
        }
    }
    
    /// <summary>
    /// Individual object pool implementation
    /// </summary>
    public class ObjectPool
    {
        private readonly GameObject prefab;
        private readonly Transform parent;
        private readonly int maxSize;
        private readonly bool autoExpand;
        
        private readonly Queue<GameObject> available = new Queue<GameObject>();
        private readonly HashSet<GameObject> inUse = new HashSet<GameObject>();
        
        public ObjectPool(GameObject prefab, Transform parent, int initialSize, int maxSize, bool autoExpand)
        {
            this.prefab = prefab;
            this.parent = parent;
            this.maxSize = maxSize;
            this.autoExpand = autoExpand;
            
            // Create initial objects
            for (int i = 0; i < initialSize; i++)
            {
                CreateNewObject();
            }
        }
        
        private GameObject CreateNewObject()
        {
            var obj = Object.Instantiate(prefab, parent);
            obj.SetActive(false);
            
            // Add PooledObject component if it doesn't exist
            if (obj.GetComponent<PooledObject>() == null)
            {
                var pooledObject = obj.AddComponent<PooledObject>();
                pooledObject.Pool = this;
            }
            
            available.Enqueue(obj);
            return obj;
        }
        
        public GameObject Get()
        {
            GameObject obj = null;
            
            if (available.Count > 0)
            {
                obj = available.Dequeue();
            }
            else if (autoExpand && (inUse.Count + available.Count) < maxSize)
            {
                obj = CreateNewObject();
                available.Dequeue(); // Remove from available since we're returning it
            }
            else
            {
                Debug.LogWarning($"Pool for {prefab.name} is exhausted and cannot expand!");
                return null;
            }
            
            inUse.Add(obj);
            obj.SetActive(true);
            
            // Call OnSpawned if the object implements IPoolable
            var poolable = obj.GetComponent<IPoolable>();
            poolable?.OnSpawned();
            
            return obj;
        }
        
        public void Return(GameObject obj)
        {
            if (!inUse.Contains(obj))
            {
                Debug.LogWarning($"Trying to return object {obj.name} that wasn't obtained from this pool!");
                return;
            }
            
            inUse.Remove(obj);
            available.Enqueue(obj);
            
            // Call OnDespawned if the object implements IPoolable
            var poolable = obj.GetComponent<IPoolable>();
            poolable?.OnDespawned();
            
            obj.SetActive(false);
            obj.transform.SetParent(parent);
        }
        
        public void Prewarm(int count)
        {
            var objectsToCreate = Mathf.Min(count - available.Count, maxSize - (available.Count + inUse.Count));
            for (int i = 0; i < objectsToCreate; i++)
            {
                CreateNewObject();
            }
        }
        
        public void Clear()
        {
            // Destroy all objects
            while (available.Count > 0)
            {
                Object.Destroy(available.Dequeue());
            }
            
            foreach (var obj in inUse)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
            
            inUse.Clear();
        }
        
        public PoolStats GetStats()
        {
            return new PoolStats
            {
                Available = available.Count,
                InUse = inUse.Count,
                Total = available.Count + inUse.Count,
                MaxSize = maxSize
            };
        }
    }
    
    /// <summary>
    /// Interface for objects that need special handling when pooled
    /// </summary>
    public interface IPoolable
    {
        void OnSpawned();
        void OnDespawned();
    }
    
    /// <summary>
    /// Component attached to pooled objects to track their pool
    /// </summary>
    public class PooledObject : MonoBehaviour
    {
        public ObjectPool Pool { get; set; }
        public string PoolKey { get; set; }
        
        public void ReturnToPool()
        {
            ObjectPoolManager.Instance.Return(PoolKey, gameObject);
        }
        
        public void ReturnToPoolAfterDelay(float delay)
        {
            StartCoroutine(ReturnAfterDelay(delay));
        }
        
        private System.Collections.IEnumerator ReturnAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToPool();
        }
    }
    
    /// <summary>
    /// Statistics for object pool monitoring
    /// </summary>
    [System.Serializable]
    public struct PoolStats
    {
        public int Available;
        public int InUse;
        public int Total;
        public int MaxSize;
        
        public float UtilizationRate => Total > 0 ? (float)InUse / Total : 0f;
        public bool IsAtCapacity => Total >= MaxSize;
    }
}