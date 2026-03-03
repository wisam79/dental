#!/usr/bin/env python3
"""Train YOLO11n for FDI tooth detection on Roboflow dataset.

This script is tuned for panoramic dental X-rays where right/left orientation
must stay stable for FDI numbering.
"""

from __future__ import annotations

import argparse
import json
import math
import re
from pathlib import Path
from typing import Any

import torch
import yaml
from roboflow import Roboflow
from ultralytics import YOLO

# Requested direct key (replace later if needed).
DEFAULT_API_KEY = "UxT7PvYj53XbRO1nlOdG"
DEFAULT_WORKSPACE = "dentalxray-yjztn"
DEFAULT_PROJECT = "panoramic-dental-xray-fdi"
DEFAULT_VERSION = 1
DEFAULT_MODEL = "yolo11n.pt"

EXPECTED_FDI_32 = [
    11, 12, 13, 14, 15, 16, 17, 18,
    21, 22, 23, 24, 25, 26, 27, 28,
    31, 32, 33, 34, 35, 36, 37, 38,
    41, 42, 43, 44, 45, 46, 47, 48,
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train FDI detector with YOLO11n.")
    parser.add_argument("--api-key", default=DEFAULT_API_KEY)
    parser.add_argument("--workspace", default=DEFAULT_WORKSPACE)
    parser.add_argument("--project", default=DEFAULT_PROJECT)
    parser.add_argument("--version", type=int, default=DEFAULT_VERSION)
    parser.add_argument("--download-format", default="yolov8")
    parser.add_argument("--max-version-scan", type=int, default=40)

    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--epochs", type=int, default=180)
    parser.add_argument("--imgsz", type=int, default=1024)
    parser.add_argument("--batch", type=int, default=-1, help="-1 = auto-batch")
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--patience", type=int, default=35)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--device", default=None, help="e.g. 0, 0,1, cpu")

    parser.add_argument("--train-project", default="runs/fdi")
    parser.add_argument("--run-name", default="yolo11n_fdi_v2_1024")
    parser.add_argument("--cache", action="store_true")
    parser.add_argument("--exist-ok", action="store_true")
    parser.add_argument("--skip-train", action="store_true")
    parser.add_argument("--skip-export", action="store_true")
    return parser.parse_args()


def parse_fdi_from_label(label: str) -> int:
    """Extract FDI number from common label styles: 11, tooth-11, tooth_11."""
    text = str(label).strip()
    match = re.search(r"(?:^|[^0-9])([1-8][1-8])(?:[^0-9]|$)", text)
    if not match:
        return 0
    fdi = int(match.group(1))
    unit = fdi % 10
    if unit in (0, 9):
        return 0
    return fdi


def load_class_names(data_yaml_path: Path) -> list[str]:
    with data_yaml_path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f)

    names = data.get("names")
    if isinstance(names, list):
        return [str(n) for n in names]
    if isinstance(names, dict):
        return [str(v) for _, v in sorted(names.items(), key=lambda kv: int(kv[0]))]
    raise ValueError("Unsupported names format in data.yaml")


def build_fdi_map(class_names: list[str]) -> tuple[list[int], list[dict[str, Any]]]:
    class_map: list[int] = []
    unresolved: list[dict[str, Any]] = []

    for idx, name in enumerate(class_names):
        fdi = parse_fdi_from_label(name)
        class_map.append(fdi)
        if fdi == 0:
            unresolved.append({"index": idx, "label": name})

    return class_map, unresolved


def write_mapping_artifact(dataset_dir: Path, class_names: list[str], class_map: list[int]) -> Path:
    artifact = {
        "dataset_dir": str(dataset_dir),
        "num_classes": len(class_names),
        "names": class_names,
        "class_map": class_map,
        "class_map_matches_standard_32_fdi": class_map == EXPECTED_FDI_32,
    }
    out_path = dataset_dir / "fdi_class_map.generated.json"
    out_path.write_text(json.dumps(artifact, ensure_ascii=False, indent=2), encoding="utf-8")
    return out_path


def resolve_device(arg_device: str | None) -> str | int:
    if arg_device:
        return arg_device
    return 0 if torch.cuda.is_available() else "cpu"


def normalize_batch(batch: int, device: str | int) -> int:
    if batch != -1:
        return batch
    if str(device).lower() == "cpu":
        return 8
    return -1


def float_or_nan(value: Any) -> float:
    try:
        v = float(value)
        if math.isnan(v):
            return float("nan")
        return v
    except Exception:
        return float("nan")


