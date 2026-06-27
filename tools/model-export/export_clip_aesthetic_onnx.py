#!/usr/bin/env python
"""Export a complete image-input aesthetic scorer to ONNX.

The exported model contract matches PhotoCuller's local ONNX scorer:

    normalized RGB image tensor [N, 3, 224, 224] -> one scalar score per image

PhotoCuller performs resize, center-crop, RGB conversion, and CLIP mean/std
normalization before running ONNX inference. The model exported here therefore
expects already-normalized CLIP image tensors, not raw bytes or PIL images.
"""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import Literal

import numpy as np
import onnx
import onnxruntime as ort
import torch
import torch.nn as nn
import torch.nn.functional as F


Backend = Literal["open_clip", "openai_clip"]


class AestheticMLP(nn.Module):
    """MLP architecture used by improved-aesthetic-predictor ViT-L/14 heads."""

    def __init__(self, input_size: int = 768) -> None:
        super().__init__()
        self.layers = nn.Sequential(
            nn.Linear(input_size, 1024),
            nn.Dropout(0.2),
            nn.Linear(1024, 128),
            nn.Dropout(0.2),
            nn.Linear(128, 64),
            nn.Dropout(0.1),
            nn.Linear(64, 16),
            nn.Linear(16, 1),
        )

    def forward(self, embedding: torch.Tensor) -> torch.Tensor:
        return self.layers(embedding.float())


class OpenClipAestheticModel(nn.Module):
    def __init__(self, clip_model: nn.Module, mlp: AestheticMLP, zero_one: bool) -> None:
        super().__init__()
        self.clip_model = clip_model
        self.mlp = mlp
        self.zero_one = zero_one

    def forward(self, image: torch.Tensor) -> torch.Tensor:
        embedding = self.clip_model.encode_image(image.float(), normalize=True)
        score = self.mlp(embedding.float())
        if self.zero_one:
            score = torch.clamp((score - 1.0) / 9.0, 0.0, 1.0)
        return score


class OpenAiClipAestheticModel(nn.Module):
    def __init__(self, clip_model: nn.Module, mlp: AestheticMLP, zero_one: bool) -> None:
        super().__init__()
        self.clip_model = clip_model
        self.mlp = mlp
        self.zero_one = zero_one

    def forward(self, image: torch.Tensor) -> torch.Tensor:
        embedding = self.clip_model.encode_image(image.float())
        embedding = F.normalize(embedding.float(), dim=-1)
        score = self.mlp(embedding)
        if self.zero_one:
            score = torch.clamp((score - 1.0) / 9.0, 0.0, 1.0)
        return score


def load_mlp(weights_path: Path) -> AestheticMLP:
    state_dict = torch.load(weights_path, map_location="cpu")
    if not isinstance(state_dict, dict):
        raise TypeError(f"Expected a state_dict in {weights_path}, got {type(state_dict)!r}")

    model = AestheticMLP()
    model.load_state_dict(state_dict)
    model.eval()
    return model


def load_clip_model(
    backend: Backend,
    clip_model_name: str,
    pretrained: str,
    download_root: Path | None,
) -> nn.Module:
    if backend == "open_clip":
        import open_clip

        model, _, _ = open_clip.create_model_and_transforms(
            clip_model_name,
            pretrained=pretrained,
            device="cpu",
            cache_dir=str(download_root) if download_root else None,
        )
        model.float()
        model.eval()
        return model

    try:
        import clip
    except Exception as ex:  # pragma: no cover - depends on local Python env
        raise RuntimeError(
            "Unable to import the OpenAI 'clip' package. Use '--backend open_clip' "
            "or repair the clip package in this Python environment."
        ) from ex

    model, _ = clip.load(
        clip_model_name,
        device="cpu",
        jit=False,
        download_root=str(download_root) if download_root else None,
    )
    model.float()
    model.eval()
    return model


