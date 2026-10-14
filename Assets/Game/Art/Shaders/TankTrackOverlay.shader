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
        _TrackStrength ("Lighten and darken strength", Vector) = (0.32, 0.5, 0, 0)

        // x is how many links span the mask and y how many coarse sweeps span it.
        _TrackParams ("Link repeats, sweep repeats", Vector) = (8, 5.5, 0, 0)

        // x and y are how far the left and right track have travelled, in mask heights.
        _TrackTravel ("Left and right track travel", Vector) = (0, 0, 0, 0)

        // x/y are the visible motion amount of the left and right track; z is the distant-pattern
        // gain. A stopped machine leaves its original painted tracks untouched.
        _TrackMotion ("Left motion, right motion, distant gain", Vector) = (0, 0, 0.62, 0)

        // Centres of the red and green mask bands, followed by their half-width, in mask UV.
        _TrackGeometry ("Red centre, green centre, half width", Vector) = (0.85, 0.15, 0.08, 0)

        // The hull sprite and the rectangle it occupies inside its own texture. Reading it lets the
        // highlight land on what is already painted instead of adding a flat sheen over it.
        _HullTex ("Hull sprite texture", 2D) = "white" {}
        _HullRect ("Hull rectangle in that texture", Vector) = (0, 0, 1, 1)

        // x is how much of the darkening survives on the darkest art and z is the luma gain of the
        // hull response. The remaining channels are reserved.
        _HullLighting ("Darkening floor, unused, luma gain, reserved", Vector) = (0.5, 0, 1.5, 0)

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
            float4 _TrackMotion;
            float4 _TrackGeometry;

            sampler2D _HullTex;
            float4 _HullRect;
            float4 _HullLighting;

            // One link per period. Most of a plate contributes nothing: only its shallow leading ridge,
            // the dark joint behind it and a trace of uneven wear are re-shaded. Lighting the whole plate
            // made the former effect read as broad bars sliding over a fender rather than steel links.
            float TrackLinkProfile(float phase, float seed)
            {
                float link = floor(phase);
                float noise = frac(sin((link + seed) * 12.9898) * 43758.5453);
                float wear = frac(sin(link * 0.13 + seed) * 78.233);
                float f = frac(phase - (noise - 0.5) * 0.06);
                float ridge = smoothstep(0.04, 0.1, f) * (1.0 - smoothstep(0.2, 0.3, f));
                float joint = smoothstep(0.79, 0.85, f) * (1.0 - smoothstep(0.96, 1.01, f));
                float faceWear = (wear - 0.5) * 0.1 *
                                 smoothstep(0.28, 0.38, f) * (1.0 - smoothstep(0.68, 0.78, f));
                return ridge * (0.42 + 0.12 * noise) - joint * (0.78 + 0.2 * noise) + faceWear;
            }

            // Distant links cannot be resolved individually. A short irregular pair of ridge and joint
            // survives instead of the old two enormous bands that flashed along the whole running gear.
            float TrackClusterProfile(float phase, float seed)
            {
                float cluster = floor(phase);
                float noise = frac(sin((cluster + seed) * 19.193) * 15731.743);
                float f = frac(phase - (noise - 0.5) * 0.09);
                float ridge = smoothstep(0.08, 0.18, f) * (1.0 - smoothstep(0.3, 0.4, f));
                float joint = smoothstep(0.55, 0.64, f) * (1.0 - smoothstep(0.78, 0.88, f));
                return ridge * 0.3 - joint * (0.34 + 0.08 * noise);
            }

            // A real link is not a uniformly lit bar across the whole belt. Its central plate and both
            // grouser edges catch slightly different light; the measured band centre/width lets every
            // machine use that shape without guessing from screen coordinates.
            float TrackLateralShape(float u, float centre, float halfWidth)
            {
                float across = saturate(abs(u - centre) / max(halfWidth, 1e-4));
                float centrePlate = 1.0 - smoothstep(0.08, 0.42, across);
                float grouserEdge = smoothstep(0.58, 0.78, across) * (1.0 - smoothstep(0.9, 1.0, across));
                return 0.5 + centrePlate * 0.2 + grouserEdge * 0.38;
            }

            fixed4 TrackOverlayFragment(v2f IN) : SV_Target
            {
                fixed4 mask = SampleSpriteTexture(IN.texcoord);

                // The sprite colour carries team, visibility and wreck tinting; the flat channels
                // of the mask carry the sides, its alpha the coverage and its blue the lateral
                // weight, so a band fades out towards its own silhouette instead of ending in a
                // straight edge.
                float tint = IN.color.a * max(IN.color.r, max(IN.color.g, IN.color.b));
                float sideMotion = saturate(mask.r * _TrackMotion.x + mask.g * _TrackMotion.y);
                float coverage = mask.a * mask.b * tint * sideMotion;

                // Travel is measured in mask heights, so the same value drives both scales and the
                // machine's own speed decides the rate of each.
                float left = IN.texcoord.y + _TrackTravel.x;
                float right = IN.texcoord.y + _TrackTravel.y;

                float links = _TrackParams.x;
                float sweeps = _TrackParams.y;

                // How many links one screen pixel covers. A link smaller than a couple of pixels
                // cannot be drawn as a link any more, so the fine pattern fades out and the coarse one
                // takes over. Both are the same segmented pattern, so a distant machine keeps reading
                // as a track instead of as a smooth glow, and the movement stays trackable: a band
                // that changes brightness without moving is what looks plastic.
                float linksPerPixel = max(fwidth(left * links), 1e-5);
                float resolvable = saturate(1.0 - (linksPerPixel - 0.25) / 0.5);
                float coarseShare = (1.0 - resolvable) * _TrackMotion.z;

                float redShape = TrackLateralShape(IN.texcoord.x, _TrackGeometry.x, _TrackGeometry.z);
                float greenShape = TrackLateralShape(IN.texcoord.x, _TrackGeometry.y, _TrackGeometry.z);

                float fine = TrackLinkProfile(left * links, 0.0) * mask.r * redShape * _TrackMotion.x
                             + TrackLinkProfile(right * links, 17.0) * mask.g * greenShape * _TrackMotion.y;
                float coarse = TrackClusterProfile(left * sweeps, 3.0) * mask.r * redShape * _TrackMotion.x
                               + TrackClusterProfile(right * sweeps, 29.0) * mask.g * greenShape * _TrackMotion.y;
                float detail = fine * resolvable + coarse * coarseShare;

                // The mask sprite lines up with the hull rectangle of the source texture, so the
                // hull's own pixels are one fetch away. Art that is already dark cannot be darkened
                // much further, so the gaps lose depth there and keep it on bright plates.
                float3 hullRgb = tex2D(_HullTex, _HullRect.xy + IN.texcoord * _HullRect.zw).rgb;
                float hullLuma = saturate(dot(hullRgb, float3(0.299, 0.587, 0.114)) * _HullLighting.z);
                float darkening = lerp(_HullLighting.x, 1.0, hullLuma);

                float profile = coverage * detail;
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
