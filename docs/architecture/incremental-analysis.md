# Incremental analysis and safe result publication

## Behavior

The scanner enumerates the selected directory and returns the complete current inventory. Unchanged photos are detached copies of stored data, identified by normalized path, file size, exact modification timestamp, successful metadata and analysis version. Changed files retain their identity and manual keep/reject decision but discard derived analysis. Missing files leave the active result only after a successful scan. Pending-delete subfolders and directory reparse points are excluded. An inaccessible directory fails the scan so an incomplete traversal cannot replace the committed result.

The engine reuses cached SHA-256/pHash values. WinUI hashes only repeated positive file sizes; the default engine/CLI path still computes every file hash. All photos participate in perceptual matching regardless of exact-hash eligibility. Groups are rebuilt from the entire inventory, with manual rejection excluded from recommendation and manual keep taking precedence.

Scoring fingerprints include the source file state, dimensions, scoring algorithm version, weights, model identity/preset/file state and blend ratio. Failed dimensions and failed inference remain retryable. At most four photos are scored concurrently, with no task allocated for every waiting file. ONNX sessions also distinguish model file versions.

Manual decisions are saved immediately. The UI serializes photo writes and waits for pending writes before analysis. Conflicting edits/moves are disabled during a scan. The previous displayed snapshot remains available throughout analysis. A private DbContext publishes group shells and photo snapshots in one transaction; cancellation or insertion failure rolls back all changes. Existing pending-delete records and operation logs survive replacement. UI collections switch only after commit.

Thumbnails use a versioned path/file-size/mtime identity and a separate file per requested edge length. Generation is bounded to four decoders, equal requests share a generation lock, and JPEGs are published by renaming a completed temporary file. Cancellation/failure removes temporary files.

## Limits

- This remains a single active analysis snapshot. Repeated scans of the current directory preserve decisions; independent multi-library histories are not introduced.
- File changes are detected by size and modification time. Replacing bytes while preserving both values requires invalidating the cache or changing the timestamp.
- The file inventory and groups are still held in memory and grouping is rebuilt on every successful scan. Viewport-priority thumbnail scheduling and paged UI loading are future work.
- The first scan after upgrading refreshes metadata and scores. Schema migration adds only the nullable `ScoringSignature` column; old thumbnails are harmless cache entries until cache cleanup.

## Regression evidence

The focused tests exercise complete inventory reuse, invalidation, consumer cancellation, manual-decision persistence and recommendation, exact-hash prefiltering, scoring configuration invalidation, real SQLite rollback after deletion, and actual image dimensions under concurrent thumbnail requests. `AnalyzeAndSave_RepeatedScans_ReusesWorkPreservesDecisionAndFindsNewDuplicate` composes the scanner, engine, scoring, grouping and SQLite repositories across successive scans, addition of a duplicate, and removal of a file.
