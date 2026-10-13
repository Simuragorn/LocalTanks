// Draws a moving tread highlight inside the track regions of a tank hull sprite.
//
// The sprite of this renderer is the vehicle's track mask, rendered by the sprite pipeline into
// one texture where the red channel marks the left track and the green channel the right one.
// Only the highlight is drawn: the hull keeps its own material, its own colour and its own
// sprite, so visibility fading, wreck tinting and turret layering continue to work unchanged.
//
// One material serves every machine. The per-tank values (tread repeats, scroll offsets) arrive
// in a MaterialPropertyBlock, so no tank needs a material instance of its own.
Shader "LocalTanks/TankTrackOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Track mask (red left, green right, blue lateral weight)", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _TrackHighlight ("Highlight tint", Color) = (1, 1, 1, 1)

        // x lightens a tread plate, y darkens the gap between two links. Both are multipliers around
        // one, so 0.2 means twenty percent brighter and 0.3 thirty percent darker.
        _TrackStrength ("Lighten and darken strength", Vector) = (0.35, 0.55, 0, 0)

        // x is how many links span the mask and y how many coarse sweeps span it.
        _TrackParams ("Link repeats, sweep repeats", Vector) = (8, 2.2, 0, 0)

        // x and y are how far the left and right track have travelled, in mask heights.
        _TrackTravel ("Left and right track travel", Vector) = (0, 0, 0, 0)

        // The hull sprite and the rectangle it occupies inside its own texture. Reading it lets the
        // highlight land on what is already painted instead of adding a flat sheen over it.
        _HullTex ("Hull sprite texture", 2D) = "white" {}
        _HullRect ("Hull rectangle in that texture", Vector) = (0, 0, 1, 1)

        // x is how much of the darkening survives on the darkest art, z is the luma gain of the hull
        // response and w is the weight of the coarse glint against the fine links.
        _HullLighting ("Darkening floor, unused, luma gain, glint weight", Vector) = (0.5, 0, 1.5, 0.22)

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
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off

        // The overlay re-shades the surface it sits on instead of adding to it: it samples the hull
        // sprite, multiplies that pixel by a value around one and writes the result back. This is
        // the only form that both lightens the plate and darkens the gap between two links, because
        // an eight bit render target clamps a blend source to zero and one after the blend, so a
        // negative additive term and a multiplier above one are both silently lost. Re-shading also
        // keeps the effect proportional to whatever is painted.
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TrackOverlay"
            Tags { "LightMode" = "Universal2D" }

            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment TrackOverlayFragment
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float4 _TrackHighlight;
            float4 _TrackStrength;
            float4 _TrackParams;
            float4 _TrackTravel;

            sampler2D _HullTex;
            float4 _HullRect;
            float4 _HullLighting;

            #define TRACK_TWO_PI 6.2831853071795865

            // One link per period: a plate with a thin gap at its end, centred on zero so the same
            // term both lightens the plate and darkens the gap. The gap shifts and deepens unevenly
            // from link to link and a slow wear term runs along the whole track, so a long run does
            // not look printed.
            float TrackLinkProfile(float phase, float seed)
            {
                float link = floor(phase);
                float noise = frac(sin((link + seed) * 12.9898) * 43758.5453);
                float wear = frac(sin(link * 0.13 + seed) * 78.233);
                float f = frac(phase) - (noise - 0.5) * 0.06;
                float gap = smoothstep(0.78, 0.88, f) * (1.0 - smoothstep(0.94, 1.04, f));
                return ((1.0 - gap) * 1.7 - 1.0) * (0.78 + 0.36 * noise) * (0.9 + 0.16 * wear);
            }

            // A narrow glint instead of a broad glow: metal catches a thin line of light, while a
            // wide smooth band is exactly what reads as plastic.
            float TrackGlint(float phase)
            {
                float wave = saturate(sin(phase * TRACK_TWO_PI));
                return wave * wave * wave * 1.6 - 0.34;
            }

            fixed4 TrackOverlayFragment(v2f IN) : SV_Target
            {
                fixed4 mask = SampleSpriteTexture(IN.texcoord);

                // The sprite colour carries team, visibility and wreck tinting; the flat channels
                // of the mask carry the sides, its alpha the coverage and its blue the lateral
                // weight, so a band fades out towards its own silhouette instead of ending in a
                // straight edge.
                float tint = IN.color.a * max(IN.color.r, max(IN.color.g, IN.color.b));
                float coverage = mask.a * mask.b * tint;

                // Travel is measured in mask heights, so the same value drives both scales and the
                // machine's own speed decides the rate of each.
                float left = IN.texcoord.y + _TrackTravel.x;
                float right = IN.texcoord.y + _TrackTravel.y;

                float links = _TrackParams.x;
                float sweeps = _TrackParams.y;

                // How many links one screen pixel covers. A link smaller than a couple of pixels
                // cannot be drawn as a link any more, so its contrast fades out. The window is wide on
                // purpose: at the battle zoom a quarter of the link contrast still reads as track
                // texture, and cutting it off there left only the smooth glint, which is precisely
                // what looks like plastic.
                float linksPerPixel = max(fwidth(left * links), 1e-5);
                float resolvable = saturate(1.0 - (linksPerPixel - 0.25) / 0.5);

                float leftDetail = TrackLinkProfile(left * links, 0.0) * mask.r;
                float rightDetail = TrackLinkProfile(right * links, 17.0) * mask.g;
                float detail = (leftDetail + rightDetail) * resolvable;
                float glint = TrackGlint(left * sweeps) * mask.r + TrackGlint(right * sweeps) * mask.g;

                // The mask sprite lines up with the hull rectangle of the source texture, so the
                // hull's own pixels are one fetch away. Art that is already dark cannot be darkened
                // much further, so the gaps lose depth there and keep it on bright plates.
                float3 hullRgb = tex2D(_HullTex, _HullRect.xy + IN.texcoord * _HullRect.zw).rgb;
                float hullLuma = saturate(dot(hullRgb, float3(0.299, 0.587, 0.114)) * _HullLighting.z);
                float darkening = lerp(_HullLighting.x, 1.0, hullLuma);

                float profile = coverage *
                    (detail * (1.0 - _HullLighting.w) + glint * _HullLighting.w);
                float modulation = profile >= 0.0
                    ? profile * _TrackStrength.x
                    : profile * _TrackStrength.y * darkening;

                // Only the light carries the highlight tint: a gap between links is a shadow and
                // must not tint the surface. The sprite tint of this renderer is applied as well,
                // so a wreck, a faded invisible machine and a team colour all survive.
                float3 tinted = modulation >= 0.0 ? modulation * _TrackHighlight.rgb : modulation;
                float3 shaded = hullRgb * (1.0 + tinted) * IN.color.rgb;
                return fixed4(shaded, coverage * IN.color.a);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
