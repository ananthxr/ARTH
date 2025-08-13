using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;

[System.Serializable]
public class ClueARData
{
    [Header("Clue Information")]
    public int clueIndex; // 0-based index matching TreasureLocation array
    public string clueName; // For debugging/identification
    
    [Header("AR Image Tracking")]
    public string referenceImageName; // Name of the reference image in the library
    
    [Header("3D Object")]
    public GameObject treasurePrefab; // The 3D object to spawn when this image is detected
    
    [Header("Optional Settings")]
    public Vector3 spawnOffset = Vector3.zero; // Offset from the detected image position
    public Vector3 spawnRotation = Vector3.zero; // Additional rotation for the spawned object
}

public class ClueARImageManager : MonoBehaviour
{
    [Header("AR Components")]
    public ARTrackedImageManager trackedImageManager;
    public TreasureHuntManager treasureHuntManager;
    public ProgressManager progressManager;
    
    [Header("Clue AR Data")]
    public ClueARData[] clueARData;
    
    [Header("Settings")]
    public bool debugMode = true;
    
    [Header("Mobile Debug")]
    public TMP_Text mobileDebugText;
    
    // Private variables
    private Dictionary<string, ClueARData> imageNameToClueData;
    private Dictionary<int, ClueARData> clueIndexToData;
    private Dictionary<string, GameObject> arObjects; // Pre-instantiated objects
    private HashSet<int> collectedClueIndices = new HashSet<int>(); // Track collected treasures
    private int currentActiveClueIndex = -1;
    
    void Start()
    {
        InitializeClueDataMappings();
        
        if (trackedImageManager != null)
        {
            // Use the correct event handler
            trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
        }
        else
        {
            Debug.LogError("ARTrackedImageManager not assigned!");
            if (mobileDebugText != null) mobileDebugText.text = "ERROR: ARTrackedImageManager not assigned!";
        }
        
        if (treasureHuntManager == null)
        {
            Debug.LogError("TreasureHuntManager not assigned!");
            if (mobileDebugText != null) mobileDebugText.text = "ERROR: TreasureHuntManager not assigned!";
        }
        
        if (progressManager == null)
        {
            progressManager = FindObjectOfType<ProgressManager>();
        }
        
        // Pre-instantiate all AR objects
        SetupARObjects();
        
        // Load progress data if available (will be called after progress manager initializes)
        StartCoroutine(LoadProgressDataDelayed());
    }
    
    void Update()
    {
        // Monitor AR tracking status when enabled - but only update every 10 frames to avoid performance issues
        if (debugMode && trackedImageManager != null && trackedImageManager.enabled && mobileDebugText != null && Time.frameCount % 10 == 0)
        {
            int trackedCount = 0;
            string trackedImages = "";
            
            foreach (var trackedImage in trackedImageManager.trackables)
            {
                if (trackedImage.trackingState == TrackingState.Tracking)
                {
                    trackedCount++;
                    trackedImages += trackedImage.referenceImage.name + " ";
                }
            }
            
            // Only update the text if we're not currently showing step-by-step debugging
            if (trackedCount > 0)
            {
                if (!mobileDebugText.text.StartsWith("STEP"))
                {
                    mobileDebugText.text = $"TRACKING {trackedCount} images: {trackedImages}";
                }
            }
            else
            {
                if (!mobileDebugText.text.StartsWith("STEP"))
                {
                    mobileDebugText.text = $"AR scanning... Active clue: {currentActiveClueIndex}. Point camera at marker.";
                }
            }
        }
    }
    
