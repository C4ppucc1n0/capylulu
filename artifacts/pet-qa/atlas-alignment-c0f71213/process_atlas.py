#!/usr/bin/env python3
"""Deterministically key, extract, align, assemble, and review generated atlas rows.

Expected generated input: one 1536x1024 contact sheet per atlas row, named
``row-01.png`` through ``row-09.png``. Each sheet is a 4x2 grid of 384x512
cells in row-major order. Cells after the row's effective frame count are ignored.
"""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps


RUN = Path(__file__).resolve().parent
REPO_ROOT = RUN.parents[2]
DEFAULT_LAYOUT = RUN / "input" / "layout.json"
DEFAULT_OUTPUT = REPO_ROOT / "output" / "imagegen" / "capylulu-atlas-aligned-v2.png"
CONTACT_SIZE = (1536, 1024)
CONTACT_COLUMNS = 4
CONTACT_ROWS = 2
CONTACT_CELL = (384, 512)
KEY_COLOR = (255, 0, 255)


def save_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def alpha_bbox(image: Image.Image) -> tuple[int, int, int, int] | None:
    return image.getchannel("A").getbbox()


def alpha_stats(image: Image.Image) -> dict[str, int]:
    histogram = image.getchannel("A").histogram()
    total = image.width * image.height
    transparent = histogram[0]
    opaque = histogram[255]
    return {
        "total_pixels": total,
        "transparent_pixels": transparent,
        "partial_alpha_pixels": total - transparent - opaque,
        "opaque_pixels": opaque,
        "visible_pixels": total - transparent,
    }


def significant_edge_alpha_pixels(image: Image.Image, band: int = 2, threshold: int = 16) -> int:
    """Count visible pixels touching a cell edge, which indicates source clipping."""
    alpha = image.getchannel("A")
    width, height = alpha.size
    count = 0
    for y in range(height):
        for x in range(width):
            if x < band or x >= width - band or y < band or y >= height - band:
                if alpha.getpixel((x, y)) >= threshold:
                    count += 1
    return count


