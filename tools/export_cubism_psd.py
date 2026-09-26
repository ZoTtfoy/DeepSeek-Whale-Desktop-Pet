"""Assemble the existing character parts into a Cubism Editor import PSD.

This prepares artwork layers; it does not create a bound Cubism .moc3 model.
Requires Pillow, and does not modify the source artwork files.
"""

from __future__ import annotations

import json
import struct
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
MODEL_DIR = ROOT / "app" / "models" / "whale-rig"
OUTPUT = ROOT / "dist" / "Whale-Cubism-Layers.psd"
SIZE = 1200
SCALE = SIZE // 300


def be16(value: int) -> bytes:
    return struct.pack(">H", value)


def be32(value: int) -> bytes:
    return struct.pack(">I", value)


def pascal_name(value: str) -> bytes:
    encoded = value.encode("ascii")[:255]
    data = bytes([len(encoded)]) + encoded
    return data + bytes((-len(data)) % 4)


def source_layers() -> list[tuple[str, Image.Image, bool]]:
    config = json.loads((MODEL_DIR / "model.json").read_text(encoding="utf-8"))
    if config["format"] != "whale-layered-1":
        raise ValueError("Unexpected model format")
    atlases: dict[str, Image.Image] = {}
    result: list[tuple[str, Image.Image, bool]] = []
    for entry in config["layers"]:
        filename = entry["image"]
        if Path(filename).name != filename:
            raise ValueError("Unsafe image name")
        if filename not in atlases:
            atlases[filename] = Image.open(MODEL_DIR / filename).convert("RGBA")
        x, y, width, height = entry["crop"]
        fx, fy, fw, fh = entry["frame"]
        cutout = atlases[filename].crop((x, y, x + width, y + height))
        resized = cutout.resize((round(fw * SCALE), round(fh * SCALE)), Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (SIZE, SIZE))
        canvas.paste(resized, (round(fx * SCALE), round(fy * SCALE)))
        if entry.get("fadeLeft", 0):
            # The runtime's subtle seam mask is only a preview aid. Keep full
            # paint in the PSD so an artist can refine overlap in Cubism.
            pass
        result.append((entry["id"], canvas, entry["channel"] != "eye-closed"))
    return result


def write_psd(path: Path, layers: list[tuple[str, Image.Image, bool]]) -> Image.Image:
    # PSD layers are stored front-to-back, opposite the model manifest.
    top_first = list(reversed(layers))
    records = bytearray()
    channels = bytearray()
    merged = Image.new("RGBA", (SIZE, SIZE))
    for _, canvas, visible in layers:
        if visible:
            merged = Image.alpha_composite(merged, canvas)

    for name, canvas, visible in top_first:
        box = canvas.getbbox() or (0, 0, 1, 1)
        left, top, right, bottom = box
        clipped = canvas.crop(box)
        rgba = clipped.split()
        records += struct.pack(">4iH", top, left, bottom, right, 4)
        for channel_id, plane in zip((0, 1, 2, -1), rgba):
            raw = plane.tobytes()
            padding = bytes(len(raw) % 2)
            records += struct.pack(">hI", channel_id, 2 + len(raw) + len(padding))
            channels += be16(0) + raw + padding  # Raw, one plane per channel.
        records += b"8BIMnorm" + bytes((255, 0, 2 if not visible else 0, 0))
        extra = be32(0) + be32(0) + pascal_name(name)
        records += be32(len(extra)) + extra

    layer_info = struct.pack(">h", -len(top_first)) + records + channels
    if len(layer_info) % 2:
        layer_info += b"\0"
    mask_section = be32(len(layer_info)) + layer_info + be32(0)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as stream:
        stream.write(b"8BPS" + be16(1) + bytes(6))
        stream.write(be16(4) + be32(SIZE) + be32(SIZE) + be16(8) + be16(3))
        stream.write(be32(0))  # Color mode data.
        stream.write(be32(0))  # Image resources.
        stream.write(be32(len(mask_section)) + mask_section)
        stream.write(be16(0))  # Raw merged image, planar RGBA.
        for plane in merged.split():
            stream.write(plane.tobytes())
    return merged


def main() -> None:
    layers = source_layers()
    merged = write_psd(OUTPUT, layers)
    with Image.open(OUTPUT) as reopened:
        reopened.load()
        if reopened.size != merged.size or reopened.convert("RGBA").tobytes() != merged.tobytes():
            raise RuntimeError("PSD merged preview failed round-trip verification")
        if len(reopened.layers) != len(layers):
            raise RuntimeError("PSD layer count failed round-trip verification")
        with OUTPUT.open("rb") as stream:
            for (expected_name, expected, _), (actual_name, mode, box, tiles) in zip(
                reversed(layers), reopened.layers
            ):
                if expected_name != actual_name or mode != "RGBA" or len(tiles) != 4:
                    raise RuntimeError(f"PSD layer metadata mismatch: {expected_name}")
                clipped = expected.crop(box)
                for tile in tiles:
                    # Pillow reports offsets relative to its copied layer-info
                    # buffer, not the start of the PSD file.
                    stream.seek(reopened._layers_position + tile.offset)
                    count = clipped.width * clipped.height
                    if stream.read(count) != clipped.getchannel(tile.args).tobytes():
                        raise RuntimeError(f"PSD layer pixels mismatch: {expected_name}")
    print(f"Exported {len(layers)} layers: {OUTPUT}")


if __name__ == "__main__":
    main()
