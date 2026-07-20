// CurvedHudTest.hlsl
// Minimal DX12 pixel shader for testing a curved virtual-window texture.
//
// Assumptions:
//   t0 = HUD texture
//   s0 = linear clamp sampler
//   b0 = optional curvature constants
//   TEXCOORD0 = UV coordinates
//   COLOR0 = premultiplied vertex tint
//
// You may need to rename semantics/registers to match VRage's stock UI shader.

Texture2D HudTexture : register(t0);
SamplerState HudSampler : register(s0);

cbuffer CurvedHudConstants : register(b0)
{
    float2 Curvature;       // Suggested: float2(-0.053333, -0.016667)
    float2 OpticalCenter;   // Suggested: float2(0.5, 0.5)

    float EdgeFade;         // Suggested: 0.025
    float Brightness;       // Suggested: 1.0
    float Aberration;       // Suggested: 0.0015
    float Opacity;          // Suggested: 1.0

    float2 HeadOffset;      // Suggested: float2(0.0, 0.0)
    float2 Padding;
};

struct PixelInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color    : COLOR0;
};

float Inside01(float2 uv)
{
    return step(0.0, uv.x)
         * step(0.0, uv.y)
         * step(uv.x, 1.0)
         * step(uv.y, 1.0);
}

float4 PSMain(PixelInput input) : SV_Target
{
    float2 centered = (input.TexCoord - OpticalCenter) * 2.0;

    // Cylindrical/spherical hybrid curvature.
    float2 warped = centered;
    warped.y *= 1.0 + Curvature.x * centered.x * centered.x;
    warped.x *= 1.0 + Curvature.y * centered.y * centered.y;

    float2 uv = warped * 0.5 + OpticalCenter + HeadOffset;

    float valid = Inside01(uv);
    if (valid <= 0.0)
        discard;

    // Small radial chromatic separation.
    float2 radial = normalize(centered + float2(1e-5, 1e-5));
    float2 chroma = radial * Aberration;

    float red   = HudTexture.Sample(HudSampler, uv + chroma).r;
    float green = HudTexture.Sample(HudSampler, uv).g;
    float blue  = HudTexture.Sample(HudSampler, uv - chroma).b;
    float alpha = HudTexture.Sample(HudSampler, uv).a;

    float2 edgeDistance = min(uv, 1.0 - uv);
    float fade = smoothstep(
        0.0,
        max(EdgeFade, 1e-5),
        min(edgeDistance.x, edgeDistance.y));

    // Assumes the UI pipeline uses premultiplied alpha.
    float4 result;
    result.rgb = float3(red, green, blue);
    result.rgb *= input.Color.rgb * Brightness;
    result.a = alpha * input.Color.a * Opacity;

    // Preserve premultiplied behavior when fading.
    result *= fade;

    return result;
}