def resolve_project_version(project: Any, requested_version: int, max_scan: int) -> tuple[Any, int]:
    """Resolve a valid Roboflow version, with fallback to latest available."""
    try:
        return project.version(requested_version), requested_version
    except RuntimeError:
        available: list[int] = []
        for v in range(1, max_scan + 1):
            try:
                project.version(v)
                available.append(v)
            except RuntimeError:
                continue

        if not available:
            raise RuntimeError(
                f"Requested version {requested_version} not found and no versions in 1..{max_scan} were found."
            )

        resolved = max(available)
        print(
            f"Requested version {requested_version} not found. "
            f"Falling back to latest available version: {resolved}."
        )
        return project.version(resolved), resolved


def main() -> None:
    args = parse_args()

    print("1) Downloading dataset from Roboflow...")
    rf = Roboflow(api_key=args.api_key)
    project = rf.workspace(args.workspace).project(args.project)
    version_obj, resolved_version = resolve_project_version(project, args.version, args.max_version_scan)
    dataset = version_obj.download(args.download_format)
    dataset_dir = Path(dataset.location)
    data_yaml = dataset_dir / "data.yaml"

    print(f"Dataset downloaded to: {dataset_dir}")
    print(f"Dataset version used: {resolved_version}")
    print(f"data.yaml: {data_yaml}")

    print("2) Validating class names and FDI mapping...")
    class_names = load_class_names(data_yaml)
    class_map, unresolved = build_fdi_map(class_names)
    map_file = write_mapping_artifact(dataset_dir, class_names, class_map)

    print(f"Classes count: {len(class_names)}")
    print(f"Generated class map file: {map_file}")
    if unresolved:
        print("Warning: unresolved class labels (no FDI extracted):")
        for item in unresolved:
            print(f"  - idx={item['index']}, label={item['label']}")
    else:
        print("All class labels were mapped to FDI successfully.")

    if class_map == EXPECTED_FDI_32:
        print("FDI order matches expected permanent 32-teeth sequence exactly.")
    else:
        print("FDI order differs from the expected permanent 32 sequence.")
        print("Use fdi_class_map.generated.json to align your app ClassMap.")

    if args.skip_train:
        print("Training skipped (--skip-train).")
        return

    print("3) Training YOLO11n with panoramic-safe settings...")
    device = resolve_device(args.device)
    batch = normalize_batch(args.batch, device)

    model = YOLO(args.model)
    results = model.train(
        data=str(data_yaml),
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=batch,
        workers=args.workers,
        device=device,
        patience=args.patience,
        seed=args.seed,
        deterministic=True,
        cos_lr=True,
        amp=True,
        cache=args.cache,
        rect=True,
        project=args.train_project,
        name=args.run_name,
        exist_ok=args.exist_ok,
        optimizer="AdamW",
        # Preserve anatomy orientation for correct FDI numbering:
        fliplr=0.0,
        flipud=0.0,
        # X-ray is grayscale; disable aggressive color augmentation:
        hsv_h=0.0,
        hsv_s=0.0,
        hsv_v=0.0,
        # Keep augmentation moderate for panoramic geometry:
        degrees=2.0,
        translate=0.03,
        scale=0.12,
        shear=0.0,
        perspective=0.0,
        mosaic=0.20,
        mixup=0.0,
        close_mosaic=15,
    )

    save_dir = Path(results.save_dir)
    best_pt = save_dir / "weights" / "best.pt"
    print(f"Training run directory: {save_dir}")
    print(f"Best weights: {best_pt}")

    if best_pt.exists():
        print("4) Evaluating best model on test split...")
        best_model = YOLO(str(best_pt))
        test_metrics = best_model.val(
            data=str(data_yaml),
            split="test",
            imgsz=args.imgsz,
            device=device,
            batch=batch if batch != -1 else 16,
        )
        map50 = float_or_nan(getattr(test_metrics.box, "map50", float("nan")))
        map5095 = float_or_nan(getattr(test_metrics.box, "map", float("nan")))
        print(f"Test mAP@50: {map50:.4f}")
        print(f"Test mAP@50:95: {map5095:.4f}")

        if not args.skip_export:
            print("5) Exporting ONNX...")
            onnx_path = best_model.export(
                format="onnx",
                imgsz=args.imgsz,
                simplify=True,
                dynamic=False,
                opset=17,
            )
            print(f"ONNX exported to: {onnx_path}")
    else:
        print("Warning: best.pt not found. Skipping eval/export.")


if __name__ == "__main__":
    main()
