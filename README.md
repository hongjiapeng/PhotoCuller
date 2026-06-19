# PhotoCuller

**Intelligently identify duplicate and similar photos, helping you keep every photo worth treasuring.**

PhotoCuller is a local photo curation tool for Windows. It scans a directory of photos, detects duplicates and similar shots, scores each image across multiple quality dimensions, and recommends the best photo in each group — so you can safely remove the rest.

Everything runs **100% locally**. No cloud uploads, no subscriptions, no API keys required for the core workflow.

---

## Features

### 🔍 Smart Scanning
- Recursively scans local directories for JPEG, PNG, HEIC, WEBP, BMP, TIFF
- Reads EXIF metadata (capture time, dimensions)
- Incremental scanning — unchanged files are skipped on re-scan
- Thumbnail generation and caching for fast browsing

### 🔗 Duplicate & Similar Detection
| Method | Purpose |
|--------|---------|
| SHA-256 | Exact duplicate detection |
| pHash (DCT) | Visually similar photos |
| dHash | Fast pre-filter for similarity |
| Time window + pHash | Burst-shot grouping |

### ⭐ Quality Scoring (0–100)

All scoring is **algorithm-based** using OpenCvSharp — no model download or GPU required.

| Dimension | Weight | Algorithm | Notes |
|-----------|--------|-----------|-------|
| **Sharpness** | 35% | Laplacian variance | Primary differentiator in a burst group |
| **Exposure** | 20% | HSV V-channel histogram; penalises crushed blacks >15% and blown highlights >15% | Ideal mean brightness: 90–200 |
| **Contrast** | 15% | Grayscale standard deviation | Ideal stddev: 35–90 |
| **Noise** | 10% | Placeholder returning 70 — interface reserved for wavelet estimator | Will be replaced in a future release |
| **Resolution** | 10% | Total megapixel count on a logarithmic scale | <0.5 MP → low; ≥8 MP → high |
| **Composition** | 10% | Placeholder returning 65 — interface reserved for SAMP-Net | Will be replaced in a future release |
| **Colour** | 0% | HSV S-channel mean saturation | Off by default; raise weight in Settings |

Default weights sum to **100**. All weights are adjustable in Settings → Scoring Weights.

### 🗂️ Safe File Management
- Moves photos to a **pending-delete folder** — never permanently deletes originals
- Preserves relative folder structure in the trash folder
- Collision-safe renaming (`photo.jpg` → `photo(1).jpg`)
- Operation log with **Ctrl+Z undo** for the current session

### 🤖 AI Model Management

The current scoring is **100% algorithm-based** — no AI model is required for the core workflow. The AI layer is an optional enhancement:

| Layer | Technology | When needed |
|-------|-----------|-------------|
| Core scoring | OpenCvSharp (Laplacian, histograms, HSV) | Always — no GPU, no model download |
| Photo descriptions & recommendation text | Any OpenAI-compatible API or local Ollama | Optional, off by default |
| Semantic search embeddings | CLIP / SigLIP (ONNX or API) | Optional |
| Face quality scoring | InsightFace (ONNX) | Optional, roadmap |

**Supported AI backends** (configure in the AI Model Management page):

| Backend | How to connect |
|---------|---------------|
| **Ollama** (local, free) | Set BaseURL to `http://localhost:11434/v1`, pick any vision model (e.g. `qwen2.5vl:7b`) |
| **OpenAI / GPT-4o** | Set BaseURL to `https://api.openai.com/v1`, enter your API key |
| **DeepSeek** | Set BaseURL to `https://api.deepseek.com/v1`, enter your API key |
| **Google Gemini** | Select Gemini provider, enter your API key |
| **Custom HTTP endpoint** | Any OpenAI-compatible endpoint |

> API keys are encrypted with **AES-256-GCM** before being written to disk and never stored in plain text. Cloud AI analysis is **disabled by default** — enable it in Settings → Privacy.

### ⚙️ Settings
- Dark / Light / System theme
- All scan, scoring, and similarity thresholds configurable
- Last-used directory restored on next launch
- Thumbnail cache management

---

## Requirements

| Requirement | Version |
|-------------|---------|
| Windows | 10 1809 (build 17763) or later |
| .NET SDK | 10.0.203 or later |
| Visual Studio | 2026 (v18) with WinUI workload |
| Windows App SDK | 1.7 (bundled, self-contained) |
| Architecture | x64 |

---

## Getting Started

### Build from Source

```powershell
git clone https://github.com/hongjiapeng/PhotoCuller.git
cd PhotoCuller

# Build with VS MSBuild (required for WinUI XAML compiler)
$msbuild = & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild `
    -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1