    void OnDestroy()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
        }
    }
    
    private void InitializeClueDataMappings()
    {
        imageNameToClueData = new Dictionary<string, ClueARData>();
        clueIndexToData = new Dictionary<int, ClueARData>();
        
        foreach (var clueData in clueARData)
        {
            if (!string.IsNullOrEmpty(clueData.referenceImageName))
            {
                imageNameToClueData[clueData.referenceImageName] = clueData;
                clueIndexToData[clueData.clueIndex] = clueData;
                
                if (debugMode)
                {
                    Debug.Log($"Registered clue {clueData.clueIndex} ({clueData.clueName}) with image '{clueData.referenceImageName}'");
                    if (mobileDebugText != null) mobileDebugText.text = $"Registered clue {clueData.clueIndex} ({clueData.clueName}) with image '{clueData.referenceImageName}'";
                }
            }
        }
    }
    
    private void SetupARObjects()
    {
        arObjects = new Dictionary<string, GameObject>();
        
        foreach (var clueData in clueARData)
        {
            if (clueData.treasurePrefab != null && !string.IsNullOrEmpty(clueData.referenceImageName))
            {
                var arObject = Instantiate(clueData.treasurePrefab, Vector3.zero, Quaternion.identity);
                arObject.name = clueData.referenceImageName;
                arObject.SetActive(false);
                arObjects[clueData.referenceImageName] = arObject;
                
                if (debugMode)
                {
                    Debug.Log($"Pre-instantiated AR object for clue {clueData.clueIndex} ({clueData.referenceImageName})");
                    if (mobileDebugText != null) mobileDebugText.text = $"Pre-instantiated AR object for clue {clueData.clueIndex} ({clueData.referenceImageName})";
                }
            }
        }
    }
    
    public void SetActiveClue(int clueIndex)
    {
        currentActiveClueIndex = clueIndex;
        
        // Ensure we have latest progress data when setting active clue
        LoadProgressData();
        
        if (debugMode)
        {
            Debug.Log($"Active clue set to index {clueIndex}, {collectedClueIndices.Count} treasures already collected");
            if (mobileDebugText != null) mobileDebugText.text = $"Active clue set to index {clueIndex}, {collectedClueIndices.Count} collected";
        }
        
        // Clear any existing AR objects
        ClearAllARObjects();
    }
    
    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        // Handle newly detected images
        foreach (var trackedImage in eventArgs.added)
        {
            UpdateTrackedImage(trackedImage);
        }
        
        // Handle updated images
        foreach (var trackedImage in eventArgs.updated)
        {
            UpdateTrackedImage(trackedImage);
        }
        
        // Handle removed images
        foreach (var trackedImage in eventArgs.removed)
        {
            UpdateTrackedImage(trackedImage.Value);
        }
    }
    
    private void UpdateTrackedImage(ARTrackedImage trackedImage)
    {
        if (trackedImage == null) return;
        
        string imageName = trackedImage.referenceImage.name;
        
        if (debugMode)
        {
            Debug.Log($"Detected image: {imageName}, Tracking State: {trackedImage.trackingState}");
            if (mobileDebugText != null) mobileDebugText.text = $"STEP 1: Detected {imageName}, State: {trackedImage.trackingState}";
        }
        
        // Check if we have an AR object for this image
        if (!arObjects.ContainsKey(imageName))
        {
            if (debugMode)
            {
                Debug.LogWarning($"No AR object found for detected image: {imageName}");
                if (mobileDebugText != null) mobileDebugText.text = $"STEP 2 FAILED: No AR object found for: {imageName}";
            }
            return;
        }
        
        if (debugMode && mobileDebugText != null) mobileDebugText.text = $"STEP 2 PASSED: AR object exists for {imageName}";
        
        // Check if this image corresponds to a clue we have data for
        if (!imageNameToClueData.ContainsKey(imageName))
        {
            if (debugMode)
            {
                Debug.LogWarning($"No clue data found for detected image: {imageName}");
                if (mobileDebugText != null) mobileDebugText.text = $"STEP 3 FAILED: No clue data for: {imageName}";
            }
            return;
        }
        
        if (debugMode && mobileDebugText != null) mobileDebugText.text = $"STEP 3 PASSED: Clue data exists for {imageName}";
        
        ClueARData clueData = imageNameToClueData[imageName];
        GameObject arObject = arObjects[imageName];
        
        // Check if this treasure has already been collected
        if (collectedClueIndices.Contains(clueData.clueIndex))
        {
            if (debugMode)
            {
                Debug.Log($"Ignoring already collected treasure for clue {clueData.clueIndex} ({imageName})");
                if (mobileDebugText != null) mobileDebugText.text = $"STEP 4 FAILED: Clue {clueData.clueIndex} already collected";
            }
            arObject.SetActive(false);
            return;
        }
        
        // Check if this is the currently active clue
        if (clueData.clueIndex != currentActiveClueIndex)
        {
            if (debugMode)
            {
                Debug.Log($"Ignoring image '{imageName}' for clue {clueData.clueIndex} - current active clue is {currentActiveClueIndex}");
                if (mobileDebugText != null) mobileDebugText.text = $"STEP 4 FAILED: Need clue {currentActiveClueIndex}, got {clueData.clueIndex} for {imageName}";
            }
            arObject.SetActive(false);
            return;
        }
        
        if (debugMode && mobileDebugText != null) mobileDebugText.text = $"STEP 4 PASSED: Clue {clueData.clueIndex} matches active {currentActiveClueIndex}";
        
        // Handle the tracked image based on its tracking state
        if (trackedImage.trackingState == TrackingState.Tracking)
        {
            if (debugMode && mobileDebugText != null) mobileDebugText.text = $"STEP 5: Trying to spawn AR object for {imageName}...";
            
            // Position and activate the object
            arObject.SetActive(true);
            arObject.transform.SetPositionAndRotation(
                trackedImage.transform.TransformPoint(clueData.spawnOffset),
                trackedImage.transform.rotation * Quaternion.Euler(clueData.spawnRotation)
            );

            if (debugMode)
            {
                Debug.Log($"AR OBJECT SPAWNED! Clue {clueData.clueIndex} ({clueData.clueName})!");
                if (mobileDebugText != null) mobileDebugText.text = $"SUCCESS! AR OBJECT SPAWNED! Clue {clueData.clueIndex} ({clueData.clueName})!";
            }

            // Enable collect button when treasure spawns
            if (treasureHuntManager != null && treasureHuntManager.collectTreasureButton != null)
            {
                treasureHuntManager.collectTreasureButton.gameObject.SetActive(true);

                // Assign listener dynamically
                treasureHuntManager.collectTreasureButton.onClick.RemoveAllListeners();
                treasureHuntManager.collectTreasureButton.onClick.AddListener(() =>
                {
                    treasureHuntManager.OnCollectTreasure(arObject);
                });
            }

            NotifyTreasureFound(clueData);
        }
        else
        {
            // Hide the object and button for any non-tracking state (Limited, None, etc.)
            arObject.SetActive(false);
            
            // Hide collect button when AR object is not visible
            if (treasureHuntManager != null && treasureHuntManager.collectTreasureButton != null)
            {
                treasureHuntManager.collectTreasureButton.gameObject.SetActive(false);
            }
            
            if (debugMode && mobileDebugText != null) mobileDebugText.text = $"STEP 5 FAILED: Tracking state not good enough: {trackedImage.trackingState}";
        }
    }
    
    private void ClearAllARObjects()
    {
        foreach (var arObject in arObjects.Values)
        {
            if (arObject != null)
            {
                arObject.SetActive(false);
            }
        }
        
        // Hide collect button when clearing all AR objects
        if (treasureHuntManager != null && treasureHuntManager.collectTreasureButton != null)
        {
            treasureHuntManager.collectTreasureButton.gameObject.SetActive(false);
        }
    }
    
    private void NotifyTreasureFound(ClueARData clueData)
    {
        // You can add custom logic here to notify the treasure hunt manager
        // For example, marking the treasure as collected, playing sounds, etc.
        
        if (debugMode)
        {
            Debug.Log($"Treasure found for clue {clueData.clueIndex} ({clueData.clueName})!");
            if (mobileDebugText != null) mobileDebugText.text = $"TREASURE FOUND! Clue {clueData.clueIndex} ({clueData.clueName})!";
        }
        
        // Example: You could call a method on TreasureHuntManager here
        // treasureHuntManager.OnTreasureCollected(clueData.clueIndex);
    }
    
    // Public methods for external control
    public void EnableARTracking()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.enabled = true;
            
            if (debugMode)
            {
                Debug.Log("AR Image tracking enabled");
                // Check if we have reference images
                if (trackedImageManager.referenceLibrary != null)
                {
                    int imageCount = trackedImageManager.referenceLibrary.count;
                    if (mobileDebugText != null) mobileDebugText.text = $"AR tracking ENABLED. {imageCount} reference images loaded. Active clue: {currentActiveClueIndex}";
                }
                else
                {
                    if (mobileDebugText != null) mobileDebugText.text = "AR tracking ENABLED but NO REFERENCE LIBRARY!";
                }
            }
        }
        else
        {
            if (mobileDebugText != null) mobileDebugText.text = "ERROR: trackedImageManager is NULL!";
        }
    }
    
    public void DisableARTracking()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.enabled = false;
            ClearAllARObjects();
            
            if (debugMode)
            {
                Debug.Log("AR Image tracking disabled");
                if (mobileDebugText != null) mobileDebugText.text = "AR Image tracking DISABLED";
            }
        }
    }
    
    // Helper method to get clue data by index
    public ClueARData GetClueDataByIndex(int clueIndex)
    {
        clueIndexToData.TryGetValue(clueIndex, out ClueARData clueData);
        return clueData;
    }
    
    // Helper method to check if a clue has AR data configured
    public bool HasClueARData(int clueIndex)
    {
        return clueIndexToData.ContainsKey(clueIndex);
    }
    
    // Method to mark a treasure as collected (called from TreasureHuntManager)
    public void MarkTreasureAsCollected(int clueIndex)
    {
        collectedClueIndices.Add(clueIndex);
        
        if (debugMode)
        {
            Debug.Log($"Treasure for clue {clueIndex} marked as collected - will not respawn");
            if (mobileDebugText != null) mobileDebugText.text = $"Treasure for clue {clueIndex} marked as collected - will not respawn";
        }
    }
    
    // Load progress data with slight delay to ensure ProgressManager is ready
    private IEnumerator LoadProgressDataDelayed()
    {
        // Wait a frame to ensure all managers are initialized
        yield return new WaitForEndOfFrame();
        
        LoadProgressData();
    }
    
    // Load collected treasures from ProgressManager
    public void LoadProgressData()
    {
        if (progressManager != null && progressManager.HasProgress())
        {
            int[] collectedTreasures = progressManager.GetCollectedTreasures();
            
            // Clear existing and load from progress
            collectedClueIndices.Clear();
            
            foreach (int clueIndex in collectedTreasures)
            {
                collectedClueIndices.Add(clueIndex);
            }
            
            if (debugMode)
            {
                Debug.Log($"Loaded progress: {collectedClueIndices.Count} treasures already collected");
                if (mobileDebugText != null) mobileDebugText.text = $"Progress loaded: {collectedClueIndices.Count} treasures collected";
            }
        }
        else
        {
            if (debugMode)
            {
                Debug.Log("No progress data to load - starting fresh");
                if (mobileDebugText != null) mobileDebugText.text = "No progress data - starting fresh";
            }
        }
    }
}