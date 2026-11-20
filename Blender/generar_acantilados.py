"""
Genera un kit de paredes de montaña estilo PSX, MODULAR y TILEABLE, y lo exporta a Unity.

Uso (no toca tu sesión de Blender abierta):
  blender -b --factory-startup --python generar_acantilados.py -- <carpeta_proyecto> [carpeta_preview]

Crea:
  Assets/Textures/T_Roca.png                 textura de roca 128x128 tileable, 5 tonos
  Assets/Models/Acantilados/Roca_*.fbx       una pieza por FBX, origen en la base
  Blender/Acantilados.blend                  las piezas en fila, ya encajadas

Cómo encajan (lo importante):
  - Todas las piezas miden WIDTH (8 m) de ancho y sus dos laterales tienen EXACTAMENTE
    el mismo perfil (alturas, profundidades y altura de la silueta en el borde).
    El lado izquierdo de cualquier pieza encaja con el derecho de cualquier otra.
  - Colócalas cada 8 m con la misma rotación. Pueden girarse 180 grados: el perfil
    es igual en los dos lados.
  - La esquina (interior, 90 grados) termina en ese mismo perfil por sus dos lados:
    con la esquina en (0,0), la pieza recta siguiente va girada -90 grados en (0,-8).
  - La variedad (picos, cornisas, huecos, tinte) va solo en el centro de cada pieza
    y se desvanece hacia los bordes, para que no se vean costuras.
"""
import bpy, bmesh, math, os, random, sys
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PROJECT = argv[0] if argv else r"C:\Users\mkmik\VHS"
PREVIEW_DIR = argv[1] if len(argv) > 1 else os.path.join(PROJECT, "Blender", "preview")

FBX_DIR = os.path.join(PROJECT, "Assets", "Models", "Acantilados")
TEX_PATH = os.path.join(PROJECT, "Assets", "Textures", "T_Roca.png")
BLEND_PATH = os.path.join(PROJECT, "Blender", "Acantilados.blend")

WIDTH = 8.0         # ancho de módulo (rejilla de colocación)
COLS = 6            # columnas por pieza (par: la esquina dobla por la del centro)
ROWS = 6            # filas por pieza (iguales en todas para que los bordes coincidan)
EDGE_TOP = 11.0     # altura de la silueta en los bordes (común a todas las piezas)
CUBE_SIZE = 2.0     # metros que cubre una repetición de la textura (128 px a 64 px/m)
SINK = 1.5          # metros que la pieza se mete bajo el suelo
LEAN = 1.8          # metros que la parte alta se inclina hacia atrás (a EDGE_TOP de altura)
SHARP_ANGLE = 30.0  # aristas más marcadas que esto quedan duras
SHADE_TOP = EDGE_TOP + 6.0  # altura de referencia para el color de vértice (común)

