using System;
using UnityEngine;

namespace VRSlashGame.Events
{
    /// <summary>
    /// Centralized, decoupled event bus for the VR Slash Game.
    /// Allows subsystems (GameManager, Blade, Spawner, UI, Audio, Haptics) to communicate
    /// without holding direct references to each other.
    /// </summary>
    public static class GameEvents
    {
        #region Game Lifecycle Events

        /// <summary>
        /// Triggered when the game loop starts.
        /// </summary>
        public static event Action OnGameStarted;

        /// <summary>
        /// Triggered when the game is over. Parameter: final score achieved.
        /// </summary>
        public static event Action<int> OnGameOver;

        /// <summary>
        /// Triggered when a new game is restarted.
        /// </summary>
        public static event Action OnGameRestarted;

        #endregion

        #region Fruit Gameplay Events

        /// <summary>
        /// Triggered when any fruit is successfully sliced.
        /// </summary>
        public static event Action<FruitSliceData> OnFruitSliced;

        /// <summary>
        /// Triggered when a fruit drops below the playfield without being sliced.
        /// </summary>
        public static event Action OnFruitMissed;

        #endregion

        #region Player State Events

        /// <summary>
        /// Triggered when player score changes. Parameter: new current score.
        /// </summary>
        public static event Action<int> OnScoreChanged;

        /// <summary>
        /// Triggered when remaining lives change. Parameter: current remaining lives.
        /// </summary>
        public static event Action<int> OnLivesChanged;

        /// <summary>
        /// Triggered when combo multiplier / streak changes. Parameter: current combo count.
        /// </summary>
        public static event Action<int> OnComboChanged;

        #endregion

        #region Invocation Dispatchers

        public static void TriggerGameStarted()
        {
            OnGameStarted?.Invoke();
        }

        public static void TriggerGameOver(int finalScore)
        {
            OnGameOver?.Invoke(finalScore);
        }

        public static void TriggerGameRestarted()
        {
            OnGameRestarted?.Invoke();
        }

        public static void TriggerFruitSliced(FruitSliceData data)
        {
            OnFruitSliced?.Invoke(data);
        }

        public static void TriggerFruitMissed()
        {
            OnFruitMissed?.Invoke();
        }

        public static void TriggerScoreChanged(int currentScore)
        {
            OnScoreChanged?.Invoke(currentScore);
        }

        public static void TriggerLivesChanged(int currentLives)
        {
            OnLivesChanged?.Invoke(currentLives);
        }

        public static void TriggerComboChanged(int comboCount)
        {
            OnComboChanged?.Invoke(comboCount);
        }

        /// <summary>
        /// Clears all event subscribers. Useful when reloading scenes or running test suites.
        /// </summary>
        public static void ClearAllSubscriptions()
        {
            OnGameStarted = null;
            OnGameOver = null;
            OnGameRestarted = null;
            OnFruitSliced = null;
            OnFruitMissed = null;
            OnScoreChanged = null;
            OnLivesChanged = null;
            OnComboChanged = null;
        }

        #endregion
    }
}
