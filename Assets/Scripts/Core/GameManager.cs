using UnityEngine;
using VRSlashGame.Events;

namespace VRSlashGame.Core
{
    /// <summary>
    /// Central manager responsible for tracking game state, score, lives, and combo streaks.
    /// Integrates with GameEvents for decoupled communication with other game elements.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private static GameManager _instance;
        public static GameManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<GameManager>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Game Configuration")]
        [Tooltip("Number of lives the player begins with.")]
        [SerializeField] private int _initialLives = 3;

        [Tooltip("Base score points awarded per sliced fruit.")]
        [SerializeField] private int _baseScorePerFruit = 100;

        [Tooltip("Bonus points multiplier applied per combo count.")]
        [SerializeField] private int _bonusScorePerCombo = 25;

        [Tooltip("Time window in seconds before the active combo resets to zero.")]
        [SerializeField] private float _comboTimeout = 1.5f;

        [Tooltip("Whether the game automatically begins when the scene loads.")]
        [SerializeField] private bool _autoStartOnAwake = true;

        [Header("Runtime State (Read-Only)")]
        [SerializeField] private GameState _currentState = GameState.NotStarted;
        [SerializeField] private int _score = 0;
        [SerializeField] private int _lives = 0;
        [SerializeField] private int _combo = 0;

        private float _comboTimer = 0f;

        public GameState CurrentState => _currentState;
        public int Score => _score;
        public int Lives => _lives;
        public int Combo => _combo;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            GameEvents.OnFruitSliced += HandleFruitSliced;
            GameEvents.OnFruitMissed += HandleFruitMissed;
        }

        private void OnDisable()
        {
            GameEvents.OnFruitSliced -= HandleFruitSliced;
            GameEvents.OnFruitMissed -= HandleFruitMissed;
        }

        private void Start()
        {
            if (_autoStartOnAwake)
            {
                StartGame();
            }
        }

        private void Update()
        {
            CheckEditorKeyboardFallback();

            if (_currentState != GameState.Playing) return;

            UpdateComboTimer();
        }

        private void CheckEditorKeyboardFallback()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool spacePressed = false;
            try
            {
                if (UnityEngine.InputSystem.Keyboard.current != null &&
                    UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    spacePressed = true;
                }
            }
            catch { }

            if (spacePressed)
            {
                Debug.Log("[GameManager] Spacebar pressed (Editor fallback).");
                if (_currentState == GameState.Playing)
                {
                    RestartGame();
                }
                else
                {
                    StartGame();
                }
            }
#endif
        }

        #region Game Loop Control

        /// <summary>
        /// Starts a new game session and initializes stats.
        /// </summary>
        public void StartGame()
        {
            _score = 0;
            _lives = _initialLives;
            _combo = 0;
            _comboTimer = 0f;
            _currentState = GameState.Playing;

            GameEvents.TriggerScoreChanged(_score);
            GameEvents.TriggerLivesChanged(_lives);
            GameEvents.TriggerComboChanged(_combo);
            GameEvents.TriggerGameStarted();

            Debug.Log($"[GameManager] Game Started. Lives: {_lives}, Score: {_score}");
        }

        /// <summary>
        /// Restarts the game from the beginning.
        /// </summary>
        public void RestartGame()
        {
            GameEvents.TriggerGameRestarted();
            StartGame();
        }

        /// <summary>
        /// Ends the current game session when lives are exhausted.
        /// </summary>
        public void GameOver()
        {
            if (_currentState == GameState.GameOver) return;

            _currentState = GameState.GameOver;
            _combo = 0;
            GameEvents.TriggerComboChanged(_combo);
            GameEvents.TriggerGameOver(_score);

            Debug.Log($"[GameManager] Game Over! Final Score: {_score}");
        }

        #endregion

        /// <summary>
        /// Returns the active score multiplier: 1x for 0-4 combo, 2x for 5-9 combo, 3x for 10+ combo.
        /// </summary>
        public int ComboMultiplier
        {
            get
            {
                if (_combo >= 10) return 3;
                if (_combo >= 5) return 2;
                return 1;
            }
        }

        #region Gameplay Handlers

        private void HandleFruitSliced(FruitSliceData sliceData)
        {
            if (_currentState != GameState.Playing) return;

            // Increment combo & reset timer
            _combo++;
            _comboTimer = _comboTimeout;
            GameEvents.TriggerComboChanged(_combo);

            // Calculate score with combo multiplier (1x <5, 2x 5-9, 3x 10+)
            int basePoints = sliceData.BasePoints > 0 ? sliceData.BasePoints : _baseScorePerFruit;
            int multiplier = ComboMultiplier;
            int earnedPoints = basePoints * multiplier;
            _score += Mathf.Max(0, earnedPoints);
            GameEvents.TriggerScoreChanged(_score);

            Debug.Log($"[GameManager] Fruit sliced! Base: {basePoints} x{multiplier} = +{earnedPoints} pts. Combo: {_combo}, Total Score: {_score}");
        }

        private void HandleFruitMissed()
        {
            if (_currentState != GameState.Playing) return;

            // Reset combo on miss
            ResetCombo();

            // Deduct life
            _lives--;
            GameEvents.TriggerLivesChanged(_lives);
            Debug.Log($"[GameManager] Fruit missed! Remaining lives: {_lives}");

            if (_lives <= 0)
            {
                _lives = 0;
                GameOver();
            }
        }

        private void UpdateComboTimer()
        {
            if (_combo <= 0) return;

            _comboTimer -= Time.deltaTime;
            if (_comboTimer <= 0f)
            {
                ResetCombo();
            }
        }

        private void ResetCombo()
        {
            if (_combo > 0)
            {
                _combo = 0;
                _comboTimer = 0f;
                GameEvents.TriggerComboChanged(_combo);
                Debug.Log("[GameManager] Combo reset to 0.");
            }
        }

        #endregion
    }
}
