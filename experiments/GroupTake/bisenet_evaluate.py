"""Offline BiSeNet feasibility check. Never edits sources or plugs a model into the WinUI app.

Upstream model/preprocessing: https://github.com/yakhyo/face-parsing
Reference class IDs: https://github.com/yakhyo/uniface/blob/main/docs/modules/parsing.md
Requires existing numpy, Pillow, opencv-python and onnxruntime. Photos stay on this machine.
"""

import argparse
import hashlib
import json
import platform
import time
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort
from PIL import Image, ImageDraw, ImageOps


MODEL_HASHES = {
    "resnet18.onnx": "0d9bd318e46987c3bdbfacae9e2c0f461cae1c6ac6ea6d43bbe541a91727e33f",
    "resnet34.onnx": "5b805bba7b5660ab7070b5a381dcf75e5b3e04199f1e9387232a77a00095102e",
}
CLASSES = ["background", "skin", "left_brow", "right_brow", "left_eye", "right_eye", "glasses",
           "left_ear", "right_ear", "earring", "nose", "mouth", "upper_lip", "lower_lip", "neck",
           "necklace", "cloth", "hair", "hat"]
HEAD_CLASSES = np.array([*range(1, 14), 17, 18], dtype=np.uint8)
PALETTE = np.array([[65, 65, 65]] + [[55, 190, 100]] * 13 +
                   [[255, 210, 40], [255, 210, 40], [245, 65, 65], [65, 120, 255], [60, 220, 220]], dtype=np.uint8)
MEAN = np.array([0.485, 0.456, 0.406], np.float32).reshape(1, 1, 3)
STD = np.array([0.229, 0.224, 0.225], np.float32).reshape(1, 1, 3)
CONTEXTS = {"tight": 1.8, "context": 2.4}


def digest(path):
    value = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for data in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(data)
    return value.hexdigest()


def crop_bounds(face, image_size, factor):
    x, y, w, h = face
    width, height = image_size
    if w <= 0 or h <= 0 or x < 0 or y < 0 or x + w > width or y + h > height:
        raise ValueError("Face box must be inside the EXIF-oriented image")
    side = min(max(16, round(max(w, h) * factor)), width, height)
    left = min(max(0, round(x + w / 2 - side / 2)), width - side)
    top = min(max(0, round(y + h / 2 + 0.03 * h - side / 2)), height - side)
    return left, top, left + side, top + side


def tensor_for(rgb):
    scaled = cv2.resize(rgb, (512, 512), interpolation=cv2.INTER_LINEAR).astype(np.float32) / 255.0
    return np.ascontiguousarray(((scaled - MEAN) / STD).transpose(2, 0, 1)[None])


def predict(session, rgb):
    start = time.perf_counter()
    outputs = session.run(None, {session.get_inputs()[0].name: tensor_for(rgb)})
    logits = outputs[0]
    if logits.shape != (1, 19, 512, 512) or not np.isfinite(logits).all():
        raise ValueError(f"Unexpected/nonfinite BiSeNet output: {logits.shape}")
    labels = logits.argmax(axis=1)[0].astype(np.uint8)
    labels = cv2.resize(labels, (rgb.shape[1], rgb.shape[0]), interpolation=cv2.INTER_NEAREST)
    elapsed = (time.perf_counter() - start) * 1000
    return labels, elapsed


