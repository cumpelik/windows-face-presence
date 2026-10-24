namespace FacePresence.Localization;

/// <summary>UI texts. English by default, Czech via the flag switch in the window (Settings.Language).</summary>
public static class L
{
    public static string Lang { get; private set; } = "en";
    public static event Action? Changed;

    public static void Set(string lang)
    {
        lang = lang == "cs" ? "cs" : "en";
        if (Lang == lang) return;
        Lang = lang;
        Changed?.Invoke();
    }

    public static string T(string key, params object[] args)
    {
        if (!Table.TryGetValue(key, out var pair)) return key;
        var s = Lang == "cs" ? pair.cs : pair.en;
        return args.Length == 0 ? s : string.Format(s, args);
    }

    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static readonly Dictionary<string, (string en, string cs)> Table = new()
    {
        // --- general / states ---
        ["app"] = ("Face Presence", "Face Presence"),
        ["already_running"] = ("Face Presence is already running (see the tray icon).", "Face Presence už běží (ikona v systémové liště)."),
        ["state.present"] = ("You're here – lock is being deferred", "Jste tu – zamčení se odkládá"),
        ["state.absent"] = ("Nobody recognized – Windows will lock normally", "Nikdo rozpoznán – Windows se zamknou standardně"),
        ["state.absent_in"] = ("Nobody recognized – Windows will lock in {0}", "Nikdo rozpoznán – Windows se zamknou za {0}"),
        ["state.not_enrolled"] = ("No face enrolled", "Obličej není registrován"),
        ["state.enrolling"] = ("Enrolling face…", "Registrace obličeje…"),
        ["state.camera_error"] = ("Camera unavailable – Windows will lock normally", "Kamera nedostupná – Windows se zamknou standardně"),
        ["state.locked"] = ("Session locked, camera released", "Session zamčena, kamera uvolněna"),
        ["state.paused"] = ("Paused", "Pozastaveno"),
        ["state.starting"] = ("Starting…", "Spouštím…"),
        ["state.stopped"] = ("Stopped", "Zastaveno"),

        // --- header stats ---
        ["stat.score"] = ("Score / threshold", "Skóre / práh"),
        ["stat.idle"] = ("Idle", "Nečinnost"),
        ["stat.next"] = ("Next check", "Další kontrola"),
        ["stat.lock"] = ("Lock after idle", "Zámek po nečinnosti"),
        ["stat.inputs"] = ("Simulated inputs", "Simulované vstupy"),
        ["stat.camera"] = ("Camera", "Kamera"),
        ["stat.none"] = ("–", "–"),
        ["stat.lock_unknown"] = ("not detected", "nezjištěn"),
        ["stat.inputs_val"] = ("{0} (last {1})", "{0} (naposledy {1})"),

        ["lock.policy"] = ("inactivity policy", "zásady nečinnosti"),
        ["lock.screensaver"] = ("secure screen saver", "zabezpečený spořič"),
        ["lock.display"] = ("display off", "vypnutí displeje"),
        ["lock.sleep"] = ("sleep", "uspání"),

        // --- header / buttons ---
        ["btn.pause"] = ("Pause", "Pozastavit"),
        ["btn.resume"] = ("Resume", "Pokračovat"),
        ["btn.diagnostics"] = ("Diagnostics…", "Diagnostika…"),
        ["btn.refresh"] = ("Refresh", "Obnovit"),
        ["btn.enroll"] = ("Enroll face", "Registrovat obličej"),
        ["btn.enroll_cancel"] = ("Cancel enrollment", "Zrušit registraci"),
        ["btn.clear"] = ("Clear enrollment", "Smazat registraci"),
        ["btn.data_folder"] = ("Data folder", "Složka s daty"),
        ["btn.about"] = ("About", "O aplikaci"),
        ["btn.run"] = ("Run diagnostics", "Spustit diagnostiku"),
        ["btn.copy"] = ("Copy to clipboard", "Kopírovat do schránky"),

        // --- cards ---
        ["card.camera"] = ("Camera", "Kamera"),
        ["card.enroll"] = ("Face enrollment", "Registrace obličeje"),
        ["card.presence"] = ("Presence check", "Kontrola přítomnosti"),
        ["card.app"] = ("Application", "Aplikace"),
        ["hint.camera"] = ("The Windows Hello IR camera is preferred – it works in the dark. Enrollment is stored per camera.",
                           "IR kamera Windows Hello má přednost – funguje i potmě. Registrace se ukládá zvlášť pro každou kameru."),
        ["hint.enroll"] = ("Enrollment collects 10 samples (about 5 s). Look at the camera and turn your head slightly. Stored separately for the IR and the color camera.",
                           "Registrace nasbírá 10 vzorků (cca 5 s). Dívejte se do kamery a mírně otáčejte hlavou. Ukládá se zvlášť pro IR a barevnou kameru."),
        ["hint.threshold"] = ("Cosine similarity. Your own face should score clearly above the threshold, other people below it. Recommended 0.40–0.50.",
                              "Kosinová podobnost. Vlastní obličej má být zřetelně nad prahem, cizí pod ním. Doporučeno 0,40–0,50."),
        ["hint.app"] = ("Closing the window only hides it to the tray. Only the real Windows lock is used, the app never unlocks and Win+L always works. On camera error, pause or exit Windows locks normally.",
                        "Zavření okna aplikaci jen skryje do lišty. Používá se výhradně skutečný zámek Windows, aplikace nikdy neodemyká a Win+L funguje vždy. Při chybě kamery, pauze nebo ukončení se Windows zamknou standardně."),
        ["lbl.present_interval"] = ("Check interval when present [s]", "Kontrola při přítomnosti [s]"),
        ["lbl.absent_interval"] = ("Check interval when absent [s]", "Kontrola při nepřítomnosti [s]"),
        ["lbl.threshold"] = ("Match threshold", "Práh shody"),
        ["chk.autostart"] = ("Start after Windows sign-in (minimized to tray)", "Spouštět po přihlášení do Windows (minimalizovaně do lišty)"),
        ["enroll.status_ok"] = ("✓  Your face is enrolled – {0} camera, {1}, {2} samples", "✓  Obličej je registrován – kamera {0}, {1}, {2} vzorků"),
        ["enroll.status_none"] = ("No face enrolled yet – until you enroll, the app never defers the lock.", "Zatím nejste registrován – dokud se nezaregistrujete, aplikace zamčení neodkládá."),
        ["enroll.status_other"] = ("No enrollment for the current {0} camera (enrolled only for: {1}). Enroll again or switch the camera.", "Pro aktuální kameru ({0}) registrace chybí (registrováno jen pro: {1}). Zaregistrujte se znovu nebo přepněte kameru."),
        ["btn.reenroll"] = ("Enroll again", "Registrovat znovu"),
        ["kind.IR"] = ("IR", "IR"),
        ["kind.Color"] = ("color", "barevná"),
        ["date.format"] = ("MMM d, yyyy H:mm", "d. M. yyyy H:mm"),

        // --- dialogs ---
        ["dlg.enroll"] = ("Face enrollment", "Registrace obličeje"),
        ["dlg.clear_q"] = ("Delete all enrolled face samples?", "Smazat všechny registrované vzorky obličeje?"),
        ["dlg.autostart"] = ("Autostart", "Autostart"),
        ["dlg.about"] = ("About", "O aplikaci"),
        ["about.text"] = ("Face Presence {0}\n\nRecognizes the enrolled user with the camera (Windows Hello IR or a webcam) and defers the automatic Windows lock while you sit at the computer. Everything runs offline, nothing leaves your PC.\n\nFace detection: YuNet, recognition: SFace (OpenCV Zoo) via OpenCV / OpenCvSharp.\n\nCopyright (c) 2026 Michal Cumpelik, MIT License.\nhttps://github.com/cumpelik/windows-face-presence\n\nData: {1}\n\nShow licenses (MIT and third-party notices)?",
                          "Face Presence {0}\n\nLokální rozpoznání registrovaného uživatele kamerou (IR Windows Hello nebo webkamera) a odkládání automatického zamčení Windows, dokud sedíte u počítače. Vše běží offline, nic neopouští váš počítač.\n\nDetekce obličeje: YuNet, rozpoznání: SFace (OpenCV Zoo) přes OpenCV / OpenCvSharp.\n\nCopyright (c) 2026 Michal Cumpelik, licence MIT.\nhttps://github.com/cumpelik/windows-face-presence\n\nData: {1}\n\nZobrazit licence (MIT a komponenty třetích stran)?"),
        ["dlg.licenses"] = ("Licenses", "Licence"),

        // --- footer ---
        ["footer.text"] = ("Built together with {0}  ·  Thanks to {1} for letting its people build utilities like this", "Vytvořeno ve spolupráci s {0}  ·  Díky {1}, že svým lidem umožňuje tvořit i takové utility"),
        ["footer.claude"] = ("Claude by Anthropic", "Claude od Anthropicu"),
        ["footer.tabidoo"] = ("tabidoo.cloud", "tabidoo.cloud"),

        // --- tray ---
        ["tray.open"] = ("Open", "Otevřít"),
        ["tray.exit"] = ("Exit", "Ukončit"),

        // --- monitor ---
        ["mon.opening"] = ("Opening camera…", "Otevírám kameru…"),
        ["mon.opened"] = ("Camera opened", "Kamera otevřena"),
        ["mon.camera_error"] = ("Camera error: {0}", "Chyba kamery: {0}"),
        ["mon.no_frames"] = ("Camera delivers no frames ({0})", "Kamera nedodává snímky ({0})"),
        ["mon.locked"] = ("Session locked – camera released", "Session zamčena – kamera uvolněna"),
        ["mon.paused"] = ("Paused", "Pozastaveno"),
        ["mon.stopped"] = ("Stopped", "Zastaveno"),
        ["mon.not_enrolled_nobody"] = ("No face enrolled (nobody in view)", "Obličej neregistrován (nikdo v záběru)"),
        ["mon.not_enrolled_face"] = ("No face enrolled – enroll yourself", "Obličej neregistrován – zaregistrujte se"),
        ["mon.present_injected"] = ("User present (simulated input sent)", "Uživatel přítomen (odeslán simulovaný vstup)"),
        ["mon.present"] = ("User present", "Uživatel přítomen"),
        ["mon.nobody"] = ("Nobody in view", "Nikdo v záběru"),
        ["mon.other"] = ("Another person / uncertain", "V záběru jiná osoba / nejisté"),
        ["mon.enrolling"] = ("Enrolling – look at the camera", "Registrace obličeje – dívejte se do kamery"),
        ["mon.not_running"] = ("Monitoring is not running (camera unavailable).", "Sledování neběží (kamera není k dispozici)."),
        ["mon.enroll_busy"] = ("Enrollment is already in progress.", "Registrace už probíhá."),
        ["mon.enroll_stopped"] = ("Monitoring was stopped.", "Sledování bylo ukončeno."),
        ["enroll.sample"] = ("Sample {0}/{1}", "Vzorek {0}/{1}"),
        ["enroll.no_face"] = ("No face found…", "Obličej nenalezen…"),
        ["enroll.small"] = ("Face too small or uncertain, come closer…", "Obličej je malý nebo nejistý, přibližte se…"),
        ["enroll.progress"] = ("Enrollment: {0}", "Registrace: {0}"),
        ["enroll.too_few"] = ("Only {0} usable samples collected, try again with better lighting.", "Získáno jen {0} použitelných vzorků, zkuste to znovu s lepším osvětlením."),
        ["enroll.saved"] = ("Saved {0} samples from the {1} camera. Minimum mutual similarity {2:F2}", "Uloženo {0} vzorků z kamery {1}. Vzájemná podobnost vzorků min. {2:F2}"),
        ["enroll.low"] = (" – low, consider repeating.", " – nízká, zvažte opakování."),
        ["enroll.cancelled"] = ("Enrollment cancelled.", "Registrace zrušena."),
        ["enroll.failed"] = ("Enrollment failed: {0}", "Registrace selhala: {0}"),
        ["host.no_camera"] = ("No camera found", "Nenalezena žádná kamera"),

        // --- diagnostics ---
        ["probe.title"] = ("Face Presence – diagnostics", "Face Presence – diagnostika"),
        ["probe.intro"] = ("Diagnostics verifies: (1) frames from the IR / color camera and face detection, (2) lock policies, (3) whether the keep-alive input resets the idle counter.\r\nDuring the test (about 10 s) look at the camera and do not move the mouse.\r\n",
                           "Diagnostika ověří: (1) obraz z IR / barevné kamery a detekci obličeje, (2) politiky zamykání, (3) zda udržovací vstup resetuje čítač nečinnosti.\r\nBěhem testu (cca 10 s) se dívejte do kamery a nehýbejte myší.\r\n"),
        ["probe.saved"] = ("Output saved to: {0}", "Výstup uložen do: {0}"),
        ["probe.error"] = ("ERROR: {0}", "CHYBA: {0}"),
        ["p.header"] = ("Face Presence diagnostics – {0}", "Face Presence diagnostika – {0}"),
        ["p.cameras"] = ("== 1) Cameras (WinRT MediaFrameSourceGroup) ==", "== 1) Kamery (WinRT MediaFrameSourceGroup) =="),
        ["p.no_groups"] = ("  No source groups found.", "  Nenalezena žádná skupina zdrojů."),
        ["p.group"] = ("  Group: {0}", "  Skupina: {0}"),
        ["p.enum_error"] = ("  Enumeration ERROR: {0}", "  CHYBA výčtu: {0}"),
        ["p.no_usable"] = ("  No usable (IR/Color, video) camera.", "  Žádná použitelná (IR/Color, video) kamera."),
        ["p.test_camera"] = ("-- Camera test: {0}", "-- Test kamery: {0}"),
        ["p.opened"] = ("   Opened: {0}, mode {1}", "   Otevřeno: {0}, režim {1}"),
        ["p.formats"] = ("   Supported formats: {0}", "   Podporované formáty: {0}"),
        ["p.frames"] = ("   Frames: {0} (illuminated {1}, dark {2}), pixel format {3}", "   Snímků: {0} (osvětlených {1}, neosvětlených {2}), pixel formát {3}"),
        ["p.size"] = ("   Size {0}x{1}, mean brightness {2:F1}/255", "   Rozměr {0}x{1}, průměrný jas {2:F1}/255"),
        ["p.sample_saved"] = ("   Sample saved: {0}", "   Vzorek uložen: {0}"),
        ["p.sample_failed"] = ("   Saving the sample failed: {0}", "   Uložení vzorku selhalo: {0}"),
        ["p.face_found"] = ("   Face FOUND: {0}x{1} px, confidence {2:F2}, embedding {3}D", "   Obličej NALEZEN: {0}x{1} px, jistota {2:F2}, embedding {3}D"),
        ["p.face_none"] = ("   No face found (look at the camera and run again).", "   Obličej nenalezen (dívejte se do kamery a spusťte znovu)."),
        ["p.no_frames"] = ("   No frame – the camera delivers no data.", "   Žádný snímek – kamera nedodává data."),
        ["p.cam_error"] = ("   ERROR: {0}", "   CHYBA: {0}"),
        ["p.policies"] = ("== 2) Lock policies ==", "== 2) Politiky zamykání =="),
        ["p.inactivity"] = ("  Interactive logon: Machine inactivity limit (InactivityTimeoutSecs): {0}", "  Interactive logon: Machine inactivity limit (InactivityTimeoutSecs): {0}"),
        ["p.not_set"] = ("not set / 0", "nenastaveno / 0"),
        ["p.inactivity_note"] = ("    -> measures keyboard/mouse only; SetThreadExecutionState has NO effect on it, hence the simulated input.", "    -> měří jen klávesnici/myš; SetThreadExecutionState na ni NEMÁ vliv, proto je nutný simulovaný vstup."),
        ["p.ss_policy"] = ("Policy (HKCU)", "Politika (HKCU)"),
        ["p.ss_user"] = ("User (HKCU)", "Uživatel (HKCU)"),
        ["p.ss_none"] = ("  Screen saver – {0}: not set", "  Spořič – {0}: nenastaveno"),
        ["p.ss"] = ("  Screen saver – {0}: active={1} timeout={2} s secure={3} exe={4}", "  Spořič – {0}: active={1} timeout={2} s secure={3} exe={4}"),
        ["p.goodbye_on"] = ("  Dynamic Lock (Bluetooth phone, EnableGoodbye): ON – locks independently of idle time", "  Dynamic Lock (Bluetooth telefon, EnableGoodbye): ZAPNUTO – zamyká nezávisle na nečinnosti"),
        ["p.goodbye_off"] = ("  Dynamic Lock (Bluetooth phone, EnableGoodbye): off", "  Dynamic Lock (Bluetooth telefon, EnableGoodbye): vypnuto"),
        ["p.modern_standby"] = ("  Modern Standby (AoAc): {0}", "  Modern Standby (AoAc): {0}"),
        ["p.yes"] = ("yes", "ano"),
        ["p.no"] = ("no", "ne"),
        ["p.display"] = ("  Display off: AC {0}, battery {1}", "  Vypnutí displeje: síť {0}, baterie {1}"),
        ["p.sleep"] = ("  Sleep: AC {0}, battery {1}", "  Uspání: síť {0}, baterie {1}"),
        ["p.require"] = ("  Require sign-in on wake: AC {0}, battery {1}", "  Vyžadovat heslo po probuzení: síť {0}, baterie {1}"),
        ["p.delaylock"] = ("  Lock after display off (DelayLockInterval): {0}", "  Zámek po vypnutí displeje (DelayLockInterval): {0}"),
        ["p.power_note"] = ("    -> display off / sleep is held by SetThreadExecutionState(ES_DISPLAY_REQUIRED); the simulated input covers them too.", "    -> vypnutí displeje / uspání drží SetThreadExecutionState(ES_DISPLAY_REQUIRED); simulovaný vstup je pokrývá také."),
        ["p.power_error"] = ("  Reading the power scheme failed: {0}", "  Čtení power schématu selhalo: {0}"),
        ["p.effective"] = ("  Effective lock estimate: {0}", "  Odhad efektivního zámku: {0}"),
        ["p.effective_val"] = ("after {0} of inactivity ({1})", "po {0} nečinnosti ({1})"),
        ["p.effective_none"] = ("no automatic lock detected", "automatický zámek nezjištěn"),
        ["p.never"] = ("never", "nikdy"),
        ["p.keepalive"] = ("== 3) Simulated input test (do not touch the mouse or keyboard for ~3 s) ==", "== 3) Test simulovaného vstupu (nehýbejte myší ani klávesnicí ~3 s) =="),
        ["p.ste"] = ("  SetThreadExecutionState(ES_DISPLAY_REQUIRED): {0}", "  SetThreadExecutionState(ES_DISPLAY_REQUIRED): {0}"),
        ["p.ok"] = ("OK", "OK"),
        ["p.failed"] = ("FAILED", "SELHALO"),
        ["p.inject"] = ("  {0}: SendInput={1}, idle before {2:F0} ms -> after {3:F0} ms => {4}", "  {0}: SendInput={1}, nečinnost před {2:F0} ms -> po {3:F0} ms => {4}"),
        ["p.resets"] = ("RESETS the idle counter ✔", "RESETUJE čítač nečinnosti ✔"),
        ["p.noreset"] = ("does NOT reset ✘", "NERESETUJE ✘"),
        ["p.switch"] = ("    -> consider switching the simulation method to {0}.", "    -> zvažte přepnout způsob simulace na {0}."),
        ["p.summary"] = ("== Summary ==", "== Shrnutí =="),
        ["p.sum_ir_ok"] = ("  IR camera (Windows Hello): FRAMES RECEIVED ✔{0}", "  IR kamera (Windows Hello): OBRAZ ZÍSKÁN ✔{0}"),
        ["p.sum_ir_face"] = (", face detected ✔", ", obličej detekován ✔"),
        ["p.sum_ir_noface"] = (", face not detected yet", ", obličej zatím nedetekován"),
        ["p.sum_ir_fail"] = ("  IR camera: found but no frames received ✘ – use the color camera", "  IR kamera: nalezena, ale obraz se nepodařilo získat ✘ – použijte barevnou kameru"),
        ["p.sum_ir_none"] = ("  IR camera: not present (or not exposed by the driver) – the color camera will be used", "  IR kamera: v systému není (nebo ji ovladač neexponuje) – použije se barevná kamera"),
        ["p.sum_color_ok"] = ("  Color camera: OK ✔", "  Barevná kamera: OK ✔"),
        ["p.sum_color_fail"] = ("  Color camera: not tested or failed", "  Barevná kamera: netestována nebo selhala"),
        ["p.sum_keep_ok"] = ("  Lock deferral: simulated input resets idle time ✔ – automatic lock can be deferred", "  Odkládání zámku: simulovaný vstup resetuje nečinnost ✔ – automatické zamčení lze odkládat"),
        ["p.sum_keep_fail"] = ("  Lock deferral: NOT CONFIRMED ✘ (maybe you moved the mouse during the test – repeat)", "  Odkládání zámku: NEPOTVRZENO ✘ (možná jste během testu pohnuli myší – opakujte)"),
        ["p.sum_limit"] = ("  The shortest known limit is {0} ({1}) – 'Simulated input after idle' must be safely below it (currently {2:F0} s).", "  Nejkratší známý limit je {0} ({1}) – 'Simulovaný vstup po nečinnosti' musí být bezpečně nižší (aktuálně {2:F0} s)."),
    };
}
