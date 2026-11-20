# Conversación: shooter estilo PSX en Unity

Resumen de la sesión con Claude Code (17/11/2026), para tenerla en local.

---

## 1. Petición inicial
> Quiero hacer un shooter con estilo PSX. Quiero aprender las bases, el texturizado, el modelado,
> el postprocesado y los shaders necesarios, todo en Unity.

**Qué se creó:**

| Archivo | Qué hace |
|---|---|
| `Assets/Shaders/PSX_Lit.shader` | Shader URP para objetos: vertex snapping, mapeado afín, luz por vértice, filtrado Point, niebla por vértice, colores de vértice y alpha test. |
| `Assets/Shaders/PSX_PostProcess.shader` | Efecto a pantalla completa: pixelado, dithering Bayer 4x4 y reducción a 32 niveles de color. Se monta con el *Full Screen Pass Renderer Feature* de URP. |
| `Assets/Scripts/FPSController.cs` | Movimiento en primera persona: andar, correr, saltar y balanceo de cámara (Input System nuevo). |
| `Assets/Scripts/HitscanWeapon.cs` | Arma de disparo instantáneo con fogonazo, retroceso, impactos y sonido. |
| `Assets/Scripts/Health.cs` | Sistema de vida genérico. |
| `Assets/Scripts/SimpleEnemy.cs` | Enemigo básico que persigue y ataca. |
| `GUIA.md` | Ruta de aprendizaje en 7 fases (estilo, proyecto URP, modelado, texturizado, shaders, postprocesado, gameplay y audio), con ejercicios. |

**Para empezar:** crea un proyecto *Universal 3D (URP)* en Unity 6, copia `Assets/` dentro
y sigue la Fase 1 de la guía.

Nota: el código no se probó dentro de Unity (no estaba disponible en la sesión).
Si un shader da error, revisa el mensaje de la consola.

---

## 2. Referencias visuales
> Añade referencias visuales a la guía para facilitarme su comprensión.

Se añadieron 13 imágenes en `img/` (2 animadas), insertadas en su sección de `GUIA.md`:

- `01_moderno_vs_psx.png` — la misma escena dibujada de forma moderna y estilo PSX
- `02_mapeado_afin.png` y `02b_afin_movimiento.gif` — por qué las texturas se doblan y por qué subdividir suelos
- `03_vertex_snapping.png` y `03b_snapping_movimiento.gif` — vértices que saltan de píxel en píxel
- `04_filtrado.png` — bilineal frente a Point
- `05_color_dithering.png` — niveles de color, dithering y matriz de Bayer
- `06_proceso_textura.png` — de foto a textura PSX (64x64, 16 colores)
- `07_poligonos.png` — cilindros de 6, 8, 12 y 32 lados
- `08_luz_por_vertice.png` — luz por píxel frente a luz por vértice
- `09_pipeline.png` — recorrido de un vértice y orden de los pasos en cada frame
- `10_niebla.png` — sin niebla y con niebla
- `11_postprocesado_pasos.png` — el postprocesado paso a paso

Las genera `img/generar_imagenes.py`, un rasterizador en Python que aplica las mismas técnicas
que los shaders. Para regenerarlas (necesita `numpy` y `pillow`):

```bash
pip install numpy pillow
python3 img/generar_imagenes.py img
```

---

## 3. Montarlo en `C:\Users\mkmik\VHS`
La sesión corría en la nube, sin acceso al PC, así que se entregó todo en `PSXShooter_kit.zip`.

**Pasos para copiarlo a mano:**
1. Descomprime el zip.
2. Copia `Assets/Scripts` y `Assets/Shaders` dentro de `C:\Users\mkmik\VHS\Assets\`.
3. Copia `GUIA.md`, `CONVERSACION.md` y la carpeta `img` en `C:\Users\mkmik\VHS\` (fuera de `Assets`).
4. Abre el proyecto en Unity y sigue la Fase 1 de la guía.

**Comprobaciones:**
- El proyecto debe usar **URP** (Package Manager → *Universal RP*). Con Built-in los shaders salen en rosa.
- Los scripts usan el **Input System nuevo**. Si el proyecto usa el antiguo:
  `Edit > Project Settings > Player > Active Input Handling` → *Both*.

**Siguientes pasos propuestos:** HUD con munición, enemigos con NavMesh que disparen,
o el shader PSX hecho también en Shader Graph.
