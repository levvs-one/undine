#version 300 es
// Undine: sunlight on the pool floor. One point per cell of the surface and per colour channel: the light refracts
// through the surface with that channel's index and lands on the floor; the points are summed into a map that the
// render shader reads. A flat surface puts every point in its own cell, so the map reads one there; ripples focus the
// points into bright lines and leave gaps.
precision highp float;
precision highp int;

uniform sampler2D uState;      // height in r
uniform float uCell;           // metres per cell
uniform float uDepth;          // floor below the rest level, metres
uniform vec3 uLight;           // unit direction the light travels, pointing down
uniform vec3 uIor;             // index at 610, 550, 465 nm
uniform int uGrid;             // points per side
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

void main() {
    int channel = gl_VertexID % 3;
    int cell = gl_VertexID / 3;
    int ix = cell % uGrid, iy = cell / uGrid;
    vec2 uv = (vec2(float(ix), float(iy)) + 0.5) / float(uGrid);
    ivec2 size = textureSize(uState, 0);
    vec2 texel = 1.0 / vec2(size);
    float h = texture(uState, uv).r;
    float hl = texture(uState, uv - vec2(texel.x, 0.0)).r, hr = texture(uState, uv + vec2(texel.x, 0.0)).r;
    float hd = texture(uState, uv - vec2(0.0, texel.y)).r, hu = texture(uState, uv + vec2(0.0, texel.y)).r;
    float side = uCell * float(size.x);
    vec3 normal = normalize(vec3(-(hr - hl) / (2.0 * uCell), 1.0, -(hu - hd) / (2.0 * uCell)));
    float n = channel == 0 ? uIor.x : (channel == 1 ? uIor.y : uIor.z);
    float cosI = clamp(-dot(uLight, normal), 0.0, 1.0);
    vec3 inside = refract(uLight, normal, 1.0 / n);
    if (dot(inside, inside) < 0.5 || inside.y >= 0.0) { gl_Position = vec4(4.0, 4.0, 4.0, 1.0); gl_PointSize = 1.0; vEnergy = vec3(0.0); return; }
    float weight = 1.0 - fresnel(cosI, 1.0, n);
    // From the surface point down to the floor: the sideways travel is the refracted direction times the drop.
    float drop = uDepth + h;
    vec2 landing = uv * side + inside.xz / (-inside.y) * drop;
    vec2 clip = landing / side * 2.0 - 1.0;
    gl_Position = vec4(clip, 0.0, 1.0);
    gl_PointSize = 2.0;
    vEnergy = weight * 0.25 * (channel == 0 ? vec3(1.0, 0.0, 0.0) : (channel == 1 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0)));
}
