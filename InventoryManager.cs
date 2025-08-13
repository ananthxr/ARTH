using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryManager : MonoBehaviour
{
    [Header("Inventory UI")]
    public GameObject inventoryPanel;
    public Button closeInventoryButton;
    
    [Header("Treasure Item Images")]
    public Image[] treasureItemImages; // Array of images representing collected treasures
    
    [Header("Inventory Display")]
    public TMP_Text inventoryTitle;
    public TMP_Text collectionStatusText;
    
    [Header("Settings")]
    public Color collectedColor = Color.white;
    public Color unCollectedColor = Color.gray;
    public float unCollectedAlpha = 0.3f;
    
    // References to other managers
    private TreasureHuntManager treasureHuntManager;
    private ClueARImageManager clueARImageManager;
    private ProgressManager progressManager;
    
    // Tracking collected items
    private HashSet<int> collectedTreasureIndices = new HashSet<int>();
    private int totalTreasures;
    
    void Start()
    {
        // Find references to other managers
        treasureHuntManager = FindObjectOfType<TreasureHuntManager>();
        clueARImageManager = FindObjectOfType<ClueARImageManager>();
        progressManager = FindObjectOfType<ProgressManager>();
        
        if (treasureHuntManager != null)
        {
            totalTreasures = treasureHuntManager.GetTotalClues();
        }
        
        // Setup UI
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);
        
        if (closeInventoryButton != null)
            closeInventoryButton.onClick.AddListener(CloseInventory);
        
        // Initialize treasure item images
        InitializeTreasureImages();
        
        // Load progress data after initialization
        StartCoroutine(LoadProgressDataDelayed());
        
        Debug.Log($"InventoryManager initialized with {totalTreasures} total treasures");
    }
    
    private void InitializeTreasureImages()
    {
        if (treasureItemImages == null) return;
        
        // Set all treasure images to uncollected state initially
        for (int i = 0; i < treasureItemImages.Length; i++)
        {
            if (treasureItemImages[i] != null)
            {
                SetTreasureImageState(i, false);
            }
        }
    }
    
    private void SetTreasureImageState(int treasureIndex, bool isCollected)
    {
        if (treasureIndex >= 0 && treasureIndex < treasureItemImages.Length && treasureItemImages[treasureIndex] != null)
        {
            Image treasureImage = treasureItemImages[treasureIndex];
            
            if (isCollected)
            {
                // Set to collected appearance
                treasureImage.color = collectedColor;
                treasureImage.gameObject.SetActive(true);
            }
            else
            {
                // Set to uncollected appearance (grayed out)
                Color uncollectedColor = this.unCollectedColor;
                uncollectedColor.a = unCollectedAlpha;
                treasureImage.color = uncollectedColor;
                treasureImage.gameObject.SetActive(true);
            }
        }
    }
    
    public void OpenInventory()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(true);
            UpdateInventoryDisplay();
        }
        
        Debug.Log("Inventory panel opened");
    }
    
    public void CloseInventory()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }
        
        Debug.Log("Inventory panel closed");
    }
    
    private void UpdateInventoryDisplay()
    {
        // Update inventory title
        if (inventoryTitle != null)
        {
            inventoryTitle.text = "Treasure Inventory";
        }
        
        // Update collection status
        if (collectionStatusText != null)
        {
            int collectedCount = collectedTreasureIndices.Count;
            collectionStatusText.text = $"Collected: {collectedCount}/{totalTreasures} Treasures";
        }
        
        // Update all treasure image states
        for (int i = 0; i < totalTreasures && i < treasureItemImages.Length; i++)
        {
            bool isCollected = collectedTreasureIndices.Contains(i);
            SetTreasureImageState(i, isCollected);
        }
    }
    
    public void AddCollectedTreasure(int clueIndex)
    {
        if (!collectedTreasureIndices.Contains(clueIndex))
        {
            collectedTreasureIndices.Add(clueIndex);
            
            // Update the specific treasure image
            SetTreasureImageState(clueIndex, true);
            
            Debug.Log($"Treasure {clueIndex} added to inventory. Total collected: {collectedTreasureIndices.Count}/{totalTreasures}");
            
            // Update display if inventory is currently open
            if (inventoryPanel != null && inventoryPanel.activeInHierarchy)
            {
                UpdateInventoryDisplay();
            }
        }
    }
    
    public bool IsTreasureCollected(int clueIndex)
    {
        return collectedTreasureIndices.Contains(clueIndex);
    }
    
    public int GetCollectedCount()
    {
        return collectedTreasureIndices.Count;
    }
    
    public bool IsInventoryOpen()
    {
        return inventoryPanel != null && inventoryPanel.activeInHierarchy;
    }
    
    // Get all collected treasure indices (useful for other systems)
    public HashSet<int> GetCollectedTreasureIndices()
    {
        return new HashSet<int>(collectedTreasureIndices);
    }
    
    // Reset inventory (useful for testing or restarting)
    public void ResetInventory()
    {
        collectedTreasureIndices.Clear();
        InitializeTreasureImages();
        
        if (inventoryPanel != null && inventoryPanel.activeInHierarchy)
        {
            UpdateInventoryDisplay();
        }
        
        Debug.Log("Inventory reset - all treasures marked as uncollected");
    }
    
    // Load progress data with slight delay to ensure ProgressManager is ready
    private IEnumerator LoadProgressDataDelayed()
    {
        // Wait a frame to ensure all managers are initialized
        yield return new WaitForEndOfFrame();
        
        LoadProgressData();
    }
    
    // Load collected treasures from ProgressManager and update visual states
    public void LoadProgressData()
    {
        if (progressManager != null && progressManager.HasProgress())
        {
            int[] collectedTreasures = progressManager.GetCollectedTreasures();
            
            // Clear existing and load from progress
            collectedTreasureIndices.Clear();
            
            foreach (int clueIndex in collectedTreasures)
            {
                collectedTreasureIndices.Add(clueIndex);
            }
            
            // Update visual states for all treasure images
            for (int i = 0; i < totalTreasures && i < treasureItemImages.Length; i++)
            {
                bool isCollected = collectedTreasureIndices.Contains(i);
                SetTreasureImageState(i, isCollected);
            }
            
            Debug.Log($"Inventory progress loaded: {collectedTreasureIndices.Count} treasures collected");
        }
        else
        {
            Debug.Log("No inventory progress data to load - starting fresh");
        }
    }
    
    // Restore inventory from progress data (called during game resume)
    public void RestoreFromProgress(int[] collectedTreasures)
    {
        if (collectedTreasures == null) return;
        
        // Clear existing and load from provided data
        collectedTreasureIndices.Clear();
        
        foreach (int clueIndex in collectedTreasures)
        {
            if (clueIndex >= 0 && clueIndex < totalTreasures)
            {
                collectedTreasureIndices.Add(clueIndex);
            }
        }
        
        // Update visual states for all treasure images
        for (int i = 0; i < totalTreasures && i < treasureItemImages.Length; i++)
        {
            bool isCollected = collectedTreasureIndices.Contains(i);
            SetTreasureImageState(i, isCollected);
        }
        
        // Update display if inventory is open
        if (inventoryPanel != null && inventoryPanel.activeInHierarchy)
        {
            UpdateInventoryDisplay();
        }
        
        Debug.Log($"Inventory restored from progress: {collectedTreasureIndices.Count} treasures collected");
    }
}