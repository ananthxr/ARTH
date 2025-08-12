# Unity API Endpoints Guide

Complete guide for all available endpoints to access and manage team data from Unity.

## 🌐 Base URL
```
https://your-app-name.vercel.app/api
```

## 📋 Available Endpoints

### 1. 📝 Register Team
**Create a new team registration**

```
POST /api/register
```

**Request Body:**
```json
{
  "teamName": "DragonHunters",
  "player1": "John Doe",
  "player2": "Jane Smith",
  "email": "team@example.com",
  "phoneNumber": "+1234567890"
}
```

**Note:** All fields are required. Team names must be unique and between 2-30 characters.

**Success Response (201):**
```json
{
  "success": true,
  "data": {
    "teamNumber": 1,
    "teamName": "DragonHunters",
    "uid": "qK234",
    "player1": "John Doe",
    "player2": "Jane Smith",
    "email": "team@example.com",
    "phoneNumber": "+1234567890",
    "score": 0,
    "createdAt": "2024-01-01T00:00:00Z"
  }
}
```

**Error Response (400):**
```json
{
  "success": false,
  "error": "Team name \"DragonHunters\" is already taken. Please choose a different name."
}
```

### 2. 🎯 Update Team Score
**Update a team's score using their UID or team name**

```
POST /api/update-score
```

**Request Body (Using UID):**
```json
{
  "uid": "qK234",
  "scoreIncrement": 100
}
```

**Request Body (Using Team Name):**
```json
{
  "teamName": "DragonHunters",
  "scoreIncrement": 100
}
```

**Success Response (200):**
```json
{
  "success": true,
  "message": "Score updated successfully. Added 100 points to team DragonHunters (qK234)."
}
```

**Error Response (404):**
```json
{
  "success": false,
  "error": "Team not found with the provided UID or team name"
}
```

### 3. 👥 Get Single Team Data
**Retrieve data for a specific team by UID or team name**

```
GET /api/team?uid=qK234
OR
GET /api/team?teamName=DragonHunters
```

**Alternative POST method:**
```
POST /api/team
```

**Request Body (POST):**
```json
{
  "uid": "qK234"
}
```
OR
```json
{
  "teamName": "DragonHunters"
}
```

**Success Response (200):**
```json
{
  "success": true,
  "data": {
    "id": "DragonHunters",
    "teamNumber": 1,
    "teamName": "DragonHunters",
    "uid": "qK234",
    "player1": "John Doe",
    "player2": "Jane Smith",
    "email": "team@example.com",
    "phoneNumber": "+1234567890",
    "score": 1500,
    "createdAt": "2024-01-01T00:00:00Z"
  }
}
```

**Error Response (404):**
```json
{
  "success": false,
  "error": "Team not found"
}
```

### 4. 🏆 Get All Teams (Scoreboard)
**Retrieve all teams with their scores, sorted by score (highest first)**

```
GET /api/scoreboard
```

**Success Response (200):**
```json
{
  "success": true,
  "data": {
    "teams": [
      {
        "id": "DragonHunters",
        "teamNumber": 1,
        "teamName": "DragonHunters",
        "uid": "qK234",
        "player1": "John Doe",
        "player2": "Jane Smith",
        "email": "team@example.com",
        "phoneNumber": "+1234567890",
        "score": 1500,
        "createdAt": "2024-01-01T00:00:00Z"
      },
      {
        "id": "Phoenix",
        "teamNumber": 2,
        "teamName": "Phoenix",
        "uid": "mR567",
        "player1": "Alice Smith",
        "player2": "Bob Jones",
        "email": "phoenix@example.com",
        "phoneNumber": "+1234567891",
        "score": 1200,
        "createdAt": "2024-01-01T01:00:00Z"
      }
    ],
    "totalTeams": 2,
    "lastUpdated": "2024-01-01T12:00:00Z"
  }
}
```

## 🎮 Unity C# Examples

### Team Registration
```csharp
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

[System.Serializable]
public class RegisterRequest
{
    public string teamName;
    public string player1;
    public string player2;
    public string email;
    public string phoneNumber;
}

[System.Serializable]
public class RegisterResponse
{
    public bool success;
    public RegisterData data;
    public string error;
}

[System.Serializable]
public class RegisterData
{
    public int teamNumber;
    public string teamName;
    public string uid;
    public string player1;
    public string player2;
    public string email;
    public string phoneNumber;
    public int score;
}

public IEnumerator RegisterTeam(string teamName, string player1, string player2, string email, string phone)
{
    string url = "https://your-app.vercel.app/api/register";
    
    RegisterRequest request = new RegisterRequest
    {
        teamName = teamName,
        player1 = player1,
        player2 = player2,
        email = email,
        phoneNumber = phone
    };
    
    string jsonData = JsonUtility.ToJson(request);
    
    using (UnityWebRequest www = UnityWebRequest.Post(url, jsonData, "application/json"))
    {
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success)
        {
            RegisterResponse response = JsonUtility.FromJson<RegisterResponse>(www.downloadHandler.text);
            if (response.success)
            {
                Debug.Log($"Team registered! UID: {response.data.uid}");
                // Save the UID for later use
                PlayerPrefs.SetString("TeamUID", response.data.uid);
                PlayerPrefs.SetString("TeamName", response.data.teamName);
            }
            else
            {
                Debug.LogError($"Registration failed: {response.error}");
            }
        }
        else
        {
            Debug.LogError($"Network error: {www.error}");
        }
    }
}
```

