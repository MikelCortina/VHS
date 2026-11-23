# Paints two tileable PSX textures (128x128) for the tunnel scene:
#   T_Roca_Montana.png : grey-brown stratified mountain rock (horizontal layers, cracks, lichen)
#   T_Tierra_Camino.png: dark damp earth with pebbles, ruts and dry grass tufts
# All noise is made periodic (FFT-filtered / wrapped) so both tile with no seams.
# Usage: blender -b --python tunel_tex.py -- <out_dir>
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
out = sys.argv[sys.argv.index('--') + 1:][0]
N = 128
rng = np.random.default_rng(1987)
fy = np.fft.fftfreq(N)[:, None]; fx = np.fft.fftfreq(N)[None, :]


def pnoise(sigma, sx=1.0, sy=1.0):
    """periodic smooth noise, unit std; sx/sy stretch the blobs along x/y"""
    g = np.exp(-2 * (np.pi * sigma) ** 2 * ((fx * sx) ** 2 + (fy * sy) ** 2))
    n = np.real(np.fft.ifft2(np.fft.fft2(rng.normal(0, 1, (N, N))) * g))
    return (n - n.mean()) / n.std()


# ---------------- rock
fbm = pnoise(16) * 0.5 + pnoise(6) * 0.3 + pnoise(2) * 0.2
strata = pnoise(5, sx=6.0, sy=1.0)                           # long horizontal bands
img = np.array((92, 88, 82.0)) + (fbm * 14 + strata * 12)[..., None]
img += rng.normal(0, 4, (N, N, 1))
# layer lines: dark thin seams where the strata noise crosses some levels
warp = (pnoise(12) * 4).astype(int)
for y0 in (8, 37, 61, 90, 113):
    for x in range(N):
        y = (y0 + warp[y0, x]) % N
        img[y, x] *= 0.72; img[(y + 1) % N, x] *= 1.10
# cracks: short wandering dark lines going down
for _ in range(6):
    x = int(rng.integers(0, N)); y = int(rng.integers(0, N)); ln = int(rng.integers(10, 30))
    for k in range(ln):
        img[(y + k) % N, x % N] *= 0.55
        x += int(rng.integers(-1, 2))
# lichen / moss patches, cold green
moss = np.clip(pnoise(5) - 1.2, 0, None)
img = img * (1 - np.clip(moss, 0, 0.7)[..., None]) + np.array((70, 82, 56.0)) * np.clip(moss, 0, 0.7)[..., None]
# vertical rain streaks below the seams
for _ in range(14):
    x = int(rng.integers(0, N)); y = int(rng.choice([9, 38, 62, 91, 114])); ln = int(rng.integers(6, 20))
    for k in range(ln):
        img[(y + k) % N, x] *= 0.85 + 0.15 * k / ln
save_png(np.clip(img, 0, 255), os.path.join(out, 'T_Roca_Montana.png'))

# ---------------- earth
fbm = pnoise(14) * 0.55 + pnoise(5) * 0.3 + pnoise(1.5) * 0.15
img = np.array((74, 60, 46.0)) + (fbm * 12)[..., None] + rng.normal(0, 5, (N, N, 1))
wet = np.clip(pnoise(18), 0, None)                              # darker damp patches
img *= (1 - 0.18 * np.clip(wet, 0, 1))[..., None]
# pebbles: small 2x2 / 3x2 light stones with a dark shadow pixel below
for _ in range(70):
    x, y = (int(v) for v in rng.integers(0, N, 2)); w = int(rng.integers(1, 4)); h = int(rng.integers(1, 3))
    c = np.array((118, 110, 98.0)) + rng.normal(0, 10)
    for j in range(h):
        for i in range(w):
            img[(y + j) % N, (x + i) % N] = c
    for i in range(w):
        img[(y + h) % N, (x + i) % N] *= 0.55
# dry grass tufts: a few short vertical olive strokes in clumps
for _ in range(16):
    cx, cy = (int(v) for v in rng.integers(0, N, 2))
    for _ in range(int(rng.integers(4, 9))):
        x = cx + int(rng.integers(-3, 4)); y = cy + int(rng.integers(-2, 3)); ln = int(rng.integers(2, 5))
        col = np.array((96, 92, 52.0)) + rng.normal(0, 8)
        for k in range(ln):
            img[(y - k) % N, x % N] = col * (0.8 + 0.2 * k / ln)
save_png(np.clip(img, 0, 255), os.path.join(out, 'T_Tierra_Camino.png'))
print('SAVED', out)
