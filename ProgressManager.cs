using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable]
public class ProgressData
{
    public int lastCompletedClueIndex = -1;     // Last successfully collected treasure (0-based, -1 means none)
    public int[] collectedTreasures;            // Array of collected clue indices
    public int totalTreasuresCollected = 0;     // Count for validation
    public int nextClueIndex = 0;               // Next clue to hunt (0-based)
    public int teamAssignedClueIndex = 0;       // Original starting clue for this team
    public string lastSavedTimestamp;           // When progress was last saved
    
    public ProgressData()
    {
        collectedTreasures = new int[0];
        lastSavedTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }
}

public class ProgressManager : MonoBehaviour
{
    [Header("Firebase Configuration")]
    [Tooltip("Base RTDB URL - will be inherited from FirebaseRTDBFetcher if not set")]
    public string realtimeDatabaseBaseUrl = "";
    
    [Tooltip("Auth token - will be inherited from FirebaseRTDBFetcher if not set")]
    public string authToken = "";
    
    [Header("Settings")]
    [Tooltip("Enable debug logging for progress operations")]
    public bool debugMode = true;
    
    [Header("Mobile Debug")]
    public TMPro.TMP_Text mobileDebugText;
    
    // References to other managers
    private FirebaseRTDBFetcher rtdbFetcher;
    private TreasureHuntManager treasureHuntManager;
    
    // Current progress data
    private ProgressData currentProgress;
    private string activeUID;
    
    void Start()
    {
        // Find references to other managers
        rtdbFetcher = FindObjectOfType<FirebaseRTDBFetcher>();
        treasureHuntManager = FindObjectOfType<TreasureHuntManager>();
        
        // Inherit Firebase settings from RTDB fetcher if not set
        if (rtdbFetcher != null)
        {
            if (string.IsNullOrEmpty(realtimeDatabaseBaseUrl))
                realtimeDatabaseBaseUrl = rtdbFetcher.realtimeDatabaseBaseUrl;
            if (string.IsNullOrEmpty(authToken))
                authToken = rtdbFetcher.authToken;
        }
        
        if (debugMode)
        {
            Debug.Log("ProgressManager initialized");
            if (mobileDebugText != null) mobileDebugText.text = "ProgressManager initialized";
        }
    }
    
    // Check if a UID has existing progress
    public void CheckProgressForUID(string uid, System.Action<bool, ProgressData> callback)
    {
        if (string.IsNullOrEmpty(uid))
        {
            callback?.Invoke(false, null);
            return;
        }
        
        activeUID = uid;
        StartCoroutine(LoadProgressFromFirebase(uid, callback));
    }
    
