# Paints a tileable 256x256 PSX ground texture: trampled grass mixed with bare dirt.
# Usage: python/blender ... suelo.py -- <out.png>
import sys, os, numpy as np
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
out = argv[0]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png

N = 256
rng = np.random.default_rng(23)


def tile_noise(cells):
    """Periodic value noise (wraps at the edges) in [-1, 1]."""
    g = rng.uniform(-1, 1, (cells, cells))
    t = np.arange(N) * cells / N
    i0 = np.floor(t).astype(int); f = t - i0; f = f * f * (3 - 2 * f)
    i1 = (i0 + 1) % cells
    a = g[i0][:, i0]; b = g[i0][:, i1]; c = g[i1][:, i0]; d = g[i1][:, i1]
    fx = f[None, :]; fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


n4, n8, n16, n32, n64 = (tile_noise(c) for c in (4, 8, 16, 32, 64))
pix = rng.normal(0, 1, (N, N))

# ---- where is dirt (paths and bald patches) vs grass
dirt_mask = n4 * 0.55 + n8 * 0.45 + n16 * 0.25 + pix * 0.06
dirt = dirt_mask > 0.05                                     # ~45% bare dirt

# ---- dirt: brown, mottled, with pebbles and darker damp spots
D_DARK = np.array((58, 46, 34.0)); D_MID = np.array((86, 68, 49.0)); D_LIGHT = np.array((112, 94, 72.0))
t = np.clip(0.5 + n16 * 0.45 + n64 * 0.25 + pix * 0.12, 0, 1)[..., None]
dirt_col = D_DARK * (1 - t) + D_MID * t
light = (n32 + pix * 0.3) > 0.55
dirt_col[light] = dirt_col[light] * 0.5 + D_LIGHT * 0.5
pebble = rng.random((N, N)) < 0.012
dirt_col[pebble] = np.array((128, 118, 100.0)) + rng.normal(0, 8, (pebble.sum(), 1))
dark_spot = rng.random((N, N)) < 0.01
dirt_col[dark_spot] = D_DARK * 0.7

# ---- trampled grass: olive / yellowish, flattened blade strokes in random directions
G_DARK = np.array((48, 52, 33.0)); G_MID = np.array((74, 78, 47.0)); G_DRY = np.array((106, 98, 62.0))
g = np.clip(0.5 + n8 * 0.35 + n32 * 0.3 + pix * 0.1, 0, 1)[..., None]
grass_col = G_DARK * (1 - g) + G_MID * g
dry = (n16 * 0.7 + n64 * 0.5) > 0.12
grass_col[dry] = grass_col[dry] * 0.45 + G_DRY * 0.55
strokes = np.zeros((N, N)); stroke_col = np.zeros((N, N, 3))
for _ in range(2600):                                      # flattened blades
    x, y = rng.integers(0, N, 2)
    ang = rng.uniform(0, np.pi)                            # trampled: any direction, not upright
    ln = rng.integers(2, 6)
    c = G_MID * rng.uniform(0.7, 1.35) if rng.random() < 0.7 else G_DRY * rng.uniform(0.8, 1.15)
    for k in range(ln):
        xx = int(round(x + np.cos(ang) * k)) % N; yy = int(round(y + np.sin(ang) * k)) % N
        strokes[yy, xx] = 1; stroke_col[yy, xx] = c
grass_col = np.where(strokes[..., None] > 0, stroke_col, grass_col)

# ---- blend with a ragged edge; a few grass tufts surviving inside the dirt
img = np.where(dirt[..., None], dirt_col, grass_col)
edge = np.abs(dirt_mask - 0.05) < 0.06
mix = edge & (rng.random((N, N)) < 0.5)
img[mix] = img[mix] * 0.5 + (dirt_col[mix] * 0.5 + grass_col[mix] * 0.5) * 0.5
tuft = dirt & (rng.random((N, N)) < 0.02)
img[tuft] = grass_col[tuft]

# ---- slight overall darkening variation (big puddle-like stains), still tileable
img *= (0.92 + n4[..., None] * 0.08)
save_png(img, out)
print('SAVED', out)