def build_model(args: argparse.Namespace) -> nn.Module:
    mlp = load_mlp(args.weights)
    clip_model = load_clip_model(
        args.backend,
        args.clip_model,
        args.pretrained,
        args.download_root,
    )

    if args.backend == "open_clip":
        model: nn.Module = OpenClipAestheticModel(
            clip_model,
            mlp,
            zero_one=args.output_range == "0-1",
        )
    else:
        model = OpenAiClipAestheticModel(
            clip_model,
            mlp,
            zero_one=args.output_range == "0-1",
        )

    model.eval()
    return model


def export_onnx(model: nn.Module, output_path: Path, opset: int) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    dummy_image = torch.randn(1, 3, 224, 224, dtype=torch.float32)

    torch.onnx.export(
        model,
        dummy_image,
        str(output_path),
        export_params=True,
        opset_version=opset,
        dynamo=False,
        do_constant_folding=True,
        input_names=["image"],
        output_names=["score"],
        dynamic_axes={
            "image": {0: "batch"},
            "score": {0: "batch"},
        },
    )


def validate(model: nn.Module, output_path: Path, batch_size: int) -> None:
    onnx_model = onnx.load(output_path)
    onnx.checker.check_model(onnx_model)

    image = torch.randn(batch_size, 3, 224, 224, dtype=torch.float32)
    with torch.no_grad():
        torch_output = model(image).detach().cpu().numpy()

    session = ort.InferenceSession(str(output_path), providers=["CPUExecutionProvider"])
    ort_output = session.run(None, {"image": image.cpu().numpy()})[0]
    np.testing.assert_allclose(torch_output, ort_output, rtol=2e-3, atol=2e-3)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Export CLIP ViT-L/14 + aesthetic MLP to a PhotoCuller-compatible ONNX model.",
    )
    parser.add_argument(
        "--weights",
        type=Path,
        required=True,
        help="Path to aesthetic_predictor_weights.pth or sac+logos+ava1-l14-linearMSE.pth.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("models/aesthetic/clip-vit-l14-aesthetic.onnx"),
        help="Output .onnx path.",
    )
    parser.add_argument(
        "--backend",
        choices=["open_clip", "openai_clip"],
        default="open_clip",
        help="CLIP implementation to export.",
    )
    parser.add_argument(
        "--clip-model",
        default="ViT-L-14",
        help="CLIP model name. For openai_clip use 'ViT-L/14'.",
    )
    parser.add_argument(
        "--pretrained",
        default="laion2b_s32b_b82k",
        help="open_clip pretrained tag. Ignored for openai_clip.",
    )
    parser.add_argument(
        "--download-root",
        type=Path,
        default=None,
        help="Optional model cache/download directory.",
    )
    parser.add_argument("--opset", type=int, default=17, help="ONNX opset version.")
    parser.add_argument(
        "--output-range",
        choices=["1-10", "0-1"],
        default="1-10",
        help="Model output range. Use PhotoCuller preset '1–10 AVA' for the default.",
    )
    parser.add_argument(
        "--validation-batch-size",
        type=int,
        default=1,
        help="Batch size for ONNX Runtime parity validation.",
    )
    parser.add_argument(
        "--skip-validation",
        action="store_true",
        help="Skip ONNX checker and ONNX Runtime parity validation.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()

    if args.backend == "openai_clip" and args.clip_model == "ViT-L-14":
        args.clip_model = "ViT-L/14"

    model = build_model(args)
    export_onnx(model, args.output, args.opset)

    if not args.skip_validation:
        validate(model, args.output, args.validation_batch_size)

    print(f"Exported image-input aesthetic ONNX: {args.output.resolve()}")
    if args.output_range == "1-10":
        print("PhotoCuller preset: clip-aesthetic-1-10 / 1-10 AVA")
    else:
        print("PhotoCuller preset: clip-aesthetic-0-1 / 0-1")


if __name__ == "__main__":
    main()
