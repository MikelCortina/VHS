# Makes a panel/grid texture (Concrete 2.png: 4x4 riveted concrete panels) truly tileable:
#  1. warps the image so the joints fall exactly every CELL px (they were at 0/65/128/193 and 0/65/127/192)
#  2. removes the dark band along the bottom edge and other thin edge bands (per-row / per-column brightness fix)
#  3. redraws every joint with the same darkness, so the tile seam (a joint) looks like any other joint
#  4. flattens large-scale lighting with a wrap-around high-pass (no repeating dark patch)
# Usage: blender -b --python tileable_paneles.py -- <in.raw> <out.png>   (raw = 256x256 RGB24 from ffmpeg)
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
src, out = sys.argv[sys.argv.index('--') + 1:][:2]
N, CELL = 256, 64
img = np.fromfile(src, np.uint8).reshape(N, N, 3).astype(float)
lum = lambda t: t.mean(2)

# ---- 1. find the joints (darkest line near each expected position) and warp them onto the grid
def joints(profile):
    js = []
    for k in range(N // CELL):
        c = k * CELL; cand = [(c + d) % N for d in range(-4, 5)]
        js.append(min(cand, key=lambda i: profile[i]))
    return js
col_j = joints(lum(img).mean(0)); row_j = joints(lum(img).mean(1))
print('joints found cols', col_j, 'rows', row_j)

def remap(js):
    """destination pixel -> source pixel, piecewise linear between joints (wrapping)."""
    src_k = [j if j < N - 8 else j - N for j in js] + [js[0] + N if js[0] < N - 8 else js[0]]
    m = np.empty(N, int)
    for k in range(len(js)):
        d0, d1 = k * CELL, (k + 1) * CELL; s0, s1 = src_k[k], src_k[k + 1]
        for d in range(d0, d1):
            m[d] = int(round(s0 + (s1 - s0) * (d - d0) / (d1 - d0))) % N
    return m
mx, my = remap(col_j), remap(row_j)
img = img[my][:, mx]

# ---- 2. thin brightness bands (e.g. the dark strip along the bottom): fix rows/cols that are not joints
def fix_bands(t, axis):
    prof = lum(t).mean(axis=1 - axis)                  # axis 0 -> per-row profile, 1 -> per-column
    gain = np.ones(N)
    for i in range(N):
        k = i % CELL
        if k == 0:                                      # joints are redrawn later
            continue
        # target: the interior of this same panel (away from its joints), so edge shading bands disappear
        p0 = i - k
        target = np.median(prof[p0 + 10:p0 + CELL - 10])
        if k <= 8 or k >= CELL - 8:
            gain[i] = np.clip(target / max(prof[i], 1), 0.7, 2.0)
    return t * (gain[:, None, None] if axis == 0 else gain[None, :, None])
img = fix_bands(fix_bands(img, 0), 1)

# ---- 3. uniform joints: one dark line every CELL px, plus a faint lit edge below/right of it
for k in range(N // CELL):
    j = k * CELL
    nb = (img[:, (j - 1) % N] + img[:, (j + 1) % N]) / 2
    img[:, j] = nb * 0.70
    pass
    nb = (img[(j - 1) % N] + img[(j + 1) % N]) / 2
    img[j] = nb * 0.70
    pass

# ---- 4. flatten big lighting with a periodic Gaussian high-pass
SIGMA, KEEP = 12.0, 0.05
fy = np.fft.fftfreq(N)[:, None]; fx = np.fft.fftfreq(N)[None, :]
g = np.exp(-2 * (np.pi * SIGMA) ** 2 * (fx ** 2 + fy ** 2))
for c in range(3):
    low = np.real(np.fft.ifft2(np.fft.fft2(img[..., c]) * g))
    img[..., c] = img[..., c] - (1 - KEEP) * (low - low.mean())
img = fix_bands(fix_bands(img, 0), 1)          # again after the high-pass: last edge bands
img = np.clip(img, 0, 255)

l = lum(img)
print('cells', [[round(l[y + 2:y + CELL - 1, x + 2:x + CELL - 1].mean()) for x in range(0, N, CELL)] for y in range(0, N, CELL)])
print('joint darkness cols', [round(l[:, j].mean()) for j in range(0, N, CELL)], 'rows', [round(l[j].mean()) for j in range(0, N, CELL)])
print('edge rows top/bottom', round(l[1:4].mean()), round(l[-4:].mean()), 'edge cols L/R', round(l[:, 1:4].mean()), round(l[:, -4:].mean()))
save_png(img, out)
print('SAVED', out)
