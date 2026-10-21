using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Acrophobia.ProceduralEnvironment
{
    /// <summary>
    /// Swaps a per-tier environment scene in and out, additively.
    ///
    /// The session scene never unloads. Everything with state - the FSM, the CSV writer,
    /// the SUDS history, the narrator, the rig - lives there and is untouched by a swap,
    /// so none of it needs persisting across a load and no serialized reference has to
    /// survive one. Only the room changes.
    ///
    /// Tiers whose entry is left blank fall back to the environment built into the core
    /// scene, which is how tiers 1 and 5 still work while they remain in place.
    ///
    /// Loading is deliberately driven from inside the interlude blackout rather than from
    /// the tier event directly: a load takes however long it takes, and the black is the
    /// only place where that is invisible.
    /// </summary>
    public class EnvironmentSceneLoader : MonoBehaviour
    {
        [Tooltip("Scene name per tier, in order. Blank means use the core scene's own " +
                 "environment instead of loading anything.")]
        [SerializeField] private string[] _sceneNames = new string[5];

        [Tooltip("The in-core environment, hidden whenever a tier scene is loaded so the " +
                 "two cannot both be present.")]
        [SerializeField] private GameObject _corePlatform;

        private string _loaded;

        /// <summary>Spawn point of the currently loaded tier scene, if it has one.</summary>
        public Transform CurrentAnchor { get; private set; }

        public string LoadedScene => _loaded;

        /// <summary>
        /// Brings up the environment for a tier. Yields until the swap is complete, so a
        /// caller holding a black screen can wait on it.
        /// </summary>
        public IEnumerator ApplyTier(int tier)
        {
            if (_sceneNames == null || _sceneNames.Length == 0) yield break;

            int index = Mathf.Clamp(tier - 1, 0, _sceneNames.Length - 1);
            string want = _sceneNames[index];

            if (_loaded == want)
            {
                // Already correct. Re-resolve the anchor anyway: a step back can return
                // to a tier whose scene is still up but whose anchor we have forgotten.
                if (!string.IsNullOrEmpty(want)) CurrentAnchor = FindAnchor(SceneManager.GetSceneByName(want));
                yield break;
            }

            if (!string.IsNullOrEmpty(_loaded))
            {
                Scene old = SceneManager.GetSceneByName(_loaded);
                if (old.IsValid() && old.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(old);
                    while (unload != null && !unload.isDone) yield return null;
                }
                _loaded = null;
            }

            CurrentAnchor = null;

            if (string.IsNullOrEmpty(want))
            {
                if (_corePlatform != null) _corePlatform.SetActive(true);
                yield break;
            }

            AsyncOperation load = SceneManager.LoadSceneAsync(want, LoadSceneMode.Additive);
            if (load == null)
            {
                // Almost always a scene missing from the build settings list.
                Debug.LogError("[EnvironmentSceneLoader] Could not load '" + want +
                               "'. Is it added to Build Settings?", this);
                if (_corePlatform != null) _corePlatform.SetActive(true);
                yield break;
            }

            while (!load.isDone) yield return null;

            _loaded = want;
            CurrentAnchor = FindAnchor(SceneManager.GetSceneByName(want));

            // Only hide the core environment once the replacement is actually present,
            // so a failed load never leaves the participant standing in nothing.
            if (_corePlatform != null) _corePlatform.SetActive(false);
        }

        private static Transform FindAnchor(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindByName(root.transform, "Spawn_Anchor");
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindByName(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform found = FindByName(c, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
