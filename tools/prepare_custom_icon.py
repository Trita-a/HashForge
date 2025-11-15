"""Normalizza un'icona personalizzata per Hash Forge.

A partire dal file `icon.png` (fornito dall'utente) applica i seguenti passaggi:
- salvataggio di un backup in `assets/icon_original.png`;
- rimozione della trasparenza in eccesso (crop dinamico sull'alpha);
- centratura su canvas quadrato, con margini minimi uniformi;
- ridimensionamento alla risoluzione desiderata e generazione dei formati
  di output (`icon.png`, `assets/icon_preview.png`, `icon.ico`).

In questo modo l'icona appare più grande e coerente con quelle delle altre
applicazioni, senza perdere l'artwork originale.
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE_PNG = ROOT / "icon.png"
BACKUP_PNG = ROOT / "assets" / "icon_original.png"
OUTPUT_PNG = ROOT / "icon.png"
OUTPUT_PREVIEW = ROOT / "assets" / "icon_preview.png"
OUTPUT_ICO = ROOT / "icon.ico"
TARGET_SIZE = 1024
MARGIN_RATIO = 0.04  # 4% di margine uniforme
ALPHA_THRESHOLD = 8  # ignora pixel quasi trasparenti durante il crop


def _ensure_parent(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)


def _crop_alpha(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    bbox = alpha.point(lambda a: 255 if a > ALPHA_THRESHOLD else 0).getbbox()
    return image.crop(bbox) if bbox else image


def _pad_to_square(image: Image.Image) -> Image.Image:
    width, height = image.size
    size = max(width, height)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    offset = ((size - width) // 2, (size - height) // 2)
    canvas.paste(image, offset, image)
    return canvas


def _scale_with_margin(image: Image.Image) -> Image.Image:
    # Prima ridimensiona alla dimensione target, poi applica il margine.
    base = image.resize((TARGET_SIZE, TARGET_SIZE), Image.LANCZOS)
    if MARGIN_RATIO <= 0:
        return base
    inner_size = int(TARGET_SIZE * (1 - 2 * MARGIN_RATIO))
    inner = base.resize((inner_size, inner_size), Image.LANCZOS)
    canvas = Image.new("RGBA", (TARGET_SIZE, TARGET_SIZE), (0, 0, 0, 0))
    offset = (TARGET_SIZE - inner_size) // 2
    canvas.paste(inner, (offset, offset), inner)
    return canvas


def prepare_icon() -> None:
    if not SOURCE_PNG.exists():
        raise FileNotFoundError(
            "icon.png non trovato. Inserisci il nuovo artwork nella root del progetto."
        )

    original = Image.open(SOURCE_PNG).convert("RGBA")

    # Salva un backup prima di manipolare l'immagine.
    _ensure_parent(BACKUP_PNG)
    original.save(BACKUP_PNG)

    processed = _crop_alpha(original)
    processed = _pad_to_square(processed)
    processed = _scale_with_margin(processed)

    # Aggiorna gli output richiesti dal progetto.
    _ensure_parent(OUTPUT_PNG)
    processed.save(OUTPUT_PNG)

    _ensure_parent(OUTPUT_PREVIEW)
    processed.save(OUTPUT_PREVIEW)

    _ensure_parent(OUTPUT_ICO)
    processed.save(
        OUTPUT_ICO,
        sizes=[(256, 256), (128, 128), (64, 64), (32, 32), (16, 16)],
    )

    print("Icona normalizzata salvata in:", OUTPUT_PNG)
    print("Anteprima aggiornata in:", OUTPUT_PREVIEW)
    print("File ICO rigenerato in:", OUTPUT_ICO)
    print("Backup dell'originale:", BACKUP_PNG)


if __name__ == "__main__":
    prepare_icon()
