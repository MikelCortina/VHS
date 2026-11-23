# Paints T_Cuerda.png (32x32): rough twisted hemp rope, diagonal strands, tileable.
# Usage: blender -b --python cuerda_tex.py -- <out.png>
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
out = sys.argv[sys.argv.index('--') + 1:][0]
N = 32; rng = np.random.default_rng(7)
y, x = np.mgrid[0:N, 0:N]
strand = ((x + y) % 8) / 8.0                              # diagonal twist, 4 strands per tile
shade = 0.65 + 0.45 * np.sin(strand * np.pi)              # round strands: lit middle, dark grooves
img = np.array((118, 96, 64.0)) * shade[..., None] + rng.normal(0, 6, (N, N, 1))
img[(x + y) % 8 == 0] *= 0.55                              # groove between strands
for _ in range(20):                                        # fibres
    a, b = rng.integers(0, N, 2); img[b, a] = (150, 128, 90)
img *= 0.85                                                # old, dirty
save_png(np.clip(img, 0, 255), out); print('SAVED', out)
