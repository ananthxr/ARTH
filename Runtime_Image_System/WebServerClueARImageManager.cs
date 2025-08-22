using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;

/// <summary>
/// Web Server Clue AR Image Manager - Works with WebServerImageManager
/// Handles AR image detection and treasure spawning using images from web server
/// </summary>
public class WebServerClueARImageManager : MonoBehaviour
{
    [Header("AR Components")]
    public ARTrackedImageManager trackedImageManager;
    public TreasureHuntManager treasureHuntManager;
    public ProgressManager progressManager;
    public WebServerImageManager webServerImageManager;

    [Header("Treasure Prefabs")]
    [Tooltip("Array of treasure prefabs. Index should match clue indices.")]
    public GameObject[] treasurePrefabs;

    [Header("Settings")]
    public bool debugMode = true;

    [Header("Mobile Debug")]
    public TMP_Text mobileDebugText;

    // AR object management
    private Dictionary<string, GameObject> arObjects;
    private Dictionary<string, ClueARData> clueDataMap;
    private HashSet<int> collectedClueIndices;
    
    // Current state
    private int currentActiveClueIndex = -1;
    private bool arTrackingEnabled = false;

    void Start()
    {
        InitializeCollections();
        
        // Wait for web server image manager to initialize
        StartCoroutine(WaitForImageManagerAndInitialize());
    }

    private void InitializeCollections()
    {
        arObjects = new Dictionary<string, GameObject>();
        clueDataMap = new Dictionary<string, ClueARData>();
        collectedClueIndices = new HashSet<int>();
    }

    private IEnumerator WaitForImageManagerAndInitialize()
    {
        LogDebug("Waiting for WebServerImageManager to initialize...");
        
        // Wait for web server image manager to load config
        while (webServerImageManager == null || webServerImageManager.ConfigData == null)
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        LogDebug("WebServerImageManager ready, building clue data mappings...");
        BuildClueDataFromWebServerConfig();
        CreateARObjects();
        
        // Subscribe to AR tracking events
        if (trackedImageManager != null)
        {
            trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
        }
        
        // Don't try to get clue index during initialization - it will be set later via SetActiveClue()
        LogDebug("WebServerClueARImageManager ready for clue assignment");
        
        LogDebug("WebServerClueARImageManager initialized successfully");
    }

    private void BuildClueDataFromWebServerConfig()
    {
        var configData = webServerImageManager.ConfigData;
        
        foreach (var imageConfig in configData.images)
        {
            // Create ClueARData from web server config
            ClueARData clueData = new ClueARData
            {
                clueIndex = imageConfig.clueIndex,
                referenceImageName = imageConfig.imageName,
                treasurePrefab = GetTreasurePrefabForClueIndex(imageConfig.clueIndex)
            };
            
            clueDataMap[imageConfig.imageName] = clueData;
            LogDebug($"Mapped web server image '{imageConfig.imageName}' to clue index {imageConfig.clueIndex}");
        }
    }

    private GameObject GetTreasurePrefabForClueIndex(int clueIndex)
    {
        if (clueIndex >= 0 && clueIndex < treasurePrefabs.Length)
        {
            return treasurePrefabs[clueIndex];
        }
        return null;
    }

    private void CreateARObjects()
    {
        LogDebug("Creating AR objects for web server images...");
        
        foreach (var kvp in clueDataMap)
        {
            string imageName = kvp.Key;
            ClueARData clueData = kvp.Value;
            
            if (clueData.treasurePrefab != null)
            {
                GameObject arObject = Instantiate(clueData.treasurePrefab);
                arObject.name = $"AR_Object_{imageName}";
                arObject.SetActive(false);
                arObjects[imageName] = arObject;
                
                LogDebug($"Created AR object for web server image: {imageName}");
            }
            else
            {
                LogWarning($"No treasure prefab assigned for clue index: {clueData.clueIndex}");
            }
        }
        
        LogDebug($"Created {arObjects.Count} AR objects from web server config");
    }

    public void SetActiveClue(int clueIndex)
    {
        LogDebug($"Setting active clue to index: {clueIndex}");
        currentActiveClueIndex = clueIndex;
        
        // Load the image for this clue from web server if not already loaded
        if (webServerImageManager != null && !webServerImageManager.IsImageLoaded(clueIndex))
        {
            LogDebug($"Loading image for clue {clueIndex} from web server...");
            StartCoroutine(webServerImageManager.LoadImageForClue(clueIndex));
        }
        else
        {
            LogDebug($"Image for clue {clueIndex} already loaded or web server manager is null");
        }
        
        // Clear all AR objects
        ClearAllARObjects();
        
        LogDebug($"Active clue set to {clueIndex}. Ready for AR tracking.");
    }

    public void EnableARTracking()
    {
        LogDebug("Enabling AR tracking for web server images");
        arTrackingEnabled = true;
        
        if (trackedImageManager != null)
        {
            trackedImageManager.enabled = true;
        }
    }

    public void DisableARTracking()
    {
        LogDebug("Disabling AR tracking");
        arTrackingEnabled = false;
        
        ClearAllARObjects();
        
        if (trackedImageManager != null)
        {
            trackedImageManager.enabled = false;
        }
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        if (!arTrackingEnabled) 
        {
            LogDebug("AR tracking disabled, ignoring tracked image events");
            return;
        }

        LogDebug($"AR tracking event received - Added: {eventArgs.added.Count}, Updated: {eventArgs.updated.Count}, Removed: {eventArgs.removed.Count}");

        foreach (ARTrackedImage trackedImage in eventArgs.updated)
        {
            UpdateTrackedImage(trackedImage);
        }

        foreach (ARTrackedImage trackedImage in eventArgs.added)
        {
            UpdateTrackedImage(trackedImage);
        }
    }

