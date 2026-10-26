# Testdaten

Die Testscans sind **nicht im Repository**: Es sind Seiten eines urheberrechtlich geschützten
Notenheftes. Sie liegen nur lokal in diesem Ordner (per `.gitignore` ausgeschlossen).

Das Testprogramm `MusicBookCreator.Tests` erwartet folgenden Aufbau:

```
TestData/
├── BH2025Test/          Seitenfolge eines Notenheftes (JPEG, ca. 300 dpi)
│   ├── Scannen0504.jpg  erste Datei: farbiges Cover  → wird unverändert kopiert, PDF-Seite randlos
│   ├── Scannen0505.jpg  …
│   ├── …                Druckseiten, teils vom Scanner bereits zugeschnitten
│   ├── Scannen0515.jpg  (Druck reicht bis an den Bildrand)
│   └── Scannen0640.jpg  letzte Datei: Rückcover → unverändert kopiert, PDF-Seite randlos
└── Scannen0651.jpg      deutlich schräg gescannte Notenseite (+1,32°) für „Begradigen“
```

Mit eigenen Scans funktioniert der Test genauso, solange die erste und letzte Datei Cover sind
und dazwischen Druckseiten liegen. Ein anderer Ordner kann auch direkt übergeben werden:

```
dotnet run -c Release --project MusicBookCreator.Tests -- "D:\Pfad\zu\Scans"
```

Fehlt `Scannen0651.jpg`, wird nur der Test „Begradigen echter Scan“ übersprungen.
