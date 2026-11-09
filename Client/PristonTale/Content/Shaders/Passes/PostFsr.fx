// Spatial upscaling after AMD FidelityFX Super Resolution 1.0 (MIT licence, Copyright (c) Advanced Micro Devices, Inc.): edge-adaptive
// upsampling (Easu) of the display-referred image with 12 taps of a direction-stretched Lanczos-like kernel, clamped to the four
// nearest texels, then robust contrast-adaptive sharpening (Rcas) at the output size.
#include "../Core/Common.hlsli"
#include "../Core/Fullscreen.hlsli"

cbuffer PostFsrConstants : register(b0)
{
    float4 SourceSize;    // width, height, 1 / width, 1 / height
    float Sharpness;      // Rcas: 0 none, 1 the most
    float3 FsrPadding;
};

Texture2D<float4> Source : register(t0);
SamplerState SourceSampler : register(s0) = sampler_state
{
    Texture = (Source); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

float3 Texel(float2 texel)
{
    return Source.SampleLevel(SourceSampler, (texel + 0.5) * SourceSize.zw, 0).rgb;
}

float Luma(float3 c)
{
    return c.b * 0.5 + c.r * 0.5 + c.g;
}

void AddDirection(inout float2 dir, inout float len, float w, float la, float lb, float lc, float ld, float le)
{
    float dc = ld - lc;
    float cb = lc - lb;
    float lenX = 1.0 / max(max(abs(dc), abs(cb)), 1.0 / 32768.0);
    float dirX = ld - lb;
    dir.x += dirX * w;
    lenX = saturate(abs(dirX) * lenX);
    len += lenX * lenX * w;

    float ec = le - lc;
    float ca = lc - la;
    float lenY = 1.0 / max(max(abs(ec), abs(ca)), 1.0 / 32768.0);
    float dirY = le - la;
    dir.y += dirY * w;
    lenY = saturate(abs(dirY) * lenY);
    len += lenY * lenY * w;
}

void Tap(inout float3 color, inout float weight, float2 offset, float2 dir, float2 len, float lob, float clip, float3 c)
{
    float2 v = float2(dot(offset, dir), dot(offset, float2(-dir.y, dir.x))) * len;
    float d2 = min(dot(v, v), clip);
    float wb = 0.4 * d2 - 1.0;
    float wa = lob * d2 - 1.0;
    wb *= wb;
    wa *= wa;
    wb = 1.5625 * wb - 0.5625;
    float w = wb * wa;
    color += c * w;
    weight += w;
}

float4 PS_Easu(FullscreenOut i) : SV_Target0
{
    float2 position = i.Uv * SourceSize.xy - 0.5;
    float2 base = floor(position);
    float2 fp = position - base;

    //    b c
    //  e f g h
    //  i j k l
    //    n o
    float3 b = Texel(base + float2(0, -1));
    float3 c = Texel(base + float2(1, -1));
    float3 e = Texel(base + float2(-1, 0));
    float3 f = Texel(base);
    float3 g = Texel(base + float2(1, 0));
    float3 h = Texel(base + float2(2, 0));
    float3 ii = Texel(base + float2(-1, 1));
    float3 j = Texel(base + float2(0, 1));
    float3 k = Texel(base + float2(1, 1));
    float3 l = Texel(base + float2(2, 1));
    float3 n = Texel(base + float2(0, 2));
    float3 o = Texel(base + float2(1, 2));

    float bL = Luma(b), cL = Luma(c), eL = Luma(e), fL = Luma(f), gL = Luma(g), hL = Luma(h);
    float iL = Luma(ii), jL = Luma(j), kL = Luma(k), lL = Luma(l), nL = Luma(n), oL = Luma(o);

    float2 dir = 0.0;
    float len = 0.0;
    AddDirection(dir, len, (1.0 - fp.x) * (1.0 - fp.y), bL, eL, fL, gL, jL);
    AddDirection(dir, len, fp.x * (1.0 - fp.y), cL, fL, gL, hL, kL);
    AddDirection(dir, len, (1.0 - fp.x) * fp.y, fL, iL, jL, kL, nL);
    AddDirection(dir, len, fp.x * fp.y, gL, jL, kL, lL, oL);

    float dirLength = dot(dir, dir);
    bool flat = dirLength < 1.0 / 32768.0;
    dir = flat ? float2(1.0, 0.0) : dir / sqrt(dirLength);

    len *= 0.5;
    len *= len;
    float stretch = dot(dir, dir) / max(abs(dir.x), abs(dir.y));
    float2 len2 = float2(1.0 + (stretch - 1.0) * len, 1.0 - 0.5 * len);
    float lob = 0.5 + (0.25 - 0.04 - 0.5) * len;
    float clip = 1.0 / lob;

    float3 color = 0.0;
    float weight = 0.0;
    Tap(color, weight, float2(0, -1) - fp, dir, len2, lob, clip, b);
    Tap(color, weight, float2(1, -1) - fp, dir, len2, lob, clip, c);
    Tap(color, weight, float2(-1, 1) - fp, dir, len2, lob, clip, ii);
    Tap(color, weight, float2(0, 1) - fp, dir, len2, lob, clip, j);
    Tap(color, weight, float2(0, 0) - fp, dir, len2, lob, clip, f);
    Tap(color, weight, float2(-1, 0) - fp, dir, len2, lob, clip, e);
    Tap(color, weight, float2(1, 1) - fp, dir, len2, lob, clip, k);
    Tap(color, weight, float2(2, 1) - fp, dir, len2, lob, clip, l);
    Tap(color, weight, float2(2, 0) - fp, dir, len2, lob, clip, h);
    Tap(color, weight, float2(1, 0) - fp, dir, len2, lob, clip, g);
    Tap(color, weight, float2(1, 2) - fp, dir, len2, lob, clip, o);
    Tap(color, weight, float2(0, 2) - fp, dir, len2, lob, clip, n);

    float3 lowest = min(f, min(g, min(j, k)));
    float3 highest = max(f, max(g, max(j, k)));
    return float4(clamp(color / weight, lowest, highest), 1.0);
}

float4 PS_Rcas(FullscreenOut i) : SV_Target0
{
    float2 centre = floor(i.Uv * SourceSize.xy);
    float3 b = Texel(centre + float2(0, -1));
    float3 d = Texel(centre + float2(-1, 0));
    float3 e = Texel(centre);
    float3 f = Texel(centre + float2(1, 0));
    float3 h = Texel(centre + float2(0, 1));

    float3 lowest = min(b, min(d, min(f, h)));
    float3 highest = max(b, max(d, max(f, h)));
    float3 hitMin = lowest / max(4.0 * highest, 1.0 / 1024.0);
    float3 hitMax = (1.0 - highest) / min(4.0 * lowest - 4.0, -1.0 / 1024.0);
    float3 lobes = max(-hitMin, hitMax);
    float lobe = max(-0.1875, min(max(lobes.r, max(lobes.g, lobes.b)), 0.0)) * Sharpness;
    return float4(saturate((lobe * (b + d + f + h) + e) / (4.0 * lobe + 1.0)), 1.0);
}

technique Easu
{
    pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Easu(); }
}

technique Rcas
{
    pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Rcas(); }
}
