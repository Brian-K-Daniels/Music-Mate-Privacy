from PIL import Image
import math
import os

GREEN = (56, 142, 60, 255)  # #388E3C
SRC = r"C:\CSharp\Source Code\MusicMate\M004\Resources\AppIcon\appicon.png"
LOGO_OUT = r"C:\CSharp\Source Code\MusicMate\M004\Resources\Splash\splash.png"
ANDROID_SPLASH_OUT = r"C:\CSharp\Source Code\MusicMate\M004\Platforms\Android\Resources\drawable\splash_screen.png"


def luminance(px):
    r, g, b, _ = px
    return 0.299 * r + 0.587 * g + 0.114 * b


def is_ring_artifact(px):
    r, g, b, a = px
    if a < 10:
        return False
    lum = luminance(px)
    if lum < 55:
        return True
    if lum > 215 and max(r, g, b) - min(r, g, b) < 35:
        return True
    return False


def build_splash_logo(outer_radius=312, ring_inner=228):
    im = Image.open(SRC).convert("RGBA")
    w, h = im.size
    cx, cy = w // 2, h // 2
    logo = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    src_px = im.load()
    logo_px = logo.load()

    for y in range(h):
        for x in range(w):
            dist = math.hypot(x - cx, y - cy)
            if dist > outer_radius:
                continue
            px = src_px[x, y]
            if dist > ring_inner and is_ring_artifact(px):
                continue
            logo_px[x, y] = px

    logo.save(LOGO_OUT, "PNG")
    print("Updated", LOGO_OUT)

    os.makedirs(os.path.dirname(ANDROID_SPLASH_OUT), exist_ok=True)
    android = Image.new("RGBA", (w, h), GREEN)
    android.alpha_composite(logo)
    android.save(ANDROID_SPLASH_OUT, "PNG")
    print("Updated", ANDROID_SPLASH_OUT)


if __name__ == "__main__":
    build_splash_logo()