& $msbuild PhotoCuller.slnx /p:Configuration=Debug /p:Platform="Any CPU" /m
```

### Run

```powershell
# After building:
.\src\PhotoCuller.App.WinUI\bin\Any CPU\Debug\net10.0-windows10.0.19041.0\win-x64\PhotoCuller.exe
```

Or open `PhotoCuller.slnx` in Visual Studio 2026, set `PhotoCuller.App.WinUI` as the startup project, and press **F5**.

### Run Tests

```powershell
dotnet test src\PhotoCuller.Tests\PhotoCuller.Tests.csproj --configuration Debug
# Expected: 47 tests, 47 passed
```

---

## Project Structure

```
PhotoCuller/
├── src/
│   ├── PhotoCuller.Core/               # Domain models, interfaces, scoring weights
│   │   ├── Models/                     # PhotoItem, PhotoGroup, AppSettings, …
│   │   ├── Enums/                      # RecommendationStatus, GroupType, …
│   │   ├── Interfaces/                 # All service contracts (platform-agnostic)
│   │   └── Scoring/                    # ScoringWeights value object
│   │
│   ├── PhotoCuller.Infrastructure/     # Implementations
│   │   ├── Database/                   # EF Core DbContext, migrations, initializer
│   │   ├── Repositories/               # EF Core + raw-SQL repositories
│   │   ├── Scanning/                   # PhotoScanner, MetadataReader, ImageDecoder
│   │   ├── Hashing/                    # SHA-256, pHash, dHash, grouping (Union-Find)
│   │   ├── ImageProcessing/            # Quality scorers (OpenCvSharp)
│   │   ├── ThumbnailCache/             # Disk-bucketed thumbnail service
│   │   ├── FileSystem/                 # FileMoveService, UndoService
│   │   └── Configuration/              # DI extensions, AES-256 key protector
│   │
│   ├── PhotoCuller.App.WinUI/          # WinUI 3 application (unpackaged)
│   │   ├── Views/                      # MainWindow, pages, controls
│   │   ├── ViewModels/                 # MVVM view-models (CommunityToolkit.Mvvm)
│   │   ├── Converters/                 # Value converters for XAML
│   │   └── Services/                   # Platform service implementations
│   │
│   └── PhotoCuller.Tests/              # xUnit + FluentAssertions + NSubstitute
│       ├── CoreTests/                  # ScoringWeights, pHash, similarity, grouping
│       └── InfrastructureTests/        # FileMoveService, SettingsRepository
│
├── Directory.Build.props               # Shared TFM, nullable, analyzers
├── Directory.Packages.props            # Central Package Management (CPM)
└── global.json                         # SDK version pin (10.0.203)
```

---

## Architecture Principles

- **`PhotoCuller.Core` has zero Windows or UI dependencies** — all platform capabilities are injected via interfaces
- **EF Core with NoTracking default** — read-heavy workload; opt-in tracking for writes
- **SQLite with WAL mode** — concurrent background writes don't block the UI
- **Streaming scan pipeline** — `IAsyncEnumerable<PhotoItem>` lets the UI display results as they arrive
- **Channel-based progress** — `IScanProgressService` uses `System.Threading.Channels` for thread-safe progress reporting
- **Union-Find grouping** — O(α·n) near-linear grouping with path compression

---

## Data Storage

| Data | Location |
|------|----------|
| SQLite database | `%LocalAppData%\PhotoCuller\photoculler.db` |
| Settings | `%LocalAppData%\PhotoCuller\settings.json` |
| Thumbnails | `%LocalAppData%\PhotoCuller\thumbnails\{xx}\{xx}\{hash}.jpg` |
| Logs | `%LocalAppData%\PhotoCuller\logs\log-YYYYMMDD.txt` |
| API key store | `%LocalAppData%\PhotoCuller\.keystore` (hidden, AES-256 key) |

---

## Privacy

- **No data leaves your machine** by default
- Cloud AI analysis requires an explicit opt-in in Settings → Privacy
- API keys are encrypted with AES-256-GCM before being written to disk
- Thumbnails and original photos never leave the local machine unless you configure a cloud model

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Z` | Undo last file move |

---

## Roadmap (not yet implemented)

- [ ] Face detection & clustering (InsightFace ONNX)
- [ ] CLIP semantic search
- [ ] Real noise analyzer (wavelet-based)
- [ ] Composition analyzer (SAMP-Net)
- [ ] Cloud AI photo descriptions via configured model
- [ ] Export report (CSV/JSON)
- [ ] RAW format support (CR2, NEF, ARW, …)

---

## License

MIT License — see [LICENSE](LICENSE) for details.