def visuals(rgb, labels):
    head = np.isin(labels, HEAD_CLASSES)
    overlay = np.clip(rgb.astype(np.float32) * 0.55 + PALETTE[labels] * 0.45, 0, 255).astype(np.uint8)
    yy, xx = np.indices(labels.shape)
    checker = np.where(((xx // 8 + yy // 8) % 2)[..., None], 210, 170).astype(np.uint8)
    checker = np.repeat(checker, 3, axis=2)
    cutout = np.where(head[..., None], rgb, checker)
    alpha = np.dstack((rgb, head.astype(np.uint8) * 255))
    return overlay, cutout, alpha, head


def inspect_points(labels, crop, points):
    results = []
    for name, p in zip(["left_eye", "right_eye", "nose", "left_mouth", "right_mouth"], points):
        x, y = int(round(p[0])) - crop[0], int(round(p[1])) - crop[1]
        if not (0 <= x < labels.shape[1] and 0 <= y < labels.shape[0]):
            results.append({"landmark": name, "in_crop": False})
            continue
        patch = labels[max(0, y - 1):y + 2, max(0, x - 1):x + 2]
        results.append({"landmark": name, "in_crop": True, "head_fraction": float(np.isin(patch, HEAD_CLASSES).mean())})
    return results


def inspect_rois(labels, crop, rois):
    results = []
    for name, rect in rois.items():
        left, top = max(rect[0], crop[0]), max(rect[1], crop[1])
        right, bottom = min(rect[2], crop[2]), min(rect[3], crop[3])
        if left >= right or top >= bottom:
            results.append({"label": name, "covered": False})
            continue
        patch = labels[top - crop[1]:bottom - crop[1], left - crop[0]:right - crop[0]]
        results.append({"label": name, "covered": True,
                        "fully_covered": [left, top, right, bottom] == rect,
                        "excluded_from_head_fraction": float((~np.isin(patch, HEAD_CLASSES)).mean()),
                        "classes": {CLASSES[i]: int((patch == i).sum()) for i in np.unique(patch)}})
    return results


def make_sheet(rows, destination, headings, note):
    cell = 300
    header = 74
    sheet = Image.new("RGB", (len(headings) * cell, header + len(rows) * (cell + 28)), (28, 28, 28))
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 5), note, fill="white")
    draw.text((8, 23), "green=face parts; blue=hair; yellow=neck; red=clothes; gray=background", fill="white")
    for i, title in enumerate(headings):
        draw.text((i * cell + 8, 47), title, fill="white")
    for r, (caption, images) in enumerate(rows):
        y = header + r * (cell + 28)
        for col, image in enumerate(images):
            image = image.convert("RGB").resize((cell, cell), Image.Resampling.NEAREST if col == 1 else Image.Resampling.LANCZOS)
            sheet.paste(image, (col * cell, y + 26))
        draw.text((8, y + 5), caption, fill="white")
    sheet.save(destination)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cases", type=Path, required=True, help="Private JSON with case IDs, photo paths and manually selected face boxes")
    parser.add_argument("--model", type=Path, action="append", required=True)
    parser.add_argument("--output", type=Path, required=True, help="New private directory, not an existing output")
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError("Output already exists; use a new directory")
    cases = json.loads(args.cases.read_text(encoding="utf-8-sig"))
    ids = [case["id"] for case in cases]
    if not cases or len(ids) != len(set(ids)) or any(not i or Path(i).name != i for i in ids):
        raise ValueError("Case IDs must be nonempty, unique filenames")
    for path in args.model:
        if path.name not in MODEL_HASHES or digest(path) != MODEL_HASHES[path.name]:
            raise ValueError(f"Unverified upstream model: {path.name}")
    photos = {}
    source_hashes = {}
    for case in cases:
        path = str(Path(case["photo"]).resolve())
        if path not in photos:
            source_hashes[path] = digest(path)
            with Image.open(path) as photo:
                photos[path] = ImageOps.exif_transpose(photo).convert("RGB")
    args.output.mkdir(parents=True, exist_ok=False)
    report = {"task": "segmentation_only_not_face_synthesis", "providers": ["CPUExecutionProvider"],
              "versions": {"python": platform.python_version(), "onnxruntime": ort.__version__, "opencv": cv2.__version__},
              "head_class_ids": HEAD_CLASSES.tolist(), "excluded_class_ids": [0, 14, 15, 16],
              "models": [], "cases": [], "note": "Classified pixels are predictions, not ground-truth annotations. No face pixels were generated."}
    for model in args.model:
        options = ort.SessionOptions()
        options.intra_op_num_threads = 4
        options.inter_op_num_threads = 1
        session = ort.InferenceSession(str(model), sess_options=options, providers=["CPUExecutionProvider"])
        warm_start = time.perf_counter()
        predict(session, np.zeros((200, 200, 3), dtype=np.uint8))
        report["models"].append({"name": model.stem, "sha256": digest(model),
                                 "warmup_ms": (time.perf_counter() - warm_start) * 1000,
                                 "input": session.get_inputs()[0].shape, "outputs": [o.shape for o in session.get_outputs()]})
        for case in cases:
            photo = photos[str(Path(case["photo"]).resolve())]
            for context, factor in CONTEXTS.items():
                crop = crop_bounds(case["face_bounds"], photo.size, factor)
                rgb = np.asarray(photo.crop(crop))
                labels, elapsed = predict(session, rgb)
                overlay, cutout, alpha, head = visuals(rgb, labels)
                out = args.output / model.stem / case["id"] / context
                out.mkdir(parents=True)
                Image.fromarray(rgb).save(out / "crop.png")
                Image.fromarray(labels).save(out / "labels.png")
                Image.fromarray(overlay).save(out / "regions.png")
                Image.fromarray(head.astype(np.uint8) * 255).save(out / "head-mask.png")
                Image.fromarray(cutout).save(out / "head-cutout-checker.png")
                Image.fromarray(alpha).save(out / "head-cutout.png")
                report["cases"].append({"id": case["id"], "group": case["group"], "frame": case["frame"],
                    "model": model.stem, "context": context, "crop_bounds": crop, "native_crop_size": [rgb.shape[1], rgb.shape[0]],
                    "elapsed_ms": elapsed, "head_fraction": float(head.mean()),
                    "hair_fraction": float((labels == 17).mean()), "cloth_fraction": float((labels == 16).mean()),
                    "landmark_checks": inspect_points(labels, crop, case.get("landmarks", [])),
                    "manual_region_checks": inspect_rois(labels, crop, case.get("check_regions", {}))})
        print(f"Finished {model.stem}: {len(cases)} selected faces x {len(CONTEXTS)} crop contexts", flush=True)
        del session
    for model in args.model:
        for group in sorted({c["group"] for c in cases}):
            selected = [c for c in cases if c["group"] == group]
            for context in CONTEXTS:
                rows = []
                for case in selected:
                    folder = args.output / model.stem / case["id"] / context
                    rows.append((case["id"], [Image.open(folder / name) for name in ["crop.png", "regions.png", "head-cutout-checker.png"]]))
                make_sheet(rows, args.output / f"{model.stem}-{group}-{context}.png",
                           ["Original crop", "Predicted regions", "Predicted head pixels"], f"{model.stem} / {group} / {context} (crops enlarged for inspection)")
    changed = [Path(path).name for path, expected in source_hashes.items() if digest(path) != expected]
    report["source_files_checked"] = len(source_hashes)
    report["source_files_changed"] = changed
    if changed:
        raise RuntimeError("Source files changed during evaluation")
    (args.output / "metrics.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Saved {len(report['cases'])} real inference results; {len(source_hashes)} sources unchanged: {args.output}", flush=True)


if __name__ == "__main__":
    main()
