---
name: photoculler-select-photos
description: Analyze a local photo directory with PhotoCuller's CLI and turn its versioned result into a read-only culling report. Use when the user asks PhotoCuller to score, group, deduplicate, review, or recommend local photos; not for editing images.
---

# PhotoCuller Select Photos

Use the repository's `PhotoCuller.Cli` as the only analysis entry point. Do not reproduce scoring, similarity, grouping, or ranking logic in the skill.

## Workflow

1. Resolve the requested photo directory to an absolute local path. Ask for a path only when none is available.
2. Run `scripts/analyze.ps1 -PhotoDirectory <path>`. Pass `-OutputPath <path>` only when the user requests a saved JSON artifact. The script prints the generated JSON path after a successful run.
3. Read the JSON and require `version` to equal `1.0`. If the version differs, stop and report that the skill does not understand the contract.
4. Read [references/report-contract.md](references/report-contract.md) when interpreting fields or producing a detailed report.
5. Return a concise Markdown culling report containing:
   - source directory, analyzed photo count, group count, and files with errors;
   - exact-duplicate groups first, followed by burst, similar, and low-quality groups;
   - each group's recommended photo, other members, overall scores, and relevant score differences;
   - ungrouped photos that merit attention when their recommendation or score makes that useful;
   - a reminder that recommendations are technical signals for human review.
6. When a temporary JSON file was created only for analysis, remove that exact temporary file after reading it. Preserve a user-requested output file.

Use `-SkipBuild` only when the Release CLI was already built from the current working-tree revision. Otherwise let the script build before analysis.

## Boundaries

- Treat the workflow as read-only. Do not move, rename, delete, overwrite, or edit source photos or their metadata.
- Do not translate `toDelete` or `manualDelete` into a filesystem operation. They are report states only.
- Base explanations only on report fields. Do not claim knowledge of emotion, composition intent, storytelling, or subject importance unless a future visual model supplies that evidence.
- Keep local paths and analysis results local unless the user explicitly asks to publish or transmit them.
- The current CLI distribution requires Windows because it carries `OpenCvSharp4.runtime.win`. Explain this limitation when invoked on another operating system.
- If some photos contain `error`, keep them visible in the report and avoid treating missing scores as zero.
