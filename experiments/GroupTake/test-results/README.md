# GroupTake verification records

Only the sanitized `validation-summary.json` is intended for version control. Real photographs,
screenshots, generated composites, model weights and local case JSON files stay outside Git.

Private validation output on the development machine:
`%LOCALAPPDATA%\GroupTake.Poc\validation-20261106\`.

The UI's **保存调试** button explicitly asks for an output folder and creates a fresh case directory.
Whole-head cases include `base.png`, `source.png`, `aligned.png`, `mask.png`, `head-result.png`,
and `metrics.json`. Legacy face cases additionally compare alpha, normal and mixed Poisson.
Whole-head metrics use `Basis = ManualHeadBoxes` and null landmark error; no landmark precision is claimed.

TRX reports and temporary external UI automation scripts are under the ignored `artifacts/GroupTake/`.
No UI automation engine or test-only endpoint is built into the app.

2026-11-07 BiSeNet WinUI results are recorded in `bisenetWinUiIntegration` in the summary.
Semantic head cases also include `target-mask-raw.png`, `target-mask-edited.png`,
`source-mask-raw.png` and `source-mask-edited.png`; metrics retain original-coordinate masks and ordered edits.
Current private UI evidence: `%USERPROFILE%\Pictures\GroupTake-Poc-Results\20261107\bisenet-winui\ui-pass6\`.
