// Far depth of field (PristonDepthOfField): a golden-angle disc of taps over the HDR scene, whose radius grows with how
// far a pixel lies behind the focus. Only the distance blurs, so the character and what stands near it stay sharp. A tap
// counts only as far as its own blur reaches this pixel: sharp foreground does not bleed into the blurred background.
#include "../Core/Common.hlsli"
#include "../Core/Fullscreen.hlsli"

cbuffer PostDepthOfFieldConstants : register(b0)
{
    float2 SourceTexel;   // 1 / source size
    float FocusDistance;  // view depth of the focus, world units
    float MaxRadius;      // largest blur radius, pixels
    float BlurStart;      // distance behind the focus where blur begins
    float BlurEnd;        // distance behind the focus where blur is full
    float2 DepthOfFieldPadding;
};

Texture2D<float4> Source : register(t0);
SamplerState SourceSampler : register(s0) = sampler_state
{
    Texture = (Source); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

Texture2D<float> Depth : register(t1);
SamplerState DepthSampler : register(s1) = sampler_state
{
    Texture = (Depth); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

static const uint TapCount = 22;
static const float GoldenAngle = 2.39996323;

float RadiusAt(float2 uv)
{
    float behind = Depth.SampleLevel(DepthSampler, uv, 0) - FocusDistance;
    return saturate((behind - BlurStart) / (BlurEnd - BlurStart)) * MaxRadius;
}

float4 PS_Blur(FullscreenOut i) : SV_Target0
{
    float3 centre = min(Source.SampleLevel(SourceSampler, i.Uv, 0).rgb, HdrCap);
    float radius = RadiusAt(i.Uv);
    if (radius < 0.5)
    {
        return float4(centre, 1.0);
    }

    float3 sum = centre;
    float weights = 1.0;
    [loop]
    for (uint k = 1; k <= TapCount; k++)
    {
        float distance = sqrt(k / (float)TapCount) * radius;
        float angle = k * GoldenAngle;
        float2 uv = i.Uv + float2(cos(angle), sin(angle)) * distance * SourceTexel;
        float weight = saturate(RadiusAt(uv) - distance + 1.0);
        sum += min(Source.SampleLevel(SourceSampler, uv, 0).rgb, HdrCap) * weight;
        weights += weight;
    }

    return float4(sum / weights, 1.0);
}

technique Blur
{
    pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Blur(); }
}
