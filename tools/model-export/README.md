# PhotoCuller model export helpers

These scripts convert LAION/improved-aesthetic-predictor style weights into
ONNX models.

PhotoCuller needs an image-input model:

```text
normalized RGB image tensor [N, 3, 224, 224] -> scalar aesthetic score
```

An `aesthetic_predictor_weights.pth` checkpoint containing only the MLP head
has the following contract:

```text
CLIP embedding [N, 768] -> scalar aesthetic score
```

So the full PhotoCuller model must export both CLIP ViT-L/14 and the MLP head.

## Recommended export

From the PhotoCuller repo root:

Create a Python virtual environment and install the export dependencies:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install torch numpy onnx onnxruntime onnxscript open_clip_torch
```

Obtain a compatible ViT-L/14 MLP checkpoint separately, then replace
`C:\Models\aesthetic_predictor_weights.pth` below with its location. Model
weights are not included in this repository and retain their upstream terms.

```powershell
.\.venv\Scripts\python.exe `
  tools\model-export\export_clip_aesthetic_onnx.py `
  --weights C:\Models\aesthetic_predictor_weights.pth `
  --output models\aesthetic\clip-vit-l14-aesthetic.onnx `
  --backend open_clip `
  --clip-model ViT-L-14 `
  --pretrained laion2b_s32b_b82k
```

Then configure PhotoCuller:

1. AI 模型管理 → 添加本地模型
2. 模型类型选择「美学评分」
3. 模型路径选择 `models\aesthetic\clip-vit-l14-aesthetic.onnx`
4. 模型输出预设选择 `1–10 AVA 美学分`
5. 设置 → 本地模型评分 → 启用，并设置混合权重

## MLP-only validation export

This verifies that the `.pth` weights can be exported, but the result is not
directly usable by PhotoCuller because it accepts embeddings rather than images.

```powershell
.\.venv\Scripts\python.exe `
  tools\model-export\export_aesthetic_mlp_onnx.py `
  --weights C:\Models\aesthetic_predictor_weights.pth `
  --output models\aesthetic\aesthetic-mlp-vit-l14.onnx
```

## Output range

The full export defaults to raw `1–10` aesthetic scores. Use PhotoCuller's
`1–10 AVA 美学分` preset.

If you export with `--output-range 0-1`, use PhotoCuller's `0–1 归一化分数`
preset instead.