    private IEnumerator LoadProgressFromFirebase(string uid, System.Action<bool, ProgressData> callback)
    {
        string url = BuildProgressUrl(uid);
        
        if (debugMode)
        {
            Debug.Log($"Loading progress from: {url}");
            if (mobileDebugText != null) mobileDebugText.text = $"Loading progress for {uid}...";
        }
        
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 15;
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                
                if (string.IsNullOrWhiteSpace(json) || json == "null")
                {
                    // No progress data exists - new game
                    if (debugMode)
                    {
                        Debug.Log($"No existing progress found for UID: {uid}");
                        if (mobileDebugText != null) mobileDebugText.text = "No existing progress - starting new game";
                    }
                    callback?.Invoke(false, null);
                }
                else
                {
                    try
                    {
                        ProgressData progressData = JsonUtility.FromJson<ProgressData>(json);
                        
                        if (IsValidProgressData(progressData))
                        {
                            currentProgress = progressData;
                            
                            if (debugMode)
                            {
                                Debug.Log($"Progress loaded: {progressData.totalTreasuresCollected} treasures collected, next clue: {progressData.nextClueIndex}");
                                if (mobileDebugText != null) mobileDebugText.text = $"Progress loaded: {progressData.totalTreasuresCollected} treasures collected";
                            }
                            
                            callback?.Invoke(true, progressData);
                        }
                        else
                        {
                            // Invalid progress data - treat as new game
                            Debug.LogWarning($"Invalid progress data for UID: {uid}, starting new game");
                            if (mobileDebugText != null) mobileDebugText.text = "Invalid progress data - starting new game";
                            callback?.Invoke(false, null);
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Failed to parse progress data: {e.Message}");
                        if (mobileDebugText != null) mobileDebugText.text = "Failed to parse progress - starting new game";
                        callback?.Invoke(false, null);
                    }
                }
            }
            else
            {
                Debug.LogError($"Failed to load progress: {request.error}");
                if (mobileDebugText != null) mobileDebugText.text = "Network error loading progress - starting new game";
                callback?.Invoke(false, null);
            }
        }
    }
    
    // Save progress after successful treasure collection
    public void SaveProgressAfterCollection(int clueIndex, int teamAssignedStartIndex, int totalClues)
    {
        if (string.IsNullOrEmpty(activeUID))
        {
            Debug.LogError("Cannot save progress: no active UID");
            if (mobileDebugText != null) mobileDebugText.text = "ERROR: Cannot save progress - no active UID";
            return;
        }
        
        // Initialize progress if it doesn't exist
        if (currentProgress == null)
        {
            currentProgress = new ProgressData();
            currentProgress.teamAssignedClueIndex = teamAssignedStartIndex;
        }
        
        // Add to collected treasures if not already present
        List<int> collectedList = new List<int>(currentProgress.collectedTreasures);
        if (!collectedList.Contains(clueIndex))
        {
            collectedList.Add(clueIndex);
            collectedList.Sort(); // Keep sorted for consistency
        }
        
        // Update progress data
        currentProgress.collectedTreasures = collectedList.ToArray();
        currentProgress.totalTreasuresCollected = collectedList.Count;
        currentProgress.lastCompletedClueIndex = clueIndex;
        currentProgress.nextClueIndex = CalculateNextClueIndex(clueIndex, teamAssignedStartIndex, totalClues);
        currentProgress.lastSavedTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        
        StartCoroutine(SaveProgressToFirebase());
    }
    
    private int CalculateNextClueIndex(int lastCompletedIndex, int teamStartIndex, int totalClues)
    {
        // Calculate next clue in the sequence for this team
        int nextIndex = (lastCompletedIndex + 1) % totalClues;
        
        // If we've come full circle back to team's starting index and collected all clues, game is complete
        if (nextIndex == teamStartIndex && currentProgress != null && currentProgress.totalTreasuresCollected >= totalClues)
        {
            return -1; // Indicates game complete
        }
        
        return nextIndex;
    }
    
    private IEnumerator SaveProgressToFirebase()
    {
        if (currentProgress == null) yield break;
        
        string url = BuildProgressUrl(activeUID);
        string json = JsonUtility.ToJson(currentProgress);
        
        if (debugMode)
        {
            Debug.Log($"Saving progress to Firebase: {json}");
            if (mobileDebugText != null) mobileDebugText.text = $"Saving progress: {currentProgress.totalTreasuresCollected} treasures";
        }
        
        using (var request = new UnityWebRequest(url, "PUT"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                if (debugMode)
                {
                    Debug.Log("Progress saved successfully");
                    if (mobileDebugText != null) mobileDebugText.text = "Progress saved successfully";
                }
            }
            else
            {
                Debug.LogError($"Failed to save progress: {request.error}");
                if (mobileDebugText != null) mobileDebugText.text = $"Failed to save progress: {request.error}";
            }
        }
    }
    
    private string BuildProgressUrl(string uid)
    {
        string baseUrl = realtimeDatabaseBaseUrl.TrimEnd('/');
        string url = $"{baseUrl}/{uid}/Progress_Follow.json";
        
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            url += $"?auth={authToken}";
        }
        
        if (debugMode)
        {
            Debug.Log($"Progress URL constructed: {url}");
            if (mobileDebugText != null) mobileDebugText.text = $"Progress URL: {url}";
        }
        
        return url;
    }
    
    private bool IsValidProgressData(ProgressData data)
    {
        if (data == null) return false;
        if (data.collectedTreasures == null) return false;
        if (data.totalTreasuresCollected < 0) return false;
        if (data.totalTreasuresCollected != data.collectedTreasures.Length) return false;
        
        return true;
    }
    
    // Public getters for other systems
    public ProgressData GetCurrentProgress()
    {
        return currentProgress;
    }
    
    public bool HasProgress()
    {
        return currentProgress != null && currentProgress.totalTreasuresCollected > 0;
    }
    
    public int[] GetCollectedTreasures()
    {
        return currentProgress?.collectedTreasures ?? new int[0];
    }
    
    public int GetNextClueIndex()
    {
        return currentProgress?.nextClueIndex ?? 0;
    }
    
    public bool IsGameComplete()
    {
        return currentProgress != null && currentProgress.nextClueIndex == -1;
    }
    
    // Initialize progress for new game and save to Firebase
    public void InitializeNewGameProgress(int teamStartIndex)
    {
        if (string.IsNullOrEmpty(activeUID))
        {
            Debug.LogError("Cannot initialize progress: no active UID");
            if (mobileDebugText != null) mobileDebugText.text = "ERROR: Cannot initialize progress - no active UID";
            return;
        }
        
        currentProgress = new ProgressData();
        currentProgress.teamAssignedClueIndex = teamStartIndex;
        currentProgress.nextClueIndex = teamStartIndex;
        
        // Immediately save the initial progress to Firebase
        StartCoroutine(SaveProgressToFirebase());
        
        if (debugMode)
        {
            Debug.Log($"Initialized new game progress with starting clue index: {teamStartIndex}");
            if (mobileDebugText != null) mobileDebugText.text = $"New game initialized at clue {teamStartIndex}";
        }
    }
    
    // Clear progress (for testing or reset)
    public void ClearProgress()
    {
        currentProgress = null;
        activeUID = "";
        
        if (debugMode)
        {
            Debug.Log("Progress cleared");
            if (mobileDebugText != null) mobileDebugText.text = "Progress cleared";
        }
    }
    
    // Set active UID (called by FirebaseRTDBFetcher when team data is fetched)
    public void SetActiveUID(string uid)
    {
        activeUID = uid;
        if (debugMode)
        {
            Debug.Log($"ProgressManager active UID set to: {uid}");
            if (mobileDebugText != null) mobileDebugText.text = $"Progress UID set: {uid}";
        }
    }
}