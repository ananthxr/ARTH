using System;
using System.Collections;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.Extensions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine.Networking;

// Simple TeamData structure
[System.Serializable]
public class TeamData
{
    public int teamNumber;
    public string uid;
    public string teamName;
    public string player1;
    public string player2;
    public string email;
    public int score;
}

public class FirebaseFetcher : MonoBehaviour
{
    public enum DataBackend
    {
        Firestore,
        RealtimeDatabase
    }
    public enum ConnectionMethod
    {
        DirectFirebaseOnly,
        RestFallbackIfNeeded,
        RestOnly
    }

    [Header("Debug")]
    public bool enableDebugLogs = true;
    
    [Header("Verification Panel UI")]
    public GameObject verificationPanel;
    public TextMeshProUGUI teamDetailsText;
    public UnityEngine.UI.Button verifyTeamButton;
    
    [Header("Connection Settings")]
    public ConnectionMethod connectionMethod = ConnectionMethod.RestFallbackIfNeeded;
    [Tooltip("Required for REST fallback: Your Firebase Project ID (e.g., arth-33ed6)")]
    public string firebaseProjectId = "";
    [Tooltip("When enabled, logs the raw REST response JSON for debugging")]
    public bool logRestResponses = false;

    [Header("Realtime Database Settings")]
    public DataBackend dataBackend = DataBackend.RealtimeDatabase;
    [Tooltip("Base URL to your RTDB, e.g., https://your-project-id-default-rtdb.region.firebasedatabase.app")]
    public string realtimeDatabaseBaseUrl = "";
    [Tooltip("Optional auth token or database secret to append as ?auth=TOKEN. Leave empty if rules allow public read for testing.")]
    public string realtimeDatabaseAuthToken = "";
    
    // Firebase instances
    private FirebaseFirestore db;
    private bool isFirebaseReady = false;
    
    // Cached team data
    private TeamData cachedTeamData;
    private bool isTeamDataLoaded = false;
    
    // Events
    public static event System.Action<TeamData> OnTeamDataFetched;
    public static event System.Action<string> OnTeamDataFetchFailed;
    public static event System.Action OnTeamVerified;
    
