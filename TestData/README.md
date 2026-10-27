# Test data

The test scans are **not part of the repository**: they are pages of a copyrighted music book.
They only exist locally in this folder (excluded via `.gitignore`).

The test runner `MusicBookCreator.Tests` expects the following layout:

```
TestData/
├── BH2025Test/          page sequence of a music book (JPEG, approx. 300 dpi)
│   ├── Scannen0504.jpg  first file: colored cover → copied unchanged, PDF page edge to edge
│   ├── Scannen0505.jpg  …
│   ├── …                printed pages, some already cropped by the scanner
│   ├── Scannen0515.jpg  (print reaches the image edge)
│   └── Scannen0640.jpg  last file: back cover → copied unchanged, PDF page edge to edge
└── Scannen0651.jpg      clearly skewed music page (+1.32°) for "Deskew"
```

The test works the same with your own scans, as long as the first and last file are covers and the
files in between are printed pages. Another folder can also be passed directly:

```
dotnet run -c Release --project MusicBookCreator.Tests -- "D:\path\to\scans"
```

If `Scannen0651.jpg` is missing, only the "Deskew real scan" test is skipped.
