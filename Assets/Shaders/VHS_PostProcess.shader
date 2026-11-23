// =====================================================================
//  PSX/VHS  —  Efecto de cinta VHS con encuadre CUADRADO (1:1), para URP
// =====================================================================
//  Se usa con un "Full Screen Pass Renderer Feature" colocado DESPUÉS del
//  de PSX PostProcess (Injection Point = After Rendering Post Processing,
//  Requirements = Color). El menú  PSX > Montar efecto VHS (tecla E)
//  lo configura solo.
//
//  Se enciende/apaga desde C# (VHSToggle.cs) con dos variables GLOBALES
//  que no están en Properties a propósito (así no se ensucia el material):
//    _VHSAmount  0 = apagado, 1 = encendido (fundido intermedio)
//    _VHSGlitch  pico de interferencias al pulsar la tecla
//
//  Qué hace:
//   1. Encuadre cuadrado centrado con bandas negras a los lados.
//   2. Resolución baja en líneas (por defecto 240x240, píxeles cuadrados).
//   3. Temblor por línea, ondulación y banda de tracking que baja.
//   4. Ruido de "cambio de cabezal" en la parte de abajo.
//   5. Color en YIQ: luma algo borrosa, croma muy borroso y desplazado.
//   6. Ruido, dropouts (rayas blancas), scanlines, viñeta y punto REC.
// =====================================================================
Shader "PSX/VHS"
{
    Properties
    {
        _SquareRes ("Resolucion del cuadrado (lineas)", Range(64, 720)) = 240
        _Jitter ("Temblor por linea (px)", Range(0, 3)) = 0.6
        _Wobble ("Ondulacion", Range(0, 3)) = 1
        _Tracking ("Banda de tracking", Range(0, 2)) = 0.6
        _TrackingSpeed ("Velocidad banda", Range(0, 1)) = 0.12
        _TrackingWidth ("Grosor banda", Range(0.01, 0.3)) = 0.06
        _HeadSwitch ("Ruido de cabezal (abajo)", Range(0, 1)) = 0.6
        _ChromaShift ("Desplazamiento croma (px)", Range(-4, 4)) = 1.5
        _ChromaBlur ("Desenfoque croma (px)", Range(0, 4)) = 1.5
        _Saturation ("Saturacion", Range(0, 2)) = 0.85
        _Noise ("Ruido", Range(0, 0.3)) = 0.06
        _Dropouts ("Dropouts (rayas blancas)", Range(0, 1)) = 0.5
        _Scanlines ("Scanlines", Range(0, 1)) = 0.35
        _Vignette ("Vineta", Range(0, 2)) = 0.8
        _RecDot ("Punto REC", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "VHS"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _SquareRes, _Jitter, _Wobble;
            float _Tracking, _TrackingSpeed, _TrackingWidth, _HeadSwitch;
            float _ChromaShift, _ChromaBlur, _Saturation;
            float _Noise, _Dropouts, _Scanlines, _Vignette, _RecDot;

            // Globales (las pone VHSToggle.cs)
            float _VHSAmount;
            float _VHSGlitch;

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            // Lee la imagen de la cámara en coordenadas del CUADRADO (0..1),
            // devuelve el color en gamma (sRGB), que es donde trabajamos.
            half3 Fetch(float2 sq, float2 scale)
            {
                sq = saturate(sq);                       // no leer fuera del cuadrado
                float2 uv = (sq - 0.5) / scale + 0.5;    // cuadrado -> pantalla
                half3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                #if !UNITY_COLORSPACE_GAMMA
                c = LinearToSRGB(c);
                #endif
                return c;
            }

            float  RGBtoY (float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
            float2 RGBtoIQ(float3 c) { return float2(dot(c, float3(0.596, -0.274, -0.322)),
                                                     dot(c, float3(0.211, -0.523,  0.312))); }
            float3 YIQtoRGB(float y, float2 iq)
            {
                return float3(y + 0.956 * iq.x + 0.621 * iq.y,
                              y - 0.272 * iq.x - 0.647 * iq.y,
                              y - 1.106 * iq.x + 1.703 * iq.y);
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half3 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;

                float amount = saturate(_VHSAmount);
                float glitch = saturate(_VHSGlitch);
                if (amount <= 0.0001)
                    return half4(src, 1);

                // ---- 1. Encuadre cuadrado --------------------------------
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 scale = aspect >= 1.0 ? float2(aspect, 1.0) : float2(1.0, 1.0 / aspect);
                float2 sq = (uv - 0.5) * scale + 0.5;    // 0..1 dentro del cuadrado
                float inside = step(0.0, sq.x) * step(sq.x, 1.0) * step(0.0, sq.y) * step(sq.y, 1.0);

                // ---- 2. Líneas a baja resolución -------------------------
                float res  = _SquareRes;
                float px   = 1.0 / res;
                float t    = floor(_Time.y * 30.0) / 30.0;   // el ruido va a 30 fps
                float row = floor(sq.y * res);
                float rowY = (row + 0.5) * px;

                // ---- 3. Distorsiones horizontales ------------------------
                float jit  = (Hash(float2(row, t)) - 0.5) * _Jitter * px;
                float wave = sin(sq.y * 7.0 + _Time.y * 1.3) * 0.0015 * _Wobble
                           + sin(sq.y * 31.0 - _Time.y * 4.0) * 0.0004 * _Wobble;

                float bandPos = frac(-_Time.y * _TrackingSpeed);
                float bd = abs(sq.y - bandPos); bd = min(bd, 1.0 - bd);
                float band = smoothstep(_TrackingWidth, 0.0, bd) * saturate(_Tracking + glitch * 2.0);
                float bandOff = band * (Hash(float2(row * 0.37, t)) - 0.3) * 0.03;

                // ---- 4. Cambio de cabezal (franja inferior) -------------
                float head = smoothstep(0.04, 0.0, sq.y) * _HeadSwitch;
                float headOff = head * (0.02 + Hash(float2(row, t + 1.0)) * 0.03);

                // Al encender/apagar: bloques de líneas que se desplazan
                float blk = floor(sq.y * res / 6.0);
                float glitchOff = glitch * (Hash(float2(blk, t)) - 0.5) * 0.12
                                * step(0.55, Hash(float2(blk, t + 3.0)));

                float2 p = float2(sq.x + jit + wave + bandOff + headOff + glitchOff, rowY);

                // ---- 5. Color VHS: luma y croma con distinto ancho de banda
                float Y = RGBtoY(Fetch(p + float2(-px, 0), scale)) * 0.25
                        + RGBtoY(Fetch(p,                  scale)) * 0.50
                        + RGBtoY(Fetch(p + float2( px, 0), scale)) * 0.25;

                float2 IQ = 0;
                float shift = _ChromaShift + glitch * 4.0;
                [unroll] for (int i = -2; i <= 2; i++)
                    IQ += RGBtoIQ(Fetch(p + float2((i * _ChromaBlur + shift) * px, 0), scale));
                IQ *= _Saturation / 5.0;

                float3 col = YIQtoRGB(Y, IQ);
                col = col * 0.9 + 0.035;                  // negros levantados, blancos suaves
                col *= float3(1.02, 1.0, 0.94);           // tono cálido de cinta vieja

                // ---- 6. Ruido, rayas, scanlines, viñeta -----------------
                float cellX = floor(sq.x * res);
                col += (Hash(float2(cellX, row) + t * 61.0) - 0.5) * (_Noise + glitch * 0.2);

                float streak = step(0.75, Hash(float2(floor(sq.x * res * 0.25), row + t * 97.0)));
                col += band * streak * 0.45;

                float drop = step(1.0 - 0.0025 * (_Dropouts + glitch * 6.0), Hash(float2(row, t * 7.13)))
                           * step(0.4, Hash(float2(floor(sq.x * 12.0), row + t)));
                col = lerp(col, 0.95, drop);

                col = lerp(col, Hash(float2(cellX, row + t * 13.0)).xxx * 0.8, head * 0.6);

                float s = frac(sq.y * res);
                col *= lerp(1.0, 0.55 + 0.45 * sin(s * PI), _Scanlines);

                float2 v = sq - 0.5;
                col *= saturate(1.0 - dot(v, v) * _Vignette * 1.6);

                // Punto REC parpadeante (arriba a la izquierda del cuadrado)
                float2 recP = float2(sq.x, rowY) - float2(0.08, 0.91);
                float rec = step(length(recP), 0.022) * step(0.5, frac(_Time.y * 0.8)) * _RecDot;
                col = lerp(col, float3(0.95, 0.08, 0.06), rec);

                col = saturate(col);
                #if !UNITY_COLORSPACE_GAMMA
                col = SRGBToLinear(col);
                #endif

                float3 framed = col * inside;             // negro fuera del cuadrado
                return half4(lerp(src, framed, amount), 1);
            }
            ENDHLSL
        }
    }
}
