using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;
using UnityEngine.Networking;

/// <summary>
/// Web Server Image Manager - Fetches images from web server instead of StreamingAssets
/// For testing dynamic image loading from external source
/// </summary>
public class WebServerImageManager : MonoBehaviour
{
    [Header("AR Components")]
    public ARTrackedImageManager trackedImageManager;
    
    [Header("Web Server Configuration")]
    public string serverBaseURL = "http://localhost:8000";
    public string configEndpoint = "config.json";
    public string imagesEndpoint = "images";
    
    [Header("Runtime Library")]
    public bool useRuntimeLibrary = true;
    public bool loadAllImagesAtStartup = false;
    
    [Header("Debug")]
    public bool debugMode = true;
    public TMP_Text mobileDebugText;
    
    // Runtime library and job tracking
    private MutableRuntimeReferenceImageLibrary mutableLibrary;
    private RuntimeReferenceImageLibrary runtimeLibrary;
    private Dictionary<string, AddReferenceImageJobState> addImageJobs;
    
    // Configuration and state
    private ImageConfigData configData;
    private Dictionary<string, RuntimeImageConfig> imageConfigs;
    private Dictionary<int, string> clueIndexToImageName;
    private Dictionary<string, Texture2D> loadedTextures;
    private List<string> addedImageNames;
    
    // Public accessors
    public ImageConfigData ConfigData => configData;
    public RuntimeReferenceImageLibrary RuntimeLibrary => runtimeLibrary;
    
    void Start()
    {
        LogDebug("WebServerImageManager starting...");
        InitializeCollections();
        
        if (useRuntimeLibrary)
        {
            StartCoroutine(InitializeRuntimeLibrary());
        }
        else
        {
            LogDebug("Runtime library disabled - WebServerImageManager ready");
        }
        
        LogDebug("WebServerImageManager initialized");
    }
    
    private void InitializeCollections()
    {
        imageConfigs = new Dictionary<string, RuntimeImageConfig>();
        clueIndexToImageName = new Dictionary<int, string>();
        loadedTextures = new Dictionary<string, Texture2D>();
        addedImageNames = new List<string>();
        addImageJobs = new Dictionary<string, AddReferenceImageJobState>();
    }
    
    private IEnumerator InitializeRuntimeLibrary()
    {
        LogDebug("Starting web server runtime library initialization...");
        
        // Wait for AR to be ready - but don't wait forever
        float waitTime = 0f;
        const float maxWaitTime = 10f; // Maximum 10 seconds wait
        
        while (ARSession.state != ARSessionState.SessionTracking && waitTime < maxWaitTime)
        {
            LogDebug($"Waiting for AR Session... Current state: {ARSession.state} (waited {waitTime:F1}s)");
            yield return new WaitForSeconds(0.5f);
            waitTime += 0.5f;
        }
        
        if (waitTime >= maxWaitTime)
        {
            LogDebug($"AR Session timeout reached ({maxWaitTime}s), proceeding anyway. Current state: {ARSession.state}");
        }
        else
        {
            LogDebug($"AR Session ready after {waitTime:F1}s. State: {ARSession.state}");
        }
        
        // Load configuration from web server
        yield return StartCoroutine(LoadConfigFromWebServer());
        
        if (configData?.images == null)
        {
            LogError("Failed to load image configuration from web server");
            yield break;
        }
        
        LogDebug($"Config loaded successfully: {configData.images.Length} images found");
        
        // Create runtime library
        if (!CreateRuntimeLibrary())
        {
            LogError("Failed to create mutable runtime library");
            yield break;
        }
        
        if (loadAllImagesAtStartup)
        {
            yield return StartCoroutine(LoadAllImagesFromWebServer());
        }
        else
        {
            LogDebug("Runtime library created. Loading first image for testing...");
            // For testing: always load first image
            if (configData.images != null && configData.images.Length > 0)
            {
                yield return StartCoroutine(LoadAndAddImageFromWebServer(configData.images[0]));
            }
        }
        
        // Debug: Print the runtime library status
        LogDebug($"Runtime library final status: {runtimeLibrary?.count} images in library");
        
        // CRITICAL: Assign the runtime library to ARTrackedImageManager
        if (runtimeLibrary != null && trackedImageManager != null)
        {
            LogDebug("Assigning runtime library to ARTrackedImageManager...");
            trackedImageManager.referenceLibrary = runtimeLibrary;
            LogDebug("Runtime library assigned successfully!");
        }
        else
        {
            LogError("CRITICAL: Cannot assign runtime library - null references!");
        }
    }
    
