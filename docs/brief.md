# Face Presence – original POC brief

This is the brief the project started from (translated from Czech).

Create a simple Windows 11 application in **C# / .NET 8** that uses the camera to detect the user's presence and
prevents the automatic Windows lock while they sit at the computer.

### Functionality
- Recognize a specific user, not just the presence of any face.
- Prefer the **IR camera (Windows Hello)** when available, otherwise use a regular camera.
- Recognition runs strictly locally, no cloud.
- Adaptive checking:
  - User recognized → next check in **10 seconds**.
  - User not recognized → next check in **1 second**.
  - Both intervals configurable.
- While the user is present, prevent the automatic Windows lock caused by inactivity.
- When the user leaves, keep the standard Windows timeout and locking.
- Minimize simulated input – use e.g. `GetLastInputInfo` and `SendInput`.

### Security
- Use only the **real Windows lock**, no custom lock screen.
- Never unlock Windows automatically.
- `Win + L` must always work.
- On camera failure or application exit, stop keeping the session active. Windows then locks normally.
- Do not change system security policies.

### Application
- Runs in the background in the system tray.
- Simple face enrollment.
- Camera selection, interval settings, current status display.
- Automatic start after sign-in.
- Distributed as a single EXE.

### Implementation
Keep the solution as simple as possible. Use OpenCV / ONNX or a more suitable local technology.

**First verify two critical points:**
1. Whether frames can be obtained from the Windows Hello IR camera.
2. Whether the real automatic Windows lock can be reliably prevented while presence is detected, without disabling
   security policies.

If something is not technically possible, explain the limitation and propose an alternative. Do not build a fake
solution that merely covers the lock screen.

The goal is a small, practical and reliable application, not a large framework.
