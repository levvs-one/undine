#version 300 es
// Undine: lamp light under the surface. One point per grid cell and per colour channel: the light refracts through
// the surface with that channel's index and lands on the floor plane; the points are summed into a map that the
// render shader reads for the floor and, by continuing the refracted ray, for the walls. The map covers the floor
// and a margin around it (uMargin times the pool side) so light bound for the walls is kept. A flat surface puts the
// points evenly, so the map reads one there; ripples focus the points into bright lines and leave gaps.
precision highp float;
precision highp int;

uniform sampler2D uState;      // height in r
uniform float uCell;           // metres per cell
uniform float uDepth;          // floor below the rest level, metres
uniform vec3 uLight;           // unit direction the light travels, pointing down
uniform vec3 uIor;             // index at 610, 550, 465 nm
uniform int uGrid;             // points per side
uniform float uMargin;         // map extent as a multiple of the pool side
uniform float uPointSize;      // texels per point; the fragment shader shapes the splat
out vec3 vEnergy;

float fresnel(float cosI, float n1, float n2) {
    float eta = n1 / n2;
    float sinT2 = eta * eta * (1.0 - cosI * cosI);
    if (sinT2 >= 1.0) return 1.0;
    float cosT = sqrt(1.0 - sinT2);
    float rs = (n1 * cosI - n2 * cosT) / (n1 * cosI + n2 * cosT);
    float rp = (n2 * cosI - n1 * cosT) / (n2 * cosI + n1 * cosT);
    return 0.5 * (rs * rs + rp * rp);
}

// Height at a point of the pool, bilinear from the nearest four cells (the texture itself is unfiltered).
float heightUv(vec2 uv) {
    ivec2 size = textureSize(uState, 0);
    vec2 p = clamp(uv * vec2(size) - 0.5, vec2(0.0), vec2(size) - 1.0);
    ivec2 i = ivec2(floor(p));
    vec2 f = p - vec2(i);
    ivec2 j = min(i + 1, size - 1);
    float a = texelFetch(uState, i, 0).r, b = texelFetch(uState, ivec2(j.x, i.y), 0).r;
    float c = texelFetch(uState, ivec2(i.x, j.y), 0).r, d = texelFetch(uState, j, 0).r;
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void main() {
    int channel = gl_VertexID % 3;
    int cell = gl_VertexID / 3;
    int ix = cell % uGrid, iy = cell / uGrid;
    vec2 uv = (vec2(float(ix), float(iy)) + 0.5) / float(uGrid);
    ivec2 size = textureSize(uState, 0);
    vec2 texel = 1.0 / vec2(size);
    float side = uCell * float(size.x);
    float h = heightUv(uv);
    float hl = heightUv(uv - vec2(texel.x, 0.0)), hr = heightUv(uv + vec2(texel.x, 0.0));
    float hd = heightUv(uv - vec2(0.0, texel.y)), hu = heightUv(uv + vec2(0.0, texel.y));
    vec3 normal = normalize(vec3(-(hr - hl) / (2.0 * uCell), 1.0, -(hu - hd) / (2.0 * uCell)));
    float n = channel == 0 ? uIor.x : (channel == 1 ? uIor.y : uIor.z);
    float cosI = clamp(-dot(uLight, normal), 0.0, 1.0);
    vec3 inside = refract(uLight, normal, 1.0 / n);
    if (dot(inside, inside) < 0.5 || inside.y >= 0.0) { gl_Position = vec4(4.0, 4.0, 4.0, 1.0); gl_PointSize = 1.0; vEnergy = vec3(0.0); return; }
    float weight = 1.0 - fresnel(cosI, 1.0, n);
    // From the surface point down to the floor plane: the sideways travel is the refracted direction times the drop.
    float drop = uDepth + h;
    vec2 landing = (uv - 0.5) * side + inside.xz / (-inside.y) * drop;
    vec2 clip = landing / (side * uMargin) * 2.0;
    gl_Position = vec4(clip, 0.0, 1.0);
    gl_PointSize = uPointSize;
    vEnergy = weight * (channel == 0 ? vec3(1.0, 0.0, 0.0) : (channel == 1 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0)));
}
