# Test Generation Research

## 2026-09-04 Application Engine Refactor

### Bounded target inventory
- `src/PhotoCuller.Application/Analysis/PhotoAnalysisEngine.cs`: UI-independent orchestration of scan, thumbnail, hash, score, group, and report stages.
- `src/PhotoCuller.Application/Contracts/PhotoAnalysisReportFactory.cs`: stable client-contract mapping, including errors, quality dimensions, timestamps, and group membership.
- `src/PhotoCuller.Infrastructure/Scanning/PhotoScanner.cs`: the existing source of supported-extension filtering; included only for the unsupported-file acceptance boundary.
- Planned tests: `src/PhotoCuller.Tests/ApplicationTests/PhotoAnalysisEngineTests.cs` and `src/PhotoCuller.Tests/InfrastructureTests/PhotoScannerTests.cs`.

### Existing conventions
- SDK-style .NET 10 test project using xUnit 2.9.3, FluentAssertions 7.2.0, and NSubstitute 5.3.0.
- Tests use `Method_Condition_ExpectedResult` names, Arrange/Act/Assert structure, and small temporary-directory fixtures for filesystem boundaries.
- `PhotoCuller.Tests.csproj` already references Application, Core, and Infrastructure and includes test files through SDK globbing.

### Acceptance checklist
- `pipeline orchestration/order/results`: prove the two-item sequence is scan -> thumbnail -> SHA-256 -> pHash for each item, then batch score -> group, and prove the returned domain collections are the exact processed instances.
- `empty input`: prove scoring and grouping receive empty collections and both domain and report results remain empty.
- `corrupted-item propagation if representable with mocks`: model a scanner-produced `PhotoItem.ErrorMessage` and prove the Engine and serialized report retain it while nullable hashes/scores remain nullable.
- `unsupported files (scanner integration or documented existing behavior)`: exercise the real `PhotoScanner` extension filter with one supported image path and one `.txt` path; only the supported path may reach repository filtering, metadata, and output.
- `CancellationToken propagation`: prove one caller token reaches scanner, thumbnail, both hash services, scoring, and grouping; prove pre-cancellation stops before dependency work. Scanner-progress reading intentionally receives a linked token so the Engine can stop its forwarding task after scanning.
- `stable report version/mapping`: prove version `1.0` and the complete photo/quality/group contract mapping, including UTC treatment of an unspecified capture timestamp.

### Static source-to-test pairing
- Required Roslyn analyzer run: 133 source files, 10 test files, 21 paired and 112 unpaired before this change.
- `PhotoAnalysisReport.cs` was explicitly unpaired, with suggested path `src/PhotoCuller.Tests/Contracts/PhotoAnalysisReportTests.cs`; the public contract will be covered through `PhotoAnalysisEngineTests` because the factory is internal and the Engine is its supported entry point.
- `PhotoAnalysisEngine.cs`, `PhotoAnalysisReportFactory.cs`, and `PhotoScanner.cs` were unpaired in the full analyzer result (only the top 30 unpaired sources were printed).
- This is a static symbol-pairing heuristic, not line or branch coverage evidence.

## Project Overview
- Path: `D:\Dev\Personal\PhotoCuller`
- Language/runtime: C# on .NET 10 (`global.json` SDK 10.0.203)
- Project system: SDK-style solution `PhotoCuller.slnx`
- Test framework: xUnit 2.9.3 with FluentAssertions 7.2.0 and NSubstitute 5.3.0
- Existing test project: `src/PhotoCuller.Tests/PhotoCuller.Tests.csproj`; it already references Core and Infrastructure and includes new `*.cs` files by SDK glob.

## Change Under Test
- Primary source: `src/PhotoCuller.Infrastructure/Hashing/DuplicateGroupingService.cs`
- New source: `src/PhotoCuller.Infrastructure/Hashing/OrbLocalFeatureMatcher.cs`
- New seam: `src/PhotoCuller.Core/Interfaces/Hashing/ILocalFeatureMatcher.cs`
- Registration: `src/PhotoCuller.Infrastructure/Configuration/InfrastructureServiceExtensions.cs`
- Existing candidate generation uses pHash, dHash, capture time, directory, and adjacent file numbers. It can still connect a sequence across a real scene change because those signals describe composition and chronology, not shared physical features.
- OpenCvSharp 4.10 is already referenced by Infrastructure, so ORB and homography verification require no new production dependency.

## Existing Tests and Conventions
- `DuplicateGroupingServiceTests` already covers exact duplicates, presets, sequential-photo grouping, dHash rejection, and incoherent-chain splitting.
- Tests use Arrange/Act/Assert, descriptive `Method_Condition_Result` names, FluentAssertions, NSubstitute, and small in-memory fakes.
- The new matcher dependency will be injected so grouping tests remain deterministic and do not read files.
- A focused `OrbLocalFeatureMatcherTests` file will exercise OpenCV behavior with locally generated images, including unreadable paths.

## Source-to-Test Pairing Audit
- The required analyzer was run once: 124 source files, 9 test files, 19 paired and 105 unpaired.
- `DuplicateGroupingService.cs` is paired with `DuplicateGroupingServiceTests.cs`.
- The new matcher will be paired explicitly with `OrbLocalFeatureMatcherTests.cs`.
- Unrelated legacy gaps are outside this bug-fix scope.

## Commands
- Scoped build: `dotnet build src/PhotoCuller.Tests/PhotoCuller.Tests.csproj -v:q`
- Scoped test: `dotnet test src/PhotoCuller.Tests/PhotoCuller.Tests.csproj -v:q`
- Harness discovery: `dotnet test PhotoCuller.slnx --list-tests --no-build`
- Final build: `dotnet build PhotoCuller.slnx --no-incremental -v:q`
- Final test: `dotnet test PhotoCuller.slnx --no-build -v:q`

## Risks and Boundaries
- ORB must be a second-stage verifier only; global near-duplicate matching remains hash-based so resized or compressed variants without enough local features are not lost.
- Sequence candidates must fail closed if decoding, feature extraction, or geometry estimation fails.
- Feature extraction should be cached for one grouping run to avoid repeatedly decoding the same photo.
- Cancellation and native `Mat` disposal must remain correct.
