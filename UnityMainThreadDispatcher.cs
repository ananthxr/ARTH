using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher instance;
    private static Queue<Action> actionQueue = new Queue<Action>();
    private static readonly object lockObject = new object();

    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("UnityMainThreadDispatcher");
                instance = go.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    public void Enqueue(Action action)
    {
        if (action == null) return;
        
        lock (lockObject)
        {
            actionQueue.Enqueue(action);
        }
    }

    void Update()
    {
        // Execute all queued actions on the main thread
        while (actionQueue.Count > 0)
        {
            Action action = null;
            
            lock (lockObject)
            {
                if (actionQueue.Count > 0)
                    action = actionQueue.Dequeue();
            }
            
            if (action != null)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error executing main thread action: {e.Message}");
                }
            }
        }
    }
}