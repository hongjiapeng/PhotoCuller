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
| Capture context + ORB / RANSAC | Burst-shot verification |
| BK-Tree + Union-Find | Similarity search and grouping |

#### Exact duplicate detection

PhotoCuller reads each file in 64 KB chunks and computes its **SHA-256** hash.
Files with the same SHA-256 digest are byte-for-byte identical, so they are
placed in the same exact-duplicate group even when their names or directories
differ. Photos assigned to an exact-duplicate group are excluded from the later
visual-grouping pass.

#### Similar photo detection

Burst shots and resized, recompressed, or lightly edited images normally have
different file hashes. PhotoCuller therefore computes two additional 64-bit
perceptual hashes:

- **pHash** converts the image to 32×32 grayscale, applies a DCT, and builds a
  hash from the top-left 8×8 low-frequency block. It primarily represents the
  image's broad visual structure.
- **dHash** resizes the image to 9×8 and compares adjacent pixel intensities in
  each row. It represents the direction of brightness changes in the image.

Hashes are compared by Hamming distance: the number of different bits. A
smaller distance indicates a closer visual match. Photos from different
directories or capture periods must pass strict thresholds for both pHash and
dHash before a similarity edge is created.

```text
Source image
 ├─ pHash: compare broad composition
 └─ dHash: compare intensity changes
                    ↓
          both thresholds pass
                    ↓
          similar-photo candidate
```

#### Burst-shot verification

Small subject or camera movements can make adjacent burst frames fail strict
hash thresholds. PhotoCuller uses the more tolerant thresholds only when the
photos share a directory, have nearby capture times, and have compatible
sequential filenames. When numeric suffixes are present, the filename prefixes
must match and the sequence numbers may differ by at most one.

These candidates are then verified with **ORB local features and RANSAC
geometry**. ORB extracts repeatable corners and texture descriptors from both
images; RANSAC checks whether their matched points support a consistent spatial
transformation. The pair is accepted as the same scene only when the match
count, inlier ratio, and image coverage all pass their required minimums. This
reduces false matches between images with similar colours or composition but
different subjects.

The three search presets use different thresholds. **Strict** prioritises fewer
false groups, **Balanced** trades precision against recall, and **Loose** covers
a longer capture window and larger visual changes.

#### Building groups from similarity edges

A **BK-Tree** searches perceptual hashes by Hamming distance, avoiding a full
all-pairs comparison across the photo library. Each photo is then treated as a
graph node, and every accepted pair becomes an edge. A path-compressed,
union-by-rank **Union-Find** collects connected nodes into candidate groups:

```text
Photo A ──similar── Photo B ──similar── Photo C
                              ↓
                       one candidate group
```

PhotoCuller then checks the first, last, and adjacent members and splits
incoherent components. This prevents a similarity chain where A resembles B
and B resembles C, but A and C no longer represent the same scene.

A group is labelled as a burst only when all members form a time-ordered
sequence captured at roughly two-second intervals and every adjacent pair also
passes the strict pHash and dHash checks. Other same-scene clusters are labelled
similar. A manual keep choice always takes priority; otherwise, the photo with
the highest overall quality score is recommended, with resolution as a
tiebreaker. All remaining photos are marked for review rather than deleted
automatically.

### ⭐ Quality Scoring (0–100)

The baseline technical score uses **traditional computer-vision algorithms
implemented through OpenCvSharp**; it does not require a trained model, a model
download, or a GPU. OpenCvSharp is the .NET wrapper around OpenCV that supplies
the underlying image operations. It is a library, not an AI model.