for d in (FBX_DIR, os.path.dirname(TEX_PATH), os.path.dirname(BLEND_PATH), PREVIEW_DIR):
    os.makedirs(d, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


# ---------------------------------------------------------------------------
# Textura de roca tileable (128x128, paleta de 5 tonos + grietas)
# ---------------------------------------------------------------------------
def periodic_noise(size, cells, rng):
    """Ruido de valor que se repite sin costuras cada 'size' píxeles."""
    lat = rng.random((cells, cells))
    xs = np.arange(size) * cells / size
    i0 = np.floor(xs).astype(int)
    i1 = (i0 + 1) % cells
    f = xs - i0
    f = f * f * (3 - 2 * f)
    a = lat[np.ix_(i0, i0)]; b = lat[np.ix_(i0, i1)]
    c = lat[np.ix_(i1, i0)]; d = lat[np.ix_(i1, i1)]
    fx = f[None, :]; fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def make_rock_texture():
    S = 128
    rng = np.random.default_rng(7)
    n = 0.2 * periodic_noise(S, 4, rng) + 0.45 * periodic_noise(S, 8, rng) + 0.35 * periodic_noise(S, 16, rng)
    y = np.arange(S)[:, None] / S
    warp = periodic_noise(S, 2, rng) * 1.2 + periodic_noise(S, 8, rng) * 0.4
    phase = (y * 3 + warp) * 2 * np.pi                 # 3 estratos por repetición, deformados
    strata = 0.5 + 0.5 * np.sin(phase)
    v = 0.8 * n + 0.2 * strata                          # el ruido manda, los estratos solo se intuyen
    v = (v - v.min()) / (v.max() - v.min())

    palette = np.array([
        (0.27, 0.25, 0.23),
        (0.35, 0.33, 0.30),
        (0.43, 0.40, 0.36),
        (0.51, 0.48, 0.43),
        (0.59, 0.56, 0.50),
    ])
    lvl = np.clip(np.floor(v * 5), 0, 4).astype(int)
    speck = rng.random((S, S)) > 0.97                  # motas claras sueltas
    lvl = np.where(speck, np.minimum(lvl + 1, 4), lvl)
    rgb = palette[lvl]

    crack_mask = (np.abs(np.sin(phase)) < 0.06) & (periodic_noise(S, 16, rng) > 0.55)
    rgb[crack_mask] = (0.18, 0.17, 0.16)               # grietas horizontales

    rgba = np.concatenate([rgb, np.ones((S, S, 1))], axis=2)
    img = bpy.data.images.new("T_Roca", S, S, alpha=False)
    img.pixels = rgba.astype(np.float32).ravel()
    img.filepath_raw = TEX_PATH
    img.file_format = "PNG"
    img.save()
    return img


rock_img = make_rock_texture()


# ---------------------------------------------------------------------------
# Material (solo para verlo en Blender: textura x color de vértice, Closest)
# ---------------------------------------------------------------------------
mat = bpy.data.materials.new("M_Roca")
if mat.node_tree is None:
    mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes.get("Principled BSDF")
bsdf.inputs["Roughness"].default_value = 1.0
tex = nt.nodes.new("ShaderNodeTexImage")
tex.image = rock_img
tex.interpolation = "Closest"
vcol = nt.nodes.new("ShaderNodeVertexColor")
vcol.layer_name = "Col"
mix = nt.nodes.new("ShaderNodeMix")
mix.data_type = "RGBA"
mix.blend_type = "MULTIPLY"
mix.inputs[0].default_value = 1.0
nt.links.new(tex.outputs["Color"], mix.inputs[6])
nt.links.new(vcol.outputs["Color"], mix.inputs[7])
nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])


# ---------------------------------------------------------------------------
# Perfil de borde común a todas las piezas
# ---------------------------------------------------------------------------
def lean_at(z):
    return max(z, 0.0) / EDGE_TOP * LEAN


_edge_rng = random.Random(1234)
EDGE_Z = [-SINK + j * (EDGE_TOP + SINK) / ROWS for j in range(ROWS + 1)]
EDGE_DEPTH = [0.0] + [(-1) ** j * _edge_rng.uniform(0.3, 0.7) for j in range(1, ROWS + 1)]
EDGE_DEPTH = [d + lean_at(z) for d, z in zip(EDGE_DEPTH, EDGE_Z)]


