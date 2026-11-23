# Paints T_Viga.png (64x64): dark weathered structural steel, plate joints every 32 px, rivet pairs, rust.
# Tileable. Usage: blender -b --python viga_tex.py -- <out.png>
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
out = sys.argv[sys.argv.index('--') + 1:][0]
N = 64; rng = np.random.default_rng(1961)
img = np.array((58, 60, 66.0)) + rng.normal(0, 4, (N, N, 1))                       # charcoal steel, cold tint
img += rng.normal(0, 1, (N // 8, N // 8)).repeat(8, 0).repeat(8, 1)[..., None] * 4
for y0 in (0, 32):                                                                  # plate joints + lit lip
    img[y0] *= 0.55; img[(y0 + 1) % N] *= 1.18
for y0 in (4, 27, 36, 59):                                                          # rivet rows near each joint
    for x0 in (8, 24, 40, 56):
        img[y0:y0 + 2, x0:x0 + 2] = (92, 92, 96)
        img[y0 + 2, x0:x0 + 2] = (30, 30, 33)
img[:, 0] *= 0.7; img[:, 63] *= 0.85                                                # flange edges
for _ in range(10):                                                                 # rust runs from the rivets/joints
    x = int(rng.integers(0, N)); y0 = int(rng.choice([2, 34])); ln = int(rng.integers(6, 26))
    for k in range(ln):
        a = 0.6 * (1 - k / ln)
        img[(y0 + k) % N, x] = img[(y0 + k) % N, x] * (1 - a) + np.array((104, 58, 34.0)) * a
for _ in range(25):                                                                 # rust specks
    x, y = rng.integers(0, N, 2)
    img[y, x] = np.array((96, 60, 40.0)) + rng.normal(0, 6)
save_png(np.clip(img, 0, 255), out)
print('SAVED', out)
