# 🎮 Unity Integration Guide - UPDATED SYSTEM

⚠️ **SYSTEM UPDATE**: This system now uses **team names as primary identifiers** with UIDs as secondary lookup keys.

This guide shows you exactly how to connect your Unity game to the updated tournament scoreboard system with team name support.

## 📝 Quick Setup Checklist

Before integrating with Unity, make sure:
- ✅ Your web app is deployed to Vercel
- ✅ Firebase is configured and working  
- ✅ You can register teams through the web interface
- ✅ API endpoints are responding correctly

## 🌐 API Endpoints

Your deployed app provides these endpoints for Unity:

| Endpoint | Method | Purpose |
|----------|---------|----------|
| `/api/register` | POST | Register a new team |
| `/api/update-score` | POST | Update team score |  
| `/api/scoreboard` | GET | Get all teams and scores |

**Base URL**: `https://your-app-name.vercel.app`

## 🔧 Unity Implementation

### 1. Create ScoreManager Script

Create a new C# script called `ScoreManager.cs`:

```csharp
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class TeamRegistration
{
    public string teamName;
    public string player1;
    public string player2;  
    public string email;
    public string phoneNumber;
}

[System.Serializable]
public class TeamResponse
{
    public bool success;
    public TeamData data;
    public string error;
}

[System.Serializable] 
public class TeamData
{
    public int teamNumber;
    public string uid;
    public string player1;
    public string player2;
    public string email;
    public int score;
}

[System.Serializable]
public class ScoreUpdate
{
    public string uid;
    public int scoreIncrement;
}

[System.Serializable]
public class ScoreUpdateResponse
{
    public bool success;
    public string message;
    public string error;
}

public class ScoreManager : MonoBehaviour
{
    [Header("Tournament Settings")]
    public string apiBaseUrl = "https://your-app-name.vercel.app/api";
    
    [Header("Team Info")]
    public string teamUID = "";
    public string player1Name = "";
    public string player2Name = "";
    
    [Header("Debug")]
    public bool enableDebugLogs = true;
    
    private void Start()
    {
        // Load saved team data if it exists
        LoadTeamData();
    }
    
    /// <summary>
    /// Register a new team for the tournament
    /// Call this from your game's registration UI
    /// </summary>
    public void RegisterTeam(string teamName, string player1, string player2, string email, string phoneNumber)
    {
        StartCoroutine(RegisterTeamCoroutine(teamName, player1, player2, email, phoneNumber));
    }
    
    /// <summary>
    /// Update the team's score  
    /// Call this when players earn points in your game
    /// </summary>
    public void AddScore(int points)
    {
        if (string.IsNullOrEmpty(teamUID))
        {
            LogError("Cannot update score: No team UID found. Please register first.");
            return;
        }
        
        StartCoroutine(UpdateScoreCoroutine(teamUID, points));
    }
    
    /// <summary>
    /// Get current scoreboard data
    /// Use this to display leaderboards in your game
    /// </summary>
    public void GetScoreboard()
    {
        StartCoroutine(GetScoreboardCoroutine());
    }
    
    private IEnumerator RegisterTeamCoroutine(string teamName, string player1, string player2, string email, string phoneNumber)
    {
        Log("Registering team...");
        
        // Create registration data
        TeamRegistration registration = new TeamRegistration
        {
            teamName = teamName,
            player1 = player1,
            player2 = player2,
            email = email,
            phoneNumber = phoneNumber
        };
        
        string jsonData = JsonUtility.ToJson(registration);
        Log($"Registration data: {jsonData}");
        
        // Create web request
        UnityWebRequest request = UnityWebRequest.Post(apiBaseUrl + "/register", jsonData, "application/json");
        
        yield return request.SendWebRequest();
        
        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            Log($"Registration response: {responseText}");
            
            TeamResponse response = JsonUtility.FromJson<TeamResponse>(responseText);
            
            if (response.success)
            {
                // Save team data
                teamUID = response.data.uid;
                player1Name = response.data.player1;  
                player2Name = response.data.player2;
                SaveTeamData();
                
                // Also save team name for future reference
                PlayerPrefs.SetString("TeamName", response.data.teamName);
                
                Log($"Team registered successfully!");
                Log($"Team Number: {response.data.teamNumber}");
                Log($"Team UID: {response.data.uid}");
                
                // Notify your game that registration was successful
                OnTeamRegistered(response.data);
            }
            else
            {
                LogError($"Registration failed: {response.error}");
                OnRegistrationFailed(response.error);
            }
        }
        else
        {
            LogError($"Registration request failed: {request.error}");
            OnRegistrationFailed(request.error);
        }
    }
    
    private IEnumerator UpdateScoreCoroutine(string uid, int scoreIncrement)
    {
        Log($"Updating score by {scoreIncrement} points...");
        
        // Create score update data
        ScoreUpdate scoreUpdate = new ScoreUpdate
        {
            uid = uid,
            scoreIncrement = scoreIncrement
        };
        
        string jsonData = JsonUtility.ToJson(scoreUpdate);
        
        // Create web request
        UnityWebRequest request = UnityWebRequest.Post(apiBaseUrl + "/update-score", jsonData, "application/json");
        
        yield return request.SendWebRequest();
        
        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            ScoreUpdateResponse response = JsonUtility.FromJson<ScoreUpdateResponse>(responseText);
            
            if (response.success)
            {
                Log($"Score updated successfully: {response.message}");
                OnScoreUpdated(scoreIncrement);
            }
            else
            {
                LogError($"Score update failed: {response.error}");
                OnScoreUpdateFailed(response.error);
            }
        }
        else
        {
            LogError($"Score update request failed: {request.error}");
            OnScoreUpdateFailed(request.error);
        }
    }
    
    private IEnumerator GetScoreboardCoroutine()
    {
        Log("Getting scoreboard data...");
        
        UnityWebRequest request = UnityWebRequest.Get(apiBaseUrl + "/scoreboard");
        
        yield return request.SendWebRequest();
        
        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            Log($"Scoreboard data received: {responseText}");
            
            // Parse and use scoreboard data in your game
            // You'll need to create classes to deserialize this based on your needs
            OnScoreboardReceived(responseText);
        }
        else
        {
            LogError($"Scoreboard request failed: {request.error}");
            OnScoreboardFailed(request.error);
        }
    }
    
    // Save team data to PlayerPrefs so it persists between game sessions
    private void SaveTeamData()
    {
        PlayerPrefs.SetString("TeamUID", teamUID);
        PlayerPrefs.SetString("Player1Name", player1Name);
        PlayerPrefs.SetString("Player2Name", player2Name);
        PlayerPrefs.Save();
    }
    
    // Load team data from PlayerPrefs
    private void LoadTeamData()
    {
        teamUID = PlayerPrefs.GetString("TeamUID", "");
        player1Name = PlayerPrefs.GetString("Player1Name", "");
        player2Name = PlayerPrefs.GetString("Player2Name", "");
        
        if (!string.IsNullOrEmpty(teamUID))
        {
            Log($"Loaded existing team: {player1Name} & {player2Name} (UID: {teamUID})");
        }
    }
    
    // Event handlers - override these in your game
    protected virtual void OnTeamRegistered(TeamData teamData)
    {
        // Handle successful team registration in your game
        Debug.Log($"Team {teamData.teamNumber} registered: {teamData.player1} & {teamData.player2}");
    }
    
    protected virtual void OnRegistrationFailed(string error)
    {
        // Handle registration failure in your game
        Debug.LogError($"Team registration failed: {error}");
    }
    
    protected virtual void OnScoreUpdated(int pointsAdded)
    {
        // Handle successful score update in your game
        Debug.Log($"Added {pointsAdded} points to team score!");
    }
    
    protected virtual void OnScoreUpdateFailed(string error)
    {
        // Handle score update failure in your game
        Debug.LogError($"Score update failed: {error}");
    }
    
    protected virtual void OnScoreboardReceived(string jsonData)
    {
        // Handle scoreboard data in your game
        Debug.Log("Scoreboard data received");
    }
    
    protected virtual void OnScoreboardFailed(string error)
    {
        // Handle scoreboard request failure
        Debug.LogError($"Failed to get scoreboard: {error}");
    }
    
    private void Log(string message)
    {
        if (enableDebugLogs)
            Debug.Log($"[ScoreManager] {message}");
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[ScoreManager] {message}");
    }
}
```