# ---------------------------------------------------------------------------
# Generador de piezas
# ---------------------------------------------------------------------------
def make_piece(name, height, seed, peak=0.0, tint=(1, 1, 1), corner=False):
    rng = random.Random(seed)
    cw = WIDTH / COLS
    peak_pos = rng.uniform(0.35, 0.65)

    # Cornisas del interior: cada fila sale hacia delante o se mete hacia atrás
    ledge = [0.0] + [rng.choice((-1, 1)) * rng.uniform(0.4, 1.4) for _ in range(ROWS)]

    # Silueta superior: los bordes a EDGE_TOP; el interior sube o baja hacia 'height'
    raw = [rng.uniform(-1.5, 1.8) for _ in range(COLS + 1)]
    top_h = []
    for i in range(COLS + 1):
        if i in (0, COLS):
            top_h.append(EDGE_TOP)
            continue
        t = i / COLS
        l = raw[i - 1]; r = raw[i + 1]
        h = height + 0.5 * raw[i] + 0.25 * (l + r) + peak * math.exp(-((t - peak_pos) ** 2) / 0.05)
        w = min(i, COLS - i) / (COLS / 2)                  # 0 en el borde, 1 en el centro
        w = min(1.0, w * 1.5)
        top_h.append(EDGE_TOP + (h - EDGE_TOP) * w)

    grid = {}      # (i, j) -> (s, depth, z)
    edge_w = {}    # (i, j) -> 0 en el borde ... 1 en el centro
    for j in range(ROWS + 1):
        for i in range(COLS + 1):
            s = -WIDTH / 2 + i * cw
            w = min(i, COLS - i) / (COLS / 2)
            w = min(1.0, w * 1.5)
            edge_w[(i, j)] = w

            if i in (0, COLS):                              # borde: perfil común exacto
                grid[(i, j)] = (s, EDGE_DEPTH[j], EDGE_Z[j])
                continue

            center = corner and i == COLS // 2
            if not center:
                s += rng.uniform(-0.3, 0.3) * cw

            col_h = top_h[i]
            z = -SINK + j * (col_h + SINK) / ROWS
            if 0 < j < ROWS:
                z += rng.uniform(-0.25, 0.25) * (col_h + SINK) / ROWS

            depth = ledge[j] + rng.uniform(-0.4, 0.4)
            if 0 < j < ROWS and rng.random() < 0.15:
                depth += rng.uniform(0.8, 1.5)              # hueco
            if j == 0:
                depth = rng.uniform(-0.3, 0.3)              # base bajo tierra
            depth += lean_at(z)
            # Cerca del borde, la profundidad tiende al perfil común
            edge_d = EDGE_DEPTH[j] + (lean_at(z) - lean_at(EDGE_Z[j]))
            depth = edge_d + (depth - edge_d) * w

            grid[(i, j)] = (s, depth, z)

    bm = bmesh.new()
    vmap = {}
    vweight = {}
    for (i, j), (s, d, z) in grid.items():
        if corner:  # esquina interior: mitad izquierda mira a -Y, mitad derecha a -X
            if i == COLS // 2:
                x, y = d * 0.7, d * 0.7
            elif s < 0:
                x, y = s, d
            else:
                x, y = d, -s
        else:
            x, y = s, d
        v = bm.verts.new((x, y, z))
        vmap[(i, j)] = v
        vweight[v] = edge_w[(i, j)]

    for j in range(ROWS):
        for i in range(COLS):
            a, b = vmap[(i, j)], vmap[(i + 1, j)]
            c, d = vmap[(i + 1, j + 1)], vmap[(i, j + 1)]
            if rng.random() < 0.5:
                bm.faces.new((a, b, c)); bm.faces.new((a, c, d))
            else:
                bm.faces.new((a, b, d)); bm.faces.new((b, c, d))

    # Facetado: juntar triángulos casi planos en losas (sin tocar el contorno,
    # para que los bordes sigan siendo idénticos entre piezas) y volver a triangular
    bmesh.ops.dissolve_limit(
        bm, angle_limit=math.radians(7),
        verts=[v for v in bm.verts if not v.is_boundary],
        edges=[e for e in bm.edges if not e.is_boundary])
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.normal_update()

    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        e.smooth = not (e.is_manifold and e.calc_face_angle(0.0) > math.radians(SHARP_ANGLE))

    # UVs: proyección cúbica, una repetición cada CUBE_SIZE metros
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        for loop in f.loops:
            co = loop.vert.co
            if ax == 1:
                u, v = co.x, co.z
            elif ax == 0:
                u, v = co.y, co.z
            else:
                u, v = co.x, co.y
            loop[uv].uv = (u / CUBE_SIZE, v / CUBE_SIZE)

    # Color de vértice: claro arriba, oscuro abajo y en los huecos, tinte de la pieza.
    # Huecos y tinte se desvanecen hacia los bordes -> sin costuras de color.
    # En Unity PSX/Lit lo usa como multiplicador directo: 1 = textura tal cual.
    col = bm.loops.layers.color.new("Col")
    shade = {}
    for vtx in bm.verts:
        w = vweight.get(vtx, 1.0)
        zt = min(max((vtx.co.z + SINK) / (SHADE_TOP + SINK), 0.0), 1.0)
        base = 0.6 + 0.4 * zt ** 0.8
        if vtx.co.z < 0.5:
            base *= 0.8                                     # contacto con el suelo
        nbrs = [e.other_vert(vtx).co for e in vtx.link_edges]
        if nbrs and w > 0:
            avg = sum(nbrs, vtx.co * 0) / len(nbrs)
            recess = -(vtx.co - avg).dot(vtx.normal)        # >0 si está más hundido
            base *= 1.0 - min(max(recess / 1.0, 0.0), 1.0) * 0.35 * w
        t = [1.0 + (c - 1.0) * w for c in tint]
        shade[vtx] = (base * t[0], base * t[1], base * t[2], 1.0)
    for f in bm.faces:
        for loop in f.loops:
            loop[col] = shade[loop.vert]

    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.color_attributes.active_color_name = "Col"
    me.color_attributes.default_color_name = "Col"
    me.materials.append(mat)
    obj = bpy.data.objects.new(name, me)
    scene.collection.objects.link(obj)
    return obj


