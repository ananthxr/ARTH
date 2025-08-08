using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[System.Serializable]
public class TeamDataRTDB
{
    public int teamNumber;
    public string teamName;
    public string uid;
    public string player1;
    public string player2;
    public string email;
    public int score;
}

public class FirebaseRTDBFetcher : MonoBehaviour
{
    [Header("Realtime Database Settings")]
    [Tooltip("Base RTDB URL without trailing slash. Example: https://arth2-169a4-default-rtdb.firebaseio.com")]
    public string realtimeDatabaseBaseUrl = "https://arth2-169a4-default-rtdb.firebaseio.com";

    [Tooltip("Node path under the DB where the team will be stored. Example: 26SIG")] 
    public string nodePath = "26SIG";

    [Tooltip("Optional auth token or database secret. Leave blank if rules allow public read/write for testing.")]
    public string authToken = "";

    [Header("UI")]
    public TMP_Text outputText;
    public Button fetchButton;
    public TMP_InputField uidInput;
    [Tooltip("Shown near the UID input to display friendly errors like 'UID not found'")]
    public TMP_Text uidStatusText;
    public GameObject verificationPanel;
    public TMP_Text verificationText;
    public Button iVerifiedButton;
    public TreasureHuntManager treasureHuntManager;

    private void Start()
    {
        if (fetchButton != null) fetchButton.onClick.AddListener(OnFetchButtonClicked);
        if (iVerifiedButton != null) iVerifiedButton.onClick.AddListener(OnIVerifiedClicked);
        if (verificationPanel != null) verificationPanel.SetActive(false);
        if (treasureHuntManager == null)
        {
            treasureHuntManager = FindObjectOfType<TreasureHuntManager>();
        }
    }

    public void OnFetchButtonClicked()
    {
        string uid = uidInput != null ? (uidInput.text ?? string.Empty).Trim() : string.Empty;
        if (string.IsNullOrEmpty(uid))
        {
            if (uidStatusText != null) uidStatusText.text = "Please enter a UID";
            else if (outputText != null) outputText.text = "Please enter a UID";
            return;
        }
        if (uidStatusText != null) uidStatusText.text = "";
        if (outputText != null) outputText.text = $"Fetching UID: {uid}...";
        StartCoroutine(GetTeamData(uid));
    }


    private string BuildUrl()
    {
        return BuildUrl(null);
    }

    private string BuildUrl(string pathOverride)
    {
        string baseUrl = (realtimeDatabaseBaseUrl ?? string.Empty).TrimEnd('/');
        string effectivePath = string.IsNullOrWhiteSpace(pathOverride) ? nodePath : pathOverride;
        string path = (effectivePath ?? string.Empty).TrimStart('/');
        string url = baseUrl + "/" + path + ".json";
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            url += (url.Contains("?") ? "&" : "?") + "auth=" + authToken;
        }
        return url;
    }


    private IEnumerator GetTeamData()
    {
        string url = BuildUrl();
        
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 15;
            yield return request.SendWebRequest();
            
            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"GET failed: {request.error}");
                if (outputText != null) outputText.text = $"GET failed: {request.error}";
                yield break;
            }
            
            string json = request.downloadHandler.text;
            TeamDataRTDB result = null;
            try
            {
                result = JsonUtility.FromJson<TeamDataRTDB>(json);
            }
            catch { }
            
            if (result != null)
            {
                string pretty = $"Name: {result.teamName}\n#:{result.teamNumber}\nUID:{result.uid}\nPlayers:{result.player1} & {result.player2}\nEmail:{result.email}\nScore:{result.score}";
                if (outputText != null) outputText.text = pretty;
                else Debug.Log(pretty);
            }
            else
            {
                if (outputText != null) outputText.text = $"GET raw: {json}";
                else Debug.Log($"GET raw: {json}");
            }
        }
    }

    private IEnumerator GetTeamData(string pathOverride)
    {
        string url = BuildUrl(pathOverride);

        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 15;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"GET failed: {request.error}");
                if (uidStatusText != null) uidStatusText.text = "Network error. Please try again.";
                else if (outputText != null) outputText.text = $"GET failed: {request.error}";
                yield break;
            }

            string json = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                // UID not present
                if (verificationPanel != null) verificationPanel.SetActive(false);
                if (uidStatusText != null) uidStatusText.text = "UID not found. Please check and try again.";
                else if (outputText != null) outputText.text = "UID not found. Please check and try again.";
                yield break;
            }
            TeamDataRTDB result = null;
            try { result = JsonUtility.FromJson<TeamDataRTDB>(json); } catch { }

            if (result != null)
            {
                // Activate and populate verification panel
                if (verificationText != null)
                {
                    verificationText.text =
                        $"Team Details:\n\n" +
                        $"Team Name: {result.teamName}\n" +
                        $"Team Number: {result.teamNumber}\n" +
                        $"Players: {result.player1} & {result.player2}\n" +
                        $"Email: {result.email}\n" +
                        $"UID: {result.uid}\n\n" +
                        $"Please verify this information is correct.";
                }
                if (verificationPanel != null) verificationPanel.SetActive(true);
                // Hide registration panel to avoid overlap
                if (treasureHuntManager != null && treasureHuntManager.registrationPanel != null)
                {
                    treasureHuntManager.registrationPanel.SetActive(false);
                }

                if (outputText != null)
                {
                    outputText.text = $"Fetched: {result.teamName} (#{result.teamNumber})";
                }

                _lastFetched = result;
            }
            else
            {
                if (outputText != null) outputText.text = $"GET raw: {json}";
                else Debug.Log($"GET raw: {json}");
            }
        }
    }

    private TeamDataRTDB _lastFetched;

    private void OnIVerifiedClicked()
    {
        if (_lastFetched == null)
        {
            if (outputText != null) outputText.text = "No data to verify";
            return;
        }

        var team = new TeamData
        {
            teamNumber = _lastFetched.teamNumber,
            uid = _lastFetched.uid,
            teamName = _lastFetched.teamName,
            player1 = _lastFetched.player1,
            player2 = _lastFetched.player2,
            email = _lastFetched.email,
            score = _lastFetched.score
        };

        if (treasureHuntManager != null)
        {
            treasureHuntManager.ReceiveExternalTeamData(team);
            // Immediately proceed as if the user confirmed in the core flow
            treasureHuntManager.OnVerifyTeam();
        }
        else
        {
            Debug.LogWarning("TreasureHuntManager not assigned/found. Cannot proceed to hunt.");
            if (outputText != null) outputText.text = "Manager missing. Assign TreasureHuntManager.";
        }

        if (verificationPanel != null)
        {
            verificationPanel.SetActive(false);
        }
    }

    private void LogError(string message)
    {
        Debug.LogError($"[FirebaseRTDBFetcher] {message}");
    }
}