    // Singleton
    private static FirebaseFetcher instance;
    public static FirebaseFetcher Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<FirebaseFetcher>();
                if (instance == null)
                {
                    GameObject go = new GameObject("FirebaseFetcher");
                    instance = go.AddComponent<FirebaseFetcher>();
                }
            }
            return instance;
        }
    }
    
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        // Setup UI
        if (verifyTeamButton != null)
        {
            verifyTeamButton.onClick.AddListener(OnVerifyTeamClicked);
        }
        
        if (verificationPanel != null)
        {
            verificationPanel.SetActive(false);
        }
        
        // Initialize Firebase unless REST-only mode or using Realtime Database
        if (dataBackend == DataBackend.Firestore && connectionMethod != ConnectionMethod.RestOnly)
        {
            StartCoroutine(InitializeFirebaseSimple());
        }
    }
    
    private IEnumerator InitializeFirebaseSimple()
    {
        Log("Initializing Firebase...");
        
        // Only initialize on mobile devices to prevent desktop config conflicts
        #if UNITY_ANDROID || UNITY_IOS
        
        var dependencyTask = FirebaseApp.CheckAndFixDependenciesAsync();
        
        // Add timeout for dependency check
        float timeout = 15f;
        float elapsed = 0f;
        
        while (!dependencyTask.IsCompleted && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (elapsed >= timeout)
        {
            LogError("❌ Firebase initialization timeout");
            isFirebaseReady = false;
            yield break;
        }
        
        // Handle the result without try-catch in coroutine
        if (dependencyTask.IsFaulted)
        {
            LogError($"❌ Firebase initialization exception: {dependencyTask.Exception?.GetBaseException()?.Message}");
            isFirebaseReady = false;
            yield break;
        }
        
        if (dependencyTask.Result == DependencyStatus.Available)
        {
            // Initialize Firebase app first
            var app = FirebaseApp.DefaultInstance;
            if (app == null)
            {
                LogError("❌ Failed to get Firebase App");
                isFirebaseReady = false;
                yield break;
            }
            
            db = FirebaseFirestore.DefaultInstance;
            
            if (db != null)
            {
                Log("✅ Firebase ready!");
                isFirebaseReady = true;
            }
            else
            {
                LogError("❌ Failed to get Firestore");
                isFirebaseReady = false;
            }
        }
        else
        {
            LogError($"❌ Firebase dependencies failed: {dependencyTask.Result}");
            isFirebaseReady = false;
        }
        
        #else
        // In Unity Editor - skip Firebase to prevent desktop config generation
        Log("⚠️ Firebase disabled in editor to prevent crashes. Build to device to test.");
        isFirebaseReady = false;
        #endif
    }
    
    public void FetchTeamData(string uid)
    {
        if (string.IsNullOrEmpty(uid))
        {
            LogError("UID cannot be empty");
            OnTeamDataFetchFailed?.Invoke("UID cannot be empty");
            return;
        }
        
        // If using Realtime Database, go via RTDB path (REST)
        if (dataBackend == DataBackend.RealtimeDatabase)
        {
            if (string.IsNullOrWhiteSpace(realtimeDatabaseBaseUrl))
            {
                LogError("Realtime Database base URL is not set");
                OnTeamDataFetchFailed?.Invoke("Configuration error: RTDB URL missing");
                return;
            }
            StartCoroutine(FetchTeamDataRealtimeCoroutine(uid));
            return;
        }

        // REST-only mode (Firestore)
        if (connectionMethod == ConnectionMethod.RestOnly)
        {
            if (string.IsNullOrWhiteSpace(firebaseProjectId))
            {
                LogError("Project ID is required for REST-only mode");
                OnTeamDataFetchFailed?.Invoke("Configuration error: Project ID missing");
                return;
            }
            Log($"[REST-Only] Fetching UID: {uid}");
            StartCoroutine(FetchTeamDataRestCoroutine(uid));
            return;
        }

        // Prefer Firebase SDK if ready
        if (isFirebaseReady && db != null)
        {
            Log($"Fetching team data for UID: {uid} (Firebase SDK)");
            StartCoroutine(FetchTeamDataCoroutine(uid));
            return;
        }

        // If SDK is not ready, optionally fall back to REST
        if (connectionMethod == ConnectionMethod.RestFallbackIfNeeded)
        {
            if (string.IsNullOrWhiteSpace(firebaseProjectId))
            {
                LogError("Firebase not ready and Project ID not set for REST fallback");
                OnTeamDataFetchFailed?.Invoke("Firebase not ready");
                return;
            }

            Log($"Firebase not ready. Falling back to REST for UID: {uid}");
            StartCoroutine(FetchTeamDataRestCoroutine(uid));
        }
        else
        {
            LogError("Firebase not ready");
            OnTeamDataFetchFailed?.Invoke("Firebase not ready");
        }
    }
    
    private IEnumerator FetchTeamDataCoroutine(string uid)
    {
        var fetchTask = FetchTeamDataAsync(uid);
        
        // Add timeout protection
        float timeout = 15f;
        float elapsed = 0f;
        
        while (!fetchTask.IsCompleted && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Check for timeout
        if (elapsed >= timeout)
        {
            LogError("❌ Firebase fetch timeout");
            // Ensure UI updates happen on main thread
            UnityMainThreadDispatcher.Instance.Enqueue(() => {
                OnTeamDataFetchFailed?.Invoke("Request timeout - please try again");
            });
            yield break;
        }
        
        // Handle task completion on main thread
        if (fetchTask.IsFaulted)
        {
            string error = fetchTask.Exception != null ? fetchTask.Exception.GetBaseException().Message : "Firebase fetch failed";
            LogError($"❌ {error}");
            
            // Ensure UI updates happen on main thread
            UnityMainThreadDispatcher.Instance.Enqueue(() => {
                OnTeamDataFetchFailed?.Invoke(error);
            });

            // Optional REST fallback
            if (connectionMethod == ConnectionMethod.RestFallbackIfNeeded && !string.IsNullOrWhiteSpace(firebaseProjectId))
            {
                Log("Attempting REST fallback after Firebase fetch faulted...");
                StartCoroutine(FetchTeamDataRestCoroutine(uid));
            }
        }
        else if (fetchTask.Result != null)
        {
            TeamData teamData = fetchTask.Result;
            cachedTeamData = teamData;
            isTeamDataLoaded = true;
            
            Log($"✅ Team data fetched: {teamData.teamName}");
            
            // Ensure UI updates happen on main thread
            UnityMainThreadDispatcher.Instance.Enqueue(() => {
                try
                {
                    ShowVerificationPanel(teamData);
                    OnTeamDataFetched?.Invoke(teamData);
                }
                catch (System.Exception e)
                {
                    LogError($"UI update error: {e.Message}");
                }
            });
        }
        else
        {
            LogError($"❌ Team not found: {uid}");
            
            // Ensure UI updates happen on main thread
            UnityMainThreadDispatcher.Instance.Enqueue(() => {
                OnTeamDataFetchFailed?.Invoke($"Team not found: {uid}");
            });

            // Optional REST fallback if SDK returned not found
            if (connectionMethod == ConnectionMethod.RestFallbackIfNeeded && !string.IsNullOrWhiteSpace(firebaseProjectId))
            {
                Log("Attempting REST fallback after 'not found' response...");
                StartCoroutine(FetchTeamDataRestCoroutine(uid));
            }
        }
    }
    
    private async Task<TeamData> FetchTeamDataAsync(string uid)
    {
        try
        {
            DocumentReference teamDocRef = db.Collection("teams").Document(uid);
            DocumentSnapshot teamSnapshot = await teamDocRef.GetSnapshotAsync();
            
            if (teamSnapshot.Exists)
            {
                var data = teamSnapshot.ToDictionary();
                
                TeamData teamData = new TeamData
                {
                    uid = uid,
                    teamName = GetField(data, "teamName"),
                    teamNumber = GetIntField(data, "teamNumber"),
                    player1 = GetField(data, "player1"),
                    player2 = GetField(data, "player2"),
                    email = GetField(data, "email"),
                    score = GetIntField(data, "score")
                };
                
                return teamData;
            }
            
            return null;
        }
        catch (System.Exception e)
        {
            Log($"Firebase exception: {e.Message}");
            throw;
        }
    }

    // ---------------- REST FALLBACK ----------------
    private IEnumerator FetchTeamDataRestCoroutine(string uid)
    {
        if (string.IsNullOrWhiteSpace(firebaseProjectId))
        {
            LogError("REST fallback requires a valid Firebase Project ID");
            OnTeamDataFetchFailed?.Invoke("Configuration error: Project ID missing");
            yield break;
        }

        string url = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/teams/{uid}";
        Log($"[REST] GET {url}");

        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 15;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"[REST] Request failed: {request.error}");
                OnTeamDataFetchFailed?.Invoke($"Network error: {request.error}");
                yield break;
            }

            string json = request.downloadHandler.text;
            if (logRestResponses) Log($"[REST] Response: {json}");

            FirestoreDocument doc = null;
            try
            {
                doc = JsonUtility.FromJson<FirestoreDocument>(json);
            }
            catch (Exception ex)
            {
                LogError($"[REST] JSON parse error: {ex.Message}");
            }

            if (doc == null || doc.fields == null)
            {
                LogError("[REST] Invalid document or missing fields");
                OnTeamDataFetchFailed?.Invoke("Invalid server response");
                yield break;
            }

            TeamData teamData = new TeamData
            {
                uid = !string.IsNullOrEmpty(doc.fields.uid?.stringValue) ? doc.fields.uid.stringValue : uid,
                teamName = doc.fields.teamName?.stringValue ?? string.Empty,
                teamNumber = ParseIntSafe(doc.fields.teamNumber?.integerValue),
                player1 = doc.fields.player1?.stringValue ?? string.Empty,
                player2 = doc.fields.player2?.stringValue ?? string.Empty,
                email = doc.fields.email?.stringValue ?? string.Empty,
                score = ParseIntSafe(doc.fields.score?.integerValue)
            };

            cachedTeamData = teamData;
            isTeamDataLoaded = true;

            // We are already on the main thread inside a coroutine
            ShowVerificationPanel(teamData);
            try
            {
                OnTeamDataFetched?.Invoke(teamData);
            }
            catch (Exception e)
            {
                LogError($"Event error: {e.Message}");
            }
        }
    }

    private int ParseIntSafe(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        if (int.TryParse(value, out var result)) return result;
        return 0;
    }

    // ---------------- REALTIME DATABASE (REST) ----------------
    private IEnumerator FetchTeamDataRealtimeCoroutine(string uid)
    {
        // Expect schema: /26SIG/{uid} → TeamData-like fields
        // Build URL: <base>/26SIG/<uid>.json[?auth=TOKEN]
        string path = $"26SIG/{uid}.json";
        string baseUrl = realtimeDatabaseBaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            LogError("RTDB base URL missing");
            OnTeamDataFetchFailed?.Invoke("Configuration error: RTDB URL missing");
            yield break;
        }

        string url = $"{baseUrl}/{path}";
        if (!string.IsNullOrWhiteSpace(realtimeDatabaseAuthToken))
        {
            url += (url.Contains("?") ? "&" : "?") + "auth=" + realtimeDatabaseAuthToken;
        }

        Log($"[RTDB] GET {url}");

        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 15;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"[RTDB] Request failed: {request.error}");
                OnTeamDataFetchFailed?.Invoke($"Network error: {request.error}");
                yield break;
            }

            string json = request.downloadHandler.text;
            if (logRestResponses) Log($"[RTDB] Response: {json}");

            // Parse minimal JSON fields without external packages
            // Expected flat object or null
            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                LogError("[RTDB] Team not found");
                OnTeamDataFetchFailed?.Invoke($"Team not found: {uid}");
                yield break;
            }

            // Very lightweight JSON extraction
            TeamData teamData = new TeamData
            {
                uid = ExtractJsonString(json, "uid") ?? uid,
                teamName = ExtractJsonString(json, "teamName") ?? string.Empty,
                teamNumber = ExtractJsonInt(json, "teamNumber"),
                player1 = ExtractJsonString(json, "player1") ?? string.Empty,
                player2 = ExtractJsonString(json, "player2") ?? string.Empty,
                email = ExtractJsonString(json, "email") ?? string.Empty,
                score = ExtractJsonInt(json, "score")
            };

            cachedTeamData = teamData;
            isTeamDataLoaded = true;
            ShowVerificationPanel(teamData);
            try { OnTeamDataFetched?.Invoke(teamData); } catch (Exception e) { LogError($"Event error: {e.Message}"); }
        }
    }

    private string ExtractJsonString(string json, string key)
    {
        // naive but robust for simple flat JSON: "key":"value"
        try
        {
            string marker = $"\"{key}\"";
            int idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            int firstQuote = json.IndexOf('"', colon + 1);
            if (firstQuote < 0) return null;
            int secondQuote = json.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0) return null;
            return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }
        catch { return null; }
    }

    private int ExtractJsonInt(string json, string key)
    {
        try
        {
            string marker = $"\"{key}\"";
            int idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return 0;
            int start = colon + 1;
            // skip spaces
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
            var numStr = json.Substring(start, end - start);
            if (int.TryParse(numStr, out var val)) return val;
            return 0;
        }
        catch { return 0; }
    }

    [Serializable]
    private class FirestoreDocument
    {
        public FirestoreFields fields;
    }

    [Serializable]
    private class FirestoreFields
    {
        public FirestoreString teamName;
        public FirestoreInteger teamNumber;
        public FirestoreString uid;
        public FirestoreString player1;
        public FirestoreString player2;
        public FirestoreString email;
        public FirestoreInteger score;
    }

    [Serializable]
    private class FirestoreString
    {
        public string stringValue;
    }

    [Serializable]
    private class FirestoreInteger
    {
        public string integerValue;
    }
    
    private void ShowVerificationPanel(TeamData teamData)
    {
        if (verificationPanel == null || teamDetailsText == null)
        {
            Log("Verification panel not assigned - skipping");
            return;
        }
        
        string displayText = $"Team Details:\n\n" +
                           $"Team Name: {teamData.teamName}\n" +
                           $"Team Number: {teamData.teamNumber}\n" +
                           $"Players: {teamData.player1} & {teamData.player2}\n" +
                           $"Email: {teamData.email}\n" +
                           $"UID: {teamData.uid}\n\n" +
                           $"Please verify this information is correct.";
        
        teamDetailsText.text = displayText;
        verificationPanel.SetActive(true);
    }
    
    private void OnVerifyTeamClicked()
    {
        Log("User verified team data");
        
        if (verificationPanel != null)
        {
            verificationPanel.SetActive(false);
        }
        
        OnTeamVerified?.Invoke();
    }
    
    // Helper methods
    private string GetField(System.Collections.Generic.IDictionary<string, object> data, string fieldName)
    {
        if (data.ContainsKey(fieldName) && data[fieldName] != null)
        {
            return data[fieldName].ToString();
        }
        return "";
    }
    
    private int GetIntField(System.Collections.Generic.IDictionary<string, object> data, string fieldName)
    {
        if (data.ContainsKey(fieldName) && data[fieldName] != null)
        {
            if (int.TryParse(data[fieldName].ToString(), out int result))
            {
                return result;
            }
        }
        return 0;
    }
    
    public TeamData GetCachedTeamData()
    {
        return cachedTeamData;
    }
    
    public bool IsTeamDataLoaded()
    {
        return isTeamDataLoaded;
    }
    
    public bool IsFirebaseReady()
    {
        return isFirebaseReady;
    }
    
    // Returns whether a fetch can be attempted given current connection method and configuration
    public bool IsFetchAvailable()
    {
        switch (connectionMethod)
        {
            case ConnectionMethod.RestOnly:
                return !string.IsNullOrWhiteSpace(firebaseProjectId);
            case ConnectionMethod.RestFallbackIfNeeded:
                return (isFirebaseReady && db != null) || !string.IsNullOrWhiteSpace(firebaseProjectId);
            case ConnectionMethod.DirectFirebaseOnly:
            default:
                return isFirebaseReady && db != null;
        }
    }
    
    public void ClearCache()
    {
        cachedTeamData = null;
        isTeamDataLoaded = false;
        Log("Cache cleared");
    }
    
    private void Log(string message)
    {
        if (enableDebugLogs)
            Debug.Log($"[FirebaseFetcher] {message}");
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[FirebaseFetcher] {message}");
    }
    
    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
        
        if (verifyTeamButton != null)
        {
            verifyTeamButton.onClick.RemoveListener(OnVerifyTeamClicked);
        }
    }
}