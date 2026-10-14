// Keeps a World of Tanks arena recognisable at map scale and sharp at battle scale.
//
// _MainTex is the arena's global surface image: roads, fields, water and settlements.
// _SelectorTex stores one material index per pixel. _DetailAtlas stores the normalised
// grain of those materials. The grain is sampled in physical world-sized repeats, so a
// close camera does not enlarge the 4096 macro image into blur and a new arena only needs
// another generated recipe, not another shader.
Shader "LocalTanks/WoTGroundDetail"
{
    Properties
    {
        [PerRendererData] _MainTex ("Arena macro surface", 2D) = "white" {}
        _Color ("Renderer tint", Color) = (1, 1, 1, 1)
        _GroundTint ("Ground tint", Color) = (1, 1, 1, 1)
        _DetailAtlas ("Normalised terrain material atlas", 2D) = "gray" {}
        _SelectorTex ("Material index selector", 2D) = "black" {}
        _DetailRepeats ("Detail repeats across the map", Vector) = (25, 25, 0, 0)
        _AtlasLayout ("Columns, rows, padding fraction, inner fraction", Vector) = (3, 2, 0.008, 0.984)
        _MaterialCount ("Material count", Float) = 1
        _DetailStrength ("Detail strength", Range(0, 1)) = 0.55

        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
            "RenderType" = "Opaque"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "False"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One Zero

        Pass
        {
            Name "GroundDetail"
            Tags { "LightMode" = "Universal2D" }

            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment GroundFragment
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            sampler2D _DetailAtlas;
            sampler2D _SelectorTex;
            float4 _GroundTint;
            float4 _DetailRepeats;
            float4 _AtlasLayout;
            float _MaterialCount;
            float _DetailStrength;

            float2 AtlasUv(float material, float2 repeated)
            {
                float column = fmod(material, _AtlasLayout.x);
                float row = floor(material / _AtlasLayout.x);
                float2 inside = _AtlasLayout.zz + repeated * _AtlasLayout.ww;
                return (float2(column, row) + inside) / _AtlasLayout.xy;
            }

            float2 MirrorRepeat(float2 value)
            {
                // Ping-pong sampling guarantees that opposite edges meet even when a source texture was
                // authored for terrain splatting rather than as a perfectly wrapping sprite.
                float2 phase = frac(value * 0.5) * 2.0;
                return 1.0 - abs(phase - 1.0);
            }

            float3 MaterialGrain(float material, float2 groundUv)
            {
                float2 primaryUv = MirrorRepeat(groundUv);
                float2 secondaryUv = MirrorRepeat(
                    float2(-groundUv.y, groundUv.x) * 0.6180339 + float2(0.371, 0.193));
                float3 primary = tex2D(_DetailAtlas, AtlasUv(material, primaryUv)).rgb * 2.0;
                float3 secondary = tex2D(_DetailAtlas, AtlasUv(material, secondaryUv)).rgb * 2.0;
                return clamp(lerp(primary, secondary, 0.27), 0.55, 1.45);
            }

            fixed4 GroundFragment(v2f IN) : SV_Target
            {
                fixed4 macro = SampleSpriteTexture(IN.texcoord) * IN.color * _GroundTint;
                float3 selector = tex2D(_SelectorTex, IN.texcoord).rgb;
                float primaryMaterial = floor(selector.r * 255.0 + 0.5);
                float secondaryMaterial = floor(selector.g * 255.0 + 0.5);
                primaryMaterial = clamp(primaryMaterial, 0.0, max(0.0, _MaterialCount - 1.0));
                secondaryMaterial = clamp(secondaryMaterial, 0.0, max(0.0, _MaterialCount - 1.0));

                float2 groundUv = IN.texcoord * _DetailRepeats.xy;
                float3 primaryGrain = MaterialGrain(primaryMaterial, groundUv);
                float3 secondaryGrain = MaterialGrain(secondaryMaterial, groundUv);

                // The atlas is normalised around 0.5, so the result stays a multiplier around one and
                // cannot wash the roads and fields out of the macro surface. The selector carries an
                // explicit blend instead of randomly alternating materials in screen-visible blocks.
                float3 grain = lerp(primaryGrain, secondaryGrain, selector.b);

                // If a repeat becomes smaller than a few screen pixels, fade it instead of allowing
                // distant terrain to shimmer. At gameplay zoom the footprint is far below this range.
                float footprint = max(fwidth(groundUv.x), fwidth(groundUv.y));
                float readable = 1.0 - smoothstep(0.18, 0.55, footprint);
                float strength = saturate(_DetailStrength) * readable;
                return fixed4(macro.rgb * lerp(float3(1.0, 1.0, 1.0), grain, strength), 1.0);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
