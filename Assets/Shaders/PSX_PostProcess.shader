// =====================================================================
//  PSX/PostProcess  —  Efecto de pantalla completa para URP
// =====================================================================
//  Se usa con el "Full Screen Pass Renderer Feature" de URP
//  (Universal Renderer Data > Add Renderer Feature > Full Screen Pass).
//  En ese feature pon: Pass Material = un material con este shader,
//  Injection Point = After Rendering Post Processing,
//  Requirements = Color.
//
//  Hace tres cosas:
//   1. PIXELADO: baja la resolución efectiva (la PS1 solía ir a 320x240).
//   2. DITHERING: patrón de Bayer 4x4, como el que aplicaba la GPU de PS1
//      para disimular las bandas de color.
//   3. PROFUNDIDAD DE COLOR: la PS1 usaba 15 bits = 32 niveles por canal.
// =====================================================================
Shader "PSX/PostProcess"
{
    Properties
    {
        _Resolution ("Resolucion virtual (x,y)", Vector) = (320, 240, 0, 0)
        _ColorLevels ("Niveles de color por canal", Range(2, 256)) = 32
        _DitherStrength ("Intensidad del dithering", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "PSXPost"

            HLSLPROGRAM
            #pragma vertex Vert      // viene de Blit.hlsl (triángulo de pantalla completa)
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            // Blit.hlsl declara _BlitTexture (la imagen de la cámara) y Vert()
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _Resolution;
            float  _ColorLevels;
            float  _DitherStrength;
            // Global: 1 mientras renderiza la cámara del inventario 3D (no se le aplica el efecto)
            float  _PSXPostBypass;

            // Matriz de Bayer 4x4 (valores 0..15)
            static const float kBayer4x4[16] =
            {
                 0,  8,  2, 10,
                12,  4, 14,  6,
                 3, 11,  1,  9,
                15,  7, 13,  5
            };

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv  = input.texcoord;
                if (_PSXPostBypass > 0.5)
                    return half4(SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv).rgb, 1);
                float2 res = _Resolution.xy;

                // ---- 1. Pixelado ----------------------------------------
                // Coordenada del "píxel gordo" y muestreo en su centro.
                float2 pixel = floor(uv * res);
                float2 pixUV = (pixel + 0.5) / res;
                half3 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, pixUV).rgb;

                // Cuantizamos en espacio gamma (sRGB), que es como la
                // PS1 guardaba el color; en lineal las bandas quedan mal.
                #if !UNITY_COLORSPACE_GAMMA
                col = LinearToSRGB(col);
                #endif

                // ---- 2. Dithering ---------------------------------------
                int2 p = int2(pixel) & 3;               // posición dentro del patrón 4x4
                float threshold = kBayer4x4[p.y * 4 + p.x] / 16.0 - 0.5; // [-0.5, 0.5)
                float steps = _ColorLevels - 1.0;
                col += threshold * _DitherStrength / steps;

                // ---- 3. Reducción de color ------------------------------
                col = floor(saturate(col) * steps + 0.5) / steps;

                #if !UNITY_COLORSPACE_GAMMA
                col = SRGBToLinear(col);
                #endif

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
