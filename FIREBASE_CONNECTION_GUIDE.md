# Firebase Connection Guide for Unity

## Quick Fix for Your 404 Error

You have **2 options** to connect Unity to your Firebase data:

---

## Option 1: Fix Vercel API URL (Easiest)

### Problem
Your `apiBaseUrl` is pointing to the wrong URL format.

### Solution
1. In Unity, select your **FirebaseFetcher** component
2. Set **Connection Method** to `VercelAPI`
3. Update **API Base URL** to:
   ```
   https://your-actual-app-name.vercel.app/api
   ```
   ⚠️ **IMPORTANT**: Make sure to:
   - Replace `your-actual-app-name` with your real Vercel app name
   - Include `/api` at the end
   - Don't include a trailing slash

### Example
If your Vercel app is deployed at `https://arfull-treasure-hunt.vercel.app`, then use:
```
https://arfull-treasure-hunt.vercel.app/api
```

### How to find your Vercel URL:
1. Go to [Vercel Dashboard](https://vercel.com/dashboard)
2. Click on your project
3. Copy the URL shown under your project name

---

## Option 2: Direct Firebase Connection (Recommended - Faster)

### Why This is Better
- No web server needed
- Faster response times
- Works even if your Vercel app is down
- Direct connection to Firebase

### Setup Steps
1. In Unity, select your **FirebaseFetcher** component
2. Set **Connection Method** to `DirectFirebase`
3. Set **Firebase Project ID** to your actual Firebase project ID

### How to find your Firebase Project ID:
1. Go to [Firebase Console](https://console.firebase.google.com)
2. Click on your project
3. Go to Project Settings (gear icon)
4. Copy the **Project ID** (not the project name)

### Example
If your Firebase project ID is `arfull-treasure-hunt-abc123`, then use:
```
arfull-treasure-hunt-abc123
```

---

## Testing Your Connection

### Test with a Real UID
1. Go to your web registration page
2. Register a test team
3. Copy the UID that's generated
4. In Unity, enter that UID and test

### Check Firebase Console
1. Go to [Firebase Console](https://console.firebase.google.com)
2. Click **Firestore Database**
3. You should see a `teams` collection with documents
4. Each document ID should be a UID (like `qK234`)
5. Each document should contain team data

---

## Common Issues & Solutions

### 404 Error
- **Vercel API**: Wrong URL format or app not deployed
- **Direct Firebase**: Wrong project ID

### 403 Forbidden
- Check your Firestore security rules
- Make sure they allow read access to the `teams` collection

### Network Timeout
- Check your internet connection
- Try the other connection method

### Data Format Issues
- Verify your Firebase documents have the expected fields
- Check the Unity console for detailed error messages

---

## Which Method Should You Use?

### Use **Vercel API** if:
- Your web app is working fine
- You want to keep all Firebase logic on the server
- You plan to add authentication later

### Use **Direct Firebase** if:
- You're getting 404 errors with the API
- You want faster performance
- You want simpler debugging

---

## Quick Test Commands

You can test your endpoints manually:

### Test Vercel API:
Open in browser: `https://your-app.vercel.app/api/team?uid=SOME_REAL_UID`

### Test Direct Firebase:
Open in browser: `https://firestore.googleapis.com/v1/projects/YOUR_PROJECT_ID/databases/(default)/documents/teams/SOME_REAL_UID`

Replace `YOUR_PROJECT_ID` and `SOME_REAL_UID` with real values.

---

## Need Help?

1. **Check Unity Console** for detailed error messages
2. **Verify your Firebase data** in the Firebase Console
3. **Test with a real UID** from a team you registered
4. **Try both connection methods** to see which works better

The **Direct Firebase** method is recommended because it's simpler and more reliable!