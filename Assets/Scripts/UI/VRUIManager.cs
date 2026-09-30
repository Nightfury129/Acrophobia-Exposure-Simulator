using UnityEngine;
using UnityEngine.UI;
using VRSlashGame.Core;
using VRSlashGame.Events;

namespace VRSlashGame.UI
{
    /// <summary>
    /// Manages the floating world-space VR user interface.
    /// Handles Title Screen ("VR SLASH"), in-game HUD (Score, Lives, Combo),
    /// and Game Over screen. Listens to GameEvents and handles A / X button & Spacebar inputs.
    /// </summary>
    public class VRUIManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject _titlePanel;
        [SerializeField] private GameObject _hudPanel;
        [SerializeField] private GameObject _gameOverPanel;

        [Header("HUD Text Elements")]
        [SerializeField] private Text _scoreText;
        [SerializeField] private Text _livesText;
        [SerializeField] private Text _comboText;

        [Header("Game Over Text Elements")]
        [SerializeField] private Text _finalScoreText;

        [Header("UI State (Read-Only)")]
        [SerializeField] private bool _isShowingTitle = true;
        [SerializeField] private bool _isShowingGameOver = false;

        public bool IsShowingTitle => _isShowingTitle;
        public bool IsShowingGameOver => _isShowingGameOver;
        public Text ScoreText => _scoreText;
        public Text LivesText => _livesText;
        public Text ComboText => _comboText;
        public Text FinalScoreText => _finalScoreText;

        private void OnEnable()
        {
            GameEvents.OnGameStarted += HandleGameStarted;
            GameEvents.OnGameOver += HandleGameOver;
            GameEvents.OnGameRestarted += HandleGameRestarted;
            GameEvents.OnScoreChanged += UpdateScoreDisplay;
            GameEvents.OnLivesChanged += UpdateLivesDisplay;
            GameEvents.OnComboChanged += UpdateComboDisplay;
        }

        private void OnDisable()
        {
            GameEvents.OnGameStarted -= HandleGameStarted;
            GameEvents.OnGameOver -= HandleGameOver;
            GameEvents.OnGameRestarted -= HandleGameRestarted;
            GameEvents.OnScoreChanged -= UpdateScoreDisplay;
            GameEvents.OnLivesChanged -= UpdateLivesDisplay;
            GameEvents.OnComboChanged -= UpdateComboDisplay;
        }

        private void Start()
        {
            ShowTitleScreen();
        }

        private void Update()
        {
            CheckStartRestartInput();
        }

        #region Input Handling

        /// <summary>
        /// Polls Meta Quest controller buttons (A or X) and keyboard Spacebar to start or restart the game.
        /// </summary>
        private void CheckStartRestartInput()
        {
            bool triggerAction = false;

            // 1. Meta Quest VR Controller input: A (Right) or X (Left)
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One) || OVRInput.GetDown(OVRInput.Button.Three) ||
                    OVRInput.GetDown(OVRInput.RawButton.A) || OVRInput.GetDown(OVRInput.RawButton.X))
                {
                    triggerAction = true;
                }
            }
            catch { }

            // 2. Keyboard fallback: Spacebar
            try
            {
                if (UnityEngine.InputSystem.Keyboard.current != null &&
                    UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    triggerAction = true;
                }
            }
            catch { }

            if (triggerAction)
            {
                if (_isShowingTitle)
                {
                    Debug.Log("[VRUIManager] Start triggered via Controller/Keyboard.");
                    if (GameManager.Instance != null)
                    {
                        GameManager.Instance.StartGame();
                    }
                }
                else if (_isShowingGameOver)
                {
                    Debug.Log("[VRUIManager] Restart triggered via Controller/Keyboard.");
                    if (GameManager.Instance != null)
                    {
                        GameManager.Instance.RestartGame();
                    }
                }
            }
        }

        #endregion

        #region Panel State Transitions

        public void ShowTitleScreen()
        {
            _isShowingTitle = true;
            _isShowingGameOver = false;

            if (_titlePanel != null) _titlePanel.SetActive(true);
            if (_hudPanel != null) _hudPanel.SetActive(false);
            if (_gameOverPanel != null) _gameOverPanel.SetActive(false);
        }

        public void ShowHUD()
        {
            _isShowingTitle = false;
            _isShowingGameOver = false;

            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_hudPanel != null) _hudPanel.SetActive(true);
            if (_gameOverPanel != null) _gameOverPanel.SetActive(false);
        }

        public void ShowGameOverScreen(int finalScore)
        {
            _isShowingTitle = false;
            _isShowingGameOver = true;

            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_hudPanel != null) _hudPanel.SetActive(false);
            if (_gameOverPanel != null) _gameOverPanel.SetActive(true);

            if (_finalScoreText != null)
            {
                _finalScoreText.text = $"FINAL SCORE: {finalScore:N0}";
            }
        }

        #endregion

        #region Event Handlers

        private void HandleGameStarted()
        {
            ShowHUD();
            UpdateScoreDisplay(0);
            UpdateLivesDisplay(3);
            UpdateComboDisplay(0);
        }

        private void HandleGameRestarted()
        {
            ShowHUD();
            UpdateScoreDisplay(0);
            UpdateLivesDisplay(3);
            UpdateComboDisplay(0);
        }

        private void HandleGameOver(int finalScore)
        {
            ShowGameOverScreen(finalScore);
        }

        public void UpdateScoreDisplay(int currentScore)
        {
            if (_scoreText != null)
            {
                _scoreText.text = $"SCORE: {currentScore:N0}";
            }
        }

        public void UpdateLivesDisplay(int remainingLives)
        {
            if (_livesText != null)
            {
                string hearts = "";
                for (int i = 0; i < remainingLives; i++)
                {
                    hearts += "❤ ";
                }
                _livesText.text = remainingLives > 0 ? $"LIVES: {hearts.Trim()}" : "LIVES: -";
            }
        }

        public void UpdateComboDisplay(int comboCount)
        {
            if (_comboText != null)
            {
                if (comboCount >= 10)
                {
                    _comboText.text = $"COMBO: {comboCount} (3x SUPER!)";
                    _comboText.color = new Color(1f, 0.25f, 0.25f); // Crimson fire
                }
                else if (comboCount >= 5)
                {
                    _comboText.text = $"COMBO: {comboCount} (2x FIRE!)";
                    _comboText.color = new Color(1f, 0.85f, 0.2f); // Vibrant gold
                }
                else if (comboCount > 0)
                {
                    _comboText.text = $"COMBO: {comboCount} (1x)";
                    _comboText.color = Color.white;
                }
                else
                {
                    _comboText.text = "COMBO: 0 (1x)";
                    _comboText.color = new Color(0.7f, 0.7f, 0.7f);
                }
            }
        }

        #endregion
    }
}
