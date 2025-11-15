"""Utility per creare l'icona ufficiale di Hash Forge.

Genera il file PNG principale (`icon.png`) usato dall'app, la preview in
`assets/icon_preview.png` e la versione multi-risoluzione `icon.ico`
necessaria per l'eseguibile Windows creato con PyInstaller. La composizione
rappresenta l'app: blocchi di dati, connessioni di hashing e una spunta di
verifica per comunicare integrità e sicurezza.
"""
from __future__ import annotations

from pathlib import Path
from typing import Iterable

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
OUTPUT_APP_ICON = ROOT / "icon.png"
OUTPUT_PREVIEW = ROOT / "assets" / "icon_preview.png"
OUTPUT_ICO = ROOT / "icon.ico"


def _ensure_dirs(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)


def _lerp_color(start: tuple[int, int, int], end: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(int(s + (e - s) * t) for s, e in zip(start, end))


def _draw_gradient(draw: ImageDraw.ImageDraw, size: int) -> None:
    top = (5, 11, 22)
    bottom = (7, 22, 46)
    for y in range(size):
        ratio = y / (size - 1)
        color = _lerp_color(top, bottom, ratio)
        draw.line([(0, y), (size, y)], fill=color)


def _draw_glow(img: Image.Image, radius: float) -> None:
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(glow)
    center = img.size[0] // 2
    bbox = [center - radius, center - radius, center + radius, center + radius]
    gdraw.ellipse(bbox, fill=(0, 194, 255, 90))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(radius / 2)))



def _draw_hex_frame(draw: ImageDraw.ImageDraw, size: int) -> None:
    import math

    center = size / 2
    radius = size * 0.34
    points: Iterable[tuple[float, float]] = [
        (center + radius * math.cos(math.radians(angle)), center + radius * math.sin(math.radians(angle)))
        for angle in range(-90, 270, 60)
    ]
    draw.polygon(points, outline=(0, 194, 255, 180), width=int(size * 0.015))


def _draw_data_prism(draw: ImageDraw.ImageDraw, size: int) -> None:
    center = size / 2
    width = size * 0.55
    height = size * 0.42
    tilt = size * 0.12
    rect = [center - width / 2, center - height / 2, center + width / 2, center + height / 2]
    gradient_colors = [(0, 194, 255, 245), (108, 181, 255, 245), (230, 233, 242, 245)]

    # Split the prism into angled bands for a "spectral" feel.
    band_count = len(gradient_colors)
    band_height = height / band_count
    for idx, color in enumerate(gradient_colors):
        top = rect[1] + idx * band_height - idx * tilt * 0.08
        bottom = top + band_height + tilt * 0.05
        path = [
            (rect[0] + tilt, top),
            (rect[2], top - tilt * 0.2),
            (rect[2] - tilt, bottom),
            (rect[0], bottom + tilt * 0.2),
        ]
        draw.polygon(path, fill=color)

    # Outline to mimic a chip/card.
    outline = [
        (rect[0], rect[1] + tilt * 0.2),
        (rect[2] - tilt * 0.4, rect[1] - tilt * 0.2),
        (rect[2], rect[3] - tilt * 0.3),
        (rect[0] + tilt * 0.4, rect[3] + tilt * 0.2),
    ]
    draw.line(outline + [outline[0]], fill=(255, 255, 255, 90), width=max(2, int(size * 0.012)))


def _draw_hash_links(draw: ImageDraw.ImageDraw, size: int) -> None:
    center = size / 2
    node_radius = size * 0.035
    link_color = (70, 210, 255, 255)
    node_color = (255, 255, 255, 255)

    nodes = [
        (center - size * 0.2, center - size * 0.18),
        (center + size * 0.18, center - size * 0.22),
        (center - size * 0.24, center + size * 0.16),
        (center + size * 0.22, center + size * 0.24),
        (center, center),
    ]

    # Draw links between nodes to represent hashing relationships.
    connections = [(0, 4), (1, 4), (2, 4), (3, 4), (0, 2), (1, 3)]
    for a, b in connections:
        draw.line([nodes[a], nodes[b]], fill=link_color, width=max(2, int(size * 0.012)))

    for x, y in nodes:
        draw.ellipse(
            [x - node_radius, y - node_radius, x + node_radius, y + node_radius],
            fill=node_color,
            outline=(0, 0, 0, 30),
            width=1,
        )


def _draw_verification_check(draw: ImageDraw.ImageDraw, size: int) -> None:
    base = size * 0.22
    start = (size * 0.36, size * 0.56)
    mid = (start[0] + base * 0.25, start[1] + base * 0.3)
    end = (mid[0] + base * 0.8, mid[1] - base * 0.7)
    draw.line([start, mid, end], fill=(110, 255, 184, 255), width=max(3, int(size * 0.03)))


def generate_icon(size: int = 512) -> None:
    base = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(base)

    # Background gradient
    _draw_gradient(draw, size)

    # Inner cards
    padding = int(size * 0.08)
    inner_bbox = [padding, padding, size - padding, size - padding]
    draw.rounded_rectangle(inner_bbox, radius=int(size * 0.12), outline=(255, 255, 255, 40), width=int(size * 0.01))

    # Glow and frame
    _draw_glow(base, radius=size * 0.36)
    _draw_hex_frame(draw, size)

    # Data integrity motif at center
    _draw_data_prism(draw, size)
    _draw_hash_links(draw, size)
    _draw_verification_check(draw, size)

    _ensure_dirs(OUTPUT_APP_ICON)
    base.save(OUTPUT_APP_ICON)

    _ensure_dirs(OUTPUT_PREVIEW)
    base.save(OUTPUT_PREVIEW)

    # Export ico with multiple resolutions
    _ensure_dirs(OUTPUT_ICO)
    sizes = [(256, 256), (128, 128), (64, 64), (32, 32), (16, 16)]
    base.save(OUTPUT_ICO, sizes=sizes)


if __name__ == "__main__":
    generate_icon()
    print(f"PNG principale: {OUTPUT_APP_ICON}")
    print(f"Anteprima PNG: {OUTPUT_PREVIEW}")
    print(f"Icona ICO legacy: {OUTPUT_ICO}")
