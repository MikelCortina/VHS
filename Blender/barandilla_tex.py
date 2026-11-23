# Paints T_Barandilla.png (64x64): ribbed sheet-metal balcony panel, faded blue-grey paint, rust runs.
# Tileable; ribs run vertically. Usage: blender -b --python barandilla_tex.py -- <out.png>
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
out = sys.argv[sys.argv.index('--') + 1:][0]
N = 64; rng = np.random.default_rng(1983)
x = np.arange(N)
rib = np.where(x % 8 < 2, 0.72, np.where(x % 8 < 4, 1.12, 1.0))                  # ribs every 8 px: shadow + highlight
paint = np.array((92, 108, 116.0))                                                # faded blue-grey
img = paint * rib[None, :, None] + rng.normal(0, 4, (N, N, 1))
img += rng.normal(0, 1, (N // 8, N // 8)).repeat(8, 0).repeat(8, 1)[..., None] * 6
for _ in range(9):                                                                # rust runs from the top rail
    cx = int(rng.integers(0, N)); ln = int(rng.integers(10, 40)); w = int(rng.integers(1, 3))
    for k in range(ln):
        a = 0.65 * (1 - k / ln)
        xs = (cx + np.arange(w)) % N
        img[k, xs] = img[k, xs] * (1 - a) + np.array((112, 62, 36.0)) * a
for _ in range(16):                                                               # chipped paint
    px, py = rng.integers(0, N, 2)
    img[py:py + 2, px:px + 2] = np.array((70, 58, 50.0))
img[0:3] = img[0:3] * 0.55 + np.array((80, 60, 45.0)) * 0.45                       # top rail
img[-2:] *= 0.6                                                                   # bottom edge
save_png(np.clip(img, 0, 255), out)
print('SAVED', out)