| Dimension | Default weight | Current calculation | Status |
|-----------|---------------:|---------------------|--------|
| **Sharpness** | 35% | Load grayscale, apply the OpenCV Laplacian, calculate response variance `v`, then map with `100 × v / (v + 500)` | Implemented |
| **Exposure** | 20% | Build a 256-bin histogram of the HSV V channel. Mean brightness from 85–200 starts at 100; lower/higher means reduce the score. Pixels in 0–15 or 240–255 incur an extra penalty when either share exceeds 15% | Implemented |
| **Contrast** | 15% | Standard deviation `s` of grayscale values: below 20 maps to 0–60, 20–75 maps to 60–100, 75–90 scores 100, and values above 90 are gradually penalised | Implemented |
| **Noise** | 10% | Fixed neutral score of 70 | **Placeholder**; no noise measurement yet |
| **Resolution** | 10% | Piecewise mapping of `width × height`: <0.5 MP → 0–30, 0.5–2 MP → 30–70, 2–8 MP → 70–95, 8–24 MP → 95–100, and ≥24 MP → 100 | Implemented without OpenCV |
| **Composition** | 10% | Fixed score of 65 | **Placeholder**; no composition measurement yet |
| **Colour** | 0% | Mean of the HSV S channel: low saturation scores lower, 140–180 scores 100, and values above 180 are gradually penalised as possible oversaturation | Implemented; disabled by default weight |

The technical score is the weighted mean of every available implemented value.
If an analyzer fails and returns `null`, the remaining weights are re-normalised.
Default technical weights sum to **100** and can be changed in Settings →
Scoring Weights.

When local aesthetic scoring is enabled, the final score becomes:

```text
overall = technical × (1 - blend) + aesthetic × blend
```

For example, the recommended 25% blend keeps 75% of the technical score and
adds 25% of the local model score. Aesthetic scoring does **not** participate in
duplicate/similar detection or group construction, but it can affect the final
ranking because the highest overall score is recommended within each group.

OpenCV/OpenCvSharp is used for image decoding, resize and colour conversion,
DCT-based pHash, dHash preprocessing, Laplacian sharpness, HSV statistics, ORB
features, descriptor matching, RANSAC homography verification, and aesthetic
model preprocessing. SHA-256 uses .NET cryptography; resolution uses stored
dimensions; BK-Tree and Union-Find are implemented in this repository; ONNX
Runtime performs model inference.

These are practical no-reference heuristics, not universal measurements of
photographic merit. The thresholds and 0–100 mapping curves are PhotoCuller
design choices and still need calibration against broader real-world datasets.

### 🗂️ Safe File Management
- Moves photos to a **pending-delete folder** — never permanently deletes originals
- Preserves relative folder structure in the trash folder
- Collision-safe renaming (`photo.jpg` → `photo(1).jpg`)
- Operation log with **Ctrl+Z undo** for the current session

### 🤖 AI Model Management

The current scoring uses **traditional computer-vision algorithms** — no AI
model is required for the core workflow. The AI layer is an optional enhancement:

| Layer | Technology | When needed |
|-------|-----------|-------------|
| Core scoring | OpenCvSharp (Laplacian, histograms, HSV) | Always — no GPU, no model download |
| Photo descriptions & recommendation text | Any OpenAI-compatible API or local Ollama | Optional, off by default |
| Semantic search embeddings | CLIP / SigLIP (ONNX or API) | Optional |
| Face quality scoring | InsightFace (ONNX) | Optional, roadmap |

#### Local ONNX aesthetic scoring

PhotoCuller can blend a local aesthetic model with its existing technical score:

1. Add a local `.onnx` model under **AI Models** and choose **Aesthetic**.
2. Select the output preset: `0–1`, `-1–1`, or `1–10 AVA`.
3. Enable local model scoring in **Settings** and choose a blend percentage
   (20–30% is a practical starting point).

The initial model contract accepts one `float32` RGB image tensor in NCHW or
NHWC layout, uses CLIP mean/std normalization, and returns one scalar score.
If loading or inference fails, scoring automatically falls back to the existing
technical metrics.

The **AI Models** page includes recommended model links. Common aesthetic
predictors, including LAION improved aesthetic predictor and Aesthetics
Predictor V2, are often published as `.pth` / `.safetensors` weights. Export
them to a compatible `.onnx` model before using them for local scoring. See
`tools/model-export/README.md` for the export scripts.

The recommended local aesthetic route is **CLIP ViT-L/14 + the LAION /
improved-aesthetic-predictor MLP head**. This family of models is well known
in open-source image-data filtering and generative-AI dataset curation: LAION
used aesthetic predictors to estimate broad 1–10 human preference scores for
large image-text datasets and to build LAION-Aesthetics subsets, while
`improved-aesthetic-predictor` is a common CLIP-embedding aesthetic baseline.

Treat this as a ranking signal, not as an absolute judge of taste:

- Good for broad visual-appeal sorting across travel, portrait, landscape, and generated images.
- Good as an extra signal blended with sharpness, exposure, contrast, and resolution.
- Not reliable for personal memory value, family expressions, storytelling, or professional artistic merit.
- May prefer clean subjects, saturated color, commercial polish, and web-image aesthetics.

For that reason, a **20–30%** aesthetic blend is recommended. The default
starting point is 25%, leaving the traditional technical score in control while
the model contributes “does this look pleasing?” signal.

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

### 🧭 Similar-Photo Tuning Guide

Choose a search effort in **Settings → Similar Photo Settings**, click **Save Settings**, and run a new scan. Existing groups are not updated retroactively.

| Search effort | Best for |
|---------------|----------|
| Precise | Finds only very close frames and minimizes false groups |
| Balanced (recommended) | Balances accuracy with recognition of nearby sequential captures |
| Find more | Recalls larger composition changes and is best with per-group review |

Photos are merged only when both pHash and dHash agree. A more tolerant threshold is used only when photos share a directory, have nearby capture times, and have adjacent sequence numbers; the first and last members are checked again to prevent gradual subject drift from creating one large group. Only highly similar frames captured within roughly two seconds are labelled as a burst. Other same-scene groups are labelled similar.

Advanced settings can still override the global threshold, capture-sequence threshold, and time window. Most users should leave them off. Perceptual hashes remain cached, but a new scan is required to rebuild groups.

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
# Expected: 50 tests, 50 passed
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
- [x] Export report (CSV)
- [ ] RAW format support (CR2, NEF, ARW, …)

---

## Technical References & Acknowledgements

PhotoCuller does not delegate its workflow to another photo-culling engine. Its
thresholds, 0–100 score mappings, multi-stage matching policy, coherent-group
splitting, and score blending are project-specific implementations. It does,
however, build on established algorithms, libraries, papers, and open-source
model work:

- **OpenCV / OpenCvSharp** — image decoding and transforms, histograms,
  Laplacian, DCT, ORB descriptors, matching, and RANSAC homography. See the
  [OpenCV documentation](https://docs.opencv.org/4.x/) and the
  [OpenCvSharp repository](https://github.com/shimat/opencvsharp).
- **Perceptual hashing** — the DCT-based pHash design follows the technique
  described in Christoph Zauner's
  [Implementation and Benchmarking of Perceptual Image Hash Functions](https://www.phash.org/docs/pubs/thesis_zauner.pdf).
  The adjacent-pixel difference hash follows Neal Krawetz's
  [dHash description](https://www.hackerfactor.com/blog/index.php?/archives/529-Kind-of-Like-That.html).
- **ORB and RANSAC** — ORB originates from Rublee et al.,
  [ORB: An efficient alternative to SIFT or SURF](https://doi.org/10.1109/ICCV.2011.6126544).
  Robust geometric verification follows the RANSAC method introduced by
  Fischler and Bolles in
  [Random Sample Consensus](https://doi.org/10.1145/358669.358692).
- **BK-Tree and Union-Find** — metric search is based on Burkhard and Keller's
  [Some Approaches to Best-Match File Searching](https://scholarship.claremont.edu/hmc_fac_pub/170/).
  The grouping implementation uses union by rank and path compression, as
  analysed by Tarjan in
  [Efficiency of a Good But Not Linear Set Union Algorithm](https://doi.org/10.1145/321879.321884).
- **Local aesthetic model** — the recommended export combines
  [OpenCLIP](https://github.com/mlfoundations/open_clip) ViT-L/14 with an MLP
  head compatible with Christoph Schuhmann's
  [improved-aesthetic-predictor](https://github.com/christophschuhmann/improved-aesthetic-predictor)
  approach. The earlier
  [LAION aesthetic predictor](https://github.com/LAION-AI/aesthetic-predictor)
  is also an important upstream reference. Inference uses
  [ONNX Runtime](https://onnxruntime.ai/docs/).

Runtime and test dependencies are declared in
[`Directory.Packages.props`](Directory.Packages.props). Upstream libraries,
model weights, and datasets retain their own licences and terms; verify those
terms before redistributing an exported model or third-party weights.

---

## License

MIT License — see [LICENSE](LICENSE) for details.