### Update Score
```csharp
[System.Serializable]
public class ScoreUpdateRequest
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

public IEnumerator UpdateScore(string uid, int points)
{
    string url = "https://your-app.vercel.app/api/update-score";
    
    ScoreUpdateRequest request = new ScoreUpdateRequest
    {
        uid = uid,
        scoreIncrement = points
    };
    
    string jsonData = JsonUtility.ToJson(request);
    
    using (UnityWebRequest www = UnityWebRequest.Post(url, jsonData, "application/json"))
    {
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success)
        {
            ScoreUpdateResponse response = JsonUtility.FromJson<ScoreUpdateResponse>(www.downloadHandler.text);
            if (response.success)
            {
                Debug.Log($"Score updated: {response.message}");
            }
            else
            {
                Debug.LogError($"Score update failed: {response.error}");
            }
        }
        else
        {
            Debug.LogError($"Network error: {www.error}");
        }
    }
}
```

### Get Team Data
```csharp
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
    public string id;
    public int teamNumber;
    public string teamName;
    public string uid;
    public string player1;
    public string player2;
    public string email;
    public string phoneNumber;
    public int score;
    public string createdAt;
}

public IEnumerator GetTeamByUID(string uid)
{
    string url = $"https://your-app.vercel.app/api/team?uid={uid}";
    
    using (UnityWebRequest www = UnityWebRequest.Get(url))
    {
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success)
        {
            TeamResponse response = JsonUtility.FromJson<TeamResponse>(www.downloadHandler.text);
            if (response.success)
            {
                Debug.Log($"Team: {response.data.teamName}, Score: {response.data.score}");
                // Use team data in your game
            }
            else
            {
                Debug.LogError($"Team not found: {response.error}");
            }
        }
        else
        {
            Debug.LogError($"Network error: {www.error}");
        }
    }
}

public IEnumerator GetTeamByName(string teamName)
{
    string url = $"https://your-app.vercel.app/api/team?teamName={teamName}";
    
    using (UnityWebRequest www = UnityWebRequest.Get(url))
    {
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success)
        {
            TeamResponse response = JsonUtility.FromJson<TeamResponse>(www.downloadHandler.text);
            if (response.success)
            {
                Debug.Log($"Team UID: {response.data.uid}, Score: {response.data.score}");
                // Use team data in your game
            }
            else
            {
                Debug.LogError($"Team not found: {response.error}");
            }
        }
        else
        {
            Debug.LogError($"Network error: {www.error}");
        }
    }
}
```

### Get All Teams (Scoreboard)
```csharp
[System.Serializable]
public class ScoreboardResponse
{
    public bool success;
    public ScoreboardData data;
    public string error;
}

[System.Serializable]
public class ScoreboardData
{
    public TeamData[] teams;
    public int totalTeams;
    public string lastUpdated;
}

public IEnumerator GetScoreboard()
{
    string url = "https://your-app.vercel.app/api/scoreboard";
    
    using (UnityWebRequest www = UnityWebRequest.Get(url))
    {
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success)
        {
            ScoreboardResponse response = JsonUtility.FromJson<ScoreboardResponse>(www.downloadHandler.text);
            if (response.success)
            {
                Debug.Log($"Total teams: {response.data.totalTeams}");
                foreach (TeamData team in response.data.teams)
                {
                    Debug.Log($"{team.teamName}: {team.score} points");
                }
                // Update your in-game leaderboard UI
            }
            else
            {
                Debug.LogError($"Failed to get scoreboard: {response.error}");
            }
        }
        else
        {
            Debug.LogError($"Network error: {www.error}");
        }
    }
}
```

## 🚀 Usage Scenarios

### Scenario 1: Team Registration in Unity
```csharp
// Let teams register directly in Unity
StartCoroutine(RegisterTeam("DragonHunters", "John", "Jane", "team@email.com", "+1234567890"));
```

### Scenario 2: Score Updates During Gameplay
```csharp
// When player finds a treasure
string teamUID = PlayerPrefs.GetString("TeamUID");
StartCoroutine(UpdateScore(teamUID, 100));
```

### Scenario 3: Display Team Info
```csharp
// Show current team info in Unity UI
string teamUID = PlayerPrefs.GetString("TeamUID");
StartCoroutine(GetTeamByUID(teamUID));
```

### Scenario 4: In-Game Leaderboard
```csharp
// Update leaderboard every 30 seconds
InvokeRepeating(nameof(UpdateLeaderboard), 0f, 30f);

void UpdateLeaderboard()
{
    StartCoroutine(GetScoreboard());
}
```

## 🔧 Best Practices

1. **Store Team UID**: Always save the UID after registration
2. **Error Handling**: Check `success` field in all responses
3. **Network Checks**: Verify internet connectivity before API calls
4. **Rate Limiting**: Don't spam APIs - batch updates or use timers
5. **Caching**: Store team data locally to reduce API calls

## 🛠️ Testing Your Integration

1. **Test Registration**: Register a team and check Firebase Console
2. **Test Score Updates**: Update scores and verify on web scoreboard
3. **Test Data Retrieval**: Get team data and verify all fields
4. **Test Error Cases**: Try invalid UIDs/team names to test error handling

All endpoints return standardized JSON responses with `success` field for easy error handling!