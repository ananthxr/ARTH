using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[System.Serializable]
public class TeamDataRTDB
{
    public int teamNumber;
    public string teamName;
    public string uid;
    public string player1;
    public string player2;
    public string email;
    public int score;
}

[System.Serializable]
public class SessionData
{
    public bool started;
    public int currentClueNumber;
    public int cluesCompleted;
    public bool physicalGamePlayed;
    public int physicalGameScore;
    
    // Enhanced fields for physical game crash recovery
    public bool physicalGameRequired = false;    // True when current clue needs physical game completion
    public string currentPhase = "clue";         // "clue", "ar_hunt", "physical_game", "completed"
    public int[] completedClueIndices;           // Array of fully completed clue indices (0-based)
    public bool physicalGamePending = false;     // True when AR treasure collected but physical game not completed yet
}

public class FirebaseRTDBFetcher : MonoBehaviour
{
    [Header("Realtime Database Settings")]
    [Tooltip("Base RTDB URL without trailing slash. Example: https://arth2-169a4-default-rtdb.firebaseio.com")]
    public string realtimeDatabaseBaseUrl = "https://arth2-169a4-default-rtdb.firebaseio.com";

    [Tooltip("Node path under the DB where the team will be stored. Example: 26SIG")]
    public string nodePath = "26SIG";

    [Tooltip("Optional auth token or database secret. Leave blank if rules allow public read/write for testing.")]
    public string authToken = "";

    [Header("UI")]
    public TMP_Text outputText;
    public Button fetchButton;
    public TMP_InputField uidInput;
    [Tooltip("Shown near the UID input to display friendly errors like 'UID not found'")]
    public TMP_Text uidStatusText;
    public GameObject verificationPanel;
    public TMP_Text verificationText;
    public Button iVerifiedButton;
    public TreasureHuntManager treasureHuntManager;
    public ProgressManager progressManager;

    [Header("Scoring (Main Score)")]
    [Tooltip("Enable to update the team's main score in RTDB when a treasure is collected (based on progress)")]
    public bool enableScoreSync = true;
    [Tooltip("Points added to main score per collected treasure")]
    public int scorePerTreasure = 100;
    [Tooltip("When true, score is auto-incremented from progress (cluesCompleted). When false, use AddScore(points) explicitly.")]
    public bool autoScoreFromProgress = false;

    [Header("Session Sync (RTDB)")]
    [Tooltip("When enabled, will write session status (started, current clue, clues completed) under UID/session")]
    public bool enableSessionSync = true;
    [Tooltip("Polling interval in seconds for progress sync")]
    public float sessionSyncIntervalSeconds = 2f;

    [Header("Game Control (Volunteer Node)")]
    [Tooltip("Enable volunteer-controlled game start/stop system")]
    public bool enableGameControl = true;
    [Tooltip("How often to check volunteer node for game control (seconds)")]
    public float volunteerCheckInterval = 6f;
    [Tooltip("Number of consecutive failures before showing Mayday panel")]
    public int maxConsecutiveFailures = 3;
    [Tooltip("Grace period in seconds before showing Mayday panel after failures")]
    public float maydayGracePeriod = 15f;
    [Tooltip("Maximum retry attempts for volunteer status checks")]
    public int maxRetryAttempts = 3;
    [Tooltip("Base delay in seconds for exponential backoff (doubles each retry)")]
    public float baseRetryDelay = 1f;
    [Tooltip("Full-screen Mayday panel that blocks all gameplay")]
    public GameObject maydayPanel;
    [Tooltip("Text component in Mayday panel to show different messages")]
    public TMP_Text maydayMessageText;
    
    [Header("AR Session Integration")]
    [Tooltip("Wait for AR Session to be tracking before making any network calls")]
    public bool waitForARSession = true;
    [Tooltip("AR Session component - will be found automatically if not assigned")]
    public ARSession arSession;

