<p align="center"><img src="docs/icon.png" width="96" alt="Ikona Still Here"></p>

<h1 align="center">Still Here</h1>

<p align="center"><b>Windows antilock, který funguje jen když sedíte u počítače</b></p>

<p align="center">
Nenechá Windows zamknout, dokud před kamerou sedíte <b>vy</b>, a když odejdete, nechá je zamknout normálně.<br>
Lokální rozpoznání obličeje, podpora IR kamery Windows Hello, jedno EXE bez instalace, žádný cloud.
</p>

<p align="center"><a href="https://github.com/cumpelik/windows-still-here/releases/latest/download/StillHere.exe"><b>⬇ Stáhnout StillHere.exe</b></a> · <a href="README.md">English</a></p>

<p align="center"><img src="docs/screenshot.png" width="760" alt="Hlavní okno Still Here"></p>

---

## Proč

Krátký limit nečinnosti je dobrá bezpečnost, ale otravuje, když čtete, přemýšlíte nebo telefonujete bez dotyku
klávesnice. Nástroje typu „mouse jiggler“ to řeší tím, že bezpečnost vypnou: počítač zůstane odemčený, i když odejdete.
Still Here odkládá zamčení **jen dokud rozpoznává váš obličej**. Jakmile odejdete, Windows se zamknou přesně podle
nastavení.

## Funkce

- **Neruší videohovory.** Když kameru používá jiná aplikace, Still Here se pozastaví a Windows zamykají jako obvykle.
- **Rozpozná vás, ne libovolný obličej.** Jednou si naskenujete obličej, cizí lidé před kamerou zamčení neodloží.
- **Přednostně IR kamera Windows Hello.** Funguje i potmě a hůř se oklame obrázkem na displeji. Bez IR kamery se
  použije běžná webkamera.
- **Běží lokálně.** Detekce (YuNet) i rozpoznání (SFace) běží na procesoru přes OpenCV. Nic se nikam neposílá.
- **Adaptivní kontrola.** Každých 10 s, když jste rozpoznáni, každou 1 s, když ne (obojí nastavitelné).
- **Skutečný zámek Windows.** Žádná vlastní zamykací obrazovka, nic se neodemyká, Win+L funguje vždy.
- **Ukazuje, kdy by se Windows zamkly** – přečte zásady nečinnosti, zabezpečený spořič, vypnutí displeje a uspání a
  při vaší nepřítomnosti odpočítává.
- Vzhled Windows 11 (Fluent, Mica, světlý i tmavý režim podle systému), ikona v liště podle stavu, živý náhled se
  skóre shody, vestavěná diagnostika, angličtina a čeština, spuštění po přihlášení.

## Jak to funguje

1. Snímek z kamery se čte přes WinRT `MediaFrameReader` (jediné API, které IR kameru Windows Hello zpřístupní;
   DirectShow ani OpenCV `VideoCapture` ji nevidí). U IR kamer se používají jen osvětlené snímky.
2. YuNet najde největší obličej, ten se zarovná na 112×112 a SFace z něj spočítá 128rozměrný otisk.
3. Otisk se porovná (kosinová podobnost) s uloženým obličejem ze stejné kamery.
4. Dokud jste rozpoznáni, aplikace:
   - drží `SetThreadExecutionState(ES_DISPLAY_REQUIRED)` – displej zůstane zapnutý a počítač neusne, a
   - čte skutečnou nečinnost přes `GetLastInputInfo`; jen když se klávesnice ani myši chvíli nedotknete (výchozí 40 s,
     nejvýš 60 % zjištěného limitu zámku) **a** obličej byl právě ověřen, pošle jeden pohyb myši o nula pixelů přes
     `SendInput`. Kurzor se nepohne, ale Windows ho započítají jako aktivitu. Jen tak lze odložit zásadu *Interactive
     logon: Machine inactivity limit*, která nebere v úvahu nic jiného než vstup.
5. Když odejdete, kamera selže, aplikaci pozastavíte, session se zamkne nebo aplikaci ukončíte, vše se zastaví a
   Windows se zamknou podle svého. Při zamčení se kamera uvolní, aby ji mohlo použít Windows Hello.
6. Když kameru používá jiná aplikace (videohovor), Still Here se stáhne: uvolní kameru a přestane odkládat zamčení,
   takže hovor nikdy neruší a Windows se chovají přesně jako bez aplikace.

## Požadavky

- Windows 11 nebo Windows 10 (verze 2004 / build 19041 a novější), x64
- Kamera, doporučeně IR kamera Windows Hello
- Povolený přístup desktopových aplikací ke kameře v *Nastavení → Ochrana osobních údajů → Kamera*

## Začínáme

