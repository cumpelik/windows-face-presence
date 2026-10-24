# Face Presence

Windows 11 tray app (C# / .NET 8, WPF with WPF UI Fluent controls; WinForms only for the tray NotifyIcon) that recognizes the enrolled user with the camera (Windows Hello IR camera
preferred) and defers the automatic Windows lock while they are present. Original brief: docs/brief.md, user docs:
README.md (English) and README.cs.md (Czech) – keep both in sync.

- Build: `dotnet build src/FacePresence/FacePresence.csproj -c Release` (works on Linux too, `EnableWindowsTargeting`).
  When working over an SMB share, copy the project to a local scratch folder and build there (bin/obj must not land
  on the share).
- Release build: `publish.cmd [version]` → `build/windows/build.ps1` (single-file, win-x64). Signing via Azure Artifact
  Signing is enabled by the `ARTIFACT_SIGNING_*` and `AZURE_*` environment variables; GitHub Actions `release.yml`
  takes them from repository secrets.
- Camera access goes exclusively through WinRT `Windows.Media.Capture.Frames` (OpenCV `VideoCapture` cannot see the IR
  camera).
- The OpenCvSharp 4.13 NuGet package has no `FaceRecognizerSF` and no `FaceDetectorYN.SetInputSize` – SFace runs through
  `CvDnn` with our own alignCrop (Umeyama), and the detector is recreated when the frame size changes.
- UI texts live in `Localization/L.cs` (English default, Czech), accessed via `L.T("…")`; every new text needs both
  languages. Code comments and logs are in English.
- Public repository (MIT), github.com/cumpelik/windows-face-presence. Component licenses: THIRD-PARTY-NOTICES.txt
  (embedded in the EXE, shown from About). The FFmpeg plugin of the OpenCvSharp runtime is excluded in the csproj (LGPL,
  not used) – re-check the notices whenever a dependency changes.
- UI is WPF + WPF UI (`ui:FluentWindow`, Mica, light/dark follows the system). Texts are bound in XAML via
  `{Binding [key], Source={x:Static loc:Loc.I}}`, so a language switch needs no rebuild. Leave layout to Grid /
  StackPanel / UniformGrid – no code that computes sizes. WinForms and System.Drawing are not global usings
  (type name clashes); import them explicitly where needed (tray only).
