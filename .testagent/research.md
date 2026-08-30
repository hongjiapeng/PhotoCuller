# Test Generation Research

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
