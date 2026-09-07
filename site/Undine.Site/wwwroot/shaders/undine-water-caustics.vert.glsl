#version 300 es
// Undine: lamp light under the surface. The surface grid is drawn as triangles, every vertex refracted through the
// surface with one colour channel's index and moved to where it lands on the floor plane; the fragment shader turns
// the change of area between the surface and the floor into brightness. Where ripples focus the light the triangles
// shrink and fold over each other and the sum is a bright line; where they spread the light the triangles grow and
// go dim. There are no gaps by construction. The map covers the floor and a margin around it (uMargin times the
// pool side) so light bound for the walls is kept. A flat surface gives exactly one everywhere.
precision highp float;
precision highp int;

uniform sampler2D uState;      // height in r
uniform float uCell;           // metres per cell
uniform float uDepth;          // floor below the rest level, metres
uniform vec3 uLight;           // unit direction the light travels, pointing down
uniform vec3 uIor;             // index at 610, 550, 465 nm
uniform int uGrid;             // vertices per side
uniform int uChannel;          // 0 red, 1 green, 2 blue
uniform float uMargin;         // map extent as a multiple of the pool side
out vec2 vSurface;             // where the light entered, metres on the rest plane
out float vWeight;             // the share of the light that went in

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
    // Six vertices per grid square, two triangles.
    int quad = gl_VertexID / 6;
    int corner = gl_VertexID % 6;
    int qx = quad % (uGrid - 1), qy = quad / (uGrid - 1);
    ivec2 offset = corner == 0 || corner == 3 ? ivec2(0, 0) : (corner == 1 ? ivec2(1, 0) : (corner == 2 || corner == 4 ? ivec2(1, 1) : ivec2(0, 1)));
    vec2 uv = vec2(ivec2(qx, qy) + offset) / float(uGrid - 1);

    ivec2 size = textureSize(uState, 0);
    vec2 texel = 1.0 / vec2(size);
    float side = uCell * float(size.x);
    float h = heightUv(uv);
    float hl = heightUv(uv - vec2(texel.x, 0.0)), hr = heightUv(uv + vec2(texel.x, 0.0));
    float hd = heightUv(uv - vec2(0.0, texel.y)), hu = heightUv(uv + vec2(0.0, texel.y));
    vec3 normal = normalize(vec3(-(hr - hl) / (2.0 * uCell), 1.0, -(hu - hd) / (2.0 * uCell)));
    float n = uChannel == 0 ? uIor.x : (uChannel == 1 ? uIor.y : uIor.z);
    float cosI = clamp(-dot(uLight, normal), 0.0, 1.0);
    vec3 inside = refract(uLight, normal, 1.0 / n);
    float weight = 1.0 - fresnel(cosI, 1.0, n);
    if (dot(inside, inside) < 0.5 || inside.y >= -0.05) {
        // Reflected away, or nearly grazing: the vertex stays under its entry point and carries no light.
        inside = vec3(0.0, -1.0, 0.0);
        weight = 0.0;
    }
    // From the surface point down to the floor plane: the sideways travel is the refracted direction times the drop.
    float drop = uDepth + h;
    vSurface = (uv - 0.5) * side;
    vec2 landing = vSurface + inside.xz / (-inside.y) * drop;
    gl_Position = vec4(landing / (side * uMargin) * 2.0, 0.0, 1.0);
    vWeight = weight;
}
