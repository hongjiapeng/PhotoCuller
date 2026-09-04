# Test Implementation Plan

## 2026-09-04 Application Engine Refactor

| Requirement | Planned evidence |
|---|---|
| `pipeline orchestration/order/results` | `PhotoAnalysisEngineTests.AnalyzeAsync_TwoPhotos_RunsPipelineInRequiredOrderAndReturnsProcessedObjects` |
| `empty input` | `PhotoAnalysisEngineTests.AnalyzeAsync_EmptyScan_ScoresAndGroupsEmptyInputAndReturnsEmptyRun` |
| `corrupted-item propagation if representable with mocks` | `PhotoAnalysisEngineTests.AnalyzeAsync_CorruptedPhoto_PreservesErrorInRunAndReport` |
| `unsupported files (scanner integration or documented existing behavior)` | `PhotoScannerTests.ScanAsync_SupportedAndUnsupportedFiles_YieldsOnlySupportedFile` |
| `CancellationToken propagation` | `PhotoAnalysisEngineTests.AnalyzeAsync_ActiveCancellationToken_PropagatesSameTokenToEveryStage` and `PhotoAnalysisEngineTests.AnalyzeAsync_PreCancelledToken_StopsBeforeStartingPipeline` |
| `stable report version/mapping` | `PhotoAnalysisEngineTests.AnalyzeAsync_CompletedRun_MapsStableReportContract` |

### Implementation phases
1. Add Engine unit tests with substituted collaborators and synchronous event capture for deterministic orchestration assertions.
2. Add one bounded `PhotoScanner` filesystem integration test using a temporary directory and substituted repository/metadata collaborators.
3. Build and run only the two new test classes during the fix loop, then run the complete test project and a non-incremental solution build once.
4. Re-read assertions against source with pseudo-mutation and assertion-quality reviews; record findings and fixes in `.testagent/status.md`.

### Scope constraint
- Do not change production code unless a failing acceptance case exposes a minimal testability defect; notify the parent agent before such a change.

## Goal
Prevent adjacent but semantically different photos from entering the same similar-photo group, while retaining genuinely overlapping sequential shots and existing global near-duplicate behavior.

## Phase 1: Pin Grouping Behavior
- Extend `DuplicateGroupingServiceTests.cs` with a matcher substitute.
- Add a regression where pHash, dHash, time, directory, and file number all pass but local-feature verification rejects the pair.
- Add a positive case where local-feature verification accepts a sequential pair and verify the matcher receives the exact paths.
- Keep global hash-only near-duplicate tests independent of local verification.

## Phase 2: Implement Local Geometry Verification
- Add `ILocalFeatureMatcher` to Core.
- Implement `OrbLocalFeatureMatcher` with grayscale downscaling, ORB descriptors, Hamming KNN ratio filtering, and RANSAC homography inlier checks.
- Cache extracted descriptors for the lifetime of one grouping service instance and dispose native resources.
- Return false when a file cannot be decoded or has too few reliable features.
- Register the matcher in dependency injection and require it only for the tolerant capture-sequence path.

## Phase 3: Test the OpenCV Implementation
- Same textured scene with a small perspective transform returns true.
- Unrelated textured scenes return false.
- Missing or unreadable files return false without throwing.

## Phase 4: Calibration and Verification
- Build the test project and fix exact OpenCvSharp API mismatches.
- Run the focused tests.
- Evaluate known good and bad transitions from the user's real folder and tune only ORB quality thresholds.
- Run all solution tests, confirm harness discovery, then run a non-incremental solution build.
- Review the generated tests for mutation-resistant assertions and caller-visible gaps; record results in `.testagent/status.md`.

## Success Criteria
- Hash-compatible scene changes are rejected when ORB geometry is not consistent.
- Genuine sequential views with shared local features are grouped.
- Matcher failure is conservative and does not create a false group.
- All existing and new tests pass; the complete solution builds.
