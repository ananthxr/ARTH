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
        
        // Pre-instantiate all AR objects
        SetupARObjects();
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
        
        if (debugMode)
        {
            Debug.Log($"Active clue set to index {clueIndex}");
            if (mobileDebugText != null) mobileDebugText.text = $"Active clue set to index {clueIndex}";
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
            
            // Notify treasure hunt manager that treasure was found
            NotifyTreasureFound(clueData);
        }
        else if (trackedImage.trackingState == TrackingState.Limited || trackedImage.trackingState == TrackingState.None)
        {
            // Hide the object if tracking is lost or limited
            arObject.SetActive(false);
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
}