### 2. Usage Examples

#### Register a Team
```csharp
// Get the ScoreManager component
ScoreManager scoreManager = FindObjectOfType<ScoreManager>();

// Register team with team name, player names, email, and phone
scoreManager.RegisterTeam("DragonHunters", "Alice", "Bob", "team@example.com", "+1234567890");
```

#### Add Points During Gameplay
```csharp
// Add points when player completes a task
scoreManager.AddScore(100);

// Add points when player defeats enemy  
scoreManager.AddScore(50);

// You can also subtract points for penalties
scoreManager.AddScore(-25);
```

#### Get Live Scoreboard
```csharp
// Request current scoreboard data
scoreManager.GetScoreboard();
```

### 3. UI Integration Example

Create a simple registration UI:

```csharp
using UnityEngine;
using UnityEngine.UI;

public class RegistrationUI : MonoBehaviour
{
    [Header("UI Elements")]
    public InputField teamNameInput;
    public InputField player1Input;
    public InputField player2Input; 
    public InputField emailInput;
    public InputField phoneInput;
    public Button registerButton;
    public Text statusText;
    
    private ScoreManager scoreManager;
    
    private void Start()
    {
        scoreManager = FindObjectOfType<ScoreManager>();
        registerButton.onClick.AddListener(RegisterTeam);
    }
    
    private void RegisterTeam()
    {
        string teamName = teamNameInput.text.Trim();
        string player1 = player1Input.text.Trim();
        string player2 = player2Input.text.Trim();
        string email = emailInput.text.Trim();
        string phone = phoneInput.text.Trim();
        
        if (string.IsNullOrEmpty(teamName) || string.IsNullOrEmpty(player1) || string.IsNullOrEmpty(player2) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(phone))
        {
            statusText.text = "Please fill in all fields";
            return;
        }
        
        statusText.text = "Registering team...";
        registerButton.interactable = false;
        
        scoreManager.RegisterTeam(teamName, player1, player2, email, phone);
    }
}
```

