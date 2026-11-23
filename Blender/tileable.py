# Makes a texture tileable by flattening its large-scale lighting (dark corners, gradients) with a periodic
# (wrap-around) Gaussian high-pass, so opposite edges match in brightness and the repeat shows no big stains.
# Fine detail (joints, rivets, stains) is kept. Input: raw RGB24 from ffmpeg.
# Usage: blender -b --python tileable.py -- <in.raw> <width> <height> <out.png> [sigma_px] [keep]
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
a = sys.argv[sys.argv.index('--') + 1:]
src, W, H, out = a[0], int(a[1]), int(a[2]), a[3]
SIGMA = float(a[4]) if len(a) > 4 else W / 10       # size of the "lighting" to remove
KEEP = float(a[5]) if len(a) > 5 else 0.15           # how much of the big variation to keep (0 = fully flat)
img = np.fromfile(src, np.uint8).reshape(H, W, 3).astype(float)

fy = np.fft.fftfreq(H)[:, None]; fx = np.fft.fftfreq(W)[None, :]
g = np.exp(-2 * (np.pi * SIGMA) ** 2 * (fx ** 2 + fy ** 2))          # periodic Gaussian blur in frequency space
out_img = np.empty_like(img)
for c in range(3):
    low = np.real(np.fft.ifft2(np.fft.fft2(img[..., c]) * g))
    out_img[..., c] = img[..., c] - (1 - KEEP) * (low - low.mean())
out_img = np.clip(out_img, 0, 255)

def report(t, name):
    l = t.mean(2)
    q = [[round(l[y:y + H // 4, x:x + W // 4].mean()) for x in range(0, W, W // 4)] for y in range(0, H, H // 4)]
    print(name, 'quadrants', q, '| edge jump L/R', round(abs(l[:, 1:3].mean() - l[:, -3:].mean()), 1),
          'T/B', round(abs(l[1:3].mean() - l[-5:-3].mean()), 1))
report(img, 'BEFORE'); report(out_img, 'AFTER ')
save_png(out_img, out)
print('SAVED', out)
