# Music Book Creator

Windows app for preparing scanned music books: automatically crop all pages of a folder, remove the
background and show-through from the back of the page, rotate pages that are upside down, and combine
the result into a PDF music book.

## Features

- **Crop** to the page content (with adjustable margin); book-fold shadows and scanner edges are removed,
  print that reaches the image edge (pages already cropped by the scanner) is kept
- **White background**: removes paper tone and show-through; contrast and brightness
- **Grayscale** (single-channel JPEG, small files)
- **Auto rotate**: upside-down pages are detected via the built-in Windows text recognition (OCR) and rotated by 180°
- **Deskew** skewed scans (up to ±5°, optional)
- **Remove specks** (small dust specks, optional; notation dots are kept)
- Photos, colored covers and similar pages are detected and **copied unchanged**
- **Preview** of original and result with zoom (mouse wheel) and panning
- **PDF music book**: one A4 page per image, first and last page edge to edge, all others with a margin of at least 1 cm
- Windows 11 look (Fluent), light/dark following the system setting

## Projects

| Folder | Contents |
|---|---|
| `MusicBookCreator` | WPF app; the image processing lives in `Processing/` (`ScanProcessor.cs`, `ScanJpegWriter.cs`, `OcrScorer.cs`) |
| `MusicBookCreator.Tests` | regression tests against real scans (see [TestData](TestData/README.md)) |
| `MusicBookCreator.IconGenerator` | generates the app icon `MusicBookCreator/Assets/app.ico` |

## Requirements

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- for auto rotate: an installed Windows OCR language pack (e.g. English or German, usually present)

## Build and run

App (development):

```
dotnet run -c Release --project MusicBookCreator
```

Self-contained exe (runs without an installed .NET, approx. 70 MB):

```
dotnet publish MusicBookCreator -c Release -o MusicBookCreator/publish
```

## Tests

```
dotnet run -c Release --project MusicBookCreator.Tests
```

The test scans are copyrighted and therefore not part of the repository – see
[TestData/README.md](TestData/README.md).

## License

[MIT](LICENSE)
