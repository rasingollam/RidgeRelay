"""Bake subtle tangent normals and smoothness masks from the painted surfaces."""
from pathlib import Path
from math import sqrt
import bpy

ROOT = Path(__file__).resolve().parent.parent
DEST = ROOT / "Assets/RidgeRelay/Art/Resources/RidgeRelay/Surfaces"
SIZE = 512


def save_image(name, pixels):
    image = bpy.data.images.new(name, width=SIZE, height=SIZE, alpha=True)
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(pixels)
    image.filepath_raw = str(DEST / (name + ".png"))
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def bake_surface(name, strength, smoothness):
    image = bpy.data.images.load(str(DEST / (name + ".png")), check_existing=False)
    image.colorspace_settings.name = "Non-Color"
    image.scale(SIZE, SIZE)
    color = list(image.pixels)
    height = [color[i * 4] * 0.2126 + color[i * 4 + 1] * 0.7152 +
              color[i * 4 + 2] * 0.0722 for i in range(SIZE * SIZE)]
    normal = [0.0] * (SIZE * SIZE * 4)
    mask = [0.0] * (SIZE * SIZE * 4)
    for y in range(SIZE):
        for x in range(SIZE):
            pixel = y * SIZE + x
            dx = (height[y * SIZE + (x + 1) % SIZE] -
                  height[y * SIZE + (x - 1) % SIZE]) * strength
            dy = (height[((y + 1) % SIZE) * SIZE + x] -
                  height[((y - 1) % SIZE) * SIZE + x]) * strength
            length = sqrt(dx * dx + dy * dy + 1)
            normal[pixel * 4:pixel * 4 + 4] = [0.5 - dx / length * 0.5,
                                               0.5 - dy / length * 0.5, 0.5 + 0.5 / length, 1]
            mask[pixel * 4 + 3] = smoothness * (0.7 + height[pixel] * 0.3)
    save_image(name + "Normal", normal)
    save_image(name + "Mask", mask)
    bpy.data.images.remove(image)


if __name__ == "__main__":
    for surface, strength, smoothness in [
        ("Sandstone", 3.5, 0.16), ("Slate", 4.0, 0.20),
        ("Timber", 3.0, 0.25), ("CanyonRock", 2.5, 0.12), ("CanyonRockV2", 2.5, 0.12)
    ]:
        bake_surface(surface, strength, smoothness)