## 🔍 Testing Your Integration

### 1. Test Registration
1. Run your Unity game
2. Fill out registration form
3. Check the web scoreboard - your team should appear
4. Check Unity console for success messages

### 2. Test Score Updates
1. Call `AddScore(100)` from your game code
2. Check the web scoreboard - score should update immediately
3. Register multiple teams and update different scores

### 3. Debug Common Issues

**"404 Not Found" errors:**
- Check your `apiBaseUrl` is correct
- Make sure your Vercel app is deployed and working

**"CORS" errors:**
- This shouldn't happen with Vercel, but if it does, the issue is on the server side

**"Team not found" when updating score:**
- Make sure you registered the team first
- Check that the UID is being saved correctly

## 💡 Best Practices

### 1. Error Handling
Always check for errors and provide user feedback:

```csharp
protected override void OnScoreUpdateFailed(string error)
{
    // Show error message to player
    gameUI.ShowErrorMessage($"Failed to update score: {error}");
    
    // Maybe try again after a delay
    Invoke(nameof(RetryScoreUpdate), 2.0f);
}
```

### 2. Offline Handling
Consider storing score updates locally and syncing when connection is restored:

```csharp
private Queue<int> pendingScoreUpdates = new Queue<int>();

public void AddScore(int points)
{
    if (Application.internetReachability == NetworkReachability.NotReachable)
    {
        // Store for later
        pendingScoreUpdates.Enqueue(points);
        return;
    }
    
    // Send immediately
    StartCoroutine(UpdateScoreCoroutine(teamUID, points));
}
```

### 3. Rate Limiting
Don't spam the API - batch score updates or add delays:

```csharp
private float lastScoreUpdate = 0f;
private const float SCORE_UPDATE_COOLDOWN = 1.0f; // 1 second between updates

public void AddScore(int points)
{
    if (Time.time - lastScoreUpdate < SCORE_UPDATE_COOLDOWN)
    {
        // Queue the update for later
        return;
    }
    
    lastScoreUpdate = Time.time;
    StartCoroutine(UpdateScoreCoroutine(teamUID, points));
}
```

## 🎯 Integration Complete!

Your Unity game is now connected to the live tournament scoreboard! Players can:

1. Register their teams through your game
2. Earn points that appear on the live web scoreboard
3. See their progress in real-time

The web scoreboard will automatically update as your game sends score updates, creating an engaging live tournament experience!