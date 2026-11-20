"""Genera las ilustraciones de la guía PSX con un rasterizador por software
que implementa las mismas técnicas que los shaders (snapping, afín, point,
luz por vértice, niebla, dithering)."""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONTB = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONTM = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
BG = (22, 22, 28)
FG = (235, 235, 240)
MUTED = (160, 160, 175)
ACCENT = (255, 170, 60)
BLUE = (90, 170, 255)


def font(s, bold=False, mono=False):
    return ImageFont.truetype(FONTM if mono else (FONTB if bold else FONT), s)


# ------------------------------------------------------------------ texturas
def noise(h, w, sy, sx, rng):
    gh, gw = h // sy, w // sx
    g = rng.random((gh, gw))
    ys, xs = np.arange(h) / sy, np.arange(w) / sx
    y0, x0 = ys.astype(int), xs.astype(int)
    fy, fx = ys - y0, xs - x0
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    y1, x1 = (y0 + 1) % gh, (x0 + 1) % gw
    y0, x0 = y0 % gh, x0 % gw
    a, b = g[y0][:, x0], g[y0][:, x1]
    c, d = g[y1][:, x0], g[y1][:, x1]
    top = a + (b - a) * fx[None]
    bot = c + (d - c) * fx[None]
    return top + (bot - top) * fy[:, None]


def fbm(h, w, rng, scales=((16, 16, .5), (8, 8, .3), (4, 4, .2))):
    return sum(wt * noise(h, w, sy, sx, rng) for sy, sx, wt in scales)


