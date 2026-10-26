# Music Book Creator

Windows-App zum Aufbereiten eingescannter Notenbücher: Seiten eines Ordners automatisch zuschneiden,
Hintergrund und Durchscheinen der Rückseite entfernen, auf dem Kopf stehende Seiten drehen und das
Ergebnis als PDF-Notenbuch zusammenfassen.

## Funktionen

- **Zuschneiden** auf den Seiteninhalt (mit einstellbarem Rand); Falzschatten und Scannerkanten werden entfernt,
  Druck bis zum Bildrand (vom Scanner vorgeschnittene Seiten) bleibt erhalten
- **Hintergrund weiß**, Papierton und durchscheinende Rückseite entfernen; Kontrast und Helligkeit
- **Graustufen** (einkanaliges JPEG, kleine Dateien)
- **Auto-Drehen**: auf dem Kopf stehende Seiten werden per Windows-Texterkennung (OCR) erkannt und um 180° gedreht
- **Begradigen** schräg gescannter Seiten (bis ±5°, optional)
- **Flecken entfernen** (kleine Staubpunkte, optional; Notenpunkte bleiben erhalten)
- Fotos, farbige Cover u. Ä. werden erkannt und **unverändert übernommen**
- **Vorschau** Original/Ergebnis mit Zoom (Mausrad) und Verschieben
- **PDF-Notenbuch**: eine A4-Seite pro Bild, erste und letzte Seite randlos, alle anderen mit mindestens 1 cm Rand
- Windows-11-Optik (Fluent), hell/dunkel nach Systemeinstellung

## Projekte

| Ordner | Inhalt |
|---|---|
| `MusicBookCreator` | WPF-App; die Bildverarbeitung liegt in `Processing/` (`ScanProcessor.cs`, `ScanJpegWriter.cs`, `OcrScorer.cs`) |
| `MusicBookCreator.Tests` | Regressionstests gegen echte Scans (siehe [TestData](TestData/README.md)) |
| `MusicBookCreator.IconGenerator` | erzeugt das App-Icon `MusicBookCreator/Assets/app.ico` |

## Voraussetzungen

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
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

## Tests

```
dotnet run -c Release --project MusicBookCreator.Tests
```

Die Testscans sind urheberrechtlich geschützt und deshalb nicht im Repository – siehe
[TestData/README.md](TestData/README.md).

## Lizenz

[MIT](LICENSE)
