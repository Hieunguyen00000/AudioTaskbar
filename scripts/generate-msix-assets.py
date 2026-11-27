#!/usr/bin/env python3
"""Generate Microsoft Store / MSIX logo assets from src/app.ico.

Run from the repo root (or anywhere): python3 scripts/generate-msix-assets.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ICO = ROOT / "src" / "app.ico"
ASSETS = ROOT / "packaging" / "msix" / "Assets"
LISTING = ROOT / "packaging" / "msix" / "StoreListing"


def load_frames(path: Path) -> list[Image.Image]:
    im = Image.open(path)
    frames: list[Image.Image] = []
    try:
        i = 0
        while True:
            im.seek(i)
            frames.append(im.convert("RGBA").copy())
            i += 1
    except EOFError:
        pass
    if not frames:
        frames = [im.convert("RGBA")]
    frames.sort(key=lambda f: f.size[0] * f.size[1], reverse=True)
    return frames


def best_source(frames: list[Image.Image], w: int, h: int) -> Image.Image:
    bigger = [f for f in frames if f.size[0] >= w and f.size[1] >= h]
    if bigger:
        bigger.sort(key=lambda f: f.size[0] * f.size[1])
        return bigger[0]
    return frames[0]


def resize_icon(frames: list[Image.Image], size: int, pad_ratio: float = 0.0) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    inner = max(1, int(round(size * (1 - pad_ratio))))
    src = best_source(frames, inner, inner).resize((inner, inner), Image.Resampling.LANCZOS)
    off = (size - inner) // 2
    canvas.paste(src, (off, off), src)
    return canvas


def save(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, "PNG", optimize=True)
    print(f"  {path.relative_to(ROOT)}  {img.size[0]}x{img.size[1]}")


def wide_tile(frames: list[Image.Image], w: int, h: int) -> Image.Image:
    bg = (7, 21, 38, 255)
    img = Image.new("RGBA", (w, h), bg)
    overlay = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(overlay)
    for y in range(h):
        a = int(40 * (y / max(1, h - 1)))
        d.line([(0, y), (w, y)], fill=(10, 40, 60, a))
    img = Image.alpha_composite(img, overlay)
    icon_h = int(h * 0.72)
    icon = resize_icon(frames, icon_h)
    margin = int(h * 0.14)
    img.paste(icon, (margin, (h - icon_h) // 2), icon)
    font_paths = [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "C:\\Windows\\Fonts\\segoeuib.ttf",
        "C:\\Windows\\Fonts\\arialbd.ttf",
    ]
    font_size = max(18, int(h * 0.22))
    font = None
    for p in font_paths:
        if Path(p).exists():
            font = ImageFont.truetype(p, font_size)
            break
    if font is None:
        font = ImageFont.load_default()
    draw = ImageDraw.Draw(img)
    text = "Audio Taskbar"
    bbox = draw.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    tx = margin + icon_h + int(h * 0.12)
    typ = font_paths[0] if Path(font_paths[0]).exists() else next((p for p in font_paths if Path(p).exists()), None)
    while tx + tw > w - margin and font_size > 10:
        font_size -= 1
        if typ:
            font = ImageFont.truetype(typ, font_size)
        bbox = draw.textbbox((0, 0), text, font=font)
        tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    ty = (h - th) // 2 - bbox[1]
    draw.text((tx, ty), text, font=font, fill=(232, 244, 255, 255))
    return img


def store_tile(frames: list[Image.Image], size: int, pad: float = 0.12) -> Image.Image:
    img = Image.new("RGBA", (size, size), (7, 21, 38, 255))
    inner = int(round(size * (1 - pad)))
    icon = best_source(frames, inner, inner).resize((inner, inner), Image.Resampling.LANCZOS)
    off = (size - inner) // 2
    img.paste(icon, (off, off), icon)
    return img.convert("RGB")


def main() -> None:
    frames = load_frames(ICO)
    print(f"Source {ICO} frames: {[f.size for f in frames]}")
    assets = {
        "StoreLogo.png": (50, 0.0),
        "StoreLogo.scale-200.png": (100, 0.04),
        "StoreLogo.scale-400.png": (200, 0.06),
        "Square44x44Logo.png": (44, 0.0),
        "Square44x44Logo.scale-200.png": (88, 0.04),
        "Square44x44Logo.scale-400.png": (176, 0.06),
        "Square44x44Logo.targetsize-44.png": (44, 0.0),
        "Square44x44Logo.targetsize-48.png": (48, 0.04),
        "Square44x44Logo.targetsize-48_altform-unplated.png": (48, 0.04),
        "Square44x44Logo.targetsize-256.png": (256, 0.08),
        "Square150x150Logo.png": (150, 0.08),
        "Square150x150Logo.scale-200.png": (300, 0.08),
        "Square150x150Logo.scale-400.png": (600, 0.08),
    }
    for name, (size, pad) in assets.items():
        save(resize_icon(frames, size, pad), ASSETS / name)
    save(wide_tile(frames, 310, 150), ASSETS / "Wide310x150Logo.png")
    save(wide_tile(frames, 620, 300), ASSETS / "Wide310x150Logo.scale-200.png")
    save(store_tile(frames, 300), LISTING / "StoreTile300x300.png")
    print("Done.")


if __name__ == "__main__":
    main()
