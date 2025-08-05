# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity-based AR treasure hunt game that combines GPS location tracking with AR image recognition. The system allows teams to register, receive timed delays based on team numbers, follow GPS clues to locations, and use AR to find virtual treasures.

## Core Architecture

### Main Components

1. **TreasureHuntManager** (`TreasureHuntManager.cs`) - Core game logic controller
   - Manages team registration and wave-based timing system
   - Handles GPS proximity detection using Niantic Lightship AR
   - Controls UI state transitions between registration, timer, clue, and AR scan panels
   - Implements Haversine formula for distance calculations

2. **ClueARImageManager** (`ClueARImageManager.cs`) - AR image tracking system
   - Manages AR image detection and 3D object spawning
   - Maps reference images to treasure prefabs and clue indices
   - Handles AR tracking states and object visibility
   - Pre-instantiates AR objects for performance

### Key Data Structures

- **TreasureLocation**: Contains clue text, latitude, and longitude for each treasure
- **ClueARData**: Maps clue indices to AR reference images and 3D prefabs

### Technology Stack

- Unity 3D game engine
- AR Foundation for cross-platform AR capabilities
- Niantic Lightship AR for GPS/world positioning
- TextMeshPro for UI text rendering
- XR Interaction Toolkit for AR interactions

## Development Workflow

### Unity-Specific Commands

This is a Unity project, so development typically happens through the Unity Editor:

- **Build for Android**: File > Build Settings > Android > Build
- **Build for iOS**: File > Build Settings > iOS > Build and Run
- **Test in Play Mode**: Press Play button in Unity Editor
- **Scene Management**: Load `ARTH_IMP_V1.unity` for the main scene

### Key Files and Locations

- Main Scene: `Assets/ARTH_Implementation_V1/ARTH_IMP_V1.unity`
- Scripts: `Assets/ARTH_Implementation_V1/V1/Scripts V1/`
- AR Reference Images: `Assets/ARTH_Implementation_V1/V1/Reference_Library/`
- 3D Models: `Assets/Import/` (contains treasure prefabs)

## Architecture Patterns

### Team Assignment Logic
Teams are assigned to waves and clues using modular arithmetic:
- `wave = (teamNumber - 1) / totalClues`
- `clueIndex = (teamNumber - 1) % totalClues`
- `delayMinutes = wave * 5`

### State Management
The system uses a centralized state machine pattern:
1. Registration Panel → Timer Panel → Clue Panel → AR Scan Panel
2. GPS proximity triggers transition from clue to AR mode
3. AR object spawning is tied to clue progression

### Performance Optimizations
- AR objects are pre-instantiated and toggled rather than created/destroyed
- Only the current active clue's AR content is displayed
- GPS tracking is activated only during active hunts

## Key Integrations

### Niantic Lightship AR
- `ARWorldPositioningCameraHelper` provides GPS coordinates
- Requires proper Lightship SDK configuration
- Used for world-scale positioning and GPS tracking

### AR Foundation
- `ARTrackedImageManager` handles image detection
- `ARCameraManager` manages AR camera functionality
- Supports both ARCore (Android) and ARKit (iOS)

## Common Development Tasks

When modifying treasure locations:
1. Update the `treasureLocations` array in TreasureHuntManager
2. Ensure corresponding AR reference images exist in the Reference_Library
3. Configure matching ClueARData entries with proper clue indices

When adding new AR content:
1. Create/import 3D prefabs for treasures
2. Add reference images to the AR Reference Image Library
3. Configure ClueARData mappings in ClueARImageManager
4. Test AR tracking in device builds (not editor)

## Important Notes

- AR functionality requires device testing - Unity Editor simulation is limited
- GPS coordinates use double precision for accuracy
- Team numbering starts from 1, but array indices are 0-based
- The proximity threshold (default 10m) determines when GPS switches to AR mode
- Pre-instantiated AR objects improve performance but increase memory usage

## Recent Updates & Debugging

### Mobile Debug System (Latest)
Both TreasureHuntManager and ClueARImageManager now have mobile debug capabilities:
- Added `mobileDebugText` field (TMP_Text) for on-screen debugging
- All Debug.Log calls also output to mobile TextMeshPro component
- ClueARImageManager has detailed step-by-step AR spawning debug messages

### Critical Index Configuration
**IMPORTANT**: ClueARData array must use 0-based indices:
- First clue: `clueIndex = 0` (not 1)
- Second clue: `clueIndex = 1` (not 2)
- This matches the team assignment calculation: `clueIndex = (teamNumber - 1) % totalClues`

### Recent Fixes & Current System Behavior

#### Treasure Collection System (FIXED - August 2025)
**Issue**: Collected treasures would reappear when camera moved away and back to marker
**Solution**: 
- Added `collectedClueIndices` HashSet in ClueARImageManager to track collected treasures
- Modified `UpdateTrackedImage()` to check if treasure already collected before spawning
- Removed scale reset in `TreasureHuntManager.ShrinkAndCollectTreasure()` 
- Added `MarkTreasureAsCollected()` method called after collection
- **Result**: Once collected, treasures never respawn even if marker is detected again

