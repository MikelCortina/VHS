# Paints the PSX window atlas T_Ventana.png (4x2 cells of 64x64 = 256x128) + an all-black emission map.
# Style: old metal-framed windows with many small panes and dirty, mottled dark-teal glass.
# Row 0 (top): dirty glass / curtains / half-lowered blind / dark empty room.
# Row 1 (bottom): blinds lowered: full / 3/4 / full brown / one leaf full + one half.
# The layout must stay 4x2: export.py assigns each window one of these cells.
import sys, os, numpy as np
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
base_out, emi_out = argv[0], argv[1]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png

C = 64
W, H = 4 * C, 2 * C
rng = np.random.default_rng(12)

FRAME = np.array((138, 150, 146.0))      # painted metal, grey-teal
FRAME_HI = np.array((176, 184, 178.0))
FRAME_LO = np.array((72, 78, 76.0))
GLASS = np.array((36, 52, 62.0))         # dark teal glass
SMUDGE = np.array((112, 128, 134.0))     # pale haze / dirt on the glass
GRIME = np.array((46, 42, 34.0))         # brownish dirt at the bottom of panes

COLS_X = [3 + round(k * (C - 6) / 5) for k in range(6)]   # 5 panes across
TRANSOM_Y = 27                                             # 2 rows of panes


def lowfreq(n=6):
    g = rng.uniform(-1, 1, (n + 1, n + 1))
    ys = np.linspace(0, n, C); xs = np.linspace(0, n, C)
    y0 = np.floor(ys).astype(int).clip(0, n - 1); x0 = np.floor(xs).astype(int).clip(0, n - 1)
    fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
    a = g[y0][:, x0]; b = g[y0][:, x0 + 1]; c = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def frame_mask():
    m = np.zeros((C, C), int)   # 0 glass, 1 frame, 2 highlight, 3 shadow
    m[:3, :] = 1; m[-3:, :] = 1; m[:, :3] = 1; m[:, -3:] = 1
    m[0, :] = 3; m[:, 0] = 3; m[-1, :] = 3; m[:, -1] = 3
    m[1, 1:-1] = 2; m[1:-1, 1] = 2
    for x in COLS_X[1:-1]:
        m[3:-3, x - 1:x + 1] = 1; m[3:-3, x + 1] = 3
    m[TRANSOM_Y - 1:TRANSOM_Y + 1, 3:-3] = 1; m[TRANSOM_Y + 1, 3:-3] = 3
    m[3, 3:-3] = np.where(m[3, 3:-3] == 0, 3, m[3, 3:-3])   # shadow under the top frame
    return m


FM = frame_mask()


def dirty_glass(dark=1.0):
    lf = lowfreq(6); lf2 = lowfreq(14)
    px = rng.normal(0, 1, (C, C))
    g = np.zeros((C, C, 3))
    for y in range(C):
        pane_y = ((y - 3) % (TRANSOM_Y - 2)) / (TRANSOM_Y - 2)   # 0 top .. 1 bottom of a pane row
        for x in range(C):
            c = GLASS * dark * (1.08 - 0.25 * y / C)
            s = max(0.0, lf[y, x] * 0.8 + lf2[y, x] * 0.4 - 0.25)    # pale smudges
            c = c * (1 - s * 0.85) + SMUDGE * dark * s * 0.85
            if pane_y > 0.7:                                          # grime at the bottom of panes
                c = c * 0.75 + GRIME * 0.25
            g[y, x] = c + px[y, x] * 5
    return g


def interior(kind):
    """What is seen behind the glass (curtains or blinds) and where."""
    out = np.zeros((C, C, 3)); alpha = np.zeros((C, C))
    for y in range(C):
        for x in range(C):
            col = None
            if kind == 'curtains' and (x < 18 or x > 45):
                col = np.array((112, 92, 70.0)) * (0.85 if (x // 3) % 2 else 1.0)
            elif kind.startswith('blind'):
                if kind in ('blind_full', 'blind_brown'): down = True
                elif kind == 'blind_half': down = y < 30
                elif kind == 'blind_34': down = y < 48
                else: down = x < 32 or y < 30          # blind_mixed
                if down:
                    slat = np.array((128, 104, 78.0)) if kind == 'blind_brown' else np.array((150, 148, 134.0))
                    col = slat * (1.0 if y % 4 else 0.6)
            if col is not None:
                out[y, x] = col; alpha[y, x] = 1
    return out, alpha


VARIANTS = [('glass', 1.0), ('curtains', 1.0), ('blind_half', 1.0), ('glass', 0.6),
            ('blind_full', 1.0), ('blind_34', 1.0), ('blind_brown', 1.0), ('blind_mixed', 1.0)]

base = np.zeros((H, W, 3))
for i, (kind, dark) in enumerate(VARIANTS):
    cx, row = i % 4, i // 4
    g = dirty_glass(dark)
    if kind != 'glass':
        inn, a = interior(kind)
        g = g * (1 - a[..., None] * 0.6) + inn * a[..., None] * 0.6
    fn = rng.normal(0, 1, (C, C))
    cell = g.copy()
    for k, col in ((1, FRAME), (2, FRAME_HI), (3, FRAME_LO)):
        sel = FM == k
        cell[sel] = col + fn[sel][:, None] * 6           # slightly worn paint
    rust = (lowfreq(10) > 0.75) & (FM > 0)                # a few rusty spots on the frame
    cell[rust] = cell[rust] * 0.6 + np.array((110, 70, 40.0)) * 0.4
    base[row * C:(row + 1) * C, cx * C:(cx + 1) * C] = cell

save_png(base, base_out); print('SAVED', base_out)
save_png(np.zeros((H, W, 3)), emi_out); print('SAVED', emi_out)
