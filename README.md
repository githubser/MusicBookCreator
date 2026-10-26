# Music Book Creator

Werkzeuge zum Aufbereiten eingescannter Notenbücher: Seiten automatisch zuschneiden, Hintergrund
und Durchscheinen der Rückseite entfernen, auf den Kopf stehende Seiten drehen und das Ergebnis
als PDF-Notenbuch zusammenfassen.

Enthalten sind eine **Windows-App** für ganze Ordner und ein **Paint.NET-Plugin** für einzelne Seiten.
Beide verwenden dieselbe Bildverarbeitung.

## Funktionen

- **Zuschneiden** auf den Seiteninhalt (mit einstellbarem Rand); Falzschatten und Scannerkanten werden entfernt,
  Druck bis zum Bildrand (vom Scanner vorgeschnittene Seiten) bleibt erhalten
- **Hintergrund weiß**, Papierton und durchscheinende Rückseite entfernen; Kontrast und Helligkeit
- **Graustufen** (einkanaliges JPEG, kleine Dateien)
- **Auto-Drehen**: auf dem Kopf stehende Seiten werden per Windows-Texterkennung (OCR) erkannt und um 180° gedreht
- **Begradigen** schräg gescannter Seiten (bis ±5°, optional)
- **Flecken entfernen** (kleine Staubpunkte, optional; Notenpunkte bleiben erhalten)
- Fotos, farbige Cover u. Ä. werden erkannt und **unverändert übernommen**
- **PDF-Notenbuch**: eine A4-Seite pro Bild, erste und letzte Seite randlos, alle anderen mit mindestens 1 cm Rand

## Projekte

| Ordner | Inhalt |
|---|---|
| `MusicBookCreator` | WPF-App (Stapelverarbeitung eines Ordners, Vorschau mit Zoom, PDF-Export) |
| `PdnScanCrop` | Paint.NET-Dateiformat-Plugin „JPEG – Scan zugeschnitten“ und die gemeinsame Bildverarbeitung (`ScanProcessor.cs`) |
| `PdnScanCrop.Ocr` | kleines OCR-Hilfsprogramm (.NET Framework 4.8) für das Plugin; enthält die gemeinsame OCR-Bewertung (`OcrScorer.cs`) |
| `MusicBookCreator.Tests` | Regressionstests gegen echte Scans (siehe [TestData](TestData/README.md)) |
| `MusicBookCreator.IconGenerator` | erzeugt das App-Icon `MusicBookCreator/Assets/app.ico` |

Gemeinsamer Code wird nicht kopiert, sondern von App und Tests per Dateiverweis eingebunden.

## Voraussetzungen

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- für das Plugin zusätzlich: [Paint.NET](https://www.getpaint.net/) 5.1 (Classic-Installation in
  `C:\Program Files\paint.net`), dessen DLLs beim Bauen referenziert werden
- für Auto-Drehen: ein installiertes Windows-OCR-Sprachpaket (Deutsch oder Englisch, normalerweise vorhanden)

## Bauen und starten

App (Entwicklung):

```
dotnet run -c Release --project MusicBookCreator
```

Eigenständige exe (läuft ohne installiertes .NET, ca. 70 MB):

```
dotnet publish MusicBookCreator -c Release -o MusicBookCreator/publish
```

Paint.NET-Plugin:

```
dotnet build PdnScanCrop -c Release
```

Danach `PdnScanCrop.dll` und `PdnScanCrop.Ocr.exe` aus `PdnScanCrop/bin/Release/net9.0-windows/`
bei geschlossenem Paint.NET nach `C:\Program Files\paint.net\FileTypes\` kopieren (Administratorrechte nötig).
Das Plugin erscheint unter *Speichern unter* als „JPEG – Scan zugeschnitten“.

## Tests

```
dotnet run -c Release --project MusicBookCreator.Tests
```

Die Testscans sind urheberrechtlich geschützt und deshalb nicht im Repository – siehe
[TestData/README.md](TestData/README.md).

## Lizenz

[MIT](LICENSE)
