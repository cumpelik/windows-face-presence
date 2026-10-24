# Face Presence (POC)

Malá Windows 11 aplikace (C# / .NET 8, WinForms) v systémové liště, která kamerou rozpoznává **konkrétního
registrovaného uživatele** a dokud sedí před počítačem, odkládá automatické zamčení Windows kvůli nečinnosti.
Rozpoznávání běží jen lokálně. Zadání je v [docs/zadani.md](docs/zadani.md).

> **Stav:** kód je kompletní a přeložený (build i single-file publish prošly), ale vznikl na Linuxu bez možnosti
> spustit ho na Windows. První krok na cílovém stroji je tlačítko **Diagnostika…**, které oba kritické body
> ověří přímo na vašem hardwaru (viz níže).

## Ověření kritických bodů

### 1) Obraz z IR kamery Windows Hello – ANO, přes WinRT

- IR kamera Windows Hello je v systému vedená jako *senzorová kamera* (`KSCATEGORY_SENSOR_CAMERA`). **DirectShow ani
  OpenCV `VideoCapture` ji nevidí** (OBS/VLC ji ukazují černou). Přístupná je přes WinRT
  `Windows.Media.Capture.Frames`: `MediaFrameSourceGroup.FindAllAsync()` vrací zdroje s
  `SourceKind == Infrared`, `MediaCapture` + `MediaFrameReader` z nich čte snímky (typicky L8/L16/NV12,
  ~340×340 až 640×360, grayscale). Funguje to v běžné desktop aplikaci (.NET 8 s TFM
  `net8.0-windows10.0.19041.0`), žádný UWP balíček není potřeba. Existují hotové prohlížeče postavené přesně na tom
  (IRCameraView, windows-ir-camera-viewer).
- IR snímky se střídají **osvětlené / neosvětlené** (IR přísvit bliká). Aplikace čte
  `InfraredMediaFrame.IsIlluminated` a pro rozpoznání používá osvětlené snímky.
- Omezení:
  - **Windows Hello potřebuje tu samou kameru na zamykací obrazovce.** Aplikace proto při `SessionLock`
    kameru okamžitě uvolní a po odemčení ji znovu otevře (jinak by mohlo selhat přihlášení obličejem).
  - Musí být povolen přístup aplikací ke kameře (Nastavení → Soukromí → Kamera). Některé OEM ovladače IR kameru
    třetím stranám neexponují – pak Diagnostika žádný `Infrared` zdroj nenajde a použije se barevná kamera.
  - IR přísvit je u některých notebooků viditelný jako slabé červené blikání; aplikace proto při přítomnosti
    kontroluje jen jednou za 10 s (kamera ale zůstává otevřená, aby se LED/inicializace nestřídaly).
  - Model rozpoznávání (SFace) je trénovaný na barevných fotkách. Na IR funguje v režimu „registrace i ověření
    na stejné kameře“, přesnost je ale nižší než na barevné kameře – proto je práh shody nastavitelný a UI ukazuje
    aktuální skóre. Šablony se ukládají zvlášť pro IR a barevnou kameru.

### 2) Zabránění skutečnému automatickému zamčení – ANO, ale jen simulovaným vstupem

Windows zamyká session kvůli nečinnosti třemi cestami, které měří různé věci:

| Mechanismus | Co měří | Stačí `SetThreadExecutionState`? |
|---|---|---|
| Spořič se „Po obnovení zobrazit přihlašovací obrazovku“ | čas od posledního vstupu | ano (ES_DISPLAY_REQUIRED) |
| Vypnutí displeje / uspání + „Vyžadovat přihlášení po probuzení“ | display idle timer | ano (ES_DISPLAY_REQUIRED) |
| **GPO/Intune „Interactive logon: Machine inactivity limit“** (`InactivityTimeoutSecs`) | **jen klávesnice/myš** | **ne** – přehrávání videa ani power requesty ji neovlivní |

Firemní stroje mají typicky právě poslední politiku. Jediný způsob, jak ji „nevypnout a přesto nezamknout“, je
aby systém **viděl vstup**. Aplikace proto:

1. Při přítomnosti drží `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED)` (bez simulace vstupu).
2. Čte **skutečnou** nečinnost přes `GetLastInputInfo` a **pouze** když přesáhne práh (výchozí 40 s, musí být
   pod nejkratším limitem – politiky mají minimum 60 s), pošle jeden `SendInput` s relativním pohybem myši
   **0/0** (kurzor se nehne, čítač nečinnosti se resetuje). Když uživatel normálně pracuje, nic se neposílá.
   Vstup se posílá vždy až **po čerstvém rozpoznání obličeje** – smyčka se kvůli tomu probudí dřív, než
   by nečinnost přerostla práh.
3. Při odchodu, chybě kamery, pauze, zamčení nebo ukončení všechno uvolní → Windows zamknou standardně.

Bezpečnostní vlastnosti: používá se jen skutečný Windows lock, aplikace nic neodemyká (simulovaný vstup z uživatelské
session se na zamčenou plochu Winlogon ani nedostane), Win+L funguje vždy, žádná politika ani registr se nemění
(zapisuje se jen `HKCU\...\Run` pro autostart a vlastní nastavení v `%LOCALAPPDATA%\FacePresence`).

Známá omezení:
- **Dynamic Lock** (Bluetooth telefon) zamyká nezávisle na nečinnosti – neřešíme, Diagnostika na něj upozorní.
- Nástroje třetích stran (DLP, „idle lock“ agenti), které si měří nečinnost po svém, mohou zamknout i tak.
- `SendInput` je blokován UIPI, když má fokus okno s vyšší integritou (UAC dialog, admin aplikace) – záznam v logu.
- Firemní monitoring může simulovaný vstup klasifikovat jako „mouse jiggler“; proto je ho minimum a je vypínatelný.
- Diagnostika obsahuje test, zda nulový pohyb na daném systému resetuje `GetLastInputInfo`; pokud ne, je k dispozici
  záložní metoda (pohyb o 1 px a zpět).

## Jak to funguje (architektura)

```
src/FacePresence/
  Program.cs                 single-instance, --minimized, globální log výjimek
  App/TrayContext.cs         NotifyIcon, menu, SessionSwitch (lock/unlock), ukončení
  App/AppHost.cs             sdílené objekty + výběr/restart kamery
  Capture/CameraEnumerator   WinRT výčet kamer (IR první)
  Capture/FrameGrabber       MediaCapture + MediaFrameReader, osvětlené IR snímky, SoftwareBitmap → Mat
  Recognition/FaceEngine     YuNet (detekce) + SFace (128D embedding) přes OpenCV DNN, kosinová podobnost
  Presence/PresenceMonitor   smyčka: snímek → rozpoznání → keep-alive → adaptivní čekání; registrace obličeje
  Presence/ActivityKeeper    GetLastInputInfo, SetThreadExecutionState, minimální SendInput
  Settings/                  settings.json (intervaly, práh, šablony), autostart v HKCU\Run
  UI/MainForm, ProbeForm     stav, náhled, nastavení, registrace, diagnostika
  Diagnostics/Probe          ověření IR kamery, lock politik a keep-alive na cílovém stroji
  Models/*.onnx              vestavěné modely (YuNet 230 kB – MIT, SFace 37 MB – Apache 2.0, OpenCV Zoo)
```

UI je anglicky (výchozí) s přepínačem na češtinu v kartě Aplikace (`Localization/L.cs`, jedna tabulka klíčů).
Hlavička ukazuje odhad „Windows se zamknou za …“ – `Presence/LockPolicy.cs` vezme minimum z politiky
`InactivityTimeoutSecs`, zabezpečeného spořiče, vypnutí displeje (jen na Modern Standby, kde vypnutí displeje = uspání)
a uspání, vždy jen pokud je zapnuté „Vyžadovat přihlášení po probuzení“, a odečte skutečnou nečinnost. Je to odhad
pro orientaci, nic se nemění.

Smyčka: rozpoznán → další kontrola za `PresentIntervalSec` (10 s), jinak za `AbsentIntervalSec` (1 s). Registrace
nasbírá 10 vzorků (cca 5 s), uloží je jako šablony dané kamery (IR/Color); shoda = max. kosinová podobnost
ke všem šablonám ≥ práh (výchozí 0,40; SFace na LFW doporučuje 0,363).

## Build a distribuce

Požadavky: .NET 8 SDK (build jde i na Linuxu díky `EnableWindowsTargeting`), na cílovém PC Windows 10 19041+ /
Windows 11 a .NET 8 Desktop Runtime (nebo build s `-SelfContained`).

```
dotnet build src/FacePresence/FacePresence.csproj -c Release
publish.cmd 0.1.0        # = pwsh build/windows/build.ps1 -Version 0.1.0  ->  out\FacePresence-0.1.0-win-x64.exe
```

Výsledek je jedno EXE (~160 MB – většinu tvoří nativní OpenCV; při prvním spuštění se rozbalí do `%TEMP%\.net`).
Data aplikace: `%LOCALAPPDATA%\FacePresence\` (`settings.json`, `log.txt`, `probe.txt`, `models\`, vzorky
`probe_IR.png` / `probe_Color.png`).

### Podepisování a release

`build/windows/build.ps1` podepíše EXE přes **Azure Artifact Signing** (Trusted Signing), když jsou nastavené proměnné
`ARTIFACT_SIGNING_ENDPOINT`, `ARTIFACT_SIGNING_ACCOUNT`, `ARTIFACT_SIGNING_PROFILE`, `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`,
`AZURE_CLIENT_SECRET` – stejná sada jako u BsonJet. SignTool i plugin se stáhnou z NuGet, na stroji nemusí být nic
nainstalované. Podpis se časově razítkuje a ověří (`Get-AuthenticodeSignature`), vedle EXE vzniká `.sha256`.

GitHub: `.github/workflows/build.yml` staví nepodepsaně při každém push/PR, `release.yml` při tagu `v*` postaví,
podepíše (secrets se stejnými jmény) a vytvoří GitHub Release s EXE. Podepsané je jen vnější single-file EXE;
nativní knihovny OpenCV rozbalené do `%TEMP%` zůstávají s původním podpisem (resp. bez něj), což stačí pro SmartScreen
a Defender, ne pro Smart App Control – tam by byl potřeba instalátor s podepsanými DLL jako u BsonJet.

## Postup ověření na Windows (testovací plán)

1. Spustit `FacePresence.exe`, otevřít **Diagnostika… → Spustit diagnostiku**, dívat se do kamery a nehýbat myší.
   Výstup říká: zda existuje `Infrared` zdroj a dá se z něj číst (+ zda je v něm detekován obličej), jaké lock
   politiky platí (`InactivityTimeoutSecs`, spořič, vypnutí displeje, Dynamic Lock) a zda udržovací vstup resetuje
   čítač nečinnosti.
2. Vybrat kameru (IR je předvybraná), kliknout **Registrovat obličej**.
3. Sledovat skóre v náhledu: vlastní obličej by měl být výrazně nad prahem, cizí pod ním; případně práh doladit.
4. Test zamykání: nastavit krátký limit (např. spořič 1 min se zabezpečením, nebo na firemním stroji ponechat
   politiku), sedět před PC bez dotyku klávesnice – nesmí se zamknout; odejít – musí se zamknout po standardním
   timeoutu. Win+L musí fungovat kdykoliv. Po zamčení a odemčení přes Windows Hello se musí sledování obnovit.
5. Ukončit aplikaci z lišty → stroj se musí zamknout standardně.
