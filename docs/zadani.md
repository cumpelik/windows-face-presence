# Face Presence – zadání POC

Vytvoř jednoduchou Windows 11 aplikaci v **C# / .NET 8**, která pomocí kamery rozpoznává přítomnost uživatele a zabraňuje automatickému zamčení Windows, dokud sedí před počítačem.

### Funkčnost
- Rozpoznávání konkrétního uživatele, ne pouze přítomnosti libovolného obličeje.
- Přednostně využít **IR kameru (Windows Hello)**, pokud je dostupná. Jinak běžnou kameru.
- Rozpoznávání běží výhradně lokálně, bez cloudu.
- Adaptivní kontrola:
  - Uživatel rozpoznán → další kontrola za **10 sekund**.
  - Uživatel nerozpoznán → další kontrola za **1 sekundu**.
  - Oba intervaly nastavitelné.
- Dokud je uživatel přítomen, zabránit automatickému zamčení Windows kvůli nečinnosti.
- Pokud uživatel odejde, ponechat standardní Windows timeout a zamykání.
- Minimalizovat simulované vstupy – využít například `GetLastInputInfo` a `SendInput`.

### Bezpečnost
- Používat výhradně **skutečný Windows Lock**, žádnou vlastní zamykací obrazovku.
- Nikdy automaticky neodemykat Windows.
- `Win + L` musí vždy fungovat.
- Při chybě kamery nebo ukončení aplikace přestat udržovat aktivitu. Windows se pak zamknou standardně.
- Neměnit systémové bezpečnostní politiky.

### Aplikace
- Běh na pozadí v systémové liště (tray).
- Jednoduchá registrace obličeje.
- Výběr kamery, nastavení intervalů, zobrazení aktuálního stavu.
- Automatické spuštění po přihlášení.
- Distribuce jako samostatné EXE.

### Implementace
Udržuj řešení maximálně jednoduché. Použij OpenCV / ONNX nebo vhodnější lokální technologii.

**Nejdříve ověř dva kritické body:**
1. Zda lze získat obraz z IR kamery Windows Hello.
2. Zda lze spolehlivě zabránit skutečnému automatickému uzamčení Windows při detekované přítomnosti, aniž by se vypnuly bezpečnostní politiky.

Pokud něco není technicky možné, vysvětli omezení a navrhni alternativu. Nevytvářej falešné řešení, které pouze překryje zamykací obrazovku.

Cílem je malá, praktická a spolehlivá aplikace, nikoli rozsáhlý framework.
