# Google Play Games Plugin (Fixed)

This is a fork of the official Play Games Unity plugin.  
**Except this one works.**

I fixed it because Unity 6 exists, and apparently nobody at Google noticed.

This repo exists for one reason:  
**My game broke. I fixed it. Maybe yours is broken too. You're welcome.**

---

## Why This Exists

The original plugin:

- Uses deprecated Unity APIs  
- Assumes 2019.4 is still relevant  
- Breaks on Unity 6 like a glass piano  
- Has 700+ open issues — a thriving monument to abandonment  

I:

- Got tired of warnings  
- Got tired of runtime crashes  
- Got tired of pretending Google maintains this  

So I rewrote chunks, patched method calls, updated API usages, and cleaned up all the `CS0618` spam.  
No duct tape. No band-aids. Just a working fork.

---

## Changes

- Updated `PlayerSettings.*(BuildTargetGroup.Android)` ➜ `NamedBuildTarget.Android`  
- Removed calls to extinct enum members (`PVRTC`, etc.)  
- Wrapped or replaced ~70 usages of deprecated interfaces like `IUserProfile`, `IScore`, `IAchievement`, etc.  
- Ensured JNI signatures align with the actual AARs (because "guess and crash" isn't a feature)  
- Runtime tested: login, achievement unlock, score submit — all verified on device

---

## What This Is Not

- A full rewrite  
- An official anything  
- A promise

---

## License

**None.**  
This is not a library.  
This is a **therapy exercise** that happens to compile.

If you use this in your project and it helps you? Neat.  
If it breaks your build? That's still an upgrade from the original.

---

## How to Use

- Clone or download the `.unitypackage`  
- Import it into Unity 6  
- Profit (or at least, **build successfully** without scrolling through 150 warnings about `ISocialPlatform`)

---

## The Future™

If Google ever updates the official plugin, I’ll probably **ignore it**.  
If something breaks in this version, feel free to open an issue.  
I might fix it. Might not. Don’t know yet.

---

## End

Have fun.  
Or don’t.  
It’s still more functional than the original.
