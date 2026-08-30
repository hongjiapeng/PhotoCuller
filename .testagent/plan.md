# Test Implementation Plan

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
