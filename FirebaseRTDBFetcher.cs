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
    public float volunteerCheckInterval = 3f;
    [Tooltip("Full-screen Mayday panel that blocks all gameplay")]
    public GameObject maydayPanel;
    
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
        
        if (_gameAllowed)
        {
            // Game is allowed - proceed with team data fetch
            if (outputText != null) outputText.text = $"Fetching UID: {uid}...";
            yield return StartCoroutine(GetTeamData(uid));
        }
        else
        {
            // Game not started - show message and don't fetch team data
            if (uidStatusText != null) uidStatusText.text = "The game has not started yet. Please wait for further instructions.";
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
                if (enableSessionSync && !string.IsNullOrEmpty(_activeUid))
                {
                    // Initialize session as not started (already AR session ready if we reach here)
                    StartCoroutine(PatchSessionField(_activeUid, "started", false));
                    // Start polling loop
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
        _lastSentStarted = false;
        _lastSentCurrentClue = -1;
        _lastSentCompleted = -1;
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
                int total = treasureHuntManager.GetTotalClues();
                int remaining = treasureHuntManager.GetRemainingClues();
                completed = Mathf.Clamp(total - remaining, 0, total);
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
        if (!enableScoreSync) return;
        if (points <= 0) return;
        if (string.IsNullOrEmpty(_activeUid)) return;
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
    }

    // ---------------- Volunteer Game Control System ----------------
    
    private IEnumerator CheckVolunteerGameStatus()
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
                _gameAllowed = response.Trim().ToLower() == "true";
                
                Debug.Log($"Volunteer game status: {(_gameAllowed ? "ALLOWED" : "NOT ALLOWED")}");
            }
            else
            {
                // If can't reach volunteer node, default to not allowed for safety
                _gameAllowed = false;
                LogError($"Failed to check volunteer status: {request.error}");
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
            
            // Check if game state changed
            if (_gameAllowed != _lastGameState)
            {
                if (!_gameAllowed)
                {
                    // Game was stopped - show Mayday panel
                    ShowMaydayPanel();
                    Debug.Log("GAME STOPPED BY VOLUNTEER - Mayday panel activated");
                }
                else
                {
                    // Game was re-enabled - hide Mayday panel
                    HideMaydayPanel();
                    Debug.Log("GAME RE-ENABLED BY VOLUNTEER - Mayday panel hidden");
                }
                
                _lastGameState = _gameAllowed;
            }
            
            yield return wait;
        }
    }
    
    private void ShowMaydayPanel()
    {
        if (maydayPanel != null)
        {
            maydayPanel.SetActive(true);
            // Mayday panel should be highest priority UI element
            maydayPanel.transform.SetAsLastSibling();
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