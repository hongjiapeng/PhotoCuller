"""Offline jaw/neck transition experiment; deliberately not the WinUI default.

Requires numpy, Pillow, opencv-python and onnxruntime. Configuration and photographs
stay in private folders. Scale is manually selected, not an automatic pose solution.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort
from PIL import Image, ImageOps

MODEL_SHA256 = "5b805bba7b5660ab7070b5a381dcf75e5b3e04199f1e9387232a77a00095102e"
HEAD_CLASSES = [*range(1, 14), 17]


def photo(path):
    with Image.open(path) as image:
        return cv2.cvtColor(np.array(ImageOps.exif_transpose(image).convert("RGB")), cv2.COLOR_RGB2BGR)


def selection(box, eyes, image):
    x, y, w, h = box
    points = np.array(eyes, np.float64)
    if (w < 16 or h < 16 or x < 0 or y < 0 or x + w > image.shape[1] or y + h > image.shape[0]
            or points.shape != (2, 2) or not np.isfinite(points).all() or points[0, 0] >= points[1, 0]
            or (points[:, 0] < x).any() or (points[:, 0] >= x + w).any()
            or (points[:, 1] < y).any() or (points[:, 1] >= y + h).any()):
        raise ValueError("Invalid complete-head bounds or image-ordered eye references")
    return points


def labels(session, image, box):
    x, y, w, h = box
    side = math.ceil(max(w, h) * 1.25)
    left = max(0, int(x + w / 2 - side / 2))
    top = max(0, int(y + h * .62 - side / 2))
    crop = image[top:top + side, left:left + side]
    rgb = cv2.cvtColor(cv2.resize(crop, (512, 512)), cv2.COLOR_BGR2RGB).astype(np.float32) / 255
    rgb = (rgb - np.array([.485, .456, .406], np.float32)) / np.array([.229, .224, .225], np.float32)
    output = session.run(None, {session.get_inputs()[0].name: rgb.transpose(2, 0, 1)[None]})[0]
    if output.shape != (1, 19, 512, 512):
        raise ValueError("Incompatible model output")
    native = cv2.resize(output.argmax(1)[0].astype(np.uint8), (crop.shape[1], crop.shape[0]), interpolation=cv2.INTER_NEAREST)
    full = np.zeros(image.shape[:2], np.uint8)
    full[top:top + native.shape[0], left:left + native.shape[1]] = native
    return full


def evaluate(config, model_path, output):
    model_hash = hashlib.sha256(model_path.read_bytes()).hexdigest()
    if model_hash != MODEL_SHA256:
        raise ValueError("This experiment was checked only with the original ResNet34 weights")
    target_path = Path(config["BasePath"]).resolve()
    source_path = Path(config["SourcePath"]).resolve()
    initial_hashes = [hashlib.sha256(p.read_bytes()).hexdigest() for p in (target_path, source_path)]
    target, source = photo(target_path), photo(source_path)
    tb, sb = config["BaseHead"], config["SourceHead"]
    ae = selection(tb, config["BaseEyes"], target)
    be = selection(sb, config["SourceEyes"], source)
    scale = float(config["Scale"])
    feather = float(config.get("FeatherRadius", 2))
    lower = float(config.get("JawBandBelowEyesPixels", 45))
    if not all(math.isfinite(v) for v in (scale, feather, lower)) or not .75 <= scale <= 1.25 or not 0 < feather <= 32 or not 0 < lower <= tb[3]:
        raise ValueError("Invalid manually selected scale, feather or jaw band")
    a_delta, b_delta = ae[1] - ae[0], be[1] - be[0]
    rotation = math.atan2(a_delta[1], a_delta[0]) - math.atan2(b_delta[1], b_delta[0])
    if abs(rotation) > math.radians(25):
        raise ValueError("Large rotation is outside this experiment")
    linear = np.array([[math.cos(rotation), -math.sin(rotation)], [math.sin(rotation), math.cos(rotation)]]) * scale
    matrix = np.column_stack((linear, ae.mean(0) - linear @ be.mean(0)))
    options = ort.SessionOptions()
    options.intra_op_num_threads, options.inter_op_num_threads = 4, 1
    session = ort.InferenceSession(str(model_path), sess_options=options, providers=["CPUExecutionProvider"])
    tl, sl = labels(session, target, tb), labels(session, source, sb)
    th = np.isin(tl, HEAD_CLASSES).astype(np.uint8) * 255
    sh = np.isin(sl, HEAD_CLASSES).astype(np.uint8) * 255
    # Clip head candidates to the selected person. Neck is permitted only underneath that same box.
    for mask, box in ((th, tb), (sh, sb)):
        x, y, w, h = box
        clip = np.zeros(mask.shape, np.uint8)
        clip[y:y + h, x:x + w] = 255
        cv2.bitwise_and(mask, clip, dst=mask)
    tf, sf = th.copy(), sh.copy()
    for mask, label, box in ((tf, tl, tb), (sf, sl, sb)):
        x, y, w, h = box
        clip = np.zeros(mask.shape, np.uint8)
        clip[y:y + h + int(h * .35), x:x + w] = 255
        mask[(label == 14) & (clip > 0)] = 255
    height, width = target.shape[:2]
    aligned = cv2.warpAffine(source, matrix, (width, height))
    wh = cv2.warpAffine(sh, matrix, (width, height), flags=cv2.INTER_NEAREST)
    wf = cv2.warpAffine(sf, matrix, (width, height), flags=cv2.INTER_NEAREST)
    support = cv2.bitwise_and(th, wh)
    if cv2.countNonZero(support) < 64:
        raise ValueError("Insufficient aligned head support")
    x, y, w, h = cv2.boundingRect(tf)
    left, top = max(0, x - 16), max(0, y - 16)
    right, bottom = min(width, x + w + 16), min(height, y + h + 16)
    roi = (slice(top, bottom), slice(left, right))
    base_patch, aligned_patch, head_support = target[roi], aligned[roi], support[roi]
    distance = cv2.distanceTransform(np.pad(head_support, 1), cv2.DIST_L2, 3)[1:-1, 1:-1]
    alpha = np.clip(distance / feather, 0, 1)
    alpha = alpha * alpha * (3 - 2 * alpha)
    patch = np.rint(base_patch * (1 - alpha[:, :, None]) + aligned_patch * alpha[:, :, None]).clip(0, 255).astype(np.uint8)
    band = cv2.bitwise_and(tf, wf)[roi].copy()
    band[:max(0, int(ae.mean(0)[1] + lower - top))] = 0
    band[np.isin(tl[roi], [15, 16])] = 0  # Predicted target clothing and necklace remain untouched.
    if cv2.countNonZero(band) < 64:
        raise ValueError("Insufficient skin/neck support for transition")
    bx, by, bw, bh = cv2.boundingRect(band)
    # OpenCV modifies its mask argument while preparing the Poisson domain. Keep our support immutable.
    blended = cv2.seamlessClone(aligned_patch, patch, band.copy(), (bx + bw // 2, by + bh // 2), cv2.NORMAL_CLONE)
    patch[band > 0] = blended[band > 0]
    result = target.copy()
    result[roi] = patch
    allowed = support.copy()
    allowed[roi] |= band
    assert np.array_equal(result[allowed == 0], target[allowed == 0])
    assert np.array_equal(result[tl == 16], target[tl == 16])
    assert initial_hashes == [hashlib.sha256(p.read_bytes()).hexdigest() for p in (target_path, source_path)]
    metrics = {"modelSha256": model_hash, "configuration": config, "matrix": matrix.tolist(),
               "nativeSize": [width, height], "outsideSupportChangedChannels": 0,
               "transitionMaskBounds": [left, top, right - left, bottom - top],
               "predictedTargetClothingChangedChannels": 0, "sourceHashesUnchanged": True,
               "visualAcceptance": "NOT ACCEPTED: manual scale, one sample; ear/hair/pose limitations remain",
               "runtime": {"opencv": cv2.__version__, "onnxruntime": ort.__version__}}
    output.mkdir(parents=True, exist_ok=False)
    transition_full = np.zeros(target.shape[:2], np.uint8)
    transition_full[roi] = band
    for name, image in (("result.png", result), ("mask.png", allowed), ("transition-mask.png", transition_full),
                        ("target-labels.png", tl), ("source-labels.png", sl)):
        if not cv2.imwrite(str(output / name), image):
            raise OSError("Image export failed")
    (output / "metrics.json").write_text(json.dumps(metrics, ensure_ascii=False, indent=2), encoding="utf-8")
    print(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", type=Path, required=True)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError("Choose a new output directory")
    evaluate(json.loads(args.case.read_text(encoding="utf-8-sig")), args.model, args.output)