    private void Start()
    {
        if (fetchButton != null) fetchButton.onClick.AddListener(OnFetchButtonClicked);
        if (iVerifiedButton != null) iVerifiedButton.onClick.AddListener(OnIVerifiedClicked);
        if (verificationPanel != null) verificationPanel.SetActive(false);
        if (treasureHuntManager == null)
        {
            treasureHuntManager = FindObjectOfType<TreasureHuntManager>();
        }
        
        if (progressManager == null)
        {
            progressManager = FindObjectOfType<ProgressManager>();
        }
        
        // Find AR Session if not assigned
        if (waitForARSession && arSession == null)
        {
            arSession = FindObjectOfType<ARSession>();
        }

        // Also hook into Start Hunt button to mark session started when pressed
        if (enableSessionSync && treasureHuntManager != null && treasureHuntManager.startHuntButton != null)
        {
            treasureHuntManager.startHuntButton.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(_activeUid))
                {
                    StartCoroutine(WaitForARSessionThenExecute(() => PatchSessionField(_activeUid, "started", true)));
                }
            });
        }
    }

    public void OnFetchButtonClicked()
    {
        string uid = uidInput != null ? (uidInput.text ?? string.Empty).Trim() : string.Empty;
        if (string.IsNullOrEmpty(uid))
        {
            if (uidStatusText != null) uidStatusText.text = "Please enter a UID";
            else if (outputText != null) outputText.text = "Please enter a UID";
            return;
        }
        if (uidStatusText != null) uidStatusText.text = "";
        
        // Check game control before proceeding with team data fetch
        if (enableGameControl)
        {
            StartCoroutine(WaitForARSessionThenExecute(() => CheckGameStatusAndFetchTeam(uid)));
            return;
        }
        
        // Original flow if game control is disabled
        if (outputText != null) outputText.text = $"Fetching UID: {uid}...";
        StartCoroutine(WaitForARSessionThenExecute(() => GetTeamData(uid)));
    }

    private IEnumerator CheckGameStatusAndFetchTeam(string uid)
    {
        if (uidStatusText != null) uidStatusText.text = "Checking game status...";
        
        yield return StartCoroutine(CheckVolunteerGameStatus());
        
        // Only proceed if we successfully checked status (no network issues)
        if (_consecutiveFailures == 0)
        {
            if (_gameAllowed)
            {
                // Game is allowed - proceed with team data fetch
                if (outputText != null) outputText.text = $"Fetching UID: {uid}...";
                yield return StartCoroutine(GetTeamData(uid));
            }
            else
            {
                // Game legitimately not started by volunteer - show message
                if (uidStatusText != null) uidStatusText.text = "The game has not started yet. Please wait for further instructions.";
            }
        }
        else
        {
            // Network issues during initial check - show appropriate message
            if (uidStatusText != null) uidStatusText.text = "Network connectivity issues. Please check your internet connection and try again.";
        }
    }


    private string BuildUrl()
    {
        return BuildUrl(null);
    }

    private string BuildUrl(string pathOverride)
    {
        string baseUrl = (realtimeDatabaseBaseUrl ?? string.Empty).TrimEnd('/');
        string effectivePath = string.IsNullOrWhiteSpace(pathOverride) ? nodePath : pathOverride;
        string path = (effectivePath ?? string.Empty).TrimStart('/');
        string url = baseUrl + "/" + path + ".json";
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            url += (url.Contains("?") ? "&" : "?") + "auth=" + authToken;
        }
        return url;
    }


    private IEnumerator GetTeamData()
    {
        string url = BuildUrl();

        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 15;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"GET failed: {request.error}");
                if (outputText != null) outputText.text = $"GET failed: {request.error}";
                yield break;
            }

            string json = request.downloadHandler.text;
            TeamDataRTDB result = null;
            try
            {
                result = JsonUtility.FromJson<TeamDataRTDB>(json);
            }
            catch { }

            if (result != null)
            {
                string pretty = $"Name: {result.teamName}\n#:{result.teamNumber}\nUID:{result.uid}\nPlayers:{result.player1} & {result.player2}\nEmail:{result.email}\nScore:{result.score}";
                if (outputText != null) outputText.text = pretty;
                else Debug.Log(pretty);
            }
            else
            {
                if (outputText != null) outputText.text = $"GET raw: {json}";
                else Debug.Log($"GET raw: {json}");
            }
        }
    }

    private IEnumerator GetTeamData(string pathOverride)
    {
        string url = BuildUrl(pathOverride);

        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 15;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"GET failed: {request.error}");
                if (uidStatusText != null) uidStatusText.text = "Network error. Please try again.";
                else if (outputText != null) outputText.text = $"GET failed: {request.error}";
                yield break;
            }

            string json = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                // UID not present
                if (verificationPanel != null) verificationPanel.SetActive(false);
                if (uidStatusText != null) uidStatusText.text = "UID not found. Please check and try again.";
                else if (outputText != null) outputText.text = "UID not found. Please check and try again.";
                yield break;
            }
            TeamDataRTDB result = null;
            try { result = JsonUtility.FromJson<TeamDataRTDB>(json); } catch { }

            if (result != null)
            {
                // Activate and populate verification panel
                if (verificationText != null)
                {
                    verificationText.text =
                        $"Team Details:\n\n" +
                        $"Team Name: {result.teamName}\n" +
                        $"Team Number: {result.teamNumber}\n" +
                        $"Players: {result.player1} & {result.player2}\n" +
                        $"Email: {result.email}\n" +
                        $"UID: {result.uid}\n\n" +
                        $"Please verify this information is correct.";
                }
                if (verificationPanel != null) verificationPanel.SetActive(true);
                // Hide registration panel to avoid overlap
                if (treasureHuntManager != null && treasureHuntManager.registrationPanel != null)
                {
                    treasureHuntManager.registrationPanel.SetActive(false);
                }

                if (outputText != null)
                {
                    outputText.text = $"Fetched: {result.teamName} (#{result.teamNumber})";
                }

                _lastFetched = result;

                // Remember active UID for session sync
                _activeUid = uidInput != null ? (uidInput.text ?? string.Empty).Trim() : string.Empty;
                
                // Check existing session data for progress restoration
                if (!string.IsNullOrEmpty(_activeUid))
                {
                    StartCoroutine(CheckSessionProgress(_activeUid));
                }
                else
                {
                    // Continue with normal verification flow
                    ContinueWithNormalFlow();
                }
                
                if (enableSessionSync && !string.IsNullOrEmpty(_activeUid))
                {
                    // Don't reset existing session data - just start polling loop
                    RestartSessionSyncLoop();
                }
            }
            else
            {
                if (outputText != null) outputText.text = $"GET raw: {json}";
                else Debug.Log($"GET raw: {json}");
            }
        }
    }

    private TeamDataRTDB _lastFetched;
    private string _activeUid;
    private Coroutine _sessionSyncRoutine;
    private bool _lastSentStarted;
    private int _lastSentCurrentClue;
    private int _lastSentCompleted;

    // Game control variables
    private Coroutine _volunteerMonitorRoutine;
    private bool _gameAllowed = false;
    private bool _lastGameState = false;
    private int _consecutiveFailures = 0;
    private bool _isInGracePeriod = false;
    private Coroutine _gracePeriodRoutine;
    
    // AR Session tracking
    private bool _arSessionReady = false;

    private void OnIVerifiedClicked()
    {
        if (_lastFetched == null)
        {
            if (outputText != null) outputText.text = "No data to verify";
            return;
        }

        var team = new TeamData
        {
            teamNumber = _lastFetched.teamNumber,
            uid = _lastFetched.uid,
            teamName = _lastFetched.teamName,
            player1 = _lastFetched.player1,
            player2 = _lastFetched.player2,
            email = _lastFetched.email,
            score = _lastFetched.score
        };

        if (treasureHuntManager != null)
        {
            treasureHuntManager.ReceiveExternalTeamData(team);
            // Immediately proceed as if the user confirmed in the core flow
            treasureHuntManager.OnVerifyTeam();
            
            // Start continuous monitoring once game begins
            if (enableGameControl)
                StartVolunteerMonitoring();
        }
        else
        {
            Debug.LogWarning("TreasureHuntManager not assigned/found. Cannot proceed to hunt.");
            if (outputText != null) outputText.text = "Manager missing. Assign TreasureHuntManager.";
        }

        if (verificationPanel != null)
        {
            verificationPanel.SetActive(false);
        }
    }
    
    // Check session data for existing progress
    private IEnumerator CheckSessionProgress(string uid)
    {
        // Check progress using ProgressManager instead of session data
        var progressManager = FindObjectOfType<ProgressManager>();
        if (progressManager != null)
        {
            progressManager.SetActiveUID(uid);
            
            bool hasProgressData = false;
            ProgressData progressData = null;
            
            // Check if progress exists
            progressManager.CheckProgressForUID(uid, (hasProgress, data) => {
                hasProgressData = hasProgress;
                progressData = data;
            });
            
            // Wait a moment for the callback
            yield return new WaitForSeconds(1f);
            
            if (hasProgressData && progressData != null)
            {
                Debug.Log($"Progress check: {progressData.totalTreasuresCollected} treasures collected, physicalGameActive={progressData.physicalGameActive}, physicalGameClueIndex={progressData.physicalGameClueIndex}");
                
                if (progressData.physicalGameActive)
                {
                    Debug.Log($"Physical game active found: {progressData.totalTreasuresCollected} treasures collected, physical game active for clue: {progressData.physicalGameClueIndex}");
                    
                    // Resume to physical game panel
                    ResumeToPhysicalGame(progressData);
                    yield break;
                }
                else if (progressData.totalTreasuresCollected > 0)
                {
                    Debug.Log($"Existing progress found: {progressData.totalTreasuresCollected} treasures collected, next clue: {progressData.nextClueIndex}");
                    
                    // Resume from progress data
                    ResumeFromProgress(progressData);
                    yield break;
                }
            }
            else
            {
                Debug.Log("No meaningful progress found - continuing with normal flow");
            }
        }
        else
        {
            Debug.LogError("ProgressManager not found - cannot check for existing progress");
        }
        
        // No meaningful progress data - continue with normal flow
        Debug.Log("No existing progress found - starting new game");
        ContinueWithNormalFlow();
    }
    
    private void ResumeFromSession(SessionData sessionData)
    {
        if (treasureHuntManager != null && _lastFetched != null)
        {
            // Disable the "I Verified" button to prevent accidental clicks during resume
            if (iVerifiedButton != null)
            {
                iVerifiedButton.interactable = false;
            }
            
            var team = new TeamData
            {
                teamNumber = _lastFetched.teamNumber,
                uid = _lastFetched.uid,
                teamName = _lastFetched.teamName,
                player1 = _lastFetched.player1,
                player2 = _lastFetched.player2,
                email = _lastFetched.email,
                score = _lastFetched.score
            };
            
            // Hide verification panel before resuming game
            if (verificationPanel != null)
            {
                verificationPanel.SetActive(false);
            }
            
            // Resume game with session data
            treasureHuntManager.ResumeFromSession(team, sessionData);
            
            // Start continuous monitoring once game begins
            if (enableGameControl)
                StartVolunteerMonitoring();
        }
        else
        {
            Debug.LogError("Cannot resume game: TreasureHuntManager or team data missing");
            ContinueWithNormalFlow();
        }
    }
    
    // Resume to physical game panel when physicalGameActive is true
    private void ResumeToPhysicalGame(ProgressData progressData)
    {
        if (treasureHuntManager != null && _lastFetched != null)
        {
            // Disable the "I Verified" button to prevent accidental clicks during resume
            if (iVerifiedButton != null)
            {
                iVerifiedButton.interactable = false;
            }
            
            var team = new TeamData
            {
                teamNumber = _lastFetched.teamNumber,
                uid = _lastFetched.uid,
                teamName = _lastFetched.teamName,
                player1 = _lastFetched.player1,
                player2 = _lastFetched.player2,
                email = _lastFetched.email,
                score = _lastFetched.score
            };
            
            // Hide verification panel before resuming game
            if (verificationPanel != null)
            {
                verificationPanel.SetActive(false);
            }
            
            // Resume directly to physical game panel
            treasureHuntManager.ResumeToPhysicalGame(team, progressData);
            
            // Start continuous monitoring once game begins
            if (enableGameControl)
                StartVolunteerMonitoring();
        }
        else
        {
            Debug.LogError("Cannot resume to physical game: TreasureHuntManager or team data missing");
            ContinueWithNormalFlow();
        }
    }
    
    // Resume from regular progress (not physical game)
    private void ResumeFromProgress(ProgressData progressData)
    {
        if (treasureHuntManager != null && _lastFetched != null)
        {
            // Disable the "I Verified" button to prevent accidental clicks during resume
            if (iVerifiedButton != null)
            {
                iVerifiedButton.interactable = false;
            }
            
            var team = new TeamData
            {
                teamNumber = _lastFetched.teamNumber,
                uid = _lastFetched.uid,
                teamName = _lastFetched.teamName,
                player1 = _lastFetched.player1,
                player2 = _lastFetched.player2,
                email = _lastFetched.email,
                score = _lastFetched.score
            };
            
            // Hide verification panel before resuming game
            if (verificationPanel != null)
            {
                verificationPanel.SetActive(false);
            }
            
            // Resume from progress data
            treasureHuntManager.ResumeFromProgress(team, progressData);
            
            // Start continuous monitoring once game begins
            if (enableGameControl)
                StartVolunteerMonitoring();
        }
        else
        {
            Debug.LogError("Cannot resume from progress: TreasureHuntManager or team data missing");
            ContinueWithNormalFlow();
        }
    }
    
    private IEnumerator InitializeSessionTracking()
    {
        if (string.IsNullOrEmpty(_activeUid)) yield break;
        
        string url = BuildUrl(_activeUid + "/session");
        
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                
                if (!string.IsNullOrWhiteSpace(json) && json != "null")
                {
                    try
                    {
                        var sessionData = JsonUtility.FromJson<SessionData>(json);
                        if (sessionData != null)
                        {
                            // Initialize tracking variables from existing session data
                            _lastSentStarted = sessionData.started;
                            _lastSentCurrentClue = sessionData.currentClueNumber;
                            _lastSentCompleted = sessionData.cluesCompleted;
                            
                            Debug.Log($"Initialized session tracking from existing data: started={_lastSentStarted}, clue={_lastSentCurrentClue}, completed={_lastSentCompleted}");
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Failed to parse existing session data for tracking: {e.Message}");
                    }
                }
            }
        }
    }
    
    private void ContinueWithNormalFlow()
    {
        // Show normal team verification panel for new game
        if (verificationPanel != null && _lastFetched != null)
        {
            verificationPanel.SetActive(true);
            
            if (verificationText != null)
            {
                verificationText.text =
                    $"Team Details:\n\n" +
                    $"Team Name: {_lastFetched.teamName}\n" +
                    $"Team Number: {_lastFetched.teamNumber}\n" +
                    $"Players: {_lastFetched.player1} & {_lastFetched.player2}\n" +
                    $"Email: {_lastFetched.email}\n" +
                    $"UID: {_lastFetched.uid}\n\n" +
                    $"Please verify this information is correct.";
            }
            
            // Hide registration panel
            if (treasureHuntManager != null && treasureHuntManager.registrationPanel != null)
            {
                treasureHuntManager.registrationPanel.SetActive(false);
            }
        }
    }

    private void RestartSessionSyncLoop()
    {
        if (!enableSessionSync) return;
        if (_sessionSyncRoutine != null)
        {
            StopCoroutine(_sessionSyncRoutine);
        }
        _sessionSyncRoutine = StartCoroutine(SessionSyncLoop());
    }

    private IEnumerator SessionSyncLoop()
    {
        // Initialize tracking variables from current session state (if exists)
        // This prevents overwriting existing session data
        _lastSentStarted = false;
        _lastSentCurrentClue = -1;
        _lastSentCompleted = -1;
        
        // First, get current session data to avoid overwriting it
        yield return StartCoroutine(InitializeSessionTracking());
        
        var wait = new WaitForSeconds(sessionSyncIntervalSeconds);
        while (enableSessionSync && !string.IsNullOrEmpty(_activeUid))
        {
            bool started = false;
            int currentClue = 0;
            int completed = 0;

            if (treasureHuntManager != null)
            {
                started = treasureHuntManager.IsGPSTrackingActive();
                currentClue = Mathf.Max(0, treasureHuntManager.GetClueIndex()); // 1-based per manager
                
                // Get completed count directly from TreasureHuntManager
                int totalClues = treasureHuntManager.GetTotalClues();
                int remainingClues = treasureHuntManager.GetRemainingClues();
                completed = Mathf.Clamp(totalClues - remainingClues, 0, totalClues);
            }

            // Batch PATCH of changed fields
            var hasChange = (started != _lastSentStarted) || (currentClue != _lastSentCurrentClue) || (completed != _lastSentCompleted);
            if (hasChange)
            {
                string json = BuildSessionJson(startedChanged: started != _lastSentStarted, started: started,
                                               currentChanged: currentClue != _lastSentCurrentClue, currentClue: currentClue,
                                               completedChanged: completed != _lastSentCompleted, completed: completed);
                if (!string.IsNullOrEmpty(json))
                {
                    yield return PatchSessionJson(_activeUid, json);
                    _lastSentStarted = started;
                    _lastSentCurrentClue = currentClue;
                    // If completed increased, update score
                    if (enableScoreSync && autoScoreFromProgress && completed > _lastSentCompleted)
                    {
                        int delta = completed - _lastSentCompleted;
                        int points = Mathf.Max(0, delta * Mathf.Max(0, scorePerTreasure));
                        if (points > 0)
                        {
                            yield return UpdateScoreBy(_activeUid, points);
                        }
                    }
                    _lastSentCompleted = completed;
                }
            }

            yield return wait;
        }
    }

    private string BuildSessionJson(bool startedChanged, bool started, bool currentChanged, int currentClue, bool completedChanged, int completed)
    {
        // Build minimal JSON with only changed fields
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("{");
        bool wrote = false;
        if (startedChanged)
        {
            sb.AppendFormat("\"started\":{0}", started ? "true" : "false");
            wrote = true;
        }
        if (currentChanged)
        {
    
            if (wrote) sb.Append(",");
            sb.AppendFormat("\"currentClueNumber\":{0}", currentClue);
            wrote = true;
        }
        if (completedChanged)
        {
            if (wrote) sb.Append(",");
            sb.AppendFormat("\"cluesCompleted\":{0}", completed);
            wrote = true;
        }
        sb.Append("}");
        return wrote ? sb.ToString() : string.Empty;
    }
    
    // Enhanced session update with phase and physical game state
    public void UpdateSessionPhase(string phase, bool physicalGameRequired = false, int[] completedClues = null, bool physicalGamePending = false)
    {
        if (string.IsNullOrEmpty(_activeUid)) return;
        
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("{");
        bool wrote = false;
        
        // Always update phase
        sb.AppendFormat("\"currentPhase\":\"{0}\"", phase);
        wrote = true;
        
        // Update physical game requirement if specified
        if (wrote) sb.Append(",");
        sb.AppendFormat("\"physicalGameRequired\":{0}", physicalGameRequired ? "true" : "false");
        
        // Update physical game pending status
        sb.Append(",");
        sb.AppendFormat("\"physicalGamePending\":{0}", physicalGamePending ? "true" : "false");
        
        // Update completed clues array if provided
        if (completedClues != null && completedClues.Length > 0)
        {
            sb.Append(",\"completedClueIndices\":[");
            for (int i = 0; i < completedClues.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(completedClues[i]);
            }
            sb.Append("]");
        }
        
        sb.Append("}");
        
        StartCoroutine(WaitForARSessionThenExecute(() => PatchSessionJson(_activeUid, sb.ToString())));
    }

    private IEnumerator PatchSessionField(string uid, string key, bool value)
    {
        string json = $"{{\"{key}\":{(value ? "true" : "false")}}}";
        yield return PatchSessionJson(uid, json);
    }

    private IEnumerator PatchSessionJson(string uid, string json)
    {
        if (string.IsNullOrEmpty(uid)) yield break;
        string url = BuildUrl(uid + "/session");
        using (var request = new UnityWebRequest(url, "PATCH"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"PATCH session failed: {request.error}");
            }
        }
    }

    // ---------------- Score Update (Main score on team root) ----------------
    private IEnumerator UpdateScoreBy(string uid, int delta)
    {
        if (string.IsNullOrEmpty(uid) || delta == 0) yield break;
        // Read current score
        string getUrl = BuildUrl(uid + "/score");
        int current = 0;
        using (var request = UnityWebRequest.Get(getUrl))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                string raw = request.downloadHandler.text;
                // raw is typically a number or null
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
                {
                    int.TryParse(raw, out current);
                }
            }
        }
        int updated = current + delta;
        // Patch new score at team root
        string patchJson = $"\"score\":{updated}";
        yield return PatchTeamJson(uid, patchJson);
    }

    private IEnumerator PatchTeamJson(string uid, string json)
    {
        string url = BuildUrl(uid);
        using (var request = new UnityWebRequest(url, "PATCH"))
        {
            byte[] body = Encoding.UTF8.GetBytes("{" + json + "}");
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"PATCH team failed: {request.error}");
            }
        }
    }

    // ---------------- Physical Game Result (explicit call) ----------------
    public void SetPhysicalGameResult(bool played, int score)
    {
        if (string.IsNullOrEmpty(_activeUid)) return;
        string json = $"{{\"physicalGamePlayed\":{(played ? "true" : "false")},\"physicalGameScore\":{Mathf.Max(0, score)}}}";
        StartCoroutine(WaitForARSessionThenExecute(() => PatchSessionJson(_activeUid, json)));
    }

    // Public API to explicitly add points to main score
    public void AddScore(int points)
    {
        Debug.Log($"AddScore called with {points} points. enableScoreSync={enableScoreSync}, activeUid={_activeUid}");
        
        if (!enableScoreSync) 
        {
            Debug.LogWarning("AddScore failed: enableScoreSync is false");
            return;
        }
        if (points <= 0) 
        {
            Debug.LogWarning($"AddScore failed: points <= 0 ({points})");
            return;
        }
        if (string.IsNullOrEmpty(_activeUid)) 
        {
            Debug.LogWarning("AddScore failed: activeUid is empty");
            return;
        }
        
        Debug.Log($"AddScore: Starting score update coroutine for {points} points");
        StartCoroutine(WaitForARSessionThenExecute(() => UpdateScoreBy(_activeUid, points)));
    }

    private void OnDestroy()
    {
        if (_sessionSyncRoutine != null)
        {
            StopCoroutine(_sessionSyncRoutine);
        }
        if (_volunteerMonitorRoutine != null)
        {
            StopCoroutine(_volunteerMonitorRoutine);
        }
        if (_gracePeriodRoutine != null)
        {
            StopCoroutine(_gracePeriodRoutine);
        }
    }

    // ---------------- Volunteer Game Control System ----------------
    
    private IEnumerator CheckVolunteerGameStatus()
    {
        bool success = false;
        
        for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
        {
            string url = BuildUrl("volunteer/start");
            
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    string response = request.downloadHandler.text;
                    // Response will be "true", "false", or "null"
                    bool currentGameState = response.Trim().ToLower() == "true";
                    
                    // Success - reset failure counter and update state
                    _consecutiveFailures = 0;
                    _gameAllowed = currentGameState;
                    success = true;
                    
                    Debug.Log($"Volunteer game status (attempt {attempt}): {(_gameAllowed ? "ALLOWED" : "NOT ALLOWED")}");
                    break;
                }
                else
                {
                    LogError($"Failed to check volunteer status (attempt {attempt}/{maxRetryAttempts}): {request.error}");
                    
                    // If this isn't the last attempt, wait with exponential backoff
                    if (attempt < maxRetryAttempts)
                    {
                        float delay = baseRetryDelay * Mathf.Pow(2, attempt - 1);
                        Debug.Log($"Retrying volunteer status check in {delay} seconds...");
                        yield return new WaitForSeconds(delay);
                    }
                }
            }
        }
        
        if (!success)
        {
            // All retry attempts failed - increment consecutive failures
            _consecutiveFailures++;
            Debug.LogWarning($"All volunteer status check attempts failed. Consecutive failures: {_consecutiveFailures}/{maxConsecutiveFailures}");
            
            // Only change game state if we've exceeded the failure threshold
            if (_consecutiveFailures >= maxConsecutiveFailures)
            {
                // Don't immediately set _gameAllowed = false, let the grace period handle it
                Debug.LogWarning("Maximum consecutive failures reached. Network issues detected.");
            }
        }
    }
    
    private void StartVolunteerMonitoring()
    {
        if (!enableGameControl) return;
        
        if (_volunteerMonitorRoutine != null)
        {
            StopCoroutine(_volunteerMonitorRoutine);
        }
        
        _volunteerMonitorRoutine = StartCoroutine(VolunteerMonitorLoop());
        _lastGameState = _gameAllowed;
    }
    
    private IEnumerator VolunteerMonitorLoop()
    {
        var wait = new WaitForSeconds(volunteerCheckInterval);
        
        while (enableGameControl)
        {
            yield return StartCoroutine(CheckVolunteerGameStatus());
            
            // Check if game state changed (only if we have recent successful checks)
            if (_consecutiveFailures == 0 && _gameAllowed != _lastGameState)
            {
                if (!_gameAllowed)
                {
                    // Game was legitimately stopped by volunteer - show Mayday panel immediately
                    ShowMaydayPanel("The game has been paused by the volunteer coordinator.");
                    Debug.Log("GAME STOPPED BY VOLUNTEER - Mayday panel activated");
                    
                    // Cancel any ongoing grace period
                    if (_gracePeriodRoutine != null)
                    {
                        StopCoroutine(_gracePeriodRoutine);
                        _gracePeriodRoutine = null;
                        _isInGracePeriod = false;
                    }
                }
                else
                {
                    // Game was re-enabled - hide Mayday panel
                    HideMaydayPanel();
                    Debug.Log("GAME RE-ENABLED BY VOLUNTEER - Mayday panel hidden");
                    
                    // Cancel any ongoing grace period
                    if (_gracePeriodRoutine != null)
                    {
                        StopCoroutine(_gracePeriodRoutine);
                        _gracePeriodRoutine = null;
                        _isInGracePeriod = false;
                    }
                }
                
                _lastGameState = _gameAllowed;
            }
            // Handle network failures with grace period
            else if (_consecutiveFailures >= maxConsecutiveFailures && !_isInGracePeriod)
            {
                // Start grace period before showing Mayday panel for network issues
                _isInGracePeriod = true;
                _gracePeriodRoutine = StartCoroutine(HandleNetworkGracePeriod());
            }
            // If we recover during grace period, cancel it
            else if (_consecutiveFailures == 0 && _isInGracePeriod)
            {
                Debug.Log("Network recovered during grace period - canceling Mayday panel");
                if (_gracePeriodRoutine != null)
                {
                    StopCoroutine(_gracePeriodRoutine);
                    _gracePeriodRoutine = null;
                }
                _isInGracePeriod = false;
            }
            
            yield return wait;
        }
    }
    
    private IEnumerator HandleNetworkGracePeriod()
    {
        Debug.Log($"Starting {maydayGracePeriod}s grace period for network issues...");
        yield return new WaitForSeconds(maydayGracePeriod);
        
        // After grace period, check if we still have network issues
        if (_consecutiveFailures >= maxConsecutiveFailures && _isInGracePeriod)
        {
            ShowMaydayPanel("Network connectivity issues detected. Please check your internet connection and wait for reconnection.");
            Debug.Log("NETWORK ISSUES DETECTED - Mayday panel activated after grace period");
        }
        
        _isInGracePeriod = false;
        _gracePeriodRoutine = null;
    }
    
    private void ShowMaydayPanel(string message = "The game has been paused. Please wait for further instructions.")
    {
        if (maydayPanel != null)
        {
            maydayPanel.SetActive(true);
            // Mayday panel should be highest priority UI element
            maydayPanel.transform.SetAsLastSibling();
            
            // Update message if text component is assigned
            if (maydayMessageText != null)
            {
                maydayMessageText.text = message;
            }
        }
        
        // Pause all game activities by disabling treasure hunt manager
        if (treasureHuntManager != null)
        {
            treasureHuntManager.enabled = false;
        }
    }
    
    private void HideMaydayPanel()
    {
        if (maydayPanel != null)
        {
            maydayPanel.SetActive(false);
        }
        
        // Re-enable game activities
        if (treasureHuntManager != null)
        {
            treasureHuntManager.enabled = true;
        }
    }

    // AR Session checking methods
    private bool IsARSessionReady()
    {
        if (!waitForARSession) return true;
        if (arSession == null) return false;
        return ARSession.state == ARSessionState.SessionTracking;
    }
    
    private IEnumerator WaitForARSessionThenExecute(System.Func<IEnumerator> networkAction)
    {
        yield return StartCoroutine(WaitForARSession());
        yield return StartCoroutine(networkAction());
    }
    
    private IEnumerator WaitForARSessionThenExecute(System.Action networkAction)
    {
        yield return StartCoroutine(WaitForARSession());
        networkAction();
    }
    
    private IEnumerator WaitForARSession()
    {
        if (!waitForARSession)
        {
            yield break;
        }
        
        // Show status message while waiting
        if (uidStatusText != null && !IsARSessionReady())
        {
            uidStatusText.text = "Initializing AR system...";
        }
        
        // Wait for AR Session to be ready
        while (!IsARSessionReady())
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        // Clear status message
        if (uidStatusText != null)
        {
            uidStatusText.text = "";
        }
        
        Debug.Log("[FirebaseRTDBFetcher] AR Session is ready - proceeding with network operations");
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[FirebaseRTDBFetcher] {message}");
    }
}