using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using Niantic.Lightship.AR.WorldPositioning;

[System.Serializable]
public class TreasureLocation
{
    public string clueText;
    public double latitude;
    public double longitude;

    [Header("Physical Game")]
    public bool hasPhysicalGame = false;
    public string physicalGameInstruction = "";
    [Tooltip("Secret code that volunteers use to verify physical game completion")]
    public string physicalGameSecretCode = "";

    public TreasureLocation(string clue, double lat, double lng)
    {
        clueText = clue;
        latitude = lat;
        longitude = lng;
    }
}


public class TreasureHuntManager : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject registrationPanel;
    public GameObject timerPanel;
    public GameObject cluePanel;

    [Header("Registration UI")]
    public TMP_InputField uidInput;
    public Button fetchTeamDataButton;

    [Header("Team Verification UI")]
    public GameObject teamVerificationPanel;
    public TMP_Text teamDetailsText;
    public TMP_Text teamMembersText;
    public TMP_Text teamNumberDisplayText;
    public Button verifyTeamButton;

    [Header("Timer UI")]
    public TMP_Text timerDisplay;
    public TMP_Text teamInfoText;
    public TMP_Text waitingMessage;

    [Header("Clue UI")]
    public TMP_Text clueDisplayText;
    public Button startHuntButton;
    public TMP_Text distanceDebugText;

    [Header("GPS & AR Components")]
    public ARCameraManager arCameraManager;
    public GameObject arScanPanel;
    public ClueARImageManager clueARImageManager;
    public ARWorldPositioningManager wpsManager;

    [Header("GPS Settings")]
    public float proximityThreshold = 10f; // meters to trigger AR mode

    [Header("Audio")]
    public AudioSource timerCompleteSound;

    [Header("Treasure Hunt Data")]
    public TreasureLocation[] treasureLocations;

    [Header("Mobile Debug")]
    public TMP_Text mobileDebugText;

    [Header("Treasure Collection UI")]
    public Button collectTreasureButton;
    public GameObject congratsPanel;
    public TMP_Text congratsMessage;
    public TMP_Text physicalGameText;
    public Button nextTreasureButton;
    
    [Header("Physical Game Verification")]
    [Tooltip("Input field where volunteers enter the secret verification code")]
    public TMP_InputField physicalGameCodeInput;
    [Tooltip("Button to verify the physical game code")]
    public Button verifyPhysicalGameButton;
    [Tooltip("Text to display verification status (success/error messages)")]
    public TMP_Text physicalGameStatusText;

    [Header("Inventory System")]
    public Button inventoryButton;
    public InventoryManager inventoryManager;

    [Header("Firebase Integration")]
    public FirebaseFetcher firebaseFetcher;
    [Header("Scoring")]
    [Tooltip("Points awarded per collected AR treasure")]
    public int scorePerTreasure = 100;
    [Tooltip("Optional RTDB fetcher to update main score when treasures are collected")]
    public FirebaseRTDBFetcher rtdbFetcher;

    // Team assignment variables
    private int teamNumber;
    private int wave;
    private int clueIndex;
    private int delayMinutes;
    private DateTime startTime;
    private bool isTimerActive = false;
    private int totalClues;

    // Firebase team data
    private TeamData currentTeamData;
    private bool isTeamVerified = false;

    // GPS tracking variables
    private ARWorldPositioningCameraHelper cameraHelper;
    private bool isGPSTrackingActive = false;
    private bool isNearTreasure = false;

    // Physical game tracking
    private bool isPhysicalGameCompleted = false;

    private int cluesFound = 0;

    public int GetRemainingClues()
    {
        return totalClues - cluesFound;
    }
    void Start()
    {
        // Initialize total clues count
        totalClues = treasureLocations.Length;

        // Validate we have clues
        if (totalClues == 0)
        {
            Debug.LogError("No treasure locations defined! Please add clues to the array.");
            if (mobileDebugText != null) mobileDebugText.text = "ERROR: No treasure locations defined!";
            return;
        }

        // Initialize Lightship GPS helper
        if (arCameraManager != null)
        {
            cameraHelper = arCameraManager.GetComponent<ARWorldPositioningCameraHelper>();
            if (cameraHelper == null)
            {
                Debug.LogError("ARWorldPositioningCameraHelper not found on ARCameraManager!");
                if (mobileDebugText != null) mobileDebugText.text = "ERROR: ARWorldPositioningCameraHelper not found!";
            }
        }
        else
        {
            Debug.LogError("ARCameraManager not assigned!");
            if (mobileDebugText != null) mobileDebugText.text = "ERROR: ARCameraManager not assigned!";
        }

        // Initialize UI state
        ShowRegistrationPanel();
        
        // Initialize physical game UI (hide at startup)
        InitializePhysicalGameUI();

        // Setup button listeners
        fetchTeamDataButton.onClick.AddListener(OnFetchTeamData);
        verifyTeamButton.onClick.AddListener(OnVerifyTeam);
        startHuntButton.onClick.AddListener(OnStartHunt);
        nextTreasureButton.onClick.AddListener(OnNextTreasure);

        if (inventoryButton != null)
            inventoryButton.onClick.AddListener(OnInventoryButtonClicked);
            
        // Only add listener if the physical game verification button exists
        if (verifyPhysicalGameButton != null)
            verifyPhysicalGameButton.onClick.AddListener(OnVerifyPhysicalGameCode);

        // Setup Firebase event listeners
        FirebaseFetcher.OnTeamDataFetched += OnTeamDataReceived;
        FirebaseFetcher.OnTeamDataFetchFailed += OnTeamDataFetchFailed;

        // Get or create FirebaseFetcher instance
        if (firebaseFetcher == null)
            firebaseFetcher = FirebaseFetcher.Instance;

        // Try auto-find RTDB fetcher if not assigned
        if (rtdbFetcher == null)
            rtdbFetcher = FindObjectOfType<FirebaseRTDBFetcher>();


        Debug.Log($"Treasure hunt initialized with {totalClues} clues");
        if (mobileDebugText != null) mobileDebugText.text = $"Treasure hunt initialized with {totalClues} clues";
    }

    void Update()
    {
        // Update timer if active
        if (isTimerActive)
        {
            UpdateTimer();
        }

        // Check GPS proximity if tracking is active
        if (isGPSTrackingActive && cameraHelper != null)
        {
            CheckProximityToTreasure();
        }
    }

    public void OnFetchTeamData()
    {
        try
        {
            string uid = uidInput?.text?.Trim() ?? "";

            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogWarning("UID cannot be empty");
                if (mobileDebugText != null) mobileDebugText.text = "WARNING: UID cannot be empty";
                return;
            }

            // Check if Firebase is ready
            if (firebaseFetcher == null)
            {
                Debug.LogError("FirebaseFetcher not assigned");
                if (mobileDebugText != null) mobileDebugText.text = "ERROR: Firebase not configured";
                return;
            }

            if (!firebaseFetcher.IsFirebaseReady())
            {
                Debug.LogWarning("Firebase not ready yet");
                if (mobileDebugText != null) mobileDebugText.text = "Firebase not ready - please wait";
                return;
            }

            Debug.Log($"Fetching team data for UID: {uid}");
            if (mobileDebugText != null) mobileDebugText.text = $"Fetching team data for UID: {uid}";

            // Disable button to prevent multiple requests
            if (fetchTeamDataButton != null)
            {
                fetchTeamDataButton.interactable = false;
                var buttonText = fetchTeamDataButton.GetComponentInChildren<TMP_Text>();
                if (buttonText != null) buttonText.text = "Fetching...";
            }

            // Use FirebaseFetcher to get team data
            firebaseFetcher.FetchTeamData(uid);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in OnFetchTeamData: {e.Message}");
            if (mobileDebugText != null) mobileDebugText.text = $"ERROR: {e.Message}";

            // Reset button state on error
            if (fetchTeamDataButton != null)
            {
                fetchTeamDataButton.interactable = true;
                var buttonText = fetchTeamDataButton.GetComponentInChildren<TMP_Text>();
                if (buttonText != null) buttonText.text = "Fetch Team Data";
            }
        }
    }

    private void OnTeamDataReceived(TeamData teamData)
    {
        try
        {
            if (teamData == null)
            {
                Debug.LogError("Received null team data");
                if (mobileDebugText != null) mobileDebugText.text = "ERROR: Received invalid team data";
                return;
            }

            Debug.Log($"Team data received: {teamData.teamName} (#{teamData.teamNumber})");

            currentTeamData = teamData;

            // Reset button state
            if (fetchTeamDataButton != null)
            {
                fetchTeamDataButton.interactable = true;
                var buttonText = fetchTeamDataButton.GetComponentInChildren<TMP_Text>();
                if (buttonText != null) buttonText.text = "Fetch Team Data";
            }

            // Show team verification panel
            ShowTeamVerificationPanel();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in OnTeamDataReceived: {e.Message}");
            if (mobileDebugText != null) mobileDebugText.text = $"ERROR processing team data: {e.Message}";
        }
    }

    // Allow external data providers (e.g., RTDB) to inject team data
    public void ReceiveExternalTeamData(TeamData teamData)
    {
        try
        {
            if (teamData == null)
            {
                Debug.LogError("Received null team data (external)");
                if (mobileDebugText != null) mobileDebugText.text = "ERROR: Received invalid team data";
                return;
            }

            currentTeamData = teamData;

            // Reset fetch button state if present
            if (fetchTeamDataButton != null)
            {
                fetchTeamDataButton.interactable = true;
                var buttonText = fetchTeamDataButton.GetComponentInChildren<TMP_Text>();
                if (buttonText != null) buttonText.text = "Fetch Team Data";
            }

            // Show team verification panel using existing UI
            ShowTeamVerificationPanel();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in ReceiveExternalTeamData: {e.Message}");
            if (mobileDebugText != null) mobileDebugText.text = $"ERROR processing team data: {e.Message}";
        }
    }

    private void OnTeamDataFetchFailed(string error)
    {
        try
        {
            Debug.LogError($"Failed to fetch team data: {error}");
            if (mobileDebugText != null) mobileDebugText.text = $"ERROR: {error}";

            // Reset button state
            if (fetchTeamDataButton != null)
            {
                fetchTeamDataButton.interactable = true;
                var buttonText = fetchTeamDataButton.GetComponentInChildren<TMP_Text>();
                if (buttonText != null) buttonText.text = "Fetch Team Data";
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in OnTeamDataFetchFailed: {e.Message}");
        }
    }

    public void OnVerifyTeam()
    {
        if (currentTeamData == null)
        {
            Debug.LogWarning("No team data to verify");
            return;
        }

        // Set the team number from Firebase data
        teamNumber = currentTeamData.teamNumber;
        isTeamVerified = true;

        Debug.Log($"Team verified: {currentTeamData.teamName} (#{teamNumber})");
        if (mobileDebugText != null) mobileDebugText.text = $"Team verified: {currentTeamData.teamName} (#{teamNumber})";

        // Now calculate team assignment using Firebase team number
        CalculateTeamAssignment();

        if (delayMinutes > 0)
        {
            ShowTimerPanel();
            StartTimer();
        }
        else
        {
            // No delay - go straight to clue
            ShowCluePanel();
        }
    }

    private void CalculateTeamAssignment()
    {
        // Calculate wave, clue index, and delay using dynamic total clues count
        wave = (teamNumber - 1) / totalClues;
        clueIndex = (teamNumber - 1) % totalClues; // 0-based index for array access
        delayMinutes = wave * 5;

        string teamInfo = currentTeamData != null ? $"{currentTeamData.teamName} (#{teamNumber})" : $"Team {teamNumber}";
        Debug.Log($"{teamInfo}: Wave {wave}, Clue Index {clueIndex + 1}, Delay {delayMinutes} minutes");
        if (mobileDebugText != null) mobileDebugText.text = $"{teamInfo}: Wave {wave}, Clue {clueIndex + 1}, Delay {delayMinutes}min";
    }

    private void StartTimer()
    {
        // Calculate when the team should start (current time + delay)
        startTime = DateTime.Now.AddMinutes(delayMinutes);
        isTimerActive = true;

        // Update team info display
        string teamDisplayName = currentTeamData != null ? $"{currentTeamData.teamName} (#{teamNumber})" : $"Team {teamNumber}";
        teamInfoText.text = $"{teamDisplayName}, Wave {wave + 1}";
        waitingMessage.text = "Waiting for your turn...";

        Debug.Log($"Timer started. Team will begin at: {startTime:HH:mm:ss}");
        if (mobileDebugText != null) mobileDebugText.text = $"Timer started. Begin at: {startTime:HH:mm:ss}";
    }

    private void UpdateTimer()
    {
        DateTime currentTime = DateTime.Now;
        TimeSpan timeRemaining = startTime - currentTime;

        if (timeRemaining.TotalSeconds <= 0)
        {
            // Timer complete
            OnTimerComplete();
        }
        else
        {
            // Update display with MM:SS format
            int minutes = (int)timeRemaining.TotalMinutes;
            int seconds = timeRemaining.Seconds;
            timerDisplay.text = $"{minutes:D2}:{seconds:D2}";
        }
    }

    private void OnTimerComplete()
    {
        isTimerActive = false;

        // Play completion sound
        if (timerCompleteSound != null)
        {
            timerCompleteSound.Play();
        }

        // Show clue panel
        ShowCluePanel();

        Debug.Log("Timer complete! Starting treasure hunt.");
        if (mobileDebugText != null) mobileDebugText.text = "Timer complete! Starting treasure hunt.";
    }

    private void ShowRegistrationPanel()
    {
        registrationPanel.SetActive(true);
        timerPanel.SetActive(false);
        cluePanel.SetActive(false);
        if (arScanPanel != null) arScanPanel.SetActive(false);
        if (teamVerificationPanel != null) teamVerificationPanel.SetActive(false);

        // Hide inventory button during registration
        if (inventoryButton != null) inventoryButton.gameObject.SetActive(false);

        // Reset verification state
        isTeamVerified = false;
        currentTeamData = null;
    }

    private void ShowTeamVerificationPanel()
    {
        if (currentTeamData == null) return;

        registrationPanel.SetActive(false);
        teamVerificationPanel.SetActive(true);
        timerPanel.SetActive(false);
        cluePanel.SetActive(false);
        if (arScanPanel != null) arScanPanel.SetActive(false);

        // Display team information
        if (teamDetailsText != null)
            teamDetailsText.text = $"Team: {currentTeamData.teamName}\nEmail: {currentTeamData.email}";

        if (teamMembersText != null)
            teamMembersText.text = $"Player 1: {currentTeamData.player1}\nPlayer 2: {currentTeamData.player2}";

        if (teamNumberDisplayText != null)
            teamNumberDisplayText.text = $"Team Number: {currentTeamData.teamNumber}";
    }

    private void ShowTimerPanel()
    {
        registrationPanel.SetActive(false);
        if (teamVerificationPanel != null) teamVerificationPanel.SetActive(false);
        timerPanel.SetActive(true);
        cluePanel.SetActive(false);
        if (arScanPanel != null) arScanPanel.SetActive(false);

        // Hide inventory button during timer
        if (inventoryButton != null) inventoryButton.gameObject.SetActive(false);
    }

    private void ShowCluePanel()
    {
        registrationPanel.SetActive(false);
        if (teamVerificationPanel != null) teamVerificationPanel.SetActive(false);
        timerPanel.SetActive(false);
        cluePanel.SetActive(true);
        if (arScanPanel != null) arScanPanel.SetActive(false);

        // Reset GPS tracking state for new clue
        isNearTreasure = false;
        startHuntButton.interactable = true;

        // Display the clue for this team
        DisplayCurrentClue();
    }

    private void DisplayCurrentClue()
    {
        if (clueIndex >= 0 && clueIndex < treasureLocations.Length)
        {
            clueDisplayText.text = treasureLocations[clueIndex].clueText;
            Debug.Log($"Displaying clue {clueIndex + 1}: {treasureLocations[clueIndex].clueText}");
            if (mobileDebugText != null) mobileDebugText.text = $"Displaying clue {clueIndex + 1}: {treasureLocations[clueIndex].clueText}";
        }
        else
        {
            Debug.LogError($"Invalid clue index: {clueIndex}");
            if (mobileDebugText != null) mobileDebugText.text = $"ERROR: Invalid clue index: {clueIndex}";
            clueDisplayText.text = "Error: Clue not found";
        }
    }

    public void OnStartHunt()
    {
        Debug.Log($"Team {teamNumber} started hunting for treasure at location: {GetCurrentTreasureLocation().latitude}, {GetCurrentTreasureLocation().longitude}");
        if (mobileDebugText != null) mobileDebugText.text = $"Team {teamNumber} started hunting - GPS tracking enabled";

        // Hide the start hunt button completely
        startHuntButton.gameObject.SetActive(false);

        // Show inventory button once hunt starts
        if (inventoryButton != null) inventoryButton.gameObject.SetActive(true);

        // Enable GPS tracking
        EnableGPSTracking();
    }

    private void EnableGPSTracking()
    {
        if (cameraHelper != null)
        {
            isGPSTrackingActive = true;
            Debug.Log("GPS tracking enabled. Looking for treasure location...");
            if (mobileDebugText != null) mobileDebugText.text = "GPS tracking enabled. Looking for treasure location...";
        }
        else
        {
            Debug.LogError("Cannot enable GPS tracking - ARWorldPositioningCameraHelper not available");
            if (mobileDebugText != null) mobileDebugText.text = "ERROR: Cannot enable GPS tracking - ARWorldPositioningCameraHelper not available";
        }
    }

    private void CheckProximityToTreasure()
    {
        TreasureLocation currentTreasure = GetCurrentTreasureLocation();
        if (currentTreasure == null) return;

        // Temporarily disabled WPS check - calculate distance directly
        // if (wpsManager == null || wpsManager.Status.ToString() != "Available")
        // {
        //     string statusMessage = GetWPSStatusMessage();
        //     if (distanceDebugText != null)
        //     {
        //         distanceDebugText.text = statusMessage;
        //     }
        //     return;
        // }

        // Get current device location from Lightship
        float deviceLatitude = (float)cameraHelper.Latitude;
        float deviceLongitude = (float)cameraHelper.Longitude;

        // Calculate distance to treasure
        double distance = CalculateDistanceInMeters(
        cameraHelper.Latitude, cameraHelper.Longitude,
        currentTreasure.latitude, currentTreasure.longitude
        );

        if (distanceDebugText != null)
        {
            distanceDebugText.text = $"Distance to treasure: {distance:F1}m";
        }
        else
        {
            Debug.Log($"Distance to treasure: {distance:F1}m");
            if (mobileDebugText != null) mobileDebugText.text = $"Distance to treasure: {distance:F1}m";
        }

        // Check if within proximity threshold
        if (distance <= proximityThreshold)
        {
            if (!isNearTreasure)
            {
                OnArrivedAtTreasure();
            }
        }
        else
        {
            if (isNearTreasure)
            {
                OnLeftTreasureArea();
            }
        }
    }

    private string GetWPSStatusMessage()
    {
        if (wpsManager == null)
        {
            return "GPS system not configured...";
        }

        string status = wpsManager.Status.ToString();

        switch (status)
        {
            case "Initializing":
                return "Speaking to satellites...";
            case "Localizing":
                return "Data travelling across the atmosphere...";
            case "Limited":
                return "Weak GPS signal, getting better location...";
            case "Failed":
                return "GPS connection failed, retrying...";
            default:
                return $"Connecting to GPS... ({status})";
        }
    }

    private void OnArrivedAtTreasure()
    {
        isNearTreasure = true;

        Debug.Log("Arrived at treasure location! Switching to AR mode.");
        if (mobileDebugText != null) mobileDebugText.text = "ARRIVED AT TREASURE! Switching to AR mode.";

        // Hide clue panel and show AR scan panel
        ShowARScanPanel();
    }

    private void OnLeftTreasureArea()
    {
        isNearTreasure = false;

        Debug.Log("Left treasure area. Returning to clue mode.");
        if (mobileDebugText != null) mobileDebugText.text = "Left treasure area. Returning to clue mode.";

        // Return to clue panel and hide AR scan panel
        ShowCluePanelFromAR();
    }

    private void ShowCluePanelFromAR()
    {
        registrationPanel.SetActive(false);
        timerPanel.SetActive(false);
        cluePanel.SetActive(true);
        if (arScanPanel != null) arScanPanel.SetActive(false);

        // Disable AR image tracking when leaving AR mode
        if (clueARImageManager != null)
        {
            clueARImageManager.DisableARTracking();
        }

        // Keep GPS tracking active but don't reset states since hunt is ongoing
        startHuntButton.interactable = false; // Keep disabled since hunt is ongoing

        // Show inventory button when returning from AR mode
        if (inventoryButton != null) inventoryButton.gameObject.SetActive(true);

        // Display the clue for this team
        DisplayCurrentClue();
    }

    private void ShowARScanPanel()
    {
        cluePanel.SetActive(false);
        if (arScanPanel != null)
        {
            arScanPanel.SetActive(true);
        }

        // Enable AR camera for scanning
        if (arCameraManager != null)
        {
            arCameraManager.enabled = true;
        }

        // Set the active clue for AR image tracking
        if (clueARImageManager != null)
        {
            clueARImageManager.SetActiveClue(clueIndex);
            clueARImageManager.EnableARTracking();
        }

        // Hide inventory button when collect treasure button is active
        if (inventoryButton != null) inventoryButton.gameObject.SetActive(false);

        Debug.Log("AR scan mode activated. Look for the treasure marker!");
        if (mobileDebugText != null) mobileDebugText.text = "AR SCAN MODE ACTIVATED! Look for the treasure marker!";
    }

    // Haversine formula to calculate distance between two GPS coordinates
    private double CalculateDistanceInMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0; // Earth's radius in meters

        double dLat = (lat2 - lat1) * (Math.PI / 180.0);
        double dLon = (lon2 - lon1) * (Math.PI / 180.0);

        double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                   Math.Cos(lat1 * (Math.PI / 180.0)) * Math.Cos(lat2 * (Math.PI / 180.0)) *
                   Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);

        double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));

        return R * c;
    }


    // Public getters for other systems to access team data
    public int GetTeamNumber() => teamNumber;
    public int GetWave() => wave;
    public int GetClueIndex() => clueIndex + 1; // Return 1-based for display
    public bool IsTimerActive() => isTimerActive;
    public int GetTotalClues() => totalClues;
    public bool IsGPSTrackingActive() => isGPSTrackingActive;
    public bool IsNearTreasure() => isNearTreasure;

    public TreasureLocation GetCurrentTreasureLocation()
    {
        if (clueIndex >= 0 && clueIndex < treasureLocations.Length)
        {
            return treasureLocations[clueIndex];
        }
        return null;
    }

    public string GetCurrentClueText()
    {
        var location = GetCurrentTreasureLocation();
        return location?.clueText ?? "No clue available";
    }

    // Get current device GPS coordinates (useful for debugging)
    public Vector2 GetDeviceCoordinates()
    {
        if (cameraHelper != null)
        {
            return new Vector2((float)cameraHelper.Latitude, (float)cameraHelper.Longitude);
        }
        return Vector2.zero;
    }


    public void OnCollectTreasure(GameObject treasureObject)
    {
        StartCoroutine(ShrinkAndCollectTreasure(treasureObject));
    }

    private IEnumerator ShrinkAndCollectTreasure(GameObject treasureObject)
    {
        Vector3 startScale = treasureObject.transform.localScale;
        float duration = 0.5f;
        float time = 0;

        while (time < duration)
        {
            time += Time.deltaTime;
            float scaleFactor = Mathf.Lerp(1f, 0f, time / duration);
            treasureObject.transform.localScale = startScale * scaleFactor;
            yield return null;
        }

        treasureObject.SetActive(false);
        // Don't reset scale - keep it shrunk to prevent reuse

        // Mark treasure as collected in AR manager to prevent respawning
        if (clueARImageManager != null)
        {
            clueARImageManager.MarkTreasureAsCollected(clueIndex);
        }

        // Add treasure to inventory
        if (inventoryManager != null)
        {
            inventoryManager.AddCollectedTreasure(clueIndex);
        }

        cluesFound++;

        // Add main score on collection
        if (rtdbFetcher != null && scorePerTreasure > 0)
        {
            rtdbFetcher.AddScore(scorePerTreasure);
        }

        // Show congrats panel
        congratsPanel.SetActive(true);

        int remaining = GetRemainingClues();
        TreasureLocation currentTreasure = GetCurrentTreasureLocation();

        if (remaining <= 0)
        {
            // Final clue found → show completion message
            congratsMessage.text = "🎉 Congrats on completing the Treasure Hunt!";
            nextTreasureButton.gameObject.SetActive(false); // Hide next button

            // Hide physical game elements for final completion
            if (physicalGameText != null)
                physicalGameText.gameObject.SetActive(false);
            HidePhysicalGameVerificationUI();
        }
        else
        {
            // Normal treasure found → show remaining count
            congratsMessage.text = $"Congrats on finding the treasure!\n" +
                           $"{remaining} clue{(remaining > 1 ? "s" : "")} remaining.";

            // Handle physical game logic
            if (currentTreasure != null && currentTreasure.hasPhysicalGame)
            {
                // Reset physical game completion status for new clue
                isPhysicalGameCompleted = false;
                
                // Show physical game instructions
                if (physicalGameText != null)
                {
                    physicalGameText.gameObject.SetActive(true);
                    physicalGameText.text = currentTreasure.physicalGameInstruction;
                }
                
                // Show verification UI and hide Next button initially
                ShowPhysicalGameVerificationUI();
                if (nextTreasureButton != null)
                    nextTreasureButton.gameObject.SetActive(false);
                
                // Clear any previous status messages
                if (physicalGameStatusText != null)
                    physicalGameStatusText.text = "Complete the physical game and get the verification code from the volunteer.";
            }
            else
            {
                // No physical game - show Next button immediately
                nextTreasureButton.gameObject.SetActive(true);
                
                if (physicalGameText != null)
                    physicalGameText.gameObject.SetActive(false);
                    
                HidePhysicalGameVerificationUI();
            }
        }
    }


    public void OnNextTreasure()
    {
        congratsPanel.SetActive(false);  // Hide congrats panel
        
        // Reset physical game state for next clue
        isPhysicalGameCompleted = false;
        HidePhysicalGameVerificationUI();

        // Move to next clue only if clues remain
        clueIndex = (clueIndex + 1) % totalClues;

        ShowCluePanel();
        startHuntButton.gameObject.SetActive(true);

        // Show inventory button when moving to next treasure
        if (inventoryButton != null) inventoryButton.gameObject.SetActive(true);

        if (collectTreasureButton != null)
            collectTreasureButton.gameObject.SetActive(false);
    }

    public void OnInventoryButtonClicked()
    {
        if (inventoryManager != null)
        {
            inventoryManager.OpenInventory();
        }
        else
        {
            Debug.LogWarning("InventoryManager not assigned to TreasureHuntManager!");
        }
    }
    
    public void OnVerifyPhysicalGameCode()
    {
        if (physicalGameCodeInput == null)
        {
            Debug.LogWarning("Physical game code input field not assigned!");
            return;
        }
        
        string enteredCode = physicalGameCodeInput.text?.Trim() ?? "";
        TreasureLocation currentTreasure = GetCurrentTreasureLocation();
        
        if (currentTreasure == null || !currentTreasure.hasPhysicalGame)
        {
            if (physicalGameStatusText != null)
                physicalGameStatusText.text = "No physical game active for this clue.";
            return;
        }
        
        if (string.IsNullOrEmpty(enteredCode))
        {
            if (physicalGameStatusText != null)
                physicalGameStatusText.text = "Please enter the verification code.";
            return;
        }
        
        // Check if the entered code matches the secret code (case-insensitive)
        if (string.Equals(enteredCode, currentTreasure.physicalGameSecretCode, StringComparison.OrdinalIgnoreCase))
        {
            // Code is correct - mark physical game as completed
            isPhysicalGameCompleted = true;
            
            if (physicalGameStatusText != null)
                physicalGameStatusText.text = "✓ Physical game verified! You can now proceed.";
                
            // Show the Next Treasure button
            if (nextTreasureButton != null)
                nextTreasureButton.gameObject.SetActive(true);
                
            // Hide the verification UI
            if (physicalGameCodeInput != null)
                physicalGameCodeInput.gameObject.SetActive(false);
            if (verifyPhysicalGameButton != null)
                verifyPhysicalGameButton.gameObject.SetActive(false);
                
            // Update RTDB with physical game completion if available (no score - handled by web interface)
            if (rtdbFetcher != null)
            {
                rtdbFetcher.SetPhysicalGameResult(true, 0); // Mark as played, no automatic score
            }
                
            Debug.Log($"Physical game verified for clue {clueIndex} with code: {enteredCode}");
            if (mobileDebugText != null) 
                mobileDebugText.text = $"Physical game verified! Code: {enteredCode}";
        }
        else
        {
            // Code is incorrect
            if (physicalGameStatusText != null)
                physicalGameStatusText.text = "❌ Incorrect code. Please check with the volunteer.";
                
            Debug.LogWarning($"Invalid physical game code entered: '{enteredCode}' (expected: '{currentTreasure.physicalGameSecretCode}')");
        }
        
        // Clear the input field for security
        physicalGameCodeInput.text = "";
    }
    
    private void ShowPhysicalGameVerificationUI()
    {
        try
        {
            if (physicalGameCodeInput != null)
            {
                physicalGameCodeInput.gameObject.SetActive(true);
                physicalGameCodeInput.text = ""; // Clear any previous input
            }
            
            if (verifyPhysicalGameButton != null)
                verifyPhysicalGameButton.gameObject.SetActive(true);
                
            if (physicalGameStatusText != null)
            {
                physicalGameStatusText.gameObject.SetActive(true);
                physicalGameStatusText.text = ""; // Clear any previous status
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Error showing physical game UI: {e.Message}");
            if (mobileDebugText != null)
                mobileDebugText.text = $"Physical game UI error: {e.Message}";
        }
    }
    
    private void HidePhysicalGameVerificationUI()
    {
        if (physicalGameCodeInput != null)
            physicalGameCodeInput.gameObject.SetActive(false);
            
        if (verifyPhysicalGameButton != null)
            verifyPhysicalGameButton.gameObject.SetActive(false);
            
        if (physicalGameStatusText != null)
            physicalGameStatusText.gameObject.SetActive(false);
    }
    
    private void InitializePhysicalGameUI()
    {
        // Hide all physical game verification UI elements at startup
        // This prevents any interference with camera initialization
        HidePhysicalGameVerificationUI();
        
        // Also ensure physical game completion state is reset
        isPhysicalGameCompleted = false;
        
        if (mobileDebugText != null)
            Debug.Log("Physical game UI initialized and hidden at startup");
    }

    void OnDestroy()
    {
        // Cleanup Firebase event listeners
        FirebaseFetcher.OnTeamDataFetched -= OnTeamDataReceived;
        FirebaseFetcher.OnTeamDataFetchFailed -= OnTeamDataFetchFailed;
    }


}