def clear_transparent_rgb(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    pixels = list(rgba.getdata())
    rgba.putdata([(0, 0, 0, 0) if pixel[3] == 0 else pixel for pixel in pixels])
    return rgba


def transparent_rgb_residue(image: Image.Image) -> int:
    return sum(1 for r, g, b, a in image.convert("RGBA").getdata() if a == 0 and (r or g or b))


def alpha_centroid(image: Image.Image) -> tuple[float, float] | None:
    alpha = image.getchannel("A")
    width, height = image.size
    total = 0
    weighted_x = 0
    weighted_y = 0
    for y in range(height):
        for x in range(width):
            value = alpha.getpixel((x, y))
            if value:
                total += value
                weighted_x += x * value
                weighted_y += y * value
    if not total:
        return None
    return weighted_x / total, weighted_y / total


def parse_layout(path: Path) -> dict:
    layout = json.loads(path.read_text(encoding="utf-8"))
    if layout.get("width") != 1050 or layout.get("height") != 1280:
        raise SystemExit(f"unexpected target canvas in {path}: {layout.get('width')}x{layout.get('height')}")
    if len(layout.get("rows", [])) != 9:
        raise SystemExit(f"expected 9 rows in {path}")
    if sum(row["effective_frames"] for row in layout["rows"]) != 57:
        raise SystemExit(f"expected 57 effective frames in {path}")
    original = RUN / "input" / "original.png"
    if original.is_file() and layout.get("source_sha256") and sha256(original) != layout["source_sha256"]:
        raise SystemExit(f"source atlas hash does not match {path}: {original}")
    return layout


def chroma_helper() -> Path:
    candidate = Path.home() / ".codex" / "skills" / ".system" / "imagegen" / "scripts" / "remove_chroma_key.py"
    if not candidate.is_file():
        raise SystemExit(f"imagegen chroma helper not found: {candidate}")
    return candidate


def remove_chroma(source: Path, destination: Path, args: argparse.Namespace) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    command = [
        sys.executable,
        str(chroma_helper()),
        "--input",
        str(source),
        "--out",
        str(destination),
        "--key-color",
        args.key_color,
        "--soft-matte",
        "--transparent-threshold",
        str(args.transparent_threshold),
        "--opaque-threshold",
        str(args.opaque_threshold),
        "--despill",
        "--force",
    ]
    if args.edge_contract:
        command.extend(["--edge-contract", str(args.edge_contract)])
    if args.edge_feather:
        command.extend(["--edge-feather", str(args.edge_feather)])
    subprocess.run(command, check=True)


def load_contact(source: Path, keyed: Path, args: argparse.Namespace) -> Image.Image:
    with Image.open(source) as opened:
        if opened.size != CONTACT_SIZE:
            raise SystemExit(f"expected {CONTACT_SIZE[0]}x{CONTACT_SIZE[1]} contact sheet, got {opened.size}: {source}")
        if args.skip_chroma:
            return clear_transparent_rgb(opened.convert("RGBA"))
    remove_chroma(source, keyed, args)
    with Image.open(keyed) as opened:
        return clear_transparent_rgb(opened.convert("RGBA"))


def extract_contact_cells(contact: Image.Image, count: int) -> list[Image.Image]:
    frames = []
    for index in range(count):
        column = index % CONTACT_COLUMNS
        row = index // CONTACT_COLUMNS
        left = column * CONTACT_CELL[0]
        top = row * CONTACT_CELL[1]
        frames.append(contact.crop((left, top, left + CONTACT_CELL[0], top + CONTACT_CELL[1])))
    return frames


def crop_visible(image: Image.Image) -> Image.Image:
    bbox = alpha_bbox(image)
    if bbox is None:
        return Image.new("RGBA", (1, 1), (0, 0, 0, 0))
    return image.crop(bbox)


def frame_geometry(frame: Image.Image) -> dict[str, object]:
    bbox = alpha_bbox(frame)
    centroid = alpha_centroid(frame)
    return {
        "bbox": list(bbox) if bbox else None,
        "centroid": [centroid[0], centroid[1]] if centroid else None,
        "foot_y": bbox[3] if bbox else None,
        "visible_width": bbox[2] - bbox[0] if bbox else 0,
        "visible_height": bbox[3] - bbox[1] if bbox else 0,
    }


def median(values: list[float]) -> float:
    ordered = sorted(values)
    middle = len(ordered) // 2
    return ordered[middle] if len(ordered) % 2 else (ordered[middle - 1] + ordered[middle]) / 2


def calculate_scales(records: list[dict], mode: str, adjustment: float) -> dict[int, float]:
    ratios_by_row: dict[int, list[float]] = {}
    all_ratios = []
    for record in records:
        original_h = record["original_geometry"]["visible_height"]
        generated_h = record["generated_geometry"]["visible_height"]
        if original_h and generated_h:
            ratio = original_h / generated_h
            ratios_by_row.setdefault(record["row"], []).append(ratio)
            all_ratios.append(ratio)
    if not all_ratios:
        raise SystemExit("no visible generated frames found")
    if mode == "none":
        return {row: adjustment for row in ratios_by_row}
    if mode == "row":
        return {row: median(values) * adjustment for row, values in ratios_by_row.items()}
    shared = median(all_ratios) * adjustment
    return {row: shared for row in ratios_by_row}


def resize_sprite(sprite: Image.Image, scale: float) -> Image.Image:
    width = max(1, round(sprite.width * scale))
    height = max(1, round(sprite.height * scale))
    if (width, height) == sprite.size:
        return sprite.copy()
    return sprite.resize((width, height), Image.Resampling.LANCZOS)


def clipped_alpha_pixels(sprite: Image.Image, x: int, y: int, width: int, height: int) -> int:
    alpha = sprite.getchannel("A")
    total = sum(alpha.histogram()[1:])
    left = max(0, -x)
    top = max(0, -y)
    right = min(sprite.width, width - x)
    bottom = min(sprite.height, height - y)
    if right <= left or bottom <= top:
        return total
    inside = sum(alpha.crop((left, top, right, bottom)).histogram()[1:])
    return total - inside


def place_frame(record: dict, scale: float) -> tuple[Image.Image, dict]:
    slot_width, slot_height = record["original"].size
    sprite = crop_visible(record["generated"])
    transformed = clear_transparent_rgb(resize_sprite(sprite, scale))
    generated_centroid = alpha_centroid(transformed)
    original_centroid = record["original_geometry"]["centroid"]
    original_foot = record["original_geometry"]["foot_y"]
    if generated_centroid is None or original_centroid is None or original_foot is None:
        raise SystemExit(f"empty occupied frame row {record['row']} frame {record['frame']}")
    x = round(original_centroid[0] - generated_centroid[0])
    y = round(original_foot - transformed.height)
    clipped = clipped_alpha_pixels(transformed, x, y, slot_width, slot_height)
    output = Image.new("RGBA", (slot_width, slot_height), (0, 0, 0, 0))
    output.alpha_composite(transformed, (x, y))
    output = clear_transparent_rgb(output)
    placed_geometry = frame_geometry(output)
    details = {
        "scale": scale,
        "paste_xy": [x, y],
        "transformed_size": list(transformed.size),
        "placed_geometry": placed_geometry,
        "clipped_alpha_pixels": clipped,
        "centroid_delta": [
            placed_geometry["centroid"][0] - original_centroid[0],
            placed_geometry["centroid"][1] - original_centroid[1],
        ] if placed_geometry["centroid"] else None,
        "foot_delta": placed_geometry["foot_y"] - original_foot if placed_geometry["foot_y"] else None,
    }
    return output, details


def checkerboard(image: Image.Image, size: tuple[int, int], square: int = 10) -> Image.Image:
    background = Image.new("RGB", size, "white")
    draw = ImageDraw.Draw(background)
    for y in range(0, size[1], square):
        for x in range(0, size[0], square):
            if (x // square + y // square) % 2:
                draw.rectangle((x, y, x + square - 1, y + square - 1), fill="#d9d9d9")
    fitted = ImageOps.contain(image.convert("RGBA"), size, Image.Resampling.LANCZOS)
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    layer.alpha_composite(fitted, ((size[0] - fitted.width) // 2, (size[1] - fitted.height) // 2))
    background.paste(layer, (0, 0), layer)
    return background


def make_row_gif(row: int, originals: list[Image.Image], results: list[Image.Image], path: Path, duration: int) -> None:
    frames = []
    for index, (before, after) in enumerate(zip(originals, results), start=1):
        preview = Image.new("RGB", (444, 250), "#20242a")
        preview.paste(checkerboard(before, (200, 200)), (12, 36))
        preview.paste(checkerboard(after, (200, 200)), (232, 36))
        draw = ImageDraw.Draw(preview)
        draw.text((12, 10), f"row {row:02d} frame {index:02d} | original", fill="white")
        draw.text((232, 10), "aligned result", fill="white")
        frames.append(preview)
    path.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=duration, loop=0, disposal=2)


def write_review_html(path: Path, output: Path, gif_paths: list[Path], duration: int, report_path: Path) -> None:
    rows = "\n".join(
        f'<section><h2>Row {index:02d}</h2><img src="{html.escape(gif.relative_to(path.parent).as_posix())}"></section>'
        for index, gif in enumerate(gif_paths, start=1)
    )
    body = f"""<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><title>Atlas alignment review</title>
<style>body{{font:16px system-ui;background:#15181d;color:#eee;margin:24px}}section{{display:inline-block;vertical-align:top;margin:0 20px 24px 0}}img{{border:1px solid #555;image-rendering:auto}}code{{color:#9fe3ff}}</style></head>
<body><h1>动作图校准逐行对照</h1>
<p>GIF 每帧 {duration} ms，仅为人工复查时长；源图未提供真实播放时长。</p>
<p>结果：<code>{html.escape(output.as_posix())}</code> · 报告：<code>{html.escape(report_path.as_posix())}</code></p>
{rows}</body></html>"""
    path.write_text(body, encoding="utf-8")


def process(args: argparse.Namespace) -> dict:
    layout_path = Path(args.layout).resolve()
    generated_dir = Path(args.generated_dir).resolve()
    output = Path(args.output).resolve()
    qa_dir = Path(args.qa_dir).resolve() if args.qa_dir else output.parent / "qa"
    keyed_dir = qa_dir / "keyed-contacts"
    layout = parse_layout(layout_path)
    records = []

    for row_info in layout["rows"]:
        row = row_info["row"]
        contact_path = generated_dir / args.row_pattern.format(row=row)
        if not contact_path.is_file():
            raise SystemExit(f"missing generated row contact sheet: {contact_path}")
        contact = load_contact(contact_path, keyed_dir / f"row-{row:02d}.png", args)
        generated_frames = extract_contact_cells(contact, row_info["effective_frames"])
        for frame_index, generated in enumerate(generated_frames, start=1):
            slot_info = row_info["slots"][frame_index - 1]
            original_path = RUN / slot_info["file"]
            with Image.open(original_path) as opened:
                original = clear_transparent_rgb(opened.convert("RGBA"))
            records.append({
                "row": row,
                "frame": frame_index,
                "slot": slot_info,
                "original": original,
                "generated": generated,
                "original_geometry": frame_geometry(original),
                "generated_geometry": frame_geometry(generated),
                "generated_edge_alpha_pixels": significant_edge_alpha_pixels(generated),
                "generated_source": str(contact_path),
            })

    scales = calculate_scales(records, args.scale_mode, args.scale_adjust)
    atlas = Image.new("RGBA", (layout["width"], layout["height"]), (0, 0, 0, 0))
    frame_reports = []
    result_by_row: dict[int, list[Image.Image]] = {}
    original_by_row: dict[int, list[Image.Image]] = {}
    errors = []

    for record in records:
        placed, placement = place_frame(record, scales[record["row"]])
        left, top, right, bottom = record["slot"]["rectangle"]
        atlas.alpha_composite(placed, (left, top))
        result_by_row.setdefault(record["row"], []).append(placed)
        original_by_row.setdefault(record["row"], []).append(record["original"])
        if placement["clipped_alpha_pixels"]:
            errors.append(
                f"row {record['row']:02d} frame {record['frame']:02d} clipped "
                f"{placement['clipped_alpha_pixels']} alpha pixels"
            )
        if record["generated_edge_alpha_pixels"]:
            errors.append(
                f"row {record['row']:02d} frame {record['frame']:02d} generated source touches "
                f"its contact-cell edge at {record['generated_edge_alpha_pixels']} significant-alpha pixels"
            )
        if alpha_bbox(placed) is None:
            errors.append(f"row {record['row']:02d} frame {record['frame']:02d} is empty")
        frame_reports.append({
            "row": record["row"],
            "frame": record["frame"],
            "slot_rectangle": record["slot"]["rectangle"],
            "original_geometry": record["original_geometry"],
            "generated_geometry": record["generated_geometry"],
            "generated_edge_alpha_pixels": record["generated_edge_alpha_pixels"],
            **placement,
        })

    atlas = clear_transparent_rgb(atlas)
    empty_slots = []
    for row_info in layout["rows"]:
        for slot in row_info["slots"]:
            if slot["occupied"]:
                continue
            cell = atlas.crop(tuple(slot["rectangle"]))
            visible = alpha_stats(cell)["visible_pixels"]
            empty_slots.append({"row": row_info["row"], "column": slot["column"], "visible_pixels": visible})
            if visible:
                errors.append(f"empty slot row {row_info['row']:02d} column {slot['column']:02d} has {visible} visible pixels")

    output.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(output)
    report_path = qa_dir / "validation-report.json"
    gif_paths = []
    for row in range(1, 10):
        gif_path = qa_dir / "comparisons" / f"row-{row:02d}-before-after.gif"
        make_row_gif(row, original_by_row[row], result_by_row[row], gif_path, args.preview_duration_ms)
        gif_paths.append(gif_path)
    html_path = qa_dir / "review.html"
    write_review_html(html_path, output, gif_paths, args.preview_duration_ms, report_path)

    result = {
        "ok": not errors,
        "output": str(output),
        "output_sha256": sha256(output),
        "dimensions": list(atlas.size),
        "mode": atlas.mode,
        "effective_frame_count": len(records),
        "empty_slot_count": len(empty_slots),
        "scale_mode": args.scale_mode,
        "scale_adjust": args.scale_adjust,
        "row_scales": {str(row): scale for row, scale in scales.items()},
        "anchor_policy": "original alpha centroid x + original alpha bbox foot y",
        "alpha": alpha_stats(atlas),
        "transparent_rgb_residue_pixels": transparent_rgb_residue(atlas),
        "empty_slots": empty_slots,
        "frames": frame_reports,
        "errors": errors,
        "review_html": str(html_path),
        "preview_duration_ms": args.preview_duration_ms,
        "preview_duration_note": "人工复查值；源图未提供帧时长。",
    }
    if result["transparent_rgb_residue_pixels"]:
        result["errors"].append("fully transparent pixels contain non-zero RGB residue")
        result["ok"] = False
    save_json(report_path, result)
    print(json.dumps({key: result[key] for key in ("ok", "output", "dimensions", "effective_frame_count", "empty_slot_count", "errors", "review_html")}, ensure_ascii=False, indent=2))
    return result


def make_test_contacts(destination: Path, layout: dict, *, chroma: bool) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    for row_info in layout["rows"]:
        background = (*KEY_COLOR, 255) if chroma else (0, 0, 0, 0)
        sheet = Image.new("RGBA", CONTACT_SIZE, background)
        for index in range(row_info["effective_frames"]):
            slot = row_info["slots"][index]
            with Image.open(RUN / slot["file"]) as opened:
                original = clear_transparent_rgb(opened.convert("RGBA"))
            sprite = crop_visible(original)
            x = (index % CONTACT_COLUMNS) * CONTACT_CELL[0] + 90
            y = (index // CONTACT_COLUMNS) * CONTACT_CELL[1] + 130
            sheet.alpha_composite(sprite, (x, y))
        if chroma:
            sheet = sheet.convert("RGB")
        sheet.save(destination / f"row-{row_info['row']:02d}.png")


def normalized_original(layout: dict) -> Image.Image:
    with Image.open(RUN / "input" / "original.png") as opened:
        return clear_transparent_rgb(opened.convert("RGBA"))


def self_test(args: argparse.Namespace) -> None:
    root = Path(args.test_dir).resolve()
    layout = parse_layout(Path(args.layout).resolve())
    rgba_dir = root / "contacts-rgba"
    chroma_dir = root / "contacts-chroma"
    make_test_contacts(rgba_dir, layout, chroma=False)
    make_test_contacts(chroma_dir, layout, chroma=True)

    common = {
        "layout": args.layout,
        "row_pattern": "row-{row:02d}.png",
        "scale_mode": "global",
        "scale_adjust": 1.0,
        "preview_duration_ms": args.preview_duration_ms,
        "key_color": "#FF00FF",
        "transparent_threshold": 12.0,
        "opaque_threshold": 96.0,
        "edge_contract": 0,
        "edge_feather": 0.0,
    }
    rgba_args = argparse.Namespace(**common, generated_dir=str(rgba_dir), output=str(root / "rgba-reconstructed.png"), qa_dir=str(root / "qa-rgba"), skip_chroma=True)
    rgba_result = process(rgba_args)
    with Image.open(rgba_result["output"]) as opened:
        reconstructed = clear_transparent_rgb(opened.convert("RGBA"))
    exact = reconstructed.tobytes() == normalized_original(layout).tobytes()

    chroma_args = argparse.Namespace(**common, generated_dir=str(chroma_dir), output=str(root / "chroma-reconstructed.png"), qa_dir=str(root / "qa-chroma"), skip_chroma=False)
    chroma_result = process(chroma_args)
    summary = {
        "ok": rgba_result["ok"] and chroma_result["ok"] and exact,
        "rgba_lossless_reconstruction": exact,
        "rgba_layout_validation": rgba_result["ok"],
        "chroma_layout_alpha_validation": chroma_result["ok"],
        "effective_frames": rgba_result["effective_frame_count"],
        "empty_slots": rgba_result["empty_slot_count"],
        "test_root": str(root),
    }
    save_json(root / "self-test-summary.json", summary)
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    if not summary["ok"]:
        raise SystemExit(1)


def add_processing_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--layout", default=str(DEFAULT_LAYOUT))
    parser.add_argument("--generated-dir", required=True)
    parser.add_argument("--row-pattern", default="row-{row:02d}.png")
    parser.add_argument("--output", default=str(DEFAULT_OUTPUT))
    parser.add_argument("--qa-dir")
    parser.add_argument("--skip-chroma", action="store_true", help="Input contacts already have useful alpha.")
    parser.add_argument("--key-color", default="#FF00FF")
    parser.add_argument("--transparent-threshold", type=float, default=12.0)
    parser.add_argument("--opaque-threshold", type=float, default=96.0)
    parser.add_argument("--edge-contract", type=int, default=0)
    parser.add_argument("--edge-feather", type=float, default=0.0)
    parser.add_argument("--scale-mode", choices=("global", "row", "none"), default="global")
    parser.add_argument("--scale-adjust", type=float, default=1.0)
    parser.add_argument("--preview-duration-ms", type=int, default=180)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    process_parser = subparsers.add_parser("process", help="Process real generated 4x2 row contact sheets.")
    add_processing_arguments(process_parser)
    test_parser = subparsers.add_parser("self-test", help="Build free local fixtures and verify assembly/keying.")
    test_parser.add_argument("--layout", default=str(DEFAULT_LAYOUT))
    test_parser.add_argument("--test-dir", default=str(RUN / "test-output"))
    test_parser.add_argument("--preview-duration-ms", type=int, default=180)
    return parser


def main() -> None:
    args = build_parser().parse_args()
    if args.command == "process":
        result = process(args)
        raise SystemExit(0 if result["ok"] else 1)
    self_test(args)


if __name__ == "__main__":
    main()
