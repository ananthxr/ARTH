# Firebase Android Setup Guide

## Android Gradle Configuration Issues Fix

The popup you saw is Unity asking to resolve Android dependencies for Firebase. Here's how to fix it:

### Step 1: Enable Android Resolution
1. Go to `Assets > External Dependency Manager > Android Resolver > Settings`
2. Check these options:
   - ✅ Auto-resolution
   - ✅ Enable Background Resolution
   - ✅ Enable Jetifier
   - ✅ Use Jetifier

### Step 2: Force Resolve Dependencies
1. Go to `Assets > External Dependency Manager > Android Resolver > Force Resolve`
2. Wait for the process to complete (may take several minutes)
3. You should see Firebase libraries in `Assets/Plugins/Android/`

### Step 3: Unity Player Settings for Firebase
1. Go to `File > Build Settings > Player Settings`
2. Under `Android Settings`:
   - Set `Minimum API Level` to at least **21** (Android 5.0)
   - Set `Target API Level` to **33** or higher
   - Under `Configuration`:
     - Set `Scripting Backend` to **IL2CPP**
     - Set `Api Compatibility Level` to **.NET Standard 2.1**
   - Under `Publishing Settings`:
     - Enable `Custom Main Gradle Template`
     - Enable `Custom Gradle Properties Template`

### Step 4: Fix Common Gradle Issues
If you get build errors, add these to `Assets/Plugins/Android/mainTemplate.gradle`:

```gradle
android {
    compileOptions {
        sourceCompatibility JavaVersion.VERSION_1_8
        targetCompatibility JavaVersion.VERSION_1_8
    }
}

dependencies {
    implementation 'com.google.firebase:firebase-firestore:24.4.4'
    implementation 'com.google.firebase:firebase-auth:21.1.0'
    // Add other Firebase dependencies as needed
}
```

### Step 5: Add gradle.properties fixes
In `Assets/Plugins/Android/gradleTemplate.properties`, add:
```
android.useAndroidX=true
android.enableJetifier=true
org.gradle.jvmargs=-Xmx4096m
```

### Step 6: Test Firebase Readiness
Before testing on device:
1. Make sure `google-services.json` is in `Assets/StreamingAssets/`
2. Build and test the Firebase initialization logs
3. Check that `IsFirebaseReady()` returns true

## Common Error Solutions

### Error: "Firebase app has not been created"
- Solution: The new initialization code with timeout should fix this

### Error: "Main thread violation"
- Solution: The UnityMainThreadDispatcher now handles all UI updates

### Error: "Task cancelled"
- Solution: New timeout protection prevents indefinite waits

### Error: "Gradle build failed"
- Solution: Follow the Gradle configuration steps above

## Testing Checklist
- [ ] Firebase initializes without timeout
- [ ] Team data fetches successfully
- [ ] UI updates don't cause crashes
- [ ] App doesn't crash when entering UID
- [ ] Error messages display properly

## Build Settings Verification
Before building to device:
1. Check that Android dependencies are resolved
2. Verify minimum API level is 21+
3. Ensure IL2CPP backend is selected
4. Confirm Firebase config files are included

The fixes in the code should prevent the mobile crashes. The Android Gradle configuration is crucial for Firebase to work properly on device builds.