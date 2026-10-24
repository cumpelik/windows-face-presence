# FacePresence

Windows 11 tray app (C# / .NET 8 WinForms) – rozpoznání registrovaného uživatele kamerou (IR Windows Hello přednostně)
a odkládání automatického zamčení Windows. Zadání: docs/zadani.md, popis a stav: README.md.

- Build: `dotnet build src/FacePresence/FacePresence.csproj -c Release` (funguje i na Linuxu, `EnableWindowsTargeting`).
  Při práci přes SMB share kopíruj projekt do scratchpadu a builduj tam (bin/obj nepatří na share).
- Publish: `publish.cmd [verze]` → `build\windows\build.ps1` (single-file, win-x64). Podpis přes Azure Artifact Signing stejně jako BsonJet (TbdMongoClient/build) – zapne se proměnnými ARTIFACT_SIGNING_*; GitHub Actions release.yml je bere ze secrets.
- Kamera jde výhradně přes WinRT `Windows.Media.Capture.Frames` (OpenCV VideoCapture IR kameru nevidí).
- OpenCvSharp 4.13 NuGet nemá `FaceRecognizerSF` ani `FaceDetectorYN.SetInputSize` – SFace jede přes `CvDnn`
  s vlastním alignCrop (Umeyama) a detektor se při změně rozměru snímku vytváří znovu.
- Texty UI: `Localization/L.cs` (en výchozí, cs), klíče `L.T("…")`; nové texty vždy do obou jazyků. Logy anglicky.
