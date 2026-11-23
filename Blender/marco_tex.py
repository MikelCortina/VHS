# Paints T_Marco_Ventana.png (64x64): weathered grey-teal painted metal, like the skyscraper frames.
# Usage: blender -b --python marco_tex.py -- <out.png>
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
out = sys.argv[sys.argv.index('--') + 1:][0]
N = 64; rng = np.random.default_rng(64)
img = np.array((86, 104, 104.0)) + rng.normal(0, 5, (N, N, 1))                   # grey-teal paint
img += rng.normal(0, 1, (N // 8, N // 8)).repeat(8, 0).repeat(8, 1)[..., None] * 5
for _ in range(14):                                                               # chipped paint -> darker metal
    x, y = rng.integers(0, N, 2); r = rng.integers(1, 3)
    img[max(0, y - r):y + r, max(0, x - r):x + r] = np.array((58, 60, 60.0)) + rng.normal(0, 4)
for _ in range(10):                                                               # rust runs downwards
    x = int(rng.integers(0, N)); y0 = int(rng.integers(0, N - 10)); ln = int(rng.integers(4, 14))
    img[y0:y0 + ln, x] = img[y0:y0 + ln, x] * 0.55 + np.array((110, 70, 44.0)) * 0.45
img[::16] *= 0.8                                                                  # panel joints
save_png(np.clip(img, 0, 255), out)
print('SAVED', out)
