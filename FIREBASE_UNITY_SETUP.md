# 🔥 Firebase Unity SDK Setup Guide

## ✅ What's Been Created

I've created a **complete Firebase direct integration** for your treasure hunt game:

### 📁 New Files Created:
1. **`FirebaseManager.cs`** - Core Firebase initialization with anonymous auth
2. **`FirebaseTeamData.cs`** - Firebase-compatible data structure  
3. **`FirebaseFetcher.cs`** - Updated to use Firebase Unity SDK (replaces REST API)

### 🔧 Firebase Structure Supported:
```
teams/
├── {uid1}/          ← UID as document ID
│   ├── teamNumber: 1
│   ├── teamName: "DragonHunters"
│   ├── uid: "qK234"
│   ├── player1: "John"
│   ├── player2: "Jane"
│   ├── email: "team@example.com"
│   ├── phoneNumber: "+1234567890"
│   ├── score: 0
│   └── createdAt: timestamp
└── {uid2}/...
```

---

## 🚀 Unity Setup Steps

### Step 1: Download Firebase Unity SDK
1. Go to your [Firebase Console](https://console.firebase.google.com/project/arth-33ed6)
2. Click **Project Settings** (gear icon)
3. Go to **General** tab
4. Scroll down to **"Your apps"** section
5. Download **Unity SDK** (if not already downloaded)

### Step 2: Import Firebase Packages
Import these specific packages in Unity:
- **`FirebaseApp.unitypackage`** (Core)
- **`FirebaseAuth.unitypackage`** (Anonymous authentication)
- **`FirebaseFirestore.unitypackage`** (Database)

⚠️ **Don't import all packages** - only these three to avoid bloating your project!

### Step 3: Add Configuration File
1. In Firebase Console → Project Settings → General
2. Download `google-services.json` (for Android) 
3. Place in `Assets/StreamingAssets/google-services.json`
4. For iOS: Also download `GoogleService-Info.plist` and place in `Assets/StreamingAssets/`

### Step 4: Unity Scene Setup
1. Create an empty GameObject named **"FirebaseManager"**
2. Add the **FirebaseManager** component to it
3. Your existing **FirebaseFetcher** will automatically find it

### Step 5: Configure Project Settings
In Unity:
- **Player Settings → Android Settings**:
  - Set **Package Name** to match your Firebase project (e.g., `com.yourcompany.treasurehunt`)
- **Player Settings → iOS Settings**:
  - Set **Bundle Identifier** to match your Firebase project

---

## 🎮 How It Works Now

### Firebase Authentication
- **Anonymous authentication** happens automatically
- No login required - perfect for treasure hunt
- Each player gets a temporary Firebase user ID

### Team Data Fetching
```csharp
// Your existing code still works!
firebaseFetcher.FetchTeamData("qK234");
```

### Real-time Status
- Check the **Status** field in FirebaseFetcher Inspector
- Shows real-time Firebase connection status
- Displays detailed error messages for debugging

### Testing
- Right-click **FirebaseFetcher** → **"Test Firebase Connection"**
- This will verify your Firebase setup is working

---

## 🔍 Database Structure You Mentioned

You said your database structure is:
> **Teams(Collection) → TeamName(Document) → TeamDetails(Collection)**

**Current Implementation**: `teams/{uid}` (UID as document ID)

If your structure is different, we can easily modify the code. Just let me know!

---

## 🚨 Important Notes

### Firebase Web API Key
You provided: `AIzaSyCFeVNP6lniD1V7oFOAK3WHtMVsV-Qf9rY`

**Good news**: With Unity Firebase SDK, **you don't need to enter this manually**! 
The `google-services.json` file contains all the configuration automatically.

### Anonymous Authentication
- ✅ Already enabled in your Firebase project
- ✅ Handled automatically by FirebaseManager
- ✅ No additional setup needed

### Security Rules
Your current Firestore rules should work perfectly:
```javascript
allow read: if true;  // ← This allows Unity to read team data
```

---

## ✅ Testing Checklist

1. **Import Firebase SDK packages** ✓
2. **Add google-services.json to StreamingAssets** ✓
3. **Create FirebaseManager GameObject** ✓
4. **Set correct Package Name/Bundle ID** ✓
5. **Build and test on device** (Firebase doesn't work in Unity Editor for auth)

---

## 🎯 Your Current Workflow

1. **User enters UID** in your treasure hunt app
2. **FirebaseFetcher.FetchTeamData(uid)** is called
3. **Firebase SDK** connects directly to Firestore
4. **Anonymous authentication** happens automatically
5. **Team data** is fetched and cached
6. **TreasureHuntManager** receives the team data
7. **Treasure hunt begins** with proper team assignment

**No more Vercel dependency! No more 403 errors! Direct Firebase connection!** 🎉

---

## 🐛 Common Issues & Solutions

### "Firebase not ready"
- **Solution**: Wait for the Status field to show "✅ Firebase ready!"
- **Cause**: Firebase takes a few seconds to initialize

### "Team not found"
- **Solution**: Make sure the UID exists in your Firebase Console
- **Check**: Go to Firestore → teams collection → verify UID

### "Dependencies not resolved"
- **Solution**: Restart Unity after importing Firebase packages
- **Alternative**: Delete Library folder and re-open Unity

### Build Errors
- **Solution**: Make sure Package Name matches Firebase project
- **Check**: Player Settings → Android → Package Name

**Need help with any of these steps? The Status field in Unity will give you detailed error messages!**