    private bool CreateRuntimeLibrary()
    {
        try
        {
            runtimeLibrary = trackedImageManager.CreateRuntimeLibrary();
            
            if (runtimeLibrary is MutableRuntimeReferenceImageLibrary mutable)
            {
                mutableLibrary = mutable;
                LogDebug("Mutable runtime library created successfully");
                return true;
            }
            else
            {
                LogError("Platform does not support mutable runtime libraries");
                return false;
            }
        }
        catch (System.Exception e)
        {
            LogError($"Exception creating runtime library: {e.Message}");
            return false;
        }
    }
    
    private IEnumerator LoadConfigFromWebServer()
    {
        string configURL = $"{serverBaseURL}/{configEndpoint}";
        LogDebug($"Loading config from web server: {configURL}");
        
        using (UnityWebRequest request = UnityWebRequest.Get(configURL))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonContent = request.downloadHandler.text;
                LogDebug($"Config downloaded successfully. Length: {jsonContent.Length} characters");
                ParseConfiguration(jsonContent);
            }
            else
            {
                LogError($"Failed to load config from web server: {request.error}");
                LogError($"Response Code: {request.responseCode}");
                LogError($"URL: {configURL}");
            }
        }
    }
    
    private void ParseConfiguration(string jsonContent)
    {
        try
        {
            configData = JsonUtility.FromJson<ImageConfigData>(jsonContent);
            
            // Build lookup dictionaries
            foreach (var imageConfig in configData.images)
            {
                imageConfigs[imageConfig.imageName] = imageConfig;
                clueIndexToImageName[imageConfig.clueIndex] = imageConfig.imageName;
            }
            
            LogDebug($"Loaded configuration for {configData.images.Length} images from web server");
        }
        catch (System.Exception e)
        {
            LogError($"Failed to parse web server configuration: {e.Message}");
        }
    }
    
    private IEnumerator LoadAllImagesFromWebServer()
    {
        LogDebug("Loading all images from web server...");
        
        // Start all image loading jobs
        foreach (var imageConfig in configData.images)
        {
            StartCoroutine(LoadAndAddImageFromWebServer(imageConfig));
        }
        
        // Wait for all jobs to complete
        yield return StartCoroutine(WaitForAllImageJobs());
        
        LogDebug($"Finished loading all images from web server. Total added: {addedImageNames.Count}");
    }
    
    public IEnumerator LoadImageForClue(int clueIndex)
    {
        if (clueIndexToImageName.ContainsKey(clueIndex))
        {
            string imageName = clueIndexToImageName[clueIndex];
            if (!addedImageNames.Contains(imageName))
            {
                var imageConfig = imageConfigs[imageName];
                yield return StartCoroutine(LoadAndAddImageFromWebServer(imageConfig));
            }
            else
            {
                LogDebug($"Image for clue {clueIndex} already loaded from web server");
            }
        }
        else
        {
            LogWarning($"No image configuration found for clue index: {clueIndex}");
        }
    }
    
    private IEnumerator LoadAndAddImageFromWebServer(RuntimeImageConfig imageConfig)
    {
        LogDebug($"Loading image from web server: {imageConfig.imageName} ({imageConfig.fileName})");
        
        // Check if already being processed
        if (addImageJobs.ContainsKey(imageConfig.imageName))
        {
            LogDebug($"Image {imageConfig.imageName} already being processed, waiting...");
            yield return StartCoroutine(WaitForAddImageJob(imageConfig.imageName, addImageJobs[imageConfig.imageName]));
            yield break;
        }
        
        // Load texture from web server
        Texture2D texture = null;
        yield return StartCoroutine(LoadTextureFromWebServer(imageConfig.fileName, (loadedTexture) => {
            texture = loadedTexture;
        }));
        
        if (texture == null)
        {
            LogError($"Failed to load texture from web server: {imageConfig.fileName}");
            yield break;
        }
        
        LogDebug($"Texture loaded successfully: {imageConfig.fileName} ({texture.width}x{texture.height})");
        
        // Store loaded texture
        loadedTextures[imageConfig.imageName] = texture;
        
        // Add image to runtime library
        if (mutableLibrary != null)
        {
            LogDebug($"Attempting to add image to runtime library: {imageConfig.imageName}");
            
            AddReferenceImageJobState jobState;
            bool jobScheduled = false;
            
            try
            {
                jobState = mutableLibrary.ScheduleAddImageWithValidationJob(
                    texture,
                    imageConfig.imageName,
                    imageConfig.physicalSizeInMeters
                );
                
                addImageJobs[imageConfig.imageName] = jobState;
                jobScheduled = true;
                LogDebug($"Successfully scheduled add image job for: {imageConfig.imageName}");
            }
            catch (System.Exception e)
            {
                LogError($"CRITICAL: Failed to schedule add image job for {imageConfig.imageName}: {e.Message}");
                LogError($"Exception details: {e.ToString()}");
                yield break;
            }
            
            if (jobScheduled)
            {
                LogDebug($"Waiting for job completion: {imageConfig.imageName}");
                yield return StartCoroutine(WaitForAddImageJob(imageConfig.imageName, addImageJobs[imageConfig.imageName]));
            }
        }
        else
        {
            LogError("CRITICAL: mutableLibrary is null - cannot add images to runtime library!");
        }
    }
    
    private IEnumerator LoadTextureFromWebServer(string fileName, System.Action<Texture2D> callback)
    {
        string imageURL = $"{serverBaseURL}/{imagesEndpoint}/{fileName}";
        LogDebug($"Loading texture from web server: {imageURL}");
        
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageURL))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                texture.wrapMode = TextureWrapMode.Clamp;
                LogDebug($"Successfully loaded texture: {fileName} ({texture.width}x{texture.height})");
                callback?.Invoke(texture);
            }
            else
            {
                LogError($"Failed to load texture from web server {fileName}: {request.error}");
                LogError($"Response Code: {request.responseCode}");
                LogError($"URL: {imageURL}");
                callback?.Invoke(null);
            }
        }
    }
    
    private IEnumerator WaitForAddImageJob(string imageName, AddReferenceImageJobState jobState)
    {
        LogDebug($"Starting job wait for: {imageName}, initial status: {jobState.status}");
        
        int waitFrames = 0;
        while (jobState.status == AddReferenceImageJobStatus.Pending)
        {
            waitFrames++;
            if (waitFrames % 60 == 0) // Log every 60 frames (about 1 second)
            {
                LogDebug($"Still waiting for job: {imageName}, frames: {waitFrames}");
            }
            yield return null;
        }
        
        LogDebug($"Job completed for {imageName} after {waitFrames} frames. Final status: {jobState.status}");
        
        if (jobState.status == AddReferenceImageJobStatus.Success)
        {
            addedImageNames.Add(imageName);
            LogDebug($"SUCCESS! Added web server image to AR library: {imageName}");
        }
        else
        {
            LogError($"FAILED! Could not add web server image to AR library: {imageName} - Status: {jobState.status}");
        }
        
        // Clean up
        addImageJobs.Remove(imageName);
    }
    
    private IEnumerator WaitForAllImageJobs()
    {
        while (addImageJobs.Count > 0)
        {
            // Check for completed jobs
            var completedJobs = new List<string>();
            
            foreach (var kvp in addImageJobs)
            {
                if (kvp.Value.status != AddReferenceImageJobStatus.Pending)
                {
                    completedJobs.Add(kvp.Key);
                }
            }
            
            // Process completed jobs
            foreach (string imageName in completedJobs)
            {
                yield return StartCoroutine(WaitForAddImageJob(imageName, addImageJobs[imageName]));
            }
            
            yield return null; // Wait one frame before checking again
        }
    }
    
    // Public API methods (same as RuntimeImageLibraryManager for compatibility)
    public bool IsImageLoaded(string imageName)
    {
        return addedImageNames.Contains(imageName);
    }
    
    public bool IsImageLoaded(int clueIndex)
    {
        if (clueIndexToImageName.ContainsKey(clueIndex))
        {
            return IsImageLoaded(clueIndexToImageName[clueIndex]);
        }
        return false;
    }
    
    public RuntimeImageConfig GetImageConfig(string imageName)
    {
        imageConfigs.TryGetValue(imageName, out RuntimeImageConfig config);
        return config;
    }
    
    public RuntimeImageConfig GetImageConfigForClue(int clueIndex)
    {
        if (clueIndexToImageName.ContainsKey(clueIndex))
        {
            return GetImageConfig(clueIndexToImageName[clueIndex]);
        }
        return null;
    }
    
    public string GetImageNameForClue(int clueIndex)
    {
        clueIndexToImageName.TryGetValue(clueIndex, out string imageName);
        return imageName;
    }
    
    public RuntimeReferenceImageLibrary GetRuntimeLibrary()
    {
        return runtimeLibrary;
    }
    
    private void LogDebug(string message)
    {
        if (debugMode)
        {
            Debug.Log($"[WebServerImageManager] {message}");
            if (mobileDebugText != null)
            {
                mobileDebugText.text = message;
            }
        }
    }
    
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[WebServerImageManager] {message}");
        if (mobileDebugText != null)
        {
            mobileDebugText.text = $"WARNING: {message}";
        }
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[WebServerImageManager] {message}");
        if (mobileDebugText != null)
        {
            mobileDebugText.text = $"ERROR: {message}";
        }
    }
    
    void OnDestroy()
    {
        // Clean up loaded textures
        foreach (var texture in loadedTextures.Values)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
        
        loadedTextures.Clear();
        addImageJobs.Clear();
    }
}