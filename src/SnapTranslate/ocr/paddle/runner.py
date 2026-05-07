import argparse
import contextlib
import io
import json
import os
import sys
from typing import Any, Iterable


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="SnapTranslate PaddleOCR runner")
    parser.add_argument("--image", required=True, help="Input image path")
    parser.add_argument("--lang", default="ch", help="PaddleOCR language, use ch for Chinese+English")
    return parser.parse_args()


def box_to_xywh(box: Any) -> list[int] | None:
    if box is None:
        return None

    if isinstance(box, list) and len(box) == 4 and all(isinstance(v, (int, float)) for v in box):
        x1, y1, x2, y2 = box
        return [int(x1), int(y1), max(1, int(x2 - x1)), max(1, int(y2 - y1))]

    if isinstance(box, list):
        points: list[tuple[float, float]] = []
        for item in box:
            if isinstance(item, list) and len(item) >= 2:
                points.append((float(item[0]), float(item[1])))
        if points:
            xs = [point[0] for point in points]
            ys = [point[1] for point in points]
            x1, x2 = min(xs), max(xs)
            y1, y2 = min(ys), max(ys)
            return [int(x1), int(y1), max(1, int(x2 - x1)), max(1, int(y2 - y1))]

    return None


def find_payload(value: Any) -> dict[str, Any] | None:
    if isinstance(value, dict):
        if "rec_texts" in value and ("rec_boxes" in value or "rec_polys" in value):
            return value
        for child in value.values():
            found = find_payload(child)
            if found is not None:
                return found
    elif isinstance(value, list):
        for child in value:
            found = find_payload(child)
            if found is not None:
                return found
    return None


def normalize_result(raw: Any) -> dict[str, Any]:
    payload = find_payload(raw)
    if payload is None:
        return {"lines": [], "status": "PaddleOCR result does not contain rec_texts/rec_boxes."}

    texts = payload.get("rec_texts") or []
    scores = payload.get("rec_scores") or []
    boxes = payload.get("rec_boxes") or payload.get("rec_polys") or []

    lines: list[dict[str, Any]] = []
    for index, text in enumerate(texts):
        if not str(text).strip():
            continue
        if index >= len(boxes):
            continue

        box = box_to_xywh(boxes[index])
        if box is None:
            continue

        score = float(scores[index]) if index < len(scores) else 0.0
        lines.append({"text": str(text).strip(), "box": box, "score": score})

    return {"lines": lines}


def result_to_jsonable(result: Any) -> Any:
    raw = getattr(result, "json", None)
    if callable(raw):
        raw = raw()
    if raw is not None:
        return raw

    # Fallback for older objects: save JSON to a temp folder and reload it.
    import tempfile

    with tempfile.TemporaryDirectory(prefix="snaptranslate-paddle-") as temp_dir:
        result.save_to_json(temp_dir)
        for name in os.listdir(temp_dir):
            if name.lower().endswith(".json"):
                with open(os.path.join(temp_dir, name), "r", encoding="utf-8") as file:
                    return json.load(file)

    return None


def main() -> int:
    args = parse_args()

    # PaddleOCR logs a lot during import/model loading. Keep stdout clean for JSON.
    with contextlib.redirect_stdout(sys.stderr):
        from paddleocr import PaddleOCR

        ocr = PaddleOCR(
            lang=args.lang,
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
        )
        results: Iterable[Any] = ocr.predict(args.image)

    all_lines: list[dict[str, Any]] = []
    statuses: list[str] = []
    for result in results:
        normalized = normalize_result(result_to_jsonable(result))
        all_lines.extend(normalized.get("lines") or [])
        if normalized.get("status"):
            statuses.append(str(normalized["status"]))

    print(json.dumps({"lines": all_lines, "status": "\n".join(statuses) or None}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(json.dumps({"lines": [], "status": str(exc)}, ensure_ascii=False))
        raise
