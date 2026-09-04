# Test Generation Status

## 2026-09-04 Application Engine Refactor

### Outcome
- Status: complete.
- Test-gap verdict: **Strong** for the six requested acceptance outcomes. Exact order, empty collections, per-item error retention, unsupported-extension exclusion, cancellation-token identity, pre-cancel short circuit, and full report mapping are all caller-visible assertions.
- Residual lower-risk branches outside this request: option validation, thumbnail-disabled operation, and scanner-progress relay details. None weakens the requested Engine/CLI acceptance boundary.
- No production code was changed for testability.

### Requirement-to-test map
| Requirement | Evidence |
|---|---|
| `pipeline orchestration/order/results` | `PhotoCuller.Tests.ApplicationTests.PhotoAnalysisEngineTests.AnalyzeAsync_TwoPhotos_RunsPipelineInRequiredOrderAndReturnsProcessedObjects` |
| `empty input` | `PhotoCuller.Tests.ApplicationTests.PhotoAnalysisEngineTests.AnalyzeAsync_EmptyScan_ScoresAndGroupsEmptyInputAndReturnsEmptyRun` |
| `corrupted-item propagation if representable with mocks` | `PhotoCuller.Tests.ApplicationTests.PhotoAnalysisEngineTests.AnalyzeAsync_CorruptedPhoto_PreservesErrorInRunAndReport` |
| `unsupported files (scanner integration or documented existing behavior)` | `PhotoCuller.Tests.InfrastructureTests.PhotoScannerTests.ScanAsync_SupportedAndUnsupportedFiles_YieldsOnlySupportedFile` |
| `CancellationToken propagation` | `PhotoCuller.Tests.ApplicationTests.PhotoAnalysisEngineTests.AnalyzeAsync_ActiveCancellationToken_PropagatesSameTokenToEveryStage`; `PhotoCuller.Tests.ApplicationTests.PhotoAnalysisEngineTests.AnalyzeAsync_PreCancelledToken_StopsBeforeStartingPipeline` |
| `stable report version/mapping` | `PhotoCuller.Tests.ApplicationTests.PhotoAnalysisEngineTests.AnalyzeAsync_CompletedRun_MapsStableReportContract` |

### Pseudo-mutation review
- Removing or reordering any per-photo pipeline stage changes the exact event ledger or processed fields in the two-photo test.
- Skipping score/group work for an empty scan leaves the captured collaborator arguments null and fails the empty-input test.
- Dropping `PhotoItem.ErrorMessage`, quality nullability, or photo identity from report mapping fails the corrupted-item test.
- Allowing arbitrary extensions causes `.txt` to reach repository/metadata/output and fails three independent negative/collection assertions.
- Replacing the caller token at any core stage fails an exact NSubstitute argument verification; removing the initial cancellation guard causes the scanner call verification to fail.
- Changing version `1.0`, any photo quality field, recommendation, timestamp conversion, or group membership fails literal/deep mapping assertions.

### Assertion-quality review
- New tests reviewed: 7.
- Assertion-free tests: 0; trivial-only tests: 0; self-referential tests: 0.
- Meaningful categories present: equality, collection, null, exception, comparison/ordering, negative, state/side-effect, and structural/deep.
- Mock-call checks use exact object/token/path arguments, not call-count-only assertions. The filesystem boundary asserts both inclusion of `.JPG` and exclusion of `.txt` at candidate, metadata, and output surfaces.

### Validation evidence
- Scoped build: `dotnet build src/PhotoCuller.Tests/PhotoCuller.Tests.csproj --no-restore -v:q` -> exit 0, 0 errors (repository warnings remain).
- Narrow tests: `dotnet test src/PhotoCuller.Tests/PhotoCuller.Tests.csproj --no-build -v:q --filter "FullyQualifiedName~PhotoAnalysisEngineTests|FullyQualifiedName~PhotoScannerTests"` -> Passed 7, Failed 0, Skipped 0.
- Full test project: `dotnet test src/PhotoCuller.Tests/PhotoCuller.Tests.csproj --no-build -v:q` -> Passed 81, Failed 0, Skipped 0.
- Solution discovery: `dotnet test PhotoCuller.slnx --list-tests --no-build -v:q` -> exit 0 and lists all 7 new tests by fully qualified name.
- Non-incremental solution build: `dotnet build PhotoCuller.slnx --no-incremental --no-restore -v:q` -> exit 0, 0 errors (187 repository warnings remain).

## Outcome
- Status: complete
- Scope verdict: **Strong**. The caller-visible allow/reject branches, decode failure, cancellation, input order, and cache refresh behavior are all protected by focused assertions.
- No credible high-risk survivor remains in the requested similarity-grouping scope. Bounded cache eviction and native disposal are internal resource-management details and were not expanded into behavioral tests.

## Requirement-to-Test Map
| Requirement | Evidence |
|---|---|
| Hash-compatible scene changes must not group | `BuildGroups_LoosePreset_HashCompatibleSceneChange_LocalFeaturesRejectPair` |
| Genuine sequential views must remain grouped | `BuildGroups_LoosePreset_HashCompatibleSameScene_LocalFeaturesKeepPair` |
| Small viewpoint changes must match regardless of input order | `IsSameScene_SmallViewpointChange_ReturnsTrue` |
| Unrelated textured scenes must be rejected | `IsSameScene_UnrelatedTexturedImages_ReturnsFalse` |
| Unreadable images must fail closed | `IsSameScene_UnreadableImage_ReturnsFalseWithoutThrowing` |
| Cancellation must propagate | `IsSameScene_CancelledBeforeExtraction_ThrowsCancellation` |
| Rescanning a changed file must refresh cached descriptors | `IsSameScene_FileChangedBetweenScans_RefreshesCachedFeatures` |

## Assertion Quality
- New tests reviewed: 7
- Assertions/verifications: 14 (average 2.0 per test)
- Assertion categories: equality, boolean, collection, negative, state/side-effect, structural/deep, exception
- Assertion-free tests: 0
- Trivial-only tests: 0
- Self-referential tests: 0
- The rejection tests assert both the empty public result and unchanged `GroupId`; the acceptance test asserts exact members, group type, and dependency interaction. These fail under plausible union/rejection/path-order regressions.

## Real-photo Calibration
- Read-only calibration against the 92-photo `2025_08_30 上海影视乐园` data set.
- Final result: 20 groups containing 54 photos; the earlier hash-only result contained 63 grouped photos.
- Scene boundaries `2077–2078`, `2088–2089`, `2121–2122`, `2136–2137`, and `2152–2153` were rejected.
- Same-scene sequences including `2089–2091`, `2102–2104`, `2120–2121`, `2122–2123`, `2149–2152`, and `2153–2155` remained grouped.
- The calibration program was temporary and its source was removed after verification.

## Final Verification
- Full tests: `dotnet test PhotoCuller.slnx --no-build --no-restore -v:q` — 73 passed, 0 failed, 0 skipped.
- Harness discovery: 73 tests discovered, including all 7 new tests.
- Full non-incremental build: `dotnet build PhotoCuller.slnx --no-incremental --no-restore -v:q` — 0 errors, 171 warnings.
- Warnings are existing analyzer/package-source advisories, including the repository's current ImageSharp and SQLite dependency advisories; none were introduced as build errors by this change.
