// Undine: the surface side of the water for Unity (Shader Graph Custom Function, file mode) and Unreal (Custom node
// or a .ush include). The engine brings the height texture, the floor colour and the sky; this file brings what the
// liquid does to light: the exact Fresnel split per channel, the refracted path to the floor, and the absorption.
// Constants per liquid are in liquids.json (index at 610, 550, 465 nm; absorption per metre at the same wavelengths).

// Exact unpolarized Fresnel power reflectance from n1 into n2; 1 past the critical angle.
float UndineFresnel(float cosI, float n1, float n2)
{
    float eta = n1 / n2;
    float sinT2 = eta * eta * (1.0 - cosI * cosI);
    if (sinT2 >= 1.0) return 1.0;
    float cosT = sqrt(1.0 - sinT2);
    float rs = (n1 * cosI - n2 * cosT) / (n1 * cosI + n2 * cosT);
    float rp = (n2 * cosI - n1 * cosT) / (n2 * cosI + n1 * cosT);
    return 0.5 * (rs * rs + rp * rp);
}

// Surface normal from a height texture: heights in metres, cell size in metres, y up.
float3 UndineNormal(float hl, float hr, float hd, float hu, float cellMetres)
{
    return normalize(float3(-(hr - hl) / (2.0 * cellMetres), 1.0, -(hu - hd) / (2.0 * cellMetres)));
}

// One colour channel of the water seen along `view` (unit, pointing into the surface) at a surface point with `normal`,
// `depthMetres` above the floor. Returns the weight of the floor colour and, through `floorOffset`, where on the floor
// to sample (metres, in the surface plane). `reflectance` is the share that reflects the sky instead.
float UndineChannel(float3 view, float3 normal, float n, float alphaPerMetre, float depthMetres, out float2 floorOffset, out float reflectance)
{
    float cosI = saturate(dot(-view, normal));
    reflectance = UndineFresnel(cosI, 1.0, n);
    float3 inside = refract(view, normal, 1.0 / n);
    floorOffset = float2(0.0, 0.0);
    if (dot(inside, inside) < 0.5 || inside.y >= 0.0) return 0.0;
    float path = depthMetres / (-inside.y);
    floorOffset = inside.xz * path;
    return (1.0 - reflectance) * exp(-alphaPerMetre * path);
}

// The whole thing for three channels. ior and absorb are the liquid's constants; sky is the reflected sky colour;
// floorColour(offset) is the engine's floor sample, so call this once per channel from the shader:
//   float2 off; float r;
//   float w = UndineChannel(view, normal, ior.r, absorb.r, depth, off, r);
//   colour.r = r * sky.r + w * SampleFloor(surfaceXz + off).r;   // and again for g and b with their own offsets
