# PhotoAnalysisReport v1.0

Read this reference when converting CLI JSON into a selection report.

## Top-level fields

- `version`: must be `"1.0"`.
- `sourceDirectory`: absolute directory analyzed by the Engine.
- `startedAtUtc`, `completedAtUtc`: analysis timestamps.
- `photos`: analyzed photo records.
- `groups`: duplicate, burst, similar, or low-quality groups.

## Photo fields

Identify photos by `id`; use `path` and `fileName` for presentation. `quality` can contain nullable `overall`, `sharpness`, `exposure`, `contrast`, `noise`, `resolution`, `composition`, `color`, and `aesthetic` scores. A null score means unavailable, not zero.

`recommendation` values are `unprocessed`, `keep`, `review`, `toDelete`, `manualKeep`, and `manualDelete`. Manual states reflect a user's previous decision and take precedence in reporting, but never authorize a filesystem operation.

If `error` is non-null, surface it explicitly and treat conclusions about that photo as incomplete.

## Group fields

- `exactDuplicate`: identical SHA-256 content. Present these first because they are the highest-confidence candidates for review.
- `burst`: nearby captures passing the current time and similarity rules.
- `similar`: visually similar photos selected by the current hash/local-feature rules.
- `lowQuality`: photos grouped by the current low-quality rule.

Each group contains `id`, `type`, `similarity`, `recommendedPhotoId`, and `photoIds`. Resolve IDs against the top-level `photos` array. Do not silently choose a replacement if `recommendedPhotoId` is null or unresolved.

## Interpretation

Use the recommended photo and score dimensions to explain technical tradeoffs. Prefer concrete comparisons such as sharper, better exposed, or higher overall score. Do not invent visual-semantic reasons that are absent from the contract.
