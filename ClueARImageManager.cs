using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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
        }
        
        if (treasureHuntManager == null)
        {
            Debug.LogError("TreasureHuntManager not assumed!");
        }
        
        // Pre-instantiate all AR objects
        SetupARObjects();
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
        }
        
        // Check if we have an AR object for this image
        if (!arObjects.ContainsKey(imageName))
        {
            if (debugMode)
            {
                Debug.LogWarning($"No AR object found for detected image: {imageName}");
            }
            return;
        }
        
        // Check if this image corresponds to a clue we have data for
        if (!imageNameToClueData.ContainsKey(imageName))
        {
            if (debugMode)
            {
                Debug.LogWarning($"No clue data found for detected image: {imageName}");
            }
            return;
        }
        
        ClueARData clueData = imageNameToClueData[imageName];
        GameObject arObject = arObjects[imageName];
        
        // Check if this is the currently active clue
        if (clueData.clueIndex != currentActiveClueIndex)
        {
            if (debugMode)
            {
                Debug.Log($"Ignoring image '{imageName}' for clue {clueData.clueIndex} - current active clue is {currentActiveClueIndex}");
            }
            arObject.SetActive(false);
            return;
        }
        
        // Handle the tracked image based on its tracking state
        if (trackedImage.trackingState == TrackingState.Tracking)
        {
            // Position and activate the object
            arObject.SetActive(true);
            arObject.transform.SetPositionAndRotation(
                trackedImage.transform.TransformPoint(clueData.spawnOffset),
                trackedImage.transform.rotation * Quaternion.Euler(clueData.spawnRotation)
            );
            
            // Notify treasure hunt manager that treasure was found
            NotifyTreasureFound(clueData);
        }
        else if (trackedImage.trackingState == TrackingState.Limited || trackedImage.trackingState == TrackingState.None)
        {
            // Hide the object if tracking is lost or limited
            arObject.SetActive(false);
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
            }
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