    private void UpdateTrackedImage(ARTrackedImage trackedImage)
    {
        string imageName = trackedImage.referenceImage.name;
        
        LogDebug($"STEP 1: Detected web server image '{imageName}', State: {trackedImage.trackingState}");

        if (trackedImage.trackingState == TrackingState.Tracking)
        {
            // Check if we have an AR object for this image
            if (!arObjects.ContainsKey(imageName))
            {
                LogDebug($"STEP 2 FAILED: No AR object exists for web server image '{imageName}'");
                return;
            }
            LogDebug($"STEP 2 PASSED: AR object exists for web server image '{imageName}'");

            // Check if we have clue data for this image
            if (!clueDataMap.ContainsKey(imageName))
            {
                LogDebug($"STEP 3 FAILED: No clue data exists for web server image '{imageName}'");
                return;
            }
            LogDebug($"STEP 3 PASSED: Clue data exists for web server image '{imageName}'");

            ClueARData clueData = clueDataMap[imageName];
            
            // Check if this clue was already collected
            if (collectedClueIndices.Contains(clueData.clueIndex))
            {
                LogDebug($"STEP 4 FAILED: Clue {clueData.clueIndex} already collected");
                return;
            }

            // Check if this is the current active clue
            if (clueData.clueIndex != currentActiveClueIndex)
            {
                LogDebug($"STEP 4 FAILED: Clue {clueData.clueIndex} doesn't match active {currentActiveClueIndex}");
                return;
            }
            LogDebug($"STEP 4 PASSED: Clue {clueData.clueIndex} matches active {currentActiveClueIndex}");

            LogDebug($"STEP 5: Trying to spawn AR object for web server image '{imageName}'...");
            
            // Position and show the AR object
            GameObject arObject = arObjects[imageName];
            arObject.transform.position = trackedImage.transform.position;
            arObject.transform.rotation = trackedImage.transform.rotation;
            
            // Apply any offset from web server config
            var imageConfig = webServerImageManager.GetImageConfig(imageName);
            if (imageConfig != null)
            {
                Vector3 offset = new Vector3(imageConfig.spawnOffset.x, imageConfig.spawnOffset.y, imageConfig.spawnOffset.z);
                Quaternion rotationOffset = Quaternion.Euler(imageConfig.spawnRotation.x, imageConfig.spawnRotation.y, imageConfig.spawnRotation.z);
                
                arObject.transform.position += arObject.transform.TransformDirection(offset);
                arObject.transform.rotation *= rotationOffset;
            }
            
            arObject.SetActive(true);
            
            // Show collect button and set up the onClick listener
            if (treasureHuntManager != null && treasureHuntManager.collectTreasureButton != null)
            {
                treasureHuntManager.collectTreasureButton.gameObject.SetActive(true);
                
                // Remove any existing listeners and add new one for this AR object
                treasureHuntManager.collectTreasureButton.onClick.RemoveAllListeners();
                treasureHuntManager.collectTreasureButton.onClick.AddListener(() => {
                    treasureHuntManager.OnCollectTreasure(arObject);
                });
                
                LogDebug($"Collect button configured for AR object: {imageName}");
            }
            
            LogDebug($"SUCCESS! AR OBJECT SPAWNED FOR WEB SERVER IMAGE: {imageName}");
        }
        else
        {
            // Hide AR objects when tracking is lost
            if (arObjects.ContainsKey(imageName))
            {
                arObjects[imageName].SetActive(false);
                
                // Hide collect button
                if (treasureHuntManager != null && treasureHuntManager.collectTreasureButton != null)
                {
                    treasureHuntManager.collectTreasureButton.gameObject.SetActive(false);
                }
                
                LogDebug($"AR object hidden for web server image '{imageName}' - tracking state: {trackedImage.trackingState}");
            }
        }
    }

    public void MarkTreasureAsCollected(int clueIndex)
    {
        collectedClueIndices.Add(clueIndex);
        LogDebug($"Marked web server treasure {clueIndex} as collected");
    }

    public void ClearAllARObjects()
    {
        foreach (var arObject in arObjects.Values)
        {
            if (arObject != null)
            {
                arObject.SetActive(false);
            }
        }
        
        if (treasureHuntManager != null && treasureHuntManager.collectTreasureButton != null)
        {
            treasureHuntManager.collectTreasureButton.gameObject.SetActive(false);
        }
        
        LogDebug("Cleared all web server AR objects");
    }

    // Public API methods for compatibility
    public RuntimeReferenceImageLibrary GetRuntimeLibrary()
    {
        return webServerImageManager?.RuntimeLibrary;
    }

    public RuntimeImageConfig GetImageConfig(string imageName)
    {
        return webServerImageManager?.GetImageConfig(imageName);
    }

    public RuntimeImageConfig GetImageConfigForClue(int clueIndex)
    {
        return webServerImageManager?.GetImageConfigForClue(clueIndex);
    }

    public string GetImageNameForClue(int clueIndex)
    {
        return webServerImageManager?.GetImageNameForClue(clueIndex);
    }

    private void LogDebug(string message)
    {
        if (debugMode)
        {
            Debug.Log($"[WebServerClueARImageManager] {message}");
            if (mobileDebugText != null)
            {
                mobileDebugText.text = message;
            }
        }
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning($"[WebServerClueARImageManager] {message}");
        if (mobileDebugText != null)
        {
            mobileDebugText.text = $"WARNING: {message}";
        }
    }

    private void LogError(string message)
    {
        Debug.LogError($"[WebServerClueARImageManager] {message}");
        if (mobileDebugText != null)
        {
            mobileDebugText.text = $"ERROR: {message}";
        }
    }

    void OnDestroy()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
        }
    }
}