def tex_brick(rng, n=64):
    Y, X = np.mgrid[0:n, 0:n]
    row = Y // 8
    off = (row % 2) * 8
    col = ((X + off) // 16) % 4
    table = np.array([.52, .24, .17]) * (0.75 + 0.5 * rng.random((n // 8 * 4, 1)))
    table += rng.normal(0, .025, table.shape)
    t = table[row * 4 + col]
    mortar = (Y % 8 == 7) | ((X + off) % 16 == 15)
    t[mortar] = [.33, .30, .27]
    t *= (0.7 + 0.6 * fbm(n, n, rng))[..., None]
    return np.clip(t, 0, 1)


def tex_floor(rng, n=64):
    Y, X = np.mgrid[0:n, 0:n]
    chk = ((X // 32) + (Y // 32)) % 2
    t = np.where(chk[..., None] == 0, [.46, .45, .42], [.30, .32, .31]).astype(float)
    t[(X % 32 == 31) | (Y % 32 == 31)] = [.16, .16, .16]
    t *= (0.75 + 0.5 * fbm(n, n, rng))[..., None]
    return np.clip(t, 0, 1)


def tex_ceiling(rng, n=64):
    Y, X = np.mgrid[0:n, 0:n]
    t = np.zeros((n, n, 3)) + [.28, .30, .34]
    lx, ly = X % 32, Y % 32
    t[(lx == 0) | (ly == 0)] = [.42, .44, .48]
    t[(lx == 31) | (ly == 31)] = [.14, .15, .17]
    for cx in (3, 28):
        for cy in (3, 28):
            t[((lx - cx) ** 2 + (ly - cy) ** 2) <= 1] = [.55, .56, .6]
    t *= (0.8 + 0.4 * fbm(n, n, rng))[..., None]
    return np.clip(t, 0, 1)


def tex_crate(rng, n=64):
    Y, X = np.mgrid[0:n, 0:n]
    grain = noise(n, n, 2, 16, rng) * .6 + noise(n, n, 1, 8, rng) * .4
    t = np.array([.55, .36, .18]) * (0.7 + 0.5 * grain)[..., None]
    t[(Y % 16 == 15)] *= 0.55
    frame = (X < 6) | (X > n - 7) | (Y < 6) | (Y > n - 7) | (np.abs(X - Y) < 4)
    t[frame] = np.array([.40, .26, .13]) * (0.8 + 0.3 * grain[frame])[..., None]
    t[(X == 6) | (X == n - 7) | (Y == 6) | (Y == n - 7)] *= 0.5
    return np.clip(t, 0, 1)


def tex_door(rng, n=64):
    Y, X = np.mgrid[0:n, 0:n]
    t = np.zeros((n, n, 3)) + [.30, .36, .30]
    t[(X % 21 == 0) & (Y < 48)] = [.17, .2, .17]
    t[Y == 24] = [.17, .2, .17]
    band = (Y >= 50) & (Y < 60)
    stripe = ((X + Y) // 5) % 2 == 0
    t[band & stripe] = [.85, .7, .1]
    t[band & ~stripe] = [.08, .08, .08]
    t[(X < 2) | (X > n - 3) | (Y < 2)] = [.12, .12, .12]
    t *= (0.8 + 0.4 * fbm(n, n, rng))[..., None]
    return np.clip(t, 0, 1)


def tex_checker(n=64, k=8):
    Y, X = np.mgrid[0:n, 0:n]
    c = ((X // (n // k)) + (Y // (n // k))) % 2
    t = np.where(c[..., None] == 0, [.93, .88, .74], [.10, .38, .42]).astype(float)
    t[(X % (n // k) == 0) | (Y % (n // k) == 0)] *= 0.8
    return t


def tex_flat(c):
    return np.ones((4, 4, 3)) * np.array(c)


rng = np.random.default_rng(7)
TEX = {
    "brick": tex_brick(rng), "floor": tex_floor(rng), "ceil": tex_ceiling(rng),
    "crate": tex_crate(rng), "door": tex_door(rng), "checker": tex_checker(),
    "lamp": tex_flat([1.0, .95, .8]), "grey": tex_flat([.75, .75, .75]),
    "lampred": tex_flat([1.0, .45, .35]), "lampblue": tex_flat([.75, .85, 1.0]),
}
EMISSIVE = {"lamp", "lampred", "lampblue"}


def sample(tex, u, v, filt):
    h, w = tex.shape[:2]
    if filt == "point":
        x = np.floor(u * w).astype(int) % w
        y = np.floor((1 - v) * h).astype(int) % h
        return tex[y, x]
    fx, fy = u * w - .5, (1 - v) * h - .5
    x0, y0 = np.floor(fx).astype(int), np.floor(fy).astype(int)
    tx, ty = (fx - x0)[..., None], (fy - y0)[..., None]
    x1, y1 = (x0 + 1) % w, (y0 + 1) % h
    x0, y0 = x0 % w, y0 % h
    a = tex[y0, x0] * (1 - tx) + tex[y0, x1] * tx
    b = tex[y1, x0] * (1 - tx) + tex[y1, x1] * tx
    return a * (1 - ty) + b * ty


# ------------------------------------------------------------------ escena
class Cam:
    def __init__(self, pos, target, fov=70):
        self.pos = np.array(pos, float)
        f = np.array(target, float) - self.pos
        self.f = f / np.linalg.norm(f)
        r = np.cross([0, 1, 0], self.f)
        self.r = r / np.linalg.norm(r)
        self.u = np.cross(self.f, self.r)
        self.t = 1 / math.tan(math.radians(fov) / 2)

    def view(self, P):
        d = P - self.pos
        return np.stack([d @ self.r, d @ self.u, d @ self.f], -1)


class Mesh:
    def __init__(self):
        self.tris = []  # (P(3,3), UV(3,2), N(3,3), tex)

    def quad(self, p, uv, n, tex):
        p, uv = np.array(p, float), np.array(uv, float)
        N = np.tile(np.array(n, float), (3, 1))
        self.tris.append((p[[0, 1, 2]], uv[[0, 1, 2]], N, tex))
        self.tris.append((p[[0, 2, 3]], uv[[0, 2, 3]], N, tex))

    def grid(self, o, eu, ev, nu, nv, su, sv, n, tex):
        o, eu, ev = map(lambda a: np.array(a, float), (o, eu, ev))
        for i in range(nu):
            for j in range(nv):
                a0, a1, b0, b1 = i / nu, (i + 1) / nu, j / nv, (j + 1) / nv
                P = [o + eu * a0 + ev * b0, o + eu * a1 + ev * b0, o + eu * a1 + ev * b1, o + eu * a0 + ev * b1]
                UV = [(a0 * su, b0 * sv), (a1 * su, b0 * sv), (a1 * su, b1 * sv), (a0 * su, b1 * sv)]
                self.quad(P, UV, n, tex)

    def cube(self, c, s, yaw, tex):
        c = np.array(c, float)
        cy, sy = math.cos(yaw), math.sin(yaw)
        R = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
        h = s / 2
        faces = [((0, 0, -1), (1, 0, 0)), ((0, 0, 1), (-1, 0, 0)), ((1, 0, 0), (0, 0, 1)),
                 ((-1, 0, 0), (0, 0, -1)), ((0, 1, 0), (1, 0, 0))]
        for n, r in faces:
            n, r = np.array(n, float), np.array(r, float)
            up = np.cross(n, r) * -1 if n[1] == 0 else np.array([0, 0, 1.0])
            if n[1] == 0:
                up = np.array([0, 1.0, 0])
            ctr = n * h
            P = [ctr - r * h - up * h, ctr + r * h - up * h, ctr + r * h + up * h, ctr - r * h + up * h]
            P = [R @ p + c + [0, h, 0] for p in P]
            self.quad(P, [(0, 0), (1, 0), (1, 1), (0, 1)], R @ n, tex)


def light_at(P, N, lights, ambient):
    col = np.zeros(P.shape) + ambient
    for kind, a, lc, rng_ in lights:
        if kind == "point":
            L = np.array(a) - P
            d = np.linalg.norm(L, axis=-1, keepdims=True)
            ndl = np.clip(np.sum(N * L / np.maximum(d, 1e-6), -1, keepdims=True), 0, None)
            att = np.clip(1 - d / rng_, 0, 1) ** 2
            col = col + np.array(lc) * ndl * att
        else:
            ndl = np.clip(np.sum(N * -np.array(a), -1, keepdims=True), 0, None)
            col = col + np.array(lc) * ndl
    return col


def clip_near(V, near=0.05):
    out = []
    n = len(V)
    for i in range(n):
        a, b = V[i], V[(i + 1) % n]
        ia, ib = a[2] >= near, b[2] >= near
        if ia:
            out.append(a)
        if ia != ib:
            t = (near - a[2]) / (b[2] - a[2])
            out.append(a + (b - a) * t)
    return out


def render(mesh, cam, W, H, affine=False, snap=False, filt="point", light="vertex",
           lights=(), ambient=(1, 1, 1), fog=None, bg=(0, 0, 0)):
    img = np.zeros((H, W, 3)) + np.array(bg, float)
    zb = np.full((H, W), np.inf)
    aspect = W / H
    for P, UV, N, tex in mesh.tris:
        V = cam.view(P)
        emis = tex in EMISSIVE
        L = np.ones((3, 3)) if (emis or light == "pixel") else light_at(P, N, lights, ambient)
        if fog and not emis:
            F = np.clip((fog[1] - V[:, 2]) / (fog[1] - fog[0]), 0, 1)[:, None]
        else:
            F = np.ones((3, 1))
        A = np.concatenate([V, UV, L, F, P, N], 1)  # 0:3 view 3:5 uv 5:8 luz 8 niebla 9:12 mundo 12:15 normal
        poly = clip_near(list(A))
        if len(poly) < 3:
            continue
        poly = np.array(poly)
        x, y, z = poly[:, 0], poly[:, 1], poly[:, 2]
        sx = (cam.t * x / (aspect * z) * .5 + .5) * W
        sy = (.5 - cam.t * y / z * .5) * H
        if snap:
            sx, sy = np.round(sx), np.round(sy)
        for k in range(1, len(poly) - 1):
            idx = [0, k, k + 1]
            raster(img, zb, sx[idx], sy[idx], z[idx], poly[idx], tex, affine, filt, light, lights, ambient, fog, emis)
    return img


def raster(img, zb, sx, sy, w, attr, tex, affine, filt, light, lights, ambient, fog, emis):
    H, W = zb.shape
    x0, x1 = max(int(np.floor(sx.min())), 0), min(int(np.ceil(sx.max())), W - 1)
    y0, y1 = max(int(np.floor(sy.min())), 0), min(int(np.ceil(sy.max())), H - 1)
    if x0 > x1 or y0 > y1:
        return
    area = (sx[1] - sx[0]) * (sy[2] - sy[0]) - (sy[1] - sy[0]) * (sx[2] - sx[0])
    if abs(area) < 1e-9:
        return
    X, Y = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)

    def e(a, b):
        return (sx[b] - sx[a]) * (Y - sy[a]) - (sy[b] - sy[a]) * (X - sx[a])
    l0, l1 = e(1, 2) / area, e(2, 0) / area
    l2 = 1 - l0 - l1
    m = (l0 >= -1e-9) & (l1 >= -1e-9) & (l2 >= -1e-9)
    if not m.any():
        return
    Lb = np.stack([l0[m], l1[m], l2[m]], -1)
    iw = Lb @ (1 / w)
    depth = 1 / iw
    ys, xs = np.nonzero(m)
    ys, xs = ys + y0, xs + x0
    vis = depth < zb[ys, xs]
    if not vis.any():
        return
    Lb, iw, depth, ys, xs = Lb[vis], iw[vis], depth[vis], ys[vis], xs[vis]
    persp = ((Lb / w) @ attr) / iw[:, None]
    lin = Lb @ attr
    I = lin if affine else persp
    col = sample(TEX[tex], I[:, 3], I[:, 4], filt)
    if not emis:
        if light == "pixel":
            Pw = persp[:, 9:12]
            Nn = persp[:, 12:15]
            Nn = Nn / np.linalg.norm(Nn, axis=-1, keepdims=True)
            col = col * light_at(Pw, Nn, lights, ambient)
        else:
            col = col * I[:, 5:8]
        if fog:
            f = I[:, 8:9] if light == "vertex" else np.clip((fog[1] - depth) / (fog[1] - fog[0]), 0, 1)[:, None]
            col = col * f + np.array(fog[2]) * (1 - f)
    img[ys, xs] = col
    zb[ys, xs] = depth


BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], float)


def quantize(img, levels, dither=1.0):
    H, W = img.shape[:2]
    steps = levels - 1
    th = BAYER[np.arange(H)[:, None] % 4, np.arange(W)[None, :] % 4] / 16 - .5
    c = img + th[..., None] * dither / steps
    return np.floor(np.clip(c, 0, 1) * steps + .5) / steps


def to_pil(a):
    return Image.fromarray((np.clip(a, 0, 1) * 255 + .5).astype(np.uint8))


def up(a, k):
    im = to_pil(a) if isinstance(a, np.ndarray) else a
    return im.resize((im.width * k, im.height * k), Image.NEAREST)


def down(a, k):
    im = to_pil(a)
    return im.resize((im.width // k, im.height // k), Image.LANCZOS)


# ------------------------------------------------------------------ composición
def compose(panels, labels, title, sub=None, notes=None, cols=None, gap=20):
    cols = cols or len(panels)
    rows = math.ceil(len(panels) / cols)
    pw, ph = panels[0].size
    top = 70 if sub else 52
    note_h = 26 * max((len(n) for n in notes), default=0) + 8 if notes else 0
    cell_h = ph + 34 + note_h
    Wc = cols * pw + (cols + 1) * gap
    Hc = top + rows * cell_h + rows * gap
    can = Image.new("RGB", (Wc, Hc), BG)
    d = ImageDraw.Draw(can)
    d.text((gap, 14), title, fill=FG, font=font(24, True))
    if sub:
        d.text((gap, 46), sub, fill=MUTED, font=font(15))
    for i, (p, lab) in enumerate(zip(panels, labels)):
        r, c = divmod(i, cols)
        x = gap + c * (pw + gap)
        y = top + gap // 2 + r * (cell_h + gap)
        d.text((x, y), lab, fill=ACCENT, font=font(17, True))
        can.paste(p, (x, y + 28))
        if notes:
            for j, line in enumerate(notes[i]):
                d.text((x, y + 34 + ph + j * 24), line, fill=MUTED, font=font(14))
    return can


def save(im, name):
    im.save(os.path.join(OUT, name), optimize=True)
    print("ok", name, im.size)


# ------------------------------------------------------------------ pasillo
def corridor(lamps=True):
    m = Mesh()
    Z0, Z1 = -1, 24
    L = Z1 - Z0
    m.grid((-2, 0, Z0), (4, 0, 0), (0, 0, L), 2, 12, 2, L / 2, (0, 1, 0), "floor")
    m.grid((2, 3, Z0), (-4, 0, 0), (0, 0, L), 2, 12, 2, L / 2, (0, -1, 0), "ceil")
    m.grid((-2, 0, Z1), (0, 0, -L), (0, 3, 0), 12, 2, L / 2, 1.5, (1, 0, 0), "brick")
    m.grid((2, 0, Z0), (0, 0, L), (0, 3, 0), 12, 2, L / 2, 1.5, (-1, 0, 0), "brick")
    m.grid((-2, 0, Z1), (4, 0, 0), (0, 3, 0), 2, 2, 2, 1.5, (0, 0, -1), "brick")
    m.quad([(-.7, 0, Z1 - .02), (.7, 0, Z1 - .02), (.7, 2.2, Z1 - .02), (-.7, 2.2, Z1 - .02)],
           [(0, 0), (1, 0), (1, 1), (0, 1)], (0, 0, -1), "door")
    m.cube((1.2, 0, 5), 1.0, .25, "crate")
    m.cube((1.25, 1.0, 5.1), .8, -.2, "crate")
    m.cube((-1.3, 0, 9), 1.0, .6, "crate")
    m.cube((1.1, 0, 15), .9, .1, "crate")
    if lamps:
        for z, t in ((6, "lamp"), (15, "lampblue"), (22, "lampred")):
            m.quad([(-.4, 2.98, z - .3), (.4, 2.98, z - .3), (.4, 2.98, z + .3), (-.4, 2.98, z + .3)],
                   [(0, 0)] * 4, (0, -1, 0), t)
    return m


LIGHTS = [("point", (0, 2.6, 6), (1.25, .95, .65), 8.5),
          ("point", (0, 2.6, 15), (.6, .8, 1.25), 8.5),
          ("point", (0, 2.5, 22), (1.4, .4, .3), 6.5)]
AMB = (.10, .10, .13)
FOGC = (.05, .05, .07)
CORR = Mesh()


def corridor_cam(dz=0.0, sway=0.0):
    return Cam((.35 + sway, 1.6, dz), (.0 + sway * .5, 1.35, 12 + dz), fov=72)


def psx_corridor(W, H, affine=True, snap=True, fog=True, cam=None):
    return render(CORR, cam or corridor_cam(), W, H, affine=affine, snap=snap, filt="point", light="vertex",
                  lights=LIGHTS, ambient=AMB, fog=(3, 22, FOGC) if fog else None, bg=FOGC)


def main():
    global CORR
    CORR = corridor()
    which = set(sys.argv[2:]) or {"all"}

    def want(k):
        return "all" in which or k in which

    # 01 comparación
    if want("01"):
        modern = render(CORR, corridor_cam(), 1280, 960, filt="bilinear", light="pixel",
                        lights=LIGHTS, ambient=AMB, fog=(3, 22, FOGC), bg=FOGC)
        psx = quantize(psx_corridor(320, 240), 32)
        save(compose([down(modern, 2), up(psx, 2)],
                     ["Render moderno", "Estilo PSX (lo que hace este kit)"],
                     "La misma escena, las mismas texturas, dos formas de dibujarla",
                     "Mismos modelos y texturas de 64x64 px. Solo cambian los shaders y el postprocesado.",
                     [["Filtrado bilineal · perspectiva correcta", "Luz por píxel · alta resolución"],
                      ["Point · mapeado afín · vertex snapping · luz por vértice", "320x240 · 32 niveles de color + dithering"]]),
             "01_moderno_vs_psx.png")

    # 02 mapeado afín
    if want("02"):
        m = Mesh()
        m.grid((-3, 0, -3), (6, 0, 0), (0, 0, 6), 1, 1, 1, 1, (0, 1, 0), "checker")
        m4 = Mesh()
        m4.grid((-3, 0, -3), (6, 0, 0), (0, 0, 6), 4, 4, 1, 1, (0, 1, 0), "checker")
        cam = Cam((-2.4, 2.3, -4.4), (.3, 0, .4), fov=70)
        kw = dict(filt="point", light="vertex", bg=BG and (.09, .09, .11))
        a = render(m, cam, 320, 240, affine=False, **kw)
        b = render(m, cam, 320, 240, affine=True, **kw)
        c = render(m4, cam, 320, 240, affine=True, **kw)
        save(compose([up(a, 2), up(b, 2), up(c, 2)],
                     ["Perspectiva correcta", "Afín, 1 quad (2 triángulos)", "Afín, subdividido 4x4 (32 tris)"],
                     "Mapeado afín: por qué las texturas se doblan en PS1",
                     "Un suelo de 6x6 m con una textura de ajedrez. La PS1 interpolaba las UV en línea recta por la pantalla.",
                     [["Lo que hace una GPU moderna:", "las líneas siguen rectas."],
                      ["Las líneas se tuercen; se nota el", "corte en diagonal entre los 2 triángulos."],
                      ["Con más polígonos el error es menor.", "Por eso se subdividían los suelos."]]),
             "02_mapeado_afin.png")

    # 02b GIF afín en movimiento
    if want("02b"):
        frames = []
        for i in range(30):
            cam = corridor_cam(dz=i * .12, sway=.25 * math.sin(i / 30 * 2 * math.pi))
            a = render(CORR, cam, 200, 150, affine=False, filt="point", lights=LIGHTS, ambient=AMB,
                       fog=(3, 22, FOGC), bg=FOGC)
            b = render(CORR, cam, 200, 150, affine=True, filt="point", lights=LIGHTS, ambient=AMB,
                       fog=(3, 22, FOGC), bg=FOGC)
            frames.append(compose([up(a, 2), up(b, 2)], ["Perspectiva correcta", "Afín (PS1)"],
                                  "Mapeado afín en movimiento (mira las baldosas)"))
        gif(frames, "02b_afin_movimiento.gif")

    # 03 snapping diagrama + gif
    if want("03"):
        save(snap_diagram(), "03_vertex_snapping.png")
    if want("03b"):
        m = Mesh()
        m.grid((-3, 0, -1), (6, 0, 0), (0, 0, 8), 6, 8, 3, 4, (0, 1, 0), "floor")
        frames = []
        for i in range(48):
            m2 = Mesh()
            m2.tris = list(m.tris)
            m2.cube((0, 0, 3.2), 1.2, i * math.radians(1.2), "crate")
            cam = Cam((-.6 + i * .012, 1.5, -.4), (0, .5, 3.2), fov=60)
            kw = dict(affine=False, filt="point", lights=[("dir", (-.4, -.8, .45), (1, .95, .85), 0)],
                      ambient=(.35, .35, .4), bg=(.09, .09, .11))
            a = render(m2, cam, 160, 120, snap=False, **kw)
            b = render(m2, cam, 160, 120, snap=True, **kw)
            frames.append(compose([up(a, 3), up(b, 3)], ["Sin snapping", "Con vertex snapping"],
                                  "Vertex snapping: los vértices saltan de píxel en píxel"))
        gif(frames, "03b_snapping_movimiento.gif", dur=70)

    # 04 filtrado
    if want("04"):
        t = to_pil(TEX["crate"]).crop((0, 0, 24, 24))
        a = t.resize((288, 288), Image.BILINEAR)
        b = t.resize((288, 288), Image.NEAREST)
        save(compose([a, b], ["Bilinear (por defecto)", "Point / no filter (PSX)"],
                     "Filtrado de texturas", "Un trozo de 24x24 px de la textura de la caja, ampliado 12 veces.",
                     [["Mezcla píxeles vecinos: borroso."], ["Cada texel es un cuadrado nítido."]]),
             "04_filtrado.png")

    # 05 color / dithering
    if want("05"):
        save(dither_figure(), "05_color_dithering.png")

    # 06 texturizado
    if want("06"):
        save(texture_process(), "06_proceso_textura.png")

    # 07 polígonos
    if want("07"):
        panels, labels, notes = [], [], []
        for nside in (6, 8, 12, 32):
            m = cylinder(nside)
            cam = Cam((0, 2.4, -3.6), (0, .95, 0), fov=50)
            img = render(m, cam, 440, 440, filt="point", lights=[("dir", (-.6, -.5, .62), (.95, .9, .8), 0)],
                         ambient=(.18, .18, .24), bg=(.09, .09, .11))
            panels.append(down(img, 2))
            labels.append(f"{nside} lados")
            notes.append([f"{2 * nside + 2 * (nside - 2)} triángulos"])
        save(compose(panels, labels, "Presupuesto de polígonos: un cilindro",
                     "Para PSX, 6-8 lados basta en brazos, tuberías o barriles. 32 es un desperdicio de polígonos.",
                     notes), "07_poligonos.png")

    # 08 luz por vértice
    if want("08"):
        panels = []
        cam = Cam((0, 7.5, -4.8), (0, 0, .2), fov=58)
        lt = [("point", (.5, 1.0, .3), (1.5, 1.3, 1.0), 5.0)]
        for sub, mode in ((8, "pixel"), (2, "vertex"), (8, "vertex")):
            m = Mesh()
            m.grid((-4, 0, -4), (8, 0, 0), (0, 0, 8), sub, sub, 4, 4, (0, 1, 0), "grey")
            panels.append(down(render(m, cam, 640, 480, filt="point", light=mode, lights=lt,
                                      ambient=(.06, .06, .08), bg=(.09, .09, .11)), 2))
        save(compose(panels, ["Luz por píxel (moderno)", "Por vértice, quads de 4 m", "Por vértice, quads de 1 m"],
                     "Iluminación por vértice (Gouraud)",
                     "Un suelo de 8x8 m con una luz puntual. La PS1 solo calculaba la luz en los vértices.",
                     [["Círculo de luz suave y redondo."], ["Pocos vértices: la luz se ve", "a manchas en forma de rombo."],
                      ["Más vértices: más precisión.", "Es el look PSX 'bueno'."]]),
             "08_luz_por_vertice.png")

    # 09 diagramas
    if want("09"):
        save(pipeline_diagram(), "09_pipeline.png")

    # 10 niebla
    if want("10"):
        a = quantize(psx_corridor(320, 240, fog=False), 32)
        b = quantize(psx_corridor(320, 240, fog=True), 32)
        save(compose([up(a, 2), up(b, 2)], ["Sin niebla", "Con niebla (color = fondo)"],
                     "Niebla: ocultar el final del escenario",
                     "La PS1 dibujaba poca distancia; la niebla escondía dónde se acababa el mundo y daba ambiente.",
                     [["El fondo negro corta de golpe."], ["Lo lejano se funde con el fondo."]]),
             "10_niebla.png")

    # 11 postprocesado por pasos
    if want("11"):
        raw = psx_corridor(640, 480, affine=True, snap=True)
        low = psx_corridor(320, 240)
        s1 = to_pil(raw)
        s2 = up(low, 2)
        s3 = up(quantize(low, 32), 2)
        s4 = up(quantize(low, 8), 2)
        crop = (150, 110, 470, 350)
        panels = [im.crop(crop).resize((480, 360), Image.NEAREST) for im in (s1, s2, s3, s4)]
        save(compose(panels, ["1. Shader PSX a 640x480", "2. + Pixelado 320x240",
                              "3. + 32 niveles + dithering", "4. 8 niveles + dithering (exagerado)"],
                     "PSX/PostProcess paso a paso (zoom al centro de la imagen)",
                     "Así se suman las tres operaciones del shader de postprocesado.",
                     [["Sin postprocesado."], ["_Resolution = (320, 240)"],
                      ["_ColorLevels = 32 (15 bits, como PS1)"], ["_ColorLevels = 8: el patrón se ve claro."]],
                     cols=2),
             "11_postprocesado_pasos.png")


def gif(frames, name, dur=90):
    pal = frames[len(frames) // 2].convert("P", palette=Image.ADAPTIVE, colors=255)
    fr = [f.quantize(palette=pal, dither=Image.Dither.NONE) for f in frames]
    fr[0].save(os.path.join(OUT, name), save_all=True, append_images=fr[1:], duration=dur, loop=0, optimize=True)
    print("ok", name, frames[0].size)


def cylinder(n, r=1.0, h=2.0):
    m = Mesh()
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        p0 = (r * math.cos(a0), 0, r * math.sin(a0))
        p1 = (r * math.cos(a1), 0, r * math.sin(a1))
        am = (a0 + a1) / 2
        nrm = (math.cos(am), 0, math.sin(am))
        m.quad([p0, p1, (p1[0], h, p1[2]), (p0[0], h, p0[2])], [(0, 0)] * 4, nrm, "grey")
    top = [(r * math.cos(2 * math.pi * i / n), h, r * math.sin(2 * math.pi * i / n)) for i in range(n)]
    for i in range(1, n - 1):
        P = np.array([top[0], top[i], top[i + 1]])
        m.tris.append((P, np.zeros((3, 2)), np.tile([0, 1.0, 0], (3, 1)), "grey"))
    return m


def arrow(d, a, b, col, w=3, head=10):
    d.line([a, b], fill=col, width=w)
    ang = math.atan2(b[1] - a[1], b[0] - a[0])
    p1 = (b[0] - head * math.cos(ang - .45), b[1] - head * math.sin(ang - .45))
    p2 = (b[0] - head * math.cos(ang + .45), b[1] - head * math.sin(ang + .45))
    d.polygon([b, p1, p2], fill=col)


def dashed(d, a, b, col, w=2, dash=8):
    L = math.dist(a, b)
    n = max(int(L / dash), 1)
    for i in range(0, n, 2):
        t0, t1 = i / n, min((i + 1) / n, 1)
        d.line([(a[0] + (b[0] - a[0]) * t0, a[1] + (b[1] - a[1]) * t0),
                (a[0] + (b[0] - a[0]) * t1, a[1] + (b[1] - a[1]) * t1)], fill=col, width=w)


def snap_diagram():
    W, H = 1180, 560
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.text((20, 14), "Vertex snapping: redondear la posición en pantalla", fill=FG, font=font(24, True))
    d.text((20, 46), "La PS1 no tenía decimales para las coordenadas de pantalla: cada vértice cae justo en un píxel.",
           fill=MUTED, font=font(15))
    # panel 1: rejilla
    cs, ox, oy, nx, ny = 46, 30, 110, 11, 8
    orig = [(1.35, 1.7), (9.55, 2.35), (4.2, 6.6)]
    snp = [(round(x), round(y)) for x, y in orig]

    def S(p):
        return (ox + p[0] * cs, oy + p[1] * cs)
    # pixeles rellenos por el triángulo ajustado (muestreo en el centro)
    (ax, ay), (bx, by), (cx, cy) = snp
    for j in range(ny):
        for i in range(nx):
            px, py = i + .5, j + .5

            def e(x0, y0, x1, y1):
                return (x1 - x0) * (py - y0) - (y1 - y0) * (px - x0)
            w0, w1, w2 = e(bx, by, cx, cy), e(cx, cy, ax, ay), e(ax, ay, bx, by)
            if (w0 >= 0 and w1 >= 0 and w2 >= 0) or (w0 <= 0 and w1 <= 0 and w2 <= 0):
                d.rectangle([ox + i * cs, oy + j * cs, ox + (i + 1) * cs, oy + (j + 1) * cs], fill=(70, 55, 35))
    for i in range(nx + 1):
        d.line([(ox + i * cs, oy), (ox + i * cs, oy + ny * cs)], fill=(60, 60, 72), width=1)
    for j in range(ny + 1):
        d.line([(ox, oy + j * cs), (ox + nx * cs, oy + j * cs)], fill=(60, 60, 72), width=1)
    for k in range(3):
        dashed(d, S(orig[k]), S(orig[(k + 1) % 3]), BLUE, 2)
        d.line([S(snp[k]), S(snp[(k + 1) % 3])], fill=ACCENT, width=3)
    for k in range(3):
        a, b = S(orig[k]), S(snp[k])
        arrow(d, a, b, FG, 2, 8)
        d.ellipse([a[0] - 5, a[1] - 5, a[0] + 5, a[1] + 5], fill=BLUE)
        d.ellipse([b[0] - 6, b[1] - 6, b[0] + 6, b[1] + 6], fill=ACCENT)
    ly = oy + ny * cs + 18
    d.line([(ox, ly + 9), (ox + 30, ly + 9)], fill=BLUE, width=2)
    d.text((ox + 40, ly), "Posición real (con decimales)", fill=FG, font=font(14))
    d.line([(ox + 290, ly + 9), (ox + 320, ly + 9)], fill=ACCENT, width=3)
    d.text((ox + 330, ly), "Posición ajustada al píxel", fill=FG, font=font(14))
    d.text((ox, ly + 26), "Cada cuadro = 1 píxel de la pantalla de 320x240", fill=MUTED, font=font(14))
    # panel 2: gráfica en el tiempo
    gx, gy, gw, gh = 600, 110, 540, 330
    d.text((gx, gy - 30), "Posición X de un vértice mientras la cámara se mueve despacio", fill=FG, font=font(15, True))
    d.rectangle([gx, gy, gx + gw, gy + gh], outline=(70, 70, 82))
    for j in range(1, 6):
        yy = gy + gh - j * gh / 6
        d.line([(gx, yy), (gx + gw, yy)], fill=(45, 45, 55))
    pts_s, pts_r = [], []
    for k in range(0, 541):
        t = k / 540
        val = 0.4 + 5.0 * t + .25 * math.sin(t * 9)
        pts_s.append((gx + k, gy + gh - val / 6 * gh))
        pts_r.append((gx + k, gy + gh - round(val) / 6 * gh))
    d.line(pts_r, fill=ACCENT, width=3)
    d.line(pts_s, fill=BLUE, width=2)
    d.text((gx + 4, gy + gh + 8), "tiempo  →", fill=MUTED, font=font(14))
    d.text((gx, gy + gh + 34), "Azul: movimiento suave (GPU moderna).", fill=FG, font=font(14))
    d.text((gx, gy + gh + 56), "Naranja: avanza a saltos de 1 píxel → los modelos 'tiemblan'.", fill=FG, font=font(14))
    d.text((gx, gy + gh + 86), "En el shader:  snapped = round(ndc * halfRes) / halfRes", fill=ACCENT, font=font(15, mono=True))
    return im


def dither_figure():
    W = 1180
    im = Image.new("RGB", (W, 640), BG)
    d = ImageDraw.Draw(im)
    d.text((20, 14), "Profundidad de color y dithering", fill=FG, font=font(24, True))
    d.text((20, 46), "La PS1 usaba 15 bits de color (32 niveles por canal) y un patrón de Bayer para disimular los saltos.",
           fill=MUTED, font=font(15))
    gw, gh = 300, 20
    t = np.linspace(0, 1, gw)[None, :, None]
    c0, c1 = np.array([.04, .05, .12]), np.array([.95, .55, .18])
    grad = np.repeat(c0 + (c1 - c0) * t, gh, 0)
    rows = [("Original (256 niveles)", grad), ("32 niveles, sin dithering", quantize(grad, 32, 0)),
            ("32 niveles + dithering (PSX)", quantize(grad, 32, 1)), ("8 niveles, sin dithering", quantize(grad, 8, 0)),
            ("8 niveles + dithering", quantize(grad, 8, 1))]
    y = 92
    for lab, a in rows:
        d.text((20, y + 12), lab, fill=FG, font=font(15))
        im.paste(up(a, 2), (290, y))
        y += gh * 2 + 30
    # zoom del dither
    zx, zy = 290, y + 8
    d.text((20, zy + 8), "Zoom x8 de la franja", fill=FG, font=font(15))
    d.text((20, zy + 30), "'8 niveles + dithering'", fill=MUTED, font=font(14))
    z = quantize(grad, 8, 1)[:, 150:220]
    im.paste(up(z[:8], 8), (zx, zy))
    # matriz de Bayer
    bx, by, bc = 960, 110, 48
    d.text((bx - 10, by - 30), "Matriz de Bayer 4x4", fill=FG, font=font(16, True))
    for j in range(4):
        for i in range(4):
            v = BAYER[j, i]
            g = int(40 + v / 15 * 200)
            d.rectangle([bx + i * bc, by + j * bc, bx + (i + 1) * bc - 2, by + (j + 1) * bc - 2], fill=(g, g, g))
            d.text((bx + i * bc + 14, by + j * bc + 13), f"{int(v):>2}", fill=(255, 255, 255) if g < 140 else (0, 0, 0),
                   font=font(17, True, mono=True))
    tx = bx - 30
    for k, line in enumerate(["Cada píxel recibe un pequeño", "desplazamiento según su", "posición en este patrón 4x4,", "antes de",
                              "redondear el color.", "", "Resultado: en vez de bandas,", "una trama que el ojo mezcla."]):
        d.text((tx, by + 4 * bc + 20 + k * 22), line, fill=MUTED, font=font(14))
    return im


def texture_process():
    r2 = np.random.default_rng(3)
    n = 256
    Y, X = np.mgrid[0:n, 0:n]
    base = fbm(n, n, r2, ((64, 64, .3), (32, 32, .2), (16, 16, .15), (8, 8, .15), (4, 4, .1), (2, 2, .1)))
    row = Y // 64
    off = (row % 2) * 64
    stone = ((X + off) // 128) % 2 + row * 2
    tint = np.array([[.55, .5, .45], [.5, .48, .46], [.6, .52, .44], [.47, .47, .5],
                     [.58, .54, .5], [.5, .45, .42], [.55, .55, .52], [.52, .48, .44]])
    photo = tint[stone % 8] * (0.55 + 0.8 * base)[..., None]
    gx, gy = (X + off) % 128, Y % 64
    edge = np.minimum(np.minimum(gx, 127 - gx), np.minimum(gy, 63 - gy))
    photo *= np.clip(edge / 6, .25, 1)[..., None]
    photo *= (1 + r2.normal(0, .07, (n, n)))[..., None]
    photo = np.clip(photo, 0, 1)
    p1 = to_pil(photo)
    p2 = p1.resize((64, 64), Image.LANCZOS)
    p3 = p2.quantize(16, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    S = 256
    panels = [p1, p2.resize((S, S), Image.NEAREST), p3.resize((S, S), Image.NEAREST)]
    can = compose(panels, ["1. Foto original (256 px)", "2. Reducida a 64x64", "3. Paleta de 16 colores"],
                  "Texturizado: de una foto a una textura PSX",
                  "El flujo clásico de la época: digitalizar, reducir y limitar los colores. Luego retocar a mano.",
                  [["Mucho detalle y colores", "que la PS1 no podría guardar."],
                   ["Interpolación Lanczos.", "Mostrada ampliada (Point)."],
                   ["Modo indexado, sin dithering.", "Paleta resultante:"]], gap=40)
    d = ImageDraw.Draw(can)
    pal = p3.getcolors(64)
    x0 = 40 + 2 * (S + 40)
    y0 = can.height - 34
    can2 = Image.new("RGB", (can.width, can.height + 30), BG)
    can2.paste(can, (0, 0))
    d = ImageDraw.Draw(can2)
    for i, (_, c) in enumerate(sorted(pal, key=lambda t: sum(t[1]))):
        d.rectangle([x0 + i * 16, y0 + 22, x0 + i * 16 + 14, y0 + 44], fill=c)
    for k in range(2):
        xa = 40 + (k + 1) * (S + 40) - 34
        arrow(d, (xa, 70 + 28 + S // 2), (xa + 28, 70 + 28 + S // 2), ACCENT, 3, 9)
    return can2


def pipeline_diagram():
    W, H = 1180, 470
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.text((20, 14), "Por dónde pasa un vértice (PSX/Lit)", fill=FG, font=font(24, True))
    boxes = [("Objeto", "positionOS"), ("Mundo", "positionWS"), ("Clip", "positionCS"), ("NDC", "-1 .. 1"),
             ("Snapping", "round()"), ("Píxeles", "rasterizado")]
    ops = ["Model", "View+Proj", "÷ w", "x halfRes", "GPU"]
    bw, bh, gap, y = 150, 74, 47, 70
    for i, (t, s) in enumerate(boxes):
        x = 20 + i * (bw + gap)
        hl = t == "Snapping"
        d.rounded_rectangle([x, y, x + bw, y + bh], 10, fill=(60, 42, 20) if hl else (38, 38, 48),
                            outline=ACCENT if hl else (90, 90, 105), width=2)
        d.text((x + bw / 2, y + 24), t, fill=FG, font=font(17, True), anchor="mm")
        d.text((x + bw / 2, y + 50), s, fill=MUTED, font=font(13, mono=True), anchor="mm")
        if i < len(ops):
            arrow(d, (x + bw + 4, y + bh / 2), (x + bw + gap - 4, y + bh / 2), (150, 150, 165), 2, 8)
            d.text((x + bw + gap / 2, y + bh + 16), ops[i], fill=MUTED, font=font(12), anchor="mm")
    d.text((20, y + bh + 40), "GetVertexPositionInputs() hace Objeto → Mundo → Clip. El snapping trabaja en NDC y "
                              "luego multiplica por w para volver a Clip.", fill=MUTED, font=font(14))

    y2 = 270
    d.text((20, y2 - 40), "Qué pasa en cada frame", fill=FG, font=font(24, True))
    steps = [("1. Geometría", "PSX/Lit en cada material", "snapping, afín, luz vértice"),
             ("2. Post de URP", "Global Volume", "vignette, color, tonemap"),
             ("3. Full Screen Pass", "PSX/PostProcess", "pixelado, dither, 32 niveles"),
             ("4. Pantalla", "", "")]
    bw2, gap2 = 248, 50
    for i, (t, a, b) in enumerate(steps):
        x = 20 + i * (bw2 + gap2)
        d.rounded_rectangle([x, y2, x + bw2, y2 + 110], 12, fill=(38, 38, 48), outline=(90, 90, 105), width=2)
        d.text((x + 16, y2 + 16), t, fill=ACCENT, font=font(18, True))
        d.text((x + 16, y2 + 50), a, fill=FG, font=font(15))
        d.text((x + 16, y2 + 76), b, fill=MUTED, font=font(13))
        if i < 3:
            arrow(d, (x + bw2 + 6, y2 + 55), (x + bw2 + gap2 - 6, y2 + 55), (150, 150, 165), 2, 8)
    d.text((20, y2 + 130), "El paso 3 va en 'After Rendering Post Processing' para que el dithering sea lo último que se aplica.",
           fill=MUTED, font=font(14))
    return im


if __name__ == "__main__":
    main()