pieces = [
    make_piece("Roca_A", 13, seed=11, peak=1.5, tint=(1.00, 0.97, 0.92)),
    make_piece("Roca_B", 16, seed=23, peak=2.5, tint=(0.92, 0.94, 1.00)),
    make_piece("Roca_C", 9, seed=37, peak=0.0, tint=(1.00, 0.95, 0.86)),
    make_piece("Roca_Esquina", 12, seed=41, peak=1.0, tint=(0.96, 0.96, 0.96), corner=True),
]


# ---------------------------------------------------------------------------
# Exportar cada pieza (en el origen) a Unity
# ---------------------------------------------------------------------------
for obj in pieces:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(FBX_DIR, obj.name + ".fbx"),
        use_selection=True,
        object_types={"MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        colors_type="SRGB",
        add_leaf_bones=False,
        path_mode="STRIP",
    )
    print("Exportado", obj.name)

# En el .blend: una fila continua A-B-C cada 8 m y la esquina cerrando a la derecha
by_name = {o.name: o for o in pieces}
by_name["Roca_A"].location = (-24, 0, 0)
by_name["Roca_B"].location = (-16, 0, 0)
by_name["Roca_C"].location = (-8, 0, 0)
by_name["Roca_Esquina"].location = (0, 0, 0)


# ---------------------------------------------------------------------------
# Referencias para la preview: suelo y una persona de 1,8 m
# ---------------------------------------------------------------------------
bpy.ops.mesh.primitive_plane_add(size=80, location=(-10, -20, 0))
bpy.context.active_object.name = "Ref_Suelo"
bpy.ops.mesh.primitive_cube_add(size=1, location=(-12, -4, 0.9))
human = bpy.context.active_object
human.name = "Ref_Persona_1.8m"
human.scale = (0.5, 0.3, 1.8)

cam_data = bpy.data.cameras.new("Cam")
cam_data.lens = 28
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
cam.location = (-12, -42, 9)
cam.rotation_euler = (math.radians(88), 0, 0)
scene.camera = cam

bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
print("Guardado", BLEND_PATH)

# Preview con Workbench: textura y color de vértice por separado
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 1400
scene.render.resolution_y = 600
shading = scene.display.shading
shading.light = "STUDIO"
for mode, fname in (("TEXTURE", "preview_textura.png"), ("VERTEX", "preview_color_vertice.png")):
    shading.color_type = mode
    scene.render.filepath = os.path.join(PREVIEW_DIR, fname)
    bpy.ops.render.render(write_still=True)
    print("Preview", scene.render.filepath)