#### Collect Button Visibility (FIXED - August 2025)
**Issue**: "Collect Treasure" button remained visible when camera moved away from marker
**Solution**:
- Modified tracking state logic from `else if` to `else` in `UpdateTrackedImage()`
- Button now hides for ANY non-tracking state (Limited, None, etc.)
- Added button hiding in `ClearAllARObjects()` method
- **Result**: Button only visible when AR object is actively tracking and visible

#### Current System State
1. **AR Object Spawning**: Fully working with comprehensive debugging
2. **Start Hunt Button**: Properly disappears when clicked using `gameObject.SetActive(false)`
3. **GPS Status Messages**: WPS status checking temporarily disabled due to camera access issues
4. **Performance**: Update() method in ClueARImageManager optimized to run every 10 frames
5. **Treasure Collection**: Prevents duplicate collection and object respawning
6. **Collect Button**: Dynamically shows/hides based on AR tracking state

### Debug Messages to Watch For
When AR objects aren't spawning, look for these step-by-step messages:
- `"STEP 1: Detected ImageName, State: Tracking"` - Image detected
- `"STEP 2 PASSED: AR object exists for ImageName"` - AR object found
- `"STEP 3 PASSED: Clue data exists for ImageName"` - Clue data found  
- `"STEP 4 FAILED: Clue X already collected"` - Treasure already collected (NEW)
- `"STEP 4 PASSED: Clue X matches active X"` - Index matches
- `"STEP 5: Trying to spawn AR object..."` - About to spawn
- `"SUCCESS! AR OBJECT SPAWNED!"` - Success
- `"STEP 5 FAILED: Tracking state not good enough: [State]"` - Lost tracking, button hidden

### Setup Checklist for New Clues
1. AR Reference Image Library contains the marker image
2. ClueARData array has entry with correct 0-based `clueIndex`
3. `referenceImageName` matches exact name in AR Reference Image Library (case-sensitive)
4. `treasurePrefab` is assigned to a 3D object
5. TreasureLocations array has corresponding entry at same index
6. Mobile debug TextMeshPro assigned to both managers for testing

## Technical Implementation Details

### Treasure Collection Flow (Current Implementation)
1. **AR Detection**: `ClueARImageManager.UpdateTrackedImage()` detects marker
2. **Validation Chain**:
   - Check if AR object exists for detected image
   - Check if clue data exists for image
   - **NEW**: Check if treasure already collected (`collectedClueIndices.Contains()`)
   - Check if clue index matches currently active clue
3. **Object Spawning**: If all checks pass, AR object appears and collect button shows
4. **Collection Process**: 
   - User clicks collect button → `TreasureHuntManager.OnCollectTreasure()`
   - Object scales down to zero over 0.5 seconds
   - Object deactivated but scale NOT reset (prevents reuse)
   - `ClueARImageManager.MarkTreasureAsCollected()` called to prevent respawning
   - Clue counter incremented, congrats panel shown

### Button Visibility Logic (Current Implementation)
- **Show Collect Button**: Only when `TrackingState.Tracking` and AR object is active
- **Hide Collect Button**: 
  - Any non-tracking state (Limited, None, etc.) 
  - When `ClearAllARObjects()` is called
  - When clue is already collected
  - When switching between clues/panels

### Key Methods & Their Purposes

#### TreasureHuntManager.cs
- `OnCollectTreasure(GameObject)`: Triggers shrink animation and marks as collected
- `ShrinkAndCollectTreasure(GameObject)`: Coroutine that shrinks object and handles collection
- `OnNextTreasure()`: Cycles to next clue, resets UI state
- `GetRemainingClues()`: Returns uncollected clue count

#### ClueARImageManager.cs  
- `UpdateTrackedImage(ARTrackedImage)`: Main AR detection and spawning logic
- `SetActiveClue(int)`: Sets which clue index should spawn AR objects
- `MarkTreasureAsCollected(int)`: Prevents respawning of collected treasures
- `ClearAllARObjects()`: Hides all AR objects and collect button
- `EnableARTracking()` / `DisableARTracking()`: Controls AR system state

### Data Persistence & State Management
- `collectedClueIndices`: HashSet tracking which treasures have been collected
- `currentActiveClueIndex`: Which clue should currently spawn AR objects
- `arObjects`: Dictionary of pre-instantiated GameObjects keyed by image name
- `cluesFound`: Counter in TreasureHuntManager for completion tracking

### Critical Architectural Decisions
1. **Pre-instantiation**: All AR objects created at Start() for performance
2. **State-based spawning**: Only current active clue can spawn AR objects
3. **Collection persistence**: Once collected, treasures cannot respawn
4. **Dynamic button management**: Collect button tied directly to AR object visibility
5. **Scale preservation**: Collected objects remain shrunk to indicate used state