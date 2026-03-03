#!/usr/bin/env python3
"""Run ONNX model diagnostics and image inference for DentalID models.

This script inspects model metadata, ONNX graph details, ONNX Runtime session
details, and executes a real inference pass on a given image.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import platform
import statistics
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

import numpy as np
import onnx
import onnxruntime as ort
from PIL import Image, ImageDraw


DEFAULT_MODEL_NAMES = [
    "teeth_detect.onnx",
    "pathology_detect.onnx",
    "encoder.onnx",
    "genderage.onnx",
]


@dataclass
class ModelInputSpec:
    name: str
    shape: List[Any]
    dtype: str


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def tensor_shape_to_list(shape: Sequence[Any]) -> List[Any]:
    result: List[Any] = []
    for dim in shape:
        if hasattr(dim, "dim_value"):
            if dim.dim_value:
                result.append(int(dim.dim_value))
            elif dim.dim_param:
                result.append(dim.dim_param)
            else:
                result.append("?")
        else:
            result.append(dim)
    return result


def get_onnx_io(model: onnx.ModelProto) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
    initializer_names = {x.name for x in model.graph.initializer}
    inputs: List[Dict[str, Any]] = []
    outputs: List[Dict[str, Any]] = []

    for i in model.graph.input:
        tensor_type = i.type.tensor_type
        inputs.append(
            {
                "name": i.name,
                "shape": tensor_shape_to_list(tensor_type.shape.dim),
                "elem_type": int(tensor_type.elem_type),
                "is_initializer": i.name in initializer_names,
            }
        )

    for o in model.graph.output:
        tensor_type = o.type.tensor_type
        outputs.append(
            {
                "name": o.name,
                "shape": tensor_shape_to_list(tensor_type.shape.dim),
                "elem_type": int(tensor_type.elem_type),
            }
        )

    return inputs, outputs


def image_to_rgb(image_path: Path) -> np.ndarray:
    with Image.open(image_path) as img:
        rgb = img.convert("RGB")
        return np.array(rgb, dtype=np.uint8)


def letterbox_rgb(
    rgb: np.ndarray, target: int
) -> Tuple[np.ndarray, float, float, float]:
    h, w = rgb.shape[:2]
    scale = min(target / w, target / h)
    new_w = int(w * scale)
    new_h = int(h * scale)
    pad_x = (target - new_w) / 2.0
    pad_y = (target - new_h) / 2.0

    resized = Image.fromarray(rgb).resize((new_w, new_h), Image.Resampling.BILINEAR)
    canvas = np.zeros((target, target, 3), dtype=np.uint8)
    y0 = int(pad_y)
    x0 = int(pad_x)
    canvas[y0 : y0 + new_h, x0 : x0 + new_w, :] = np.asarray(resized, dtype=np.uint8)
    return canvas, scale, pad_x, pad_y


def nchw_float01(rgb: np.ndarray) -> np.ndarray:
    return np.transpose(rgb.astype(np.float32) / 255.0, (2, 0, 1))[None, ...]


def hwc_float01(rgb: np.ndarray) -> np.ndarray:
    return rgb.astype(np.float32) / 255.0


def nchw_bgr_float255(rgb: np.ndarray) -> np.ndarray:
    bgr = rgb[:, :, ::-1].astype(np.float32)
    return np.transpose(bgr, (2, 0, 1))[None, ...]


def prep_for_model(model_name: str, rgb: np.ndarray, input_shape: Sequence[Any]) -> Tuple[np.ndarray, Dict[str, Any]]:
    low_name = model_name.lower()

    if "teeth" in low_name or "pathology" in low_name:
        target = 640
        canvas, scale, pad_x, pad_y = letterbox_rgb(rgb, target)
        return nchw_float01(canvas), {
            "kind": "detection",
            "target_size": target,
            "letterbox_scale": scale,
            "pad_x": pad_x,
            "pad_y": pad_y,
        }

    if "encoder" in low_name:
        target = 1024
        canvas, scale, pad_x, pad_y = letterbox_rgb(rgb, target)
        return hwc_float01(canvas), {
            "kind": "encoder",
            "target_size": target,
            "letterbox_scale": scale,
            "pad_x": pad_x,
            "pad_y": pad_y,
        }

    if "genderage" in low_name:
        target = 96
        resized = Image.fromarray(rgb).resize((target, target), Image.Resampling.BILINEAR)
        arr = np.asarray(resized, dtype=np.uint8)
        return nchw_bgr_float255(arr), {
            "kind": "genderage",
            "target_size": target,
            "color_order": "BGR",
            "range": "0..255",
        }

    # Generic fallback based on input shape.
    shape = list(input_shape)
    if len(shape) == 4 and shape[0] in (1, "?", None):
        h = shape[2] if isinstance(shape[2], int) else 640
        w = shape[3] if isinstance(shape[3], int) else 640
        canvas, scale, pad_x, pad_y = letterbox_rgb(rgb, min(h, w))
        return nchw_float01(canvas), {
            "kind": "generic_nchw",
            "target_size": min(h, w),
            "letterbox_scale": scale,
            "pad_x": pad_x,
            "pad_y": pad_y,
        }

    if len(shape) == 3 and isinstance(shape[0], int) and isinstance(shape[1], int):
        target = min(shape[0], shape[1])
        canvas, scale, pad_x, pad_y = letterbox_rgb(rgb, target)
        return hwc_float01(canvas), {
            "kind": "generic_hwc",
            "target_size": target,
            "letterbox_scale": scale,
            "pad_x": pad_x,
            "pad_y": pad_y,
        }

    raise ValueError(f"Unsupported input shape for preprocessing: {shape}")


def array_stats(arr: np.ndarray) -> Dict[str, Any]:
    flat = arr.astype(np.float32).ravel()
    if flat.size == 0:
        return {"size": 0}
    return {
        "shape": list(arr.shape),
        "dtype": str(arr.dtype),
        "size": int(flat.size),
        "min": float(np.min(flat)),
        "max": float(np.max(flat)),
        "mean": float(np.mean(flat)),
        "std": float(np.std(flat)),
    }


def clamp(value: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, value))


def select_yolo_detections(
    output: np.ndarray,
    threshold: float,
    iou_threshold: float,
    class_names: Optional[List[str]] = None,
    class_map: Optional[List[int]] = None,
) -> Dict[str, Any]:
    # Expects [1, C, N] where C = 4 + num_classes
    if output.ndim != 3 or output.shape[0] != 1 or output.shape[1] < 5:
        return {"error": "output shape is not standard YOLO [1,C,N]"}

    c = output.shape[1]
    n = output.shape[2]
    num_classes = c - 4
    confs = output[0, 4:, :]  # [num_classes, N]
    best_conf = np.max(confs, axis=0)  # [N]
    best_cls = np.argmax(confs, axis=0)  # [N]
    boxes_xywh = output[0, 0:4, :].T.astype(np.float32)  # [N,4]

    kept_idx = np.where(best_conf >= threshold)[0]

    def iou_xywh(a: np.ndarray, b: np.ndarray) -> float:
        ax1 = float(a[0] - a[2] / 2.0)
        ay1 = float(a[1] - a[3] / 2.0)
        ax2 = float(a[0] + a[2] / 2.0)
        ay2 = float(a[1] + a[3] / 2.0)
        bx1 = float(b[0] - b[2] / 2.0)
        by1 = float(b[1] - b[3] / 2.0)
        bx2 = float(b[0] + b[2] / 2.0)
        by2 = float(b[1] + b[3] / 2.0)
        ix1 = max(ax1, bx1)
        iy1 = max(ay1, by1)
        ix2 = min(ax2, bx2)
        iy2 = min(ay2, by2)
        iw = max(0.0, ix2 - ix1)
        ih = max(0.0, iy2 - iy1)
        inter = iw * ih
        area_a = max(0.0, ax2 - ax1) * max(0.0, ay2 - ay1)
        area_b = max(0.0, bx2 - bx1) * max(0.0, by2 - by1)
        den = area_a + area_b - inter
        return inter / den if den > 0 else 0.0

    sorted_idx = kept_idx[np.argsort(best_conf[kept_idx])[::-1]]
    selected_idx: List[int] = []
    suppressed = np.zeros(sorted_idx.shape[0], dtype=bool)
    for i in range(sorted_idx.shape[0]):
        if suppressed[i]:
            continue
        idx_i = int(sorted_idx[i])
        selected_idx.append(idx_i)
        bi = boxes_xywh[idx_i]
        for j in range(i + 1, sorted_idx.shape[0]):
            if suppressed[j]:
                continue
            idx_j = int(sorted_idx[j])
            bj = boxes_xywh[idx_j]
            if iou_xywh(bi, bj) > iou_threshold:
                suppressed[j] = True

    selected: List[Dict[str, Any]] = []
    for idx in selected_idx:
        cls_idx = int(best_cls[idx])
        cls_name = (
            class_names[cls_idx]
            if class_names is not None and 0 <= cls_idx < len(class_names)
            else f"class_{cls_idx}"
        )
        fdi_number = None
        if class_map is not None and 0 <= cls_idx < len(class_map):
            fdi_number = int(class_map[cls_idx])
        selected.append(
            {
                "prediction_index": int(idx),
                "class_index": cls_idx,
                "class_name": cls_name,
                "fdi_number": fdi_number,
                "confidence": float(best_conf[idx]),
                "xc": float(boxes_xywh[idx][0]),
                "yc": float(boxes_xywh[idx][1]),
                "w": float(boxes_xywh[idx][2]),
                "h": float(boxes_xywh[idx][3]),
            }
        )

    return {
        "channels": int(c),
        "num_predictions": int(n),
        "num_classes": int(num_classes),
        "best_conf": best_conf,
        "best_cls": best_cls,
        "boxes_xywh": boxes_xywh,
        "kept_count": int(kept_idx.size),
        "selected": selected,
    }


def build_fdi_gap_analysis(
    post: Dict[str, Any],
    class_map: List[int],
    threshold: float,
) -> Dict[str, Any]:
    best_conf: np.ndarray = post["best_conf"]
    selected: List[Dict[str, Any]] = post["selected"]
    present_fdi = {int(d["fdi_number"]) for d in selected if d.get("fdi_number") is not None and int(d["fdi_number"]) > 0}

    rows: List[Dict[str, Any]] = []
    missing_low_conf: List[int] = []
    missing_other: List[int] = []

    for class_idx, fdi in enumerate(class_map):
        mx = float(best_conf[class_idx]) if class_idx < best_conf.shape[0] else 0.0
        present = int(fdi) in present_fdi
        if present:
            reason = "present_after_nms"
        elif mx < threshold:
            reason = "low_confidence_model_side"
            missing_low_conf.append(int(fdi))
        else:
            reason = "suppressed_or_misclassified_postprocess"
            missing_other.append(int(fdi))
        rows.append(
            {
                "fdi": int(fdi),
                "class_index": int(class_idx),
                "max_class_confidence": mx,
                "present_after_nms": present,
                "reason": reason,
            }
        )

    return {
        "total_fdi_in_class_map": int(len(class_map)),
        "present_after_nms_count": int(len(present_fdi)),
        "missing_after_nms_count": int(len(class_map) - len(present_fdi)),
        "missing_low_confidence_model_side": sorted(missing_low_conf),
        "missing_suppressed_or_misclassified": sorted(missing_other),
        "rows": rows,
    }


def draw_detection_overlay(
    image_path: Path,
    detections: List[Dict[str, Any]],
    prep_info: Dict[str, Any],
    out_path: Path,
    *,
    color: Tuple[int, int, int] = (0, 200, 255),
    label_mode: str = "class",
    max_boxes: int = 300,
) -> Dict[str, Any]:
    with Image.open(image_path) as src:
        img = src.convert("RGB")
    draw = ImageDraw.Draw(img)
    orig_w, orig_h = img.size

    target = float(prep_info.get("target_size", 640))
    pad_x = float(prep_info.get("pad_x", 0.0))
    pad_y = float(prep_info.get("pad_y", 0.0))
    valid_w = max(1e-6, target - (2.0 * pad_x))
    valid_h = max(1e-6, target - (2.0 * pad_y))

    sorted_dets = sorted(detections, key=lambda d: d.get("confidence", 0.0), reverse=True)
    drawn = 0
    for det in sorted_dets[:max_boxes]:
        x_canvas = float(det["xc"]) - (float(det["w"]) / 2.0)
        y_canvas = float(det["yc"]) - (float(det["h"]) / 2.0)
        x_norm = clamp((x_canvas - pad_x) / valid_w, 0.0, 1.0)
        y_norm = clamp((y_canvas - pad_y) / valid_h, 0.0, 1.0)
        w_norm = clamp(float(det["w"]) / valid_w, 0.0, 1.0)
        h_norm = clamp(float(det["h"]) / valid_h, 0.0, 1.0)

        x0 = int(round(x_norm * orig_w))
        y0 = int(round(y_norm * orig_h))
        x1 = int(round((x_norm + w_norm) * orig_w))
        y1 = int(round((y_norm + h_norm) * orig_h))

        if x1 <= x0 or y1 <= y0:
            continue

        draw.rectangle((x0, y0, x1, y1), outline=color, width=2)

        if label_mode == "fdi" and det.get("fdi_number") is not None:
            label = f"{det['fdi_number']}:{det['confidence']:.2f}"
        else:
            label = f"{det.get('class_name', 'cls')}:{det['confidence']:.2f}"

        text_x = x0 + 2
        text_y = y0 - 12 if y0 >= 14 else y0 + 2
        draw.rectangle((text_x - 1, text_y - 1, text_x + (7 * len(label)), text_y + 11), fill=(0, 0, 0))
        draw.text((text_x, text_y), label, fill=color)
        drawn += 1

    out_path.parent.mkdir(parents=True, exist_ok=True)
    img.save(out_path)
    return {"path": str(out_path), "drawn_boxes": int(drawn), "requested_max_boxes": int(max_boxes)}


def parse_yolo_output_summary(
    output: np.ndarray,
    model_name: str,
    thresholds: Dict[str, float],
    iou_threshold: float,
    class_names: Optional[List[str]] = None,
    class_map: Optional[List[int]] = None,
) -> Dict[str, Any]:
    if "teeth" in model_name.lower():
        threshold = thresholds.get("teeth_threshold", 0.35)
    else:
        threshold = thresholds.get("pathology_threshold", 0.35)

    post = select_yolo_detections(
        output,
        threshold=threshold,
        iou_threshold=iou_threshold,
        class_names=class_names,
        class_map=class_map,
    )
    if "error" in post:
        return {"note": post["error"]}

    best_conf: np.ndarray = post["best_conf"]
    best_cls: np.ndarray = post["best_cls"]
    selected = post["selected"]

    top_indices = np.argsort(best_conf)[::-1][:10]
    top_preds: List[Dict[str, Any]] = []
    for idx in top_indices.tolist():
        cls_idx = int(best_cls[idx])
        cls_name = (
            class_names[cls_idx]
            if class_names is not None and 0 <= cls_idx < len(class_names)
            else f"class_{cls_idx}"
        )
        top_preds.append(
            {
                "prediction_index": int(idx),
                "class_index": cls_idx,
                "class_name": cls_name,
                "confidence": float(best_conf[idx]),
            }
        )

    cls_hist: Dict[str, int] = {}
    for det in selected:
        cls_name = str(det.get("class_name", "class"))
        cls_hist[cls_name] = cls_hist.get(cls_name, 0) + 1

    unique_fdi = None
    if class_map:
        fdi_values = [int(det["fdi_number"]) for det in selected if det.get("fdi_number") and int(det["fdi_number"]) > 0]
        unique_fdi = sorted(set(fdi_values))

    return {
        "yolo_channels": int(post["channels"]),
        "num_predictions": int(post["num_predictions"]),
        "num_classes": int(post["num_classes"]),
        "threshold_used": float(threshold),
        "kept_predictions_count": int(post["kept_count"]),
        "nms_iou_threshold_used": float(iou_threshold),
        "kept_after_nms_count": int(len(selected)),
        "max_confidence": float(np.max(best_conf)) if best_conf.size else 0.0,
        "mean_best_confidence": float(np.mean(best_conf)) if best_conf.size else 0.0,
        "top_predictions": top_preds,
        "kept_class_histogram": cls_hist,
        "unique_fdi_after_nms": unique_fdi,
        "unique_fdi_count_after_nms": int(len(unique_fdi)) if unique_fdi is not None else None,
        "selected_preview": selected[:50],
    }


def parse_genderage_summary(output_arrays: List[np.ndarray]) -> Dict[str, Any]:
    if not output_arrays:
        return {"note": "no outputs"}
    arr = output_arrays[0].reshape(-1)
    if arr.size < 3:
        return {"note": "unexpected output length", "length": int(arr.size)}
    p_female = float(arr[0])
    p_male = float(arr[1])
    age = int(round(float(arr[2])))
    age = max(0, min(120, age))
    return {
        "female_score": p_female,
        "male_score": p_male,
        "predicted_gender": "Female" if p_female > p_male else "Male",
        "predicted_age": age,
    }


def parse_encoder_summary(output_arrays: List[np.ndarray]) -> Dict[str, Any]:
    if not output_arrays:
        return {"note": "no outputs"}
    arr = output_arrays[0]
    summary: Dict[str, Any] = {"output_stats": array_stats(arr)}
    if arr.ndim >= 4:
        c = int(arr.shape[1])
        h = int(arr.shape[2])
        w = int(arr.shape[3])
        pooled = arr[0].reshape(c, h * w).mean(axis=1)
        summary["mean_pooled_vector"] = {
            "length": int(pooled.size),
            "l2_norm": float(np.linalg.norm(pooled)),
            "mean": float(np.mean(pooled)),
            "std": float(np.std(pooled)),
            "min": float(np.min(pooled)),
            "max": float(np.max(pooled)),
            "preview_first_12": [float(x) for x in pooled[:12].tolist()],
        }
    return summary


def infer_model(
    model_path: Path,
    image_path: Path,
    thresholds: Dict[str, float],
    iou_threshold: float,
    pathology_classes: Optional[List[str]],
    fdi_class_map: Optional[List[int]],
    debug_dir: Optional[Path],
) -> Dict[str, Any]:
    session = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
    inputs = session.get_inputs()
    outputs = session.get_outputs()

    if not inputs:
        raise RuntimeError("Model has no inputs.")

    inp = inputs[0]
    spec = ModelInputSpec(name=inp.name, shape=list(inp.shape), dtype=inp.type)
    rgb = image_to_rgb(image_path)
    tensor, prep_info = prep_for_model(model_path.name, rgb, spec.shape)

    # Align tensor dtype with session input type if needed.
    if "float16" in spec.dtype:
        tensor = tensor.astype(np.float16)
    elif "float" in spec.dtype:
        tensor = tensor.astype(np.float32)

    start = time.perf_counter()
    output_arrays = session.run(None, {spec.name: tensor})
    duration_ms = (time.perf_counter() - start) * 1000.0

    output_summaries = []
    for o_info, arr in zip(outputs, output_arrays):
        output_summaries.append(
            {
                "name": o_info.name,
                "type": o_info.type,
                "shape_runtime": list(arr.shape),
                "stats": array_stats(arr),
            }
        )

    specialized: Dict[str, Any] = {}
    overlay_info: Optional[Dict[str, Any]] = None
    if "teeth" in model_path.name.lower() and output_arrays:
        threshold = float(thresholds.get("teeth_threshold", 0.35))
        post = select_yolo_detections(
            output_arrays[0],
            threshold=threshold,
            iou_threshold=iou_threshold,
            class_names=None,
            class_map=fdi_class_map,
        )
        specialized = parse_yolo_output_summary(
            output_arrays[0],
            model_path.name,
            thresholds,
            iou_threshold=iou_threshold,
            class_names=None,
            class_map=fdi_class_map,
        )
        if fdi_class_map:
            specialized["fdi_gap_analysis"] = build_fdi_gap_analysis(
                post,
                class_map=fdi_class_map,
                threshold=threshold,
            )
        if debug_dir is not None and "selected" in post and prep_info.get("kind") == "detection":
            overlay_path = debug_dir / f"{model_path.stem}_overlay.png"
            overlay_info = draw_detection_overlay(
                image_path,
                post["selected"],
                prep_info,
                overlay_path,
                color=(0, 220, 255),
                label_mode="fdi",
                max_boxes=200,
            )
    elif "pathology" in model_path.name.lower() and output_arrays:
        threshold = float(thresholds.get("pathology_threshold", 0.35))
        post = select_yolo_detections(
            output_arrays[0],
            threshold=threshold,
            iou_threshold=iou_threshold,
            class_names=pathology_classes,
            class_map=None,
        )
        specialized = parse_yolo_output_summary(
            output_arrays[0],
            model_path.name,
            thresholds,
            iou_threshold=iou_threshold,
            class_names=pathology_classes,
            class_map=None,
        )
        if debug_dir is not None and "selected" in post and prep_info.get("kind") == "detection":
            overlay_path = debug_dir / f"{model_path.stem}_overlay.png"
            overlay_info = draw_detection_overlay(
                image_path,
                post["selected"],
                prep_info,
                overlay_path,
                color=(255, 80, 80),
                label_mode="class",
                max_boxes=350,
            )
    elif "genderage" in model_path.name.lower():
        specialized = parse_genderage_summary(output_arrays)
    elif "encoder" in model_path.name.lower():
        specialized = parse_encoder_summary(output_arrays)

    return {
        "session": {
            "providers": session.get_providers(),
            "provider_options": session.get_provider_options(),
            "inputs": [
                {"name": i.name, "shape": list(i.shape), "type": i.type} for i in inputs
            ],
            "outputs": [
                {"name": o.name, "shape": list(o.shape), "type": o.type} for o in outputs
            ],
        },
        "preprocess": prep_info,
        "input_tensor_stats": array_stats(tensor),
        "inference_time_ms": duration_ms,
        "outputs": output_summaries,
        "specialized_summary": specialized,
        "overlay": overlay_info,
    }


def inspect_onnx_model(path: Path) -> Dict[str, Any]:
    model = onnx.load(str(path), load_external_data=True)
    onnx.checker.check_model(model)
    inputs, outputs = get_onnx_io(model)
    opsets = [{"domain": op.domain or "ai.onnx", "version": int(op.version)} for op in model.opset_import]
    unique_ops = sorted({node.op_type for node in model.graph.node})

    return {
        "file": {
            "path": str(path),
            "size_bytes": path.stat().st_size,
            "sha256": sha256_file(path),
        },
        "onnx": {
            "ir_version": int(model.ir_version),
            "producer_name": model.producer_name,
            "producer_version": model.producer_version,
            "domain": model.domain,
            "model_version": int(model.model_version),
            "doc_string": model.doc_string[:300] if model.doc_string else "",
            "graph_name": model.graph.name,
            "opsets": opsets,
            "metadata_props": {p.key: p.value for p in model.metadata_props},
            "graph": {
                "node_count": len(model.graph.node),
                "initializer_count": len(model.graph.initializer),
                "sparse_initializer_count": len(model.graph.sparse_initializer),
                "value_info_count": len(model.graph.value_info),
                "unique_ops": unique_ops,
                "inputs": inputs,
                "outputs": outputs,
            },
        },
    }


def load_app_thresholds(repo_root: Path) -> Dict[str, float]:
    appsettings = repo_root / "src" / "DentalID.Desktop" / "appsettings.json"
    defaults = {"teeth_threshold": 0.35, "pathology_threshold": 0.35, "iou_threshold": 0.4}
    if not appsettings.exists():
        return defaults
    try:
        data = json.loads(appsettings.read_text(encoding="utf-8"))
        ai = data.get("AI", {})
        thr = ai.get("Thresholds", {})
        ai_settings = data.get("AiSettings", {})
        return {
            "teeth_threshold": float(thr.get("TeethThreshold", defaults["teeth_threshold"])),
            "pathology_threshold": float(thr.get("DefaultThreshold", defaults["pathology_threshold"])),
            "iou_threshold": float(ai_settings.get("IouThreshold", defaults["iou_threshold"])),
        }
    except Exception:
        return defaults


def load_fdi_class_map(repo_root: Path) -> Optional[List[int]]:
    appsettings = repo_root / "src" / "DentalID.Desktop" / "appsettings.json"
    if not appsettings.exists():
        return None
    try:
        data = json.loads(appsettings.read_text(encoding="utf-8"))
        class_map = data.get("AI", {}).get("FdiMapping", {}).get("ClassMap")
        if isinstance(class_map, list):
            return [int(x) for x in class_map]
        return None
    except Exception:
        return None


def load_pathology_classes(repo_root: Path) -> Optional[List[str]]:
    appsettings = repo_root / "src" / "DentalID.Desktop" / "appsettings.json"
    if not appsettings.exists():
        return None
    try:
        data = json.loads(appsettings.read_text(encoding="utf-8"))
        return data.get("AI", {}).get("Model", {}).get("PathologyClasses")
    except Exception:
        return None


def build_report(
    repo_root: Path,
    models_dir: Path,
    image_path: Path,
    model_names: Sequence[str],
    debug_dir: Optional[Path] = None,
) -> Dict[str, Any]:
    thresholds = load_app_thresholds(repo_root)
    pathology_classes = load_pathology_classes(repo_root)
    fdi_class_map = load_fdi_class_map(repo_root)

    report: Dict[str, Any] = {
        "run_info": {
            "timestamp_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "python": platform.python_version(),
            "platform": platform.platform(),
            "onnx_version": onnx.__version__,
            "onnxruntime_version": ort.__version__,
            "cwd": str(repo_root),
            "models_dir": str(models_dir),
            "image_path": str(image_path),
            "thresholds_used": thresholds,
            "debug_dir": str(debug_dir) if debug_dir is not None else None,
        },
        "image_info": {},
        "models": [],
        "summary": {},
    }

    rgb = image_to_rgb(image_path)
    report["image_info"] = {
        "width": int(rgb.shape[1]),
        "height": int(rgb.shape[0]),
        "channels": int(rgb.shape[2]),
        "dtype": str(rgb.dtype),
        "stats": array_stats(rgb),
    }

    per_model = []
    for name in model_names:
        model_path = models_dir / name
        entry: Dict[str, Any] = {"name": name, "exists": model_path.exists()}
        if not model_path.exists():
            entry["error"] = "Model file not found."
            per_model.append(entry)
            continue

        try:
            entry["inspection"] = inspect_onnx_model(model_path)
            entry["inference"] = infer_model(
                model_path,
                image_path,
                thresholds,
                iou_threshold=float(thresholds.get("iou_threshold", 0.4)),
                pathology_classes=pathology_classes,
                fdi_class_map=fdi_class_map,
                debug_dir=debug_dir,
            )
            entry["status"] = "ok"
        except Exception as ex:  # noqa: BLE001
            entry["status"] = "error"
            entry["error"] = f"{type(ex).__name__}: {ex}"
        per_model.append(entry)

    report["models"] = per_model

    ok_count = sum(1 for m in per_model if m.get("status") == "ok")
    err_count = sum(1 for m in per_model if m.get("status") == "error")
    missing_count = sum(1 for m in per_model if not m.get("exists", False))

    inference_times = [
        m["inference"]["inference_time_ms"]
        for m in per_model
        if m.get("status") == "ok" and "inference" in m
    ]
    report["summary"] = {
        "models_total": len(per_model),
        "models_ok": ok_count,
        "models_error": err_count,
        "models_missing": missing_count,
        "inference_time_ms": {
            "min": min(inference_times) if inference_times else None,
            "max": max(inference_times) if inference_times else None,
            "mean": statistics.mean(inference_times) if inference_times else None,
        },
    }

    return report


def print_console_summary(report: Dict[str, Any]) -> None:
    print("=== Model Diagnostics Summary ===")
    print(f"Image: {report['run_info']['image_path']}")
    print(
        f"Models OK: {report['summary']['models_ok']} / {report['summary']['models_total']} | "
        f"Errors: {report['summary']['models_error']} | Missing: {report['summary']['models_missing']}"
    )

    for m in report["models"]:
        name = m.get("name", "<unknown>")
        status = m.get("status", "missing")
        if status != "ok":
            print(f"- {name}: {status} ({m.get('error', 'n/a')})")
            continue

        t_ms = m["inference"]["inference_time_ms"]
        print(f"- {name}: ok | {t_ms:.2f} ms")
        spec = m["inference"].get("specialized_summary", {})
        if "kept_predictions_count" in spec:
            print(
                f"  kept_predictions={spec['kept_predictions_count']} "
                f"after_nms={spec.get('kept_after_nms_count')} "
                f"max_conf={spec.get('max_confidence', 0.0):.4f}"
            )
            if spec.get("unique_fdi_count_after_nms") is not None:
                print(
                    f"  unique_fdi_after_nms={spec.get('unique_fdi_count_after_nms')} "
                    f"values={spec.get('unique_fdi_after_nms')}"
                )
            gap = spec.get("fdi_gap_analysis")
            if isinstance(gap, dict):
                print(
                    "  gap_diagnosis "
                    f"low_conf={gap.get('missing_low_confidence_model_side')} "
                    f"suppressed_or_miscls={gap.get('missing_suppressed_or_misclassified')}"
                )
        if "predicted_gender" in spec:
            print(
                f"  predicted_gender={spec['predicted_gender']} "
                f"predicted_age={spec.get('predicted_age')}"
            )
        if "mean_pooled_vector" in spec:
            vec = spec["mean_pooled_vector"]
            print(
                f"  pooled_vec_len={vec['length']} l2_norm={vec['l2_norm']:.4f}"
            )
        overlay = m["inference"].get("overlay")
        if overlay and overlay.get("path"):
            print(f"  overlay={overlay['path']} drawn_boxes={overlay.get('drawn_boxes')}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Inspect DentalID ONNX models and run test inference on one image."
    )
    parser.add_argument(
        "--models-dir",
        default="models",
        help="Directory that contains ONNX models.",
    )
    parser.add_argument(
        "--image",
        required=True,
        help="Path to test image.",
    )
    parser.add_argument(
        "--models",
        nargs="*",
        default=DEFAULT_MODEL_NAMES,
        help="Model file names to inspect (relative to models-dir).",
    )
    parser.add_argument(
        "--output",
        default="model_diagnostics_report.json",
        help="Output JSON report path.",
    )
    parser.add_argument(
        "--debug-dir",
        default="scripts/model_diagnostics_debug",
        help="Directory for annotated detection overlays. Use empty string to disable.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    repo_root = Path.cwd()
    models_dir = Path(args.models_dir).expanduser().resolve()
    image_path = Path(args.image).expanduser().resolve()
    output_path = Path(args.output).expanduser().resolve()
    debug_dir = Path(args.debug_dir).expanduser().resolve() if str(args.debug_dir).strip() else None

    if not image_path.exists():
        raise FileNotFoundError(f"Image not found: {image_path}")

    report = build_report(repo_root, models_dir, image_path, args.models, debug_dir=debug_dir)
    output_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print_console_summary(report)
    print(f"\nFull report written to: {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