1. Stáhněte [`StillHere.exe`](https://github.com/cumpelik/windows-still-here/releases/latest/download/StillHere.exe) (vždy nejnovější verze) a spusťte ho. Je to jeden soubor, ve kterém
   je vše – nic se neinstaluje, ani .NET. První spuštění trvá pár sekund, než se rozbalí.
2. IR kamera se vybere automaticky. Klikněte na **Naskenovat obličej** a asi 5 s se dívejte do kamery.
3. Sledujte skóre v náhledu: váš obličej má být zřetelně nad prahem (výchozí 0,40). Když se k němu přiblíží někdo
   jiný, zvyšte práh na 0,45–0,50.
4. Případně zaškrtněte **Spouštět po přihlášení do Windows**.
5. **Diagnostika…** na vašem počítači ověří, že IR kamera dává obraz, že simulovaný vstup nuluje čítač nečinnosti a
   jaké zásady zamykání platí.

Zavření okna aplikaci jen schová do lišty. Okno vrátíte opětovným spuštěním EXE nebo dvojklikem na ikonu v liště. Aktualizace: stáhněte nový `StillHere.exe` přes starý.

## Bezpečnost a soukromí

- Still Here nikdy nemění bezpečnostní zásady systému a nikdy neodemyká Windows. Umí jen *odložit* automatické
  zamčení kvůli nečinnosti, dokud vás rozpoznává.
- **Chybí detekce živosti.** Dost dobrý obrázek vašeho obličeje před kamerou může počítač držet odemčený. IR kamera
  to ztěžuje, ale nevylučuje. Nepoužívejte Still Here tam, kde je to nepřijatelné riziko.
- Obličej se ukládá jako číselné otisky (žádné fotky) do `%LOCALAPPDATA%\StillHere\settings.json`,
  nešifrovaně a čitelně pro váš účet Windows. Nic se nikam neposílá.
- Na spravovaných počítačích bývá limit nečinnosti firemní zásada. Ověřte si, že vaše organizace takové nástroje
  dovoluje.

## Známá omezení

- *Dynamický zámek* (zamčení po odchodu telefonu přes Bluetooth) a zamykací agenti třetích stran fungují nezávisle a
  odložit je nelze.
- Windows blokují `SendInput`, když má fokus okno se zvýšenými oprávněními (UAC dotaz, aplikace spuštěná jako správce).
- Model rozpoznání byl trénován na barevných fotkách. Na IR obrazu funguje dobře, když obličej naskenujete na stejné kameře,
  ale skóre jsou nižší než u barevné kamery.
- Aplikace, které drží kameru otevřenou pořád (virtuální kamery, OBS, některé nástroje výrobců), nechávají Still Here
  pozastavený stejně jako videohovor. V okně je vidět, o kterou aplikaci jde.
- Jedno EXE si při startu rozbalí nativní knihovny do `%TEMP%\.net`. Kde AppLocker nebo WDAC zakazují spouštět kód
  z dočasných složek (běžné na spravovaných počítačích), Still Here nenaběhne.
- Bezpečnostní nástroje (EDR) mohou simulovaný vstup označit jako „mouse jiggler“, přestože se posílá jen po ověření
  obličeje.
- Dokud vás aplikace rozpoznává, chatovací aplikace jako Teams nebo Slack vás ukazují jako dostupného – Windows berou
  simulovaný vstup jako aktivitu.
- Automatické aktualizace nejsou. Novou verzi stáhněte jako `StillHere.exe` přes tu starou.

## Build

```
dotnet build src/StillHere/StillHere.csproj -c Release
pwsh build/windows/build.ps1 -Version 0.1.0     # -> out/StillHere.exe
```

Build funguje i na Linuxu a macOS (`EnableWindowsTargeting`), spuštění vyžaduje Windows. `build.ps1` podepíše EXE přes
Azure Artifact Signing, když jsou nastavené proměnné prostředí `ARTIFACT_SIGNING_*` a `AZURE_*`; bez nich zůstane EXE
nepodepsané. GitHub Actions staví každý push (`build.yml`) a pro každý tag `v*` vydají release (`release.yml`);
secrets pro podpis bere release z prostředí `release`.

## Licence

MIT – viz [LICENSE](LICENSE). Přibalené komponenty (OpenCV, OpenCvSharp, modely YuNet a SFace a knihovny staticky
přilinkované do OpenCV) mají vlastní licence, jejich seznam s plnými texty je v
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) a v aplikaci pod *O aplikaci*.

## Poděkování

Vytvořeno ve spolupráci s [Claude](https://claude.ai) od Anthropicu. Díky [tabidoo.cloud](https://tabidoo.cloud), že
svým lidem umožňuje tvořit i takové utility.
