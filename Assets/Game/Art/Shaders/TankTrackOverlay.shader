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
        _TrackHighlight ("Highlight colour", Color) = (1, 0.99, 0.96, 1)

        // x lights a tread link, y darkens the seam between two links.
        _TrackStrength ("Highlight and seam strength", Vector) = (0.14, 0.1, 0, 0)

        // x is how many links span the mask and y how many coarse sweeps span it.
        _TrackParams ("Link repeats, sweep repeats", Vector) = (8, 2.2, 0, 0)

        // x and y are how far the left and right track have travelled, in mask heights.
        _TrackTravel ("Left and right track travel", Vector) = (0, 0, 0, 0)

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

        // Light is added and taken away instead of replacing or multiplying the hull: the art is
        // dark, so a purely multiplicative sheen is invisible, while a replacement would paint a
        // flat band over the plates. The magnitude stays small, so the track stays part of the
        // sprite rather than an overlay.
        Blend One One

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

            #define TRACK_TWO_PI 6.2831853071795865

            // One link per period: a raised plate with a recessed seam at its end. The small
            // per-link variation keeps a long run from looking printed.
            float TrackLinkProfile(float phase)
            {
                float f = frac(phase);
                float plate = smoothstep(0.05, 0.20, f) * (1.0 - smoothstep(0.70, 0.88, f));
                float variation = frac(sin(floor(phase) * 12.9898) * 43758.5453);
                return (plate - 0.45) * (0.78 + 0.44 * variation);
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

                // How many links one screen pixel covers. Past roughly a third of a link per pixel
                // the link pattern cannot be resolved any more and would only shimmer, so its
                // contrast fades out and the coarse sweep alone carries the movement.
                float linksPerPixel = max(fwidth(left * links), 1e-5);
                float resolvable = saturate(1.0 - (linksPerPixel - 0.12) / 0.28);

                float linkDetail =
                    (TrackLinkProfile(left * links) * mask.r + TrackLinkProfile(right * links) * mask.g)
                    * resolvable;
                // The coarse sweep is a smooth wave on purpose: a low frequency survives a distant
                // camera, while hard link edges at that size would read as painted stripes.
                float sweep =
                    sin(left * sweeps * TRACK_TWO_PI) * mask.r + sin(right * sweeps * TRACK_TWO_PI) * mask.g;

                float profile = coverage * (linkDetail * 0.65 + sweep * 0.35);

                float highlight = saturate(profile) * _TrackStrength.x;
                float seam = saturate(-profile) * _TrackStrength.y;

                // Additive blending ignores alpha, so rgb carries the whole effect strength.
                half3 rgb = _TrackHighlight.rgb * highlight - seam;
                return fixed4(rgb, 0);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
