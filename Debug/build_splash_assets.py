"""Rebuild splash assets from the opaque Home-page logo (no inner transparency holes)."""
from __future__ import annotations

import math
import os
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
# logo.png is the opaque circular master used on Home (TitleLogoBorder / LargeLogoBorder).
LOGO_SRC = ROOT / "Resources" / "Images" / "logo.png"
SPLASH_OUT = ROOT / "Resources" / "Splash" / "splash.png"
ANDROID_SPLASH_OUT = ROOT / "Platforms" / "Android" / "Resources" / "drawable" / "splash_screen.png"
# Match ThemeMainBackground factory default on HomePage.
SPLASH_BG = (140, 250, 100, 255)  # #8CFA64


def build_splash_logo(outer_radius: int | None = None) -> None:
    logo = Image.open(LOGO_SRC).convert("RGBA")
    w, h = logo.size
    cx, cy = w // 2, h // 2
    if outer_radius is None:
        outer_radius = min(cx, cy) - 1

    splash = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    logo_px = logo.load()
    splash_px = splash.load()

    for y in range(h):
        for x in range(w):
            if math.hypot(x - cx, y - cy) <= outer_radius:
                splash_px[x, y] = logo_px[x, y]

    splash.save(SPLASH_OUT, "PNG")
    print("Updated", SPLASH_OUT)

    os.makedirs(ANDROID_SPLASH_OUT.parent, exist_ok=True)
    android = Image.new("RGBA", (w, h), SPLASH_BG)
    android.alpha_composite(splash)
    android.save(ANDROID_SPLASH_OUT, "PNG")
    print("Updated", ANDROID_SPLASH_OUT)


if __name__ == "__main__":
    build_splash_logo()
