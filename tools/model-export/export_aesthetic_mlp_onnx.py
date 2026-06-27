#!/usr/bin/env python
"""Export the LAION aesthetic predictor MLP head to ONNX.

This is a diagnostic/export helper for the small predictor head only:

    CLIP image embedding (768 floats) -> aesthetic score

The exported model is useful for validating the .pth weights, but it is not the
model PhotoCuller needs for direct image scoring. For PhotoCuller, use
export_clip_aesthetic_onnx.py so CLIP and the MLP head are exported together.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
import torch
import torch.nn as nn


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


def load_mlp(weights_path: Path) -> AestheticMLP:
    state_dict = torch.load(weights_path, map_location="cpu")
    if not isinstance(state_dict, dict):
        raise TypeError(f"Expected a state_dict in {weights_path}, got {type(state_dict)!r}")

    model = AestheticMLP()
    model.load_state_dict(state_dict)
    model.eval()
    return model


def export_onnx(model: nn.Module, output_path: Path, opset: int) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    dummy_embedding = torch.randn(1, 768, dtype=torch.float32)
    dummy_embedding = torch.nn.functional.normalize(dummy_embedding, dim=-1)

    torch.onnx.export(
        model,
        dummy_embedding,
        str(output_path),
        export_params=True,
        opset_version=opset,
        dynamo=False,
        do_constant_folding=True,
        input_names=["embedding"],
        output_names=["score"],
        dynamic_axes={
            "embedding": {0: "batch"},
            "score": {0: "batch"},
        },
    )


def validate(model: nn.Module, output_path: Path) -> None:
    onnx_model = onnx.load(output_path)
    onnx.checker.check_model(onnx_model)

    embedding = torch.randn(2, 768, dtype=torch.float32)
    embedding = torch.nn.functional.normalize(embedding, dim=-1)

    with torch.no_grad():
        torch_output = model(embedding).detach().cpu().numpy()

    session = ort.InferenceSession(str(output_path), providers=["CPUExecutionProvider"])
    ort_output = session.run(None, {"embedding": embedding.cpu().numpy()})[0]
    np.testing.assert_allclose(torch_output, ort_output, rtol=1e-4, atol=1e-4)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Export the aesthetic MLP head to ONNX.",
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
        default=Path("models/aesthetic/aesthetic-mlp-vit-l14.onnx"),
        help="Output .onnx path.",
    )
    parser.add_argument("--opset", type=int, default=17, help="ONNX opset version.")
    parser.add_argument(
        "--skip-validation",
        action="store_true",
        help="Skip ONNX checker and ONNX Runtime parity validation.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    model = load_mlp(args.weights)
    export_onnx(model, args.output, args.opset)

    if not args.skip_validation:
        validate(model, args.output)

    print(f"Exported MLP ONNX: {args.output.resolve()}")
    print("Note: this model accepts a 768-dim CLIP embedding, not an image tensor.")


if __name__ == "__main__":
    main()
