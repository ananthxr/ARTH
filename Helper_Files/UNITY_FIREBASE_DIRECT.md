# 🔥 Unity Direct Firebase Integration

**NEW APPROACH**: Unity connects directly to Firebase, bypassing the web API layer for better performance and reliability.

## 📋 Prerequisites

1. **Firebase Unity SDK**: Download from [Firebase Console](https://console.firebase.google.com/project/arth-33ed6/settings/general)
2. **Google Services JSON**: Download `google-services.json` from your Firebase project
3. **Anonymous Authentication**: Already enabled in your project

## 🔧 Unity Setup Steps

### Step 1: Import Firebase SDK
1. Download Firebase Unity SDK from Firebase Console
2. Import these packages in Unity:
   - `FirebaseAuth.unitypackage`
   - `FirebaseFirestore.unitypackage`
   - `FirebaseApp.unitypackage`

### Step 2: Add Configuration File
1. Place `google-services.json` in `Assets/StreamingAssets/`
2. Ensure the file is included in your build

### Step 3: Firebase Configuration Script

Create `Assets/Scripts/FirebaseManager.cs`:

```csharp
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.Extensions;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class FirebaseManager : MonoBehaviour
{
    [Header("Firebase Status")]
    public bool isInitialized = false;
    public bool isAuthenticated = false;
    
    [Header("Debug")]
    public bool enableDebugLogs = true;
    
    // Firebase instances
    private FirebaseApp app;
    private FirebaseAuth auth;
    private FirebaseFirestore db;
    
    // Singleton pattern
    public static FirebaseManager Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeFirebase();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void InitializeFirebase()
    {
        Log("Initializing Firebase...");
        
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                // Firebase dependencies are available
                app = FirebaseApp.DefaultInstance;
                auth = FirebaseAuth.DefaultInstance;
                db = FirebaseFirestore.DefaultInstance;
                
                Log("Firebase initialized successfully!");
                isInitialized = true;
                
                // Authenticate anonymously
                AuthenticateAnonymously();
            }
            else
            {
                LogError($"Could not resolve Firebase dependencies: {dependencyStatus}");
            }
        });
    }
    
    private void AuthenticateAnonymously()
    {
        Log("Authenticating anonymously...");
        
        auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled)
            {
                LogError("Anonymous authentication was canceled.");
                return;
            }
            if (task.IsFaulted)
            {
                LogError($"Anonymous authentication failed: {task.Exception}");
                return;
            }
            
            FirebaseUser user = task.Result;
            Log($"Anonymous authentication successful! User ID: {user.UserId}");
            isAuthenticated = true;
        });
    }
    
    public bool IsReady()
    {
        return isInitialized && isAuthenticated;
    }
    
    public FirebaseFirestore GetFirestore()
    {
        if (!IsReady())
        {
            LogError("Firebase not ready! Call IsReady() first.");
            return null;
        }
        return db;
    }
    
    private void Log(string message)
    {
        if (enableDebugLogs)
            Debug.Log($"[FirebaseManager] {message}");
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[FirebaseManager] {message}");
    }
}
```

### Step 4: Team Data Model

Create `Assets/Scripts/TeamData.cs`:

```csharp
using System;
using Firebase.Firestore;

[FirestoreData]
public class TeamData
{
    [FirestoreProperty("teamNumber")]
    public int TeamNumber { get; set; }
    
    [FirestoreProperty("teamName")]
    public string TeamName { get; set; }
    
    [FirestoreProperty("uid")]
    public string UID { get; set; }
    
    [FirestoreProperty("player1")]
    public string Player1 { get; set; }
    
    [FirestoreProperty("player2")]
    public string Player2 { get; set; }
    
    [FirestoreProperty("email")]
    public string Email { get; set; }
    
    [FirestoreProperty("phoneNumber")]
    public string PhoneNumber { get; set; }
    
    [FirestoreProperty("score")]
    public int Score { get; set; }
    
    [FirestoreProperty("createdAt")]
    public Timestamp CreatedAt { get; set; }
    
    // Constructor for new teams
    public TeamData() { }
    
    public TeamData(string teamName, string player1, string player2, string email, string phoneNumber)
    {
        TeamName = teamName;
        Player1 = player1;
        Player2 = player2;
        Email = email;
        PhoneNumber = phoneNumber;
        Score = 0;
        CreatedAt = Timestamp.GetCurrentTimestamp();
        UID = GenerateUID();
    }
    
    private string GenerateUID()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new System.Random();
        var result = new char[5];
        for (int i = 0; i < 5; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        return new string(result);
    }
}
```

### Step 5: Tournament Manager

Create `Assets/Scripts/TournamentManager.cs`:

```csharp
using Firebase.Firestore;
using Firebase.Extensions;
using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

public class TournamentManager : MonoBehaviour
{
    [Header("Tournament Settings")]
    public string currentTeamUID = "";
    
    [Header("Events")]
    public UnityEvent<TeamData> OnTeamRegistered;
    public UnityEvent<string> OnRegistrationFailed;
    public UnityEvent<int> OnScoreUpdated;
    public UnityEvent<string> OnScoreUpdateFailed;
    public UnityEvent<List<TeamData>> OnScoreboardUpdated;
    
    private FirebaseFirestore db;
    private ListenerRegistration scoreboardListener;
    
    private void Start()
    {
        // Wait for Firebase to be ready
        if (FirebaseManager.Instance.IsReady())
        {
            db = FirebaseManager.Instance.GetFirestore();
            StartScoreboardListener();
        }
        else
        {
            // Check every second until Firebase is ready
            InvokeRepeating(nameof(CheckFirebaseReady), 1f, 1f);
        }
    }
    
    private void CheckFirebaseReady()
    {
        if (FirebaseManager.Instance.IsReady())
        {
            db = FirebaseManager.Instance.GetFirestore();
            StartScoreboardListener();
            CancelInvoke(nameof(CheckFirebaseReady));
        }
    }
    
    /// <summary>
    /// Register a new team directly in Firebase
    /// </summary>
    public async void RegisterTeam(string teamName, string player1, string player2, string email, string phoneNumber)
    {
        if (db == null)
        {
            OnRegistrationFailed.Invoke("Firebase not ready");
            return;
        }
        
        try {
            // Check if team name is taken
            var teamsRef = db.Collection("teams");
            var query = teamsRef.WhereEqualTo("teamName", teamName);
            var snapshot = await query.GetSnapshotAsync();
            
            if (snapshot.Count > 0)
            {
                OnRegistrationFailed.Invoke($"Team name '{teamName}' is already taken");
                return;
            }
            
            // Get next team number
            var allTeamsSnapshot = await teamsRef.GetSnapshotAsync();
            int teamNumber = allTeamsSnapshot.Count + 1;
            
            // Create team data
            var teamData = new TeamData(teamName, player1, player2, email, phoneNumber)
            {
                TeamNumber = teamNumber
            };
            
            // Save to Firebase using UID as document ID
            var teamDocRef = teamsRef.Document(teamData.UID);
            await teamDocRef.SetAsync(teamData);
            
            // Save locally
            currentTeamUID = teamData.UID;
            PlayerPrefs.SetString("TeamUID", currentTeamUID);
            PlayerPrefs.SetString("TeamName", teamName);
            PlayerPrefs.Save();
            
            Debug.Log($"Team registered successfully! UID: {teamData.UID}");
            OnTeamRegistered.Invoke(teamData);
            
        } catch (System.Exception e) {
            Debug.LogError($"Registration failed: {e.Message}");
            OnRegistrationFailed.Invoke(e.Message);
        }
    }
    
    /// <summary>
    /// Update team score directly in Firebase
    /// </summary>
    public async void UpdateScore(int scoreIncrement)
    {
        if (string.IsNullOrEmpty(currentTeamUID))
        {
            currentTeamUID = PlayerPrefs.GetString("TeamUID", "");
            if (string.IsNullOrEmpty(currentTeamUID))
            {
                OnScoreUpdateFailed.Invoke("No team UID found. Please register first.");
                return;
            }
        }
        
        if (db == null)
        {
            OnScoreUpdateFailed.Invoke("Firebase not ready");
            return;
        }
        
        try {
            var teamDocRef = db.Collection("teams").Document(currentTeamUID);
            
            // Use Firebase transaction for atomic score update
            await db.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(teamDocRef);
                if (!snapshot.Exists)
                {
                    throw new System.Exception("Team not found");
                }
                
                var currentScore = snapshot.GetValue<int>("score");
                var newScore = currentScore + scoreIncrement;
                
                transaction.Update(teamDocRef, "score", newScore);
                return newScore;
            });
            
            Debug.Log($"Score updated successfully! Added {scoreIncrement} points.");
            OnScoreUpdated.Invoke(scoreIncrement);
            
        } catch (System.Exception e) {
            Debug.LogError($"Score update failed: {e.Message}");
            OnScoreUpdateFailed.Invoke(e.Message);
        }
    }
    
    /// <summary>
    /// Start listening to scoreboard changes
    /// </summary>
    private void StartScoreboardListener()
    {
        var teamsRef = db.Collection("teams");
        var query = teamsRef.OrderByDescending("score").OrderBy("teamNumber");
        
        scoreboardListener = query.Listen(snapshot =>
        {
            var teams = new List<TeamData>();
            foreach (var doc in snapshot.Documents)
            {
                if (doc.Exists)
                {
                    var team = doc.ConvertTo<TeamData>();
                    teams.Add(team);
                }
            }
            
            Debug.Log($"Scoreboard updated: {teams.Count} teams");
            OnScoreboardUpdated.Invoke(teams);
        });
    }
    
    /// <summary>
    /// Get current team data
    /// </summary>
    public async void GetCurrentTeamData()
    {
        if (string.IsNullOrEmpty(currentTeamUID))
            currentTeamUID = PlayerPrefs.GetString("TeamUID", "");
            
        if (string.IsNullOrEmpty(currentTeamUID) || db == null)
            return;
            
        try {
            var teamDoc = await db.Collection("teams").Document(currentTeamUID).GetSnapshotAsync();
            if (teamDoc.Exists)
            {
                var teamData = teamDoc.ConvertTo<TeamData>();
                Debug.Log($"Current team: {teamData.TeamName}, Score: {teamData.Score}");
            }
        } catch (System.Exception e) {
            Debug.LogError($"Failed to get team data: {e.Message}");
        }
    }
    
    private void OnDestroy()
    {
        // Clean up listener
        scoreboardListener?.Stop();
    }
}
```

## 📱 Firebase Project Configuration

Your Firebase project `arth-33ed6` needs these settings:

### Authentication
- ✅ Anonymous authentication (already enabled)

### Firestore Database
- ✅ Rules allow read/write (already configured)

### Firestore Structure
```
teams/
├── {uid1}/
│   ├── teamNumber: 1
│   ├── teamName: "DragonHunters" 
│   ├── uid: "qK234"
│   ├── player1: "John"
│   ├── player2: "Jane"
│   ├── email: "team@example.com"
│   ├── phoneNumber: "+1234567890"
│   ├── score: 1500
│   └── createdAt: timestamp
└── {uid2}/
    └── ...
```

## 🎮 Usage Examples

### Register Team (UI Integration)
```csharp
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
    
    private TournamentManager tournament;
    
    private void Start()
    {
        tournament = FindObjectOfType<TournamentManager>();
        registerButton.onClick.AddListener(OnRegisterClicked);
        
        // Subscribe to events
        tournament.OnTeamRegistered.AddListener(OnTeamRegistered);
        tournament.OnRegistrationFailed.AddListener(OnRegistrationFailed);
    }
    
    private void OnRegisterClicked()
    {
        statusText.text = "Registering...";
        registerButton.interactable = false;
        
        tournament.RegisterTeam(
            teamNameInput.text.Trim(),
            player1Input.text.Trim(),
            player2Input.text.Trim(),
            emailInput.text.Trim(),
            phoneInput.text.Trim()
        );
    }
    
    private void OnTeamRegistered(TeamData team)
    {
        statusText.text = $"Registered as Team #{team.TeamNumber}";
        statusText.color = Color.green;
    }
    
    private void OnRegistrationFailed(string error)
    {
        statusText.text = $"Error: {error}";
        statusText.color = Color.red;
        registerButton.interactable = true;
    }
}
```

### Add Score During Gameplay
```csharp
public class GameManager : MonoBehaviour
{
    private TournamentManager tournament;
    
    private void Start()
    {
        tournament = FindObjectOfType<TournamentManager>();
    }
    
    // Call this when player earns points
    public void OnPlayerScoredPoints(int points)
    {
        tournament.UpdateScore(points);
    }
    
    // Example: Player defeats enemy
    public void OnEnemyDefeated()
    {
        tournament.UpdateScore(100);
    }
    
    // Example: Player completes level
    public void OnLevelComplete()
    {
        tournament.UpdateScore(500);
    }
}
```

### Display Live Scoreboard
```csharp
public class ScoreboardUI : MonoBehaviour
{
    [Header("UI Elements")]
    public Transform scoreboardParent;
    public GameObject teamRowPrefab;
    
    private TournamentManager tournament;
    
    private void Start()
    {
        tournament = FindObjectOfType<TournamentManager>();
        tournament.OnScoreboardUpdated.AddListener(UpdateScoreboardDisplay);
    }
    
    private void UpdateScoreboardDisplay(List<TeamData> teams)
    {
        // Clear existing rows
        foreach (Transform child in scoreboardParent)
        {
            Destroy(child.gameObject);
        }
        
        // Create new rows
        for (int i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            var row = Instantiate(teamRowPrefab, scoreboardParent);
            
            // Assuming the prefab has these components
            row.transform.Find("RankText").GetComponent<Text>().text = $"#{i + 1}";
            row.transform.Find("TeamText").GetComponent<Text>().text = team.TeamName;
            row.transform.Find("ScoreText").GetComponent<Text>().text = team.Score.ToString();
            row.transform.Find("PlayersText").GetComponent<Text>().text = $"{team.Player1} & {team.Player2}";
        }
    }
}
```

## 🚀 Benefits of Direct Firebase Integration

1. **No Vercel Dependency**: Removes web server as potential failure point
2. **Real-time Updates**: Instant scoreboard updates via Firebase listeners
3. **Better Performance**: Fewer network hops, direct connection
4. **Offline Support**: Firebase SDK handles offline scenarios
5. **Simplified Architecture**: One less layer to debug

## 📦 Files Your Unity Developer Needs

1. `FirebaseManager.cs` - Core Firebase initialization
2. `TeamData.cs` - Data model matching Firestore structure
3. `TournamentManager.cs` - Main tournament operations
4. `google-services.json` - Firebase project configuration
5. Firebase Unity SDK packages

The web scoreboard will continue to work as normal since both Unity and the web app read from the same Firebase database!