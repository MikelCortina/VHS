// =====================================================================
//  PSX/Lit  —  Shader para URP que imita el hardware de PlayStation 1
// =====================================================================
//  Qué hace y por qué (cada punto es una limitación real de la PS1):
//
//  1. VERTEX SNAPPING (vértices que "tiemblan")
//     La GTE de la PS1 calculaba las posiciones en pantalla con números
//     enteros, sin decimales. Los vértices saltaban de píxel en píxel.
//     Lo imitamos redondeando la posición en pantalla a una rejilla.
//
//  2. AFFINE TEXTURE MAPPING (texturas que se "doblan")
//     La PS1 no corregía la perspectiva al interpolar UVs. Las texturas
//     se deforman al mirar los polígonos de lado. Lo imitamos
//     multiplicando la UV por w en el vértice y dividiendo en el píxel.
//
//  3. ILUMINACIÓN POR VÉRTICE (Gouraud)
//     La luz se calculaba solo en los vértices y se interpolaba.
//     Por eso los modelos low-poly tienen esa luz "a manchas".
//
//  4. FILTRADO POINT (sin suavizado)
//     La PS1 no tenía filtrado bilineal: píxeles nítidos y gordos.
//
//  5. NIEBLA POR VÉRTICE
//     Se usaba para ocultar el poco alcance de dibujado (Silent Hill).
//
//  6. EMISIÓN
//     Para lámparas y pantallas: brillan sin depender de la luz y la
//     niebla las tapa menos. La luz que proyectan la pone PSXLampLight.cs.
//
//  Requisitos: URP (Unity 2022.3 / Unity 6). Usa el Rendering Path
//  "Forward" en tu Universal Renderer Data para las luces adicionales.
// =====================================================================
Shader "PSX/Lit"
{
    Properties
    {
        _BaseMap ("Textura", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)

        [Header(PSX)]
        _SnapResolution ("Resolucion del snapping (x,y)", Vector) = (320, 240, 0, 0)
        _SnapStrength ("Intensidad del snapping", Range(0,1)) = 1
        _AffineStrength ("Deformacion afin", Range(0,1)) = 1
        [Toggle] _UseVertexColor ("Usar color de vertice", Float) = 1

        [Header(Emision)]
        [NoScaleOffset] _EmissionMap ("Mapa de emision (blanco = brilla)", 2D) = "white" {}
        [HDR] _EmissionColor ("Color de emision", Color) = (0,0,0,1)
        _EmissionFogResist ("Resistencia a la niebla", Range(0,1)) = 0.7

        [Header(Transparencia recortada)]
        [Toggle(_ALPHATEST_ON)] _AlphaTest ("Alpha Test (recorte)", Float) = 0
        _Cutoff ("Umbral de recorte", Range(0,1)) = 0.5

        [Header(Render)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // ------------------------------------------------------------
        //  Pase principal
        // ------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            TEXTURE2D(_EmissionMap);
            // sampler_PointRepeat ya lo declara URP (GlobalSamplers.hlsl):
            // filtrado Point + Repeat, da igual cómo esté importada la textura.

            // Todo lo del material dentro del CBUFFER -> compatible con SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                float4 _SnapResolution;
                half   _SnapStrength;
                half   _AffineStrength;
                half   _UseVertexColor;
                half   _Cutoff;
                half   _AlphaTest;
                half   _Cull;
                half4  _EmissionColor;
                half   _EmissionFogResist;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 affineUV   : TEXCOORD0; // xy = uv * w, z = w
                float2 perspUV    : TEXCOORD1; // uv normal (corregida)
                half3  lighting   : TEXCOORD2; // luz calculada en el vértice
                half4  color      : TEXCOORD3;
                half   fog        : TEXCOORD4;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;

                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                float4 cs = pos.positionCS;

                // ---- 1. Vertex snapping ---------------------------------
                // cs.xy / cs.w = coordenadas de pantalla en rango [-1, 1].
                // Las multiplicamos por media resolución (un píxel mide 2/res),
                // redondeamos, y deshacemos la operación.
                float2 halfRes = _SnapResolution.xy * 0.5;
                float2 ndc     = cs.xy / cs.w;
                float2 snapped = round(ndc * halfRes) / halfRes;
                cs.xy = lerp(ndc, snapped, _SnapStrength) * cs.w;
                o.positionCS = cs;

                // ---- 2. UVs para el mapeado afín ------------------------
                float2 uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.perspUV  = uv;
                // La GPU interpola con corrección de perspectiva (divide por w).
                // Si multiplicamos por w aquí y luego dividimos en el fragment,
                // esa corrección se cancela -> interpolación lineal en pantalla.
                o.affineUV = float3(uv * cs.w, cs.w);

                // ---- 3. Iluminación por vértice -------------------------
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                Light mainLight = GetMainLight();
                half3 light = mainLight.color * saturate(dot(normalWS, mainLight.direction));
                light += SampleSH(normalWS); // luz ambiental (Lighting > Environment)

                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; i++)
                {
                    Light l = GetAdditionalLight(i, pos.positionWS);
                    light += l.color * l.distanceAttenuation * saturate(dot(normalWS, l.direction));
                }
                #endif
                o.lighting = light;

                o.color = lerp(half4(1,1,1,1), v.color, _UseVertexColor);

                // ---- 5. Niebla por vértice ------------------------------
                o.fog = ComputeFogFactor(cs.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 affine = i.affineUV.xy / i.affineUV.z;
                float2 uv = lerp(i.perspUV, affine, _AffineStrength);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_PointRepeat, uv) * _BaseColor * i.color;

                #if defined(_ALPHATEST_ON)
                clip(tex.a - _Cutoff);
                #endif

                half3 col = tex.rgb * i.lighting;

                // ---- 6. Emisión (lámparas, pantallas...) ------------------
                // Se suma después de la luz: brilla aunque no le dé ninguna luz.
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_PointRepeat, uv).rgb * _EmissionColor.rgb;
                col += emission;

                // La niebla se come menos las partes que brillan (como las
                // farolas de Silent Hill, que se ven a lo lejos entre la niebla).
                half3 fogged = MixFog(col, i.fog);
                half glow = saturate(max(emission.r, max(emission.g, emission.b)));
                col = lerp(fogged, col, glow * _EmissionFogResist);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------
        //  DepthOnly: necesario para que el objeto aparezca en la
        //  textura de profundidad (lo usan algunos efectos de URP).
        // ------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                float4 _SnapResolution;
                half   _SnapStrength;
                half   _AffineStrength;
                half   _UseVertexColor;
                half   _Cutoff;
                half   _AlphaTest;
                half   _Cull;
                half4  _EmissionColor;
                half   _EmissionFogResist;
            CBUFFER_END

            float4 vert (float4 positionOS : POSITION) : SV_POSITION
            {
                float4 cs = TransformObjectToHClip(positionOS.xyz);
                float2 halfRes = _SnapResolution.xy * 0.5;
                float2 ndc = cs.xy / cs.w;
                cs.xy = lerp(ndc, round(ndc * halfRes) / halfRes, _SnapStrength) * cs.w;
                return cs;
            }

            half frag () : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
