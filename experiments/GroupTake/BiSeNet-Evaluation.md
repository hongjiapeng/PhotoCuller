# BiSeNet offline evaluation — 2026-11-06

## Decision

**Useful improvement over the ellipse, but raw predictions are not safe enough for automatic head replacement.**
Both official models excluded the three collar/neck patches which the old ellipse included. However, rear people's dark clothing can be mislabeled as hair or a hat. These failures are saved alongside the promising examples. No WinUI integration or new fusion result is claimed by this experiment.

The model predicts a segmentation mask only. Head cutouts copy the original crop's RGB pixels with a binary alpha channel; no face is generated, restored or repainted.

## Scope and method

- User photos: IMG_8681, IMG_8682, IMG_8688, IMG_8693, around 4000×2665 (10.7MP), from one burst.
- Three manually corresponding people: short hair/brown jacket, long hair, glasses. Existing face boxes and five points supply crop centers and diagnostic samples; no new identity matcher.
- Two square crop sizes: 1.8× and 2.4× the greater face-box dimension. Crop center is box center plus 0.03× box height vertically. Full EXIF orientation is applied first.
- Two official checkpoints, ResNet18 and ResNet34; 12 selected face instances × 2 crops × 2 models = **48 real inference results**.
- Official preprocessing: RGB, 512×512 linear resize, `/255`, ImageNet mean `[0.485,0.456,0.406]` / std `[0.229,0.224,0.225]`, NCHW. First output must be finite `[1,19,512,512]`; argmax classes are restored to the native crop by nearest-neighbor.
- Head mask includes classes 1–13, 17 (hair), 18 (hat); excludes 0 (background), 14/15 (neck/necklace), 16 (clothes). Hat false positives were deliberately retained as failure evidence, not silently removed.
- CPUExecutionProvider only, ONNX Runtime 1.24.3, OpenCV Python 4.13.0, Python 3.12.10. No packages or shared C# dependencies changed. No photos uploaded. Model hashes verified against official release asset digests.

## Measurements

| Model | ONNX bytes | Median inference + preprocessing/postprocessing | Crop-mask agreement median / minimum |
| --- | ---: | ---: | ---: |
| ResNet18 | 53,205,364 | 196.5ms | 0.881 / 0.694 |
| ResNet34 | 93,632,554 | 279.4ms | 0.897 / 0.725 |

Times exclude session creation/warmup, photo loading, PNG export and contact sheets. These are local single-run measurements, not a benchmark. Crop-mask agreement is IoU between predictions for the same person at two crop sizes after mapping back to original coordinates. **It is not ground-truth accuracy.**

The 3×3 samples at five supplied landmarks remained entirely inside the predicted head mask for all cases: 120/120 patches for each model. This checks only a few core locations; it does not validate all face pixels, expression quality, or head boundaries.

For IMG_8681's brown-jacket subject, all three previously marked garment/neck regions were excluded from the head mask by both models under both crop sizes:

| Original-coordinate patch | ResNet18 exclusion | ResNet34 exclusion |
| --- | ---: | ---: |
| Left collar `[1885,1385,1897,1400)` | 100% | 100% |
| Right collar `[1992,1370,2004,1394)` | 100% | 100% |
| Neck/collar `[1914,1414,1969,1418)` | 100% | 100% |

These are small manually inspected patches from one frame, not an image-wide clothing accuracy measure. Some collar pixels were called background rather than clothes; excluding them from the head mask is correct for compositing but does not establish exact class accuracy.

## Visual review and failure evidence

- Brown-jacket IMG_8681/8682: promising separation of hair, face, ears, neck and collar.
- Brown-jacket IMG_8688/8693: rear dark clothing can join the hair class, producing a false hair extension above the head. Merely keeping the face-connected component cannot remove an attached false region.
- Glasses IMG_8688: rear olive clothing is predicted as a hat. Tight-crop false hat area: 8,226 native pixels for ResNet18, 1,040 for ResNet34. These are visibly non-hat regions in this crop.
- Glasses IMG_8693: small disconnected background fragments remain in the raw head mask.
- Long hair: useful separation from shirt/neck, but some hair exits the crop and shoulder-overlapping strands complicate a full-head swap.
- Ear and hair edges remain binary and sometimes jagged. Native face boxes are about 85–120 pixels wide; resizing them to 512 does not recover fine hair detail.

ResNet34 is the better candidate **within this limited visual comparison**. Do not interpret this as a general accuracy ranking or a passed natural-compositing acceptance test. Further experiments should isolate the component containing the selected face, consider an explicit no-hat profile for this hat-free set, constrain false hair extensions and retain manual boundary protection. None of these follow-ups was used to hide the raw failures in this report.

## Artifacts and reproduction

Private output: `%USERPROFILE%\Pictures\GroupTake-Poc-Results\20261106\bisenet-eval-01\`.
Includes `metrics.json`, `summary.json`, per-case raw `labels.png`, `regions.png`, `head-mask.png`, RGBA cutout, checkerboard preview and contact sheets. `bisenet-success-and-failures.png` includes good and bad examples. All four source photo SHA256 hashes remained unchanged.

Models now default to `%USERPROFILE%\.cache\GroupTake.Poc\models\bisenet\`, outside Git. The earlier offline run used LocalAppData; that cache remains intact. A shared user-profile cache avoids development-process LocalAppData visibility differences. No private pictures are committed.

```powershell
./experiments/GroupTake/Get-BiSeNetModels.ps1
# Copy bisenet-cases.example.json to a private directory; set real paths, face boxes and corresponding points.
python experiments/GroupTake/bisenet_evaluate.py --cases C:/Private/cases.json --model "$env:USERPROFILE/.cache/GroupTake.Poc/models/bisenet/resnet18.onnx" --model "$env:USERPROFILE/.cache/GroupTake.Poc/models/bisenet/resnet34.onnx" --output C:/Private/new-evaluation
```

The output directory must not exist. Required Python modules: numpy, Pillow, opencv-python, onnxruntime; the developer machine already had them. Model downloader checks SHA256 and never replaces an existing mismatched file.

Model digests:

- ResNet18: `0d9bd318e46987c3bdbfacae9e2c0f461cae1c6ac6ea6d43bbe541a91727e33f`
- ResNet34: `5b805bba7b5660ab7070b5a381dcf75e5b3e04199f1e9387232a77a00095102e`

References: [upstream models and code](https://github.com/yakhyo/face-parsing), [ONNX preprocessing](https://github.com/yakhyo/face-parsing/blob/main/onnx_inference.py), [class definitions](https://github.com/yakhyo/uniface/blob/main/docs/modules/parsing.md), [official weights](https://github.com/yakhyo/face-parsing/releases/tag/weights).

## 2026-11-07 WinUI integration follow-up

The WinUI experiment now runs the same verified weights using the repository's existing Microsoft.ML.OnnxRuntime package (1.27.0), without Python. This follow-up excludes class 18 (hat), constrains the mask to the manually selected complete-head bounds, and offers original-coordinate erase/restore with undo/redo. These are explicit changes from the **raw** offline masks above; earlier failure evidence remains valid. No connected-component cleanup or hair matting is claimed. See [POC-Findings.md](POC-Findings.md) for the workflow, acceptance criteria and limitations.
