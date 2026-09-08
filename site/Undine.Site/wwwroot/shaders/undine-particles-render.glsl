// Undine: the particle liquid on screen. Screen-space fluid rendering (van der Laan, Green and Sainz 2009): the
// particles are drawn as spheres into a depth image and a thickness image, the depth is smoothed so single
// spheres blend into a surface, and the surface is lit as the liquid: reflection of the room by the exact Fresnel
// equations, refraction of what is behind bent by the liquid's index, absorption over the thickness by the liquid's
// own k table. The tank and room are drawn first as the background. Sections are separated by //@ lines.

//@ prelude
#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform vec2 uResolution;
uniform mat4 uView;            // world to eye
uniform mat4 uProjection;      // eye to clip
uniform vec3 uEye;
uniform vec3 uBox;             // the tank
uniform float uRadius;         // sphere radius drawn per particle, metres
uniform vec3 uIor;
uniform vec3 uAlpha;
uniform vec3 uLight;           // unit direction the light travels
uniform float uLampStrength;
uniform float uExposure;
uniform int uFloorStyle;

const float AMBIENT = 0.26;

vec3 sky(vec3 d) {
    vec3 toLamp = -uLight;
    vec3 walls = vec3(0.62, 0.60, 0.57) * (0.75 + 0.25 * clamp(d.y * 2.0 + 0.5, 0.0, 1.0));
    vec3 ceiling = vec3(0.80, 0.79, 0.77);
    float up = smoothstep(0.25, 0.45, d.y);
    float panel = smoothstep(0.86, 0.90, d.y) * (1.0 - smoothstep(0.30, 0.36, abs(d.x))) * (1.0 - smoothstep(0.20, 0.26, abs(d.z)));
    vec3 s = mix(walls, ceiling, up) + vec3(3.0, 2.95, 2.85) * panel;
    vec3 wRight = normalize(cross(vec3(0.0, 1.0, 0.0), toLamp));
    vec3 wUp = cross(toLamp, wRight);
    float along = dot(d, toLamp);
    if (along > 0.5) {
        vec2 uv = vec2(dot(d, wRight), dot(d, wUp)) / along;
        vec2 ext = vec2(0.34, 0.26);
        float inside = (1.0 - smoothstep(ext.x - 0.01, ext.x + 0.01, abs(uv.x))) * (1.0 - smoothstep(ext.y - 0.01, ext.y + 0.01, abs(uv.y)));
        float bars = min(smoothstep(0.0, 0.02, abs(uv.x)), min(smoothstep(0.0, 0.02, abs(uv.x - ext.x * 0.667)), smoothstep(0.0, 0.02, abs(uv.x + ext.x * 0.667)))) * smoothstep(0.0, 0.02, abs(uv.y));
        vec3 daylight = mix(vec3(0.80, 0.86, 0.95), vec3(0.55, 0.70, 0.95), clamp(uv.y / ext.y * 0.5 + 0.5, 0.0, 1.0)) * 3.2;
        s = mix(s, mix(vec3(0.35, 0.34, 0.33), daylight * uLampStrength, bars), inside);
    }
    return s;
}

vec3 tonemap(vec3 c) {
    c *= uExposure;
    return clamp((c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14), 0.0, 1.0);
}

vec3 compand(vec3 c) {
    return mix(12.92 * c, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}

float fresnel(float cosI, float n1, float n2) {
    float eta = n1 / n2;
    float sinT2 = eta * eta * (1.0 - cosI * cosI);
    if (sinT2 >= 1.0) return 1.0;
    float cosT = sqrt(1.0 - sinT2);
    float rs = (n1 * cosI - n2 * cosT) / (n1 * cosI + n2 * cosT);
    float rp = (n2 * cosI - n1 * cosT) / (n2 * cosI + n1 * cosT);
    return 0.5 * (rs * rs + rp * rp);
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

// The tank's floor and walls: white tiles with a dark grout, or stone, or dark.
vec3 lining(vec2 uv, float w) {
    if (uFloorStyle == 2) return vec3(0.07, 0.07, 0.08);
    if (uFloorStyle == 1) return vec3(0.72, 0.70, 0.66) * (0.9 + 0.1 * hash(floor(uv * 60.0)));
    vec2 q = uv / 0.03;
    vec2 f = abs(fract(q) - 0.5);
    float grout = smoothstep(0.44 - w / 0.03, 0.46 + w / 0.03, max(f.x, f.y));
    return mix(vec3(0.88, 0.90, 0.90), vec3(0.55, 0.58, 0.60), grout);
}

// Where a ray meets the tank's inside (floor or a wall), and its colour; the tank is open at the top.
vec4 tank(vec3 o, vec3 d, out vec3 hit) {
    float best = 1e9;
    vec3 n = vec3(0.0);
    vec2 uv = vec2(0.0);
    // Floor.
    if (d.y < 0.0) { float t = -o.y / d.y; vec3 p = o + d * t; if (t > 0.0 && abs(p.x - uBox.x * 0.5) <= uBox.x * 0.5 && abs(p.z - uBox.z * 0.5) <= uBox.z * 0.5) { best = t; n = vec3(0.0, 1.0, 0.0); uv = p.xz; } }
    // Walls, only from inside.
    for (int k = 0; k < 4; k++) {
        // Inward normals; a wall is met only travelling against its normal, so the glass nearest the eye is looked through.
        vec3 wn = k == 0 ? vec3(1.0, 0.0, 0.0) : k == 1 ? vec3(-1.0, 0.0, 0.0) : k == 2 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 0.0, -1.0);
        float planeDot = k == 0 ? 0.0 : k == 1 ? -uBox.x : k == 2 ? 0.0 : -uBox.z;
        float denom = dot(d, wn);
        if (denom >= 0.0) continue;
        float t = (planeDot - dot(o, wn)) / denom;
        vec3 p = o + d * t;
        if (t > 0.0 && t < best && p.y >= 0.0 && p.y <= uBox.y && abs(p.x - uBox.x * 0.5) <= uBox.x * 0.5 + 1e-4 && abs(p.z - uBox.z * 0.5) <= uBox.z * 0.5 + 1e-4) {
            best = t; n = wn; uv = k < 2 ? vec2(p.z, p.y) : vec2(p.x, p.y);
        }
    }
    if (best >= 1e9) { hit = vec3(0.0); return vec4(0.0); }
    hit = o + d * best;
    float direct = max(0.0, dot(n, -uLight));
    // A room's light reaches the walls from every side; the lamp adds where it faces them.
    vec3 light = vec3(AMBIENT * (1.6 + 0.6 * n.y)) + uLampStrength * vec3(1.0, 0.97, 0.92) * direct;
    return vec4(lining(uv, best * 0.002) * light, 1.0);
}

//@ background
// What is behind the liquid: the tank inside or the room, per pixel, with its eye-space depth in alpha.
uniform vec3 uForward, uRight, uUp;
uniform float uTanHalf;
out vec4 outColour;
void main() {
    vec2 px = gl_FragCoord.xy;
    float aspect = uResolution.x / uResolution.y;
    float sx = (px.x / uResolution.x * 2.0 - 1.0) * uTanHalf * aspect;
    float sy = (px.y / uResolution.y * 2.0 - 1.0) * uTanHalf;
    vec3 dir = normalize(uForward + uRight * sx + uUp * sy);
    vec3 hit;
    vec4 t = tank(uEye, dir, hit);
    if (t.a > 0.0) { outColour = vec4(t.rgb, length(hit - uEye)); return; }
    // The room beyond: the floor far below, or the walls and ceiling.
    if (dir.y < -0.05) {
        float tt = (-0.75 - uEye.y) / dir.y;
        vec3 p = uEye + dir * tt;
        float tile = mod(floor(p.x / 0.3) + floor(p.z / 0.3), 2.0);
        vec3 c = mix(vec3(0.20, 0.19, 0.18), vec3(0.26, 0.25, 0.23), tile) * (AMBIENT * 2.0 + 0.3 * uLampStrength);
        outColour = vec4(mix(vec3(0.30, 0.29, 0.28), c, exp(-tt * 0.35)), 1e6);
        return;
    }
    outColour = vec4(sky(dir), 1e6);
}

//@ sphereVertex
// Each particle as a point sprite sized for its sphere at its depth.
uniform sampler2D uPos;
uniform int uSide;
out vec3 vEye;
void main() {
    ivec2 t = ivec2(gl_VertexID % uSide, gl_VertexID / uSide);
    vec4 P = texelFetch(uPos, t, 0);
    if (P.w < 0.5) { gl_Position = vec4(4.0, 4.0, 4.0, 1.0); gl_PointSize = 1.0; vEye = vec3(0.0); return; }
    vec4 eye = uView * vec4(P.xyz, 1.0);
    vEye = eye.xyz;
    gl_Position = uProjection * eye;
    gl_PointSize = uRadius * uProjection[1][1] * uResolution.y / max(1e-3, -eye.z);
}

//@ sphereDepth
// The sphere's front surface: its eye-space depth into the colour, with the depth test doing the sorting.
in vec3 vEye;
out vec4 outDepth;
void main() {
    vec2 q = gl_PointCoord * 2.0 - 1.0;
    q.y = -q.y;
    float r2 = dot(q, q);
    if (r2 > 1.0) discard;
    float z = sqrt(1.0 - r2);
    vec3 p = vEye + vec3(q * uRadius, z * uRadius);
    float depth = -p.z;
    vec4 clip = uProjection * vec4(p, 1.0);
    gl_FragDepth = clip.z / clip.w * 0.5 + 0.5;
    outDepth = vec4(depth, 0.0, 0.0, 1.0);
}

//@ sphereThickness
// How much liquid the ray crosses, summed over spheres with additive blending.
in vec3 vEye;
out vec4 outThickness;
void main() {
    vec2 q = gl_PointCoord * 2.0 - 1.0;
    float r2 = dot(q, q);
    if (r2 > 1.0) discard;
    outThickness = vec4(2.0 * uRadius * sqrt(1.0 - r2), 0.0, 0.0, 1.0);
}

//@ smooth
// Bilateral blur of the depth image along one axis: spheres blend into a surface, but not across silhouettes.
uniform sampler2D uDepth;
uniform vec2 uAxis;
uniform float uBlurRadius;     // pixels
out vec4 outDepth;
void main() {
    ivec2 t = ivec2(gl_FragCoord.xy);
    float centre = texelFetch(uDepth, t, 0).r;
    if (centre <= 0.0) { outDepth = vec4(0.0); return; }
    float sum = 0.0, weight = 0.0;
    float sigmaDepth = uRadius * 3.0;
    int r = int(uBlurRadius);
    for (int k = -r; k <= r; k++) {
        ivec2 tk = t + ivec2(vec2(k) * uAxis);
        float d = texelFetch(uDepth, clamp(tk, ivec2(0), ivec2(uResolution) - 1), 0).r;
        if (d <= 0.0) continue;
        float ws = exp(-float(k * k) / (2.0 * uBlurRadius * uBlurRadius * 0.25));
        float dd = (d - centre) / sigmaDepth;
        float wd = exp(-dd * dd);
        sum += d * ws * wd;
        weight += ws * wd;
    }
    outDepth = vec4(weight > 0.0 ? sum / weight : centre, 0.0, 0.0, 1.0);
}

//@ compose
// The liquid over the background: the surface from the smoothed depth, lit by the liquid's own optics.
uniform sampler2D uDepth;
uniform sampler2D uThickness;
uniform sampler2D uBackground;
uniform vec3 uForward, uRight, uUp;
uniform float uTanHalf;
uniform mat4 uInverseView;
uniform int uDebug;            // 1 normals, 2 thickness, 3 depth, 4 refraction shift
out vec4 outColour;

vec3 eyeAt(ivec2 t) {
    float d = texelFetch(uDepth, t, 0).r;
    vec2 ndc = (vec2(t) + 0.5) / uResolution * 2.0 - 1.0;
    float aspect = uResolution.x / uResolution.y;
    return vec3(ndc.x * uTanHalf * aspect * d, ndc.y * uTanHalf * d, -d);
}

void main() {
    ivec2 t = ivec2(gl_FragCoord.xy);
    vec4 back = texelFetch(uBackground, t, 0);
    float depth = texelFetch(uDepth, t, 0).r;
    if (depth <= 0.0 || depth > back.a) { outColour = vec4(compand(tonemap(back.rgb)), 1.0); return; }
    vec3 p = eyeAt(t);
    ivec2 size = ivec2(uResolution) - 1;
    vec3 px = eyeAt(min(t + ivec2(1, 0), size)) - p, mx = p - eyeAt(max(t - ivec2(1, 0), ivec2(0)));
    vec3 py = eyeAt(min(t + ivec2(0, 1), size)) - p, my = p - eyeAt(max(t - ivec2(0, 1), ivec2(0)));
    vec3 dx = abs(px.z) < abs(mx.z) ? px : mx;
    vec3 dy = abs(py.z) < abs(my.z) ? py : my;
    vec3 nEye = normalize(cross(dx, dy));
    if (nEye.z < 0.0) nEye = -nEye;
    vec3 n = normalize((uInverseView * vec4(nEye, 0.0)).xyz);
    vec3 view = normalize(-p);
    vec3 viewWorld = normalize((uInverseView * vec4(view, 0.0)).xyz);
    float cosI = clamp(dot(n, viewWorld), 0.0, 1.0);
    // Spheres of three quarters of the spacing overlap; the sum over them is the liquid's thickness times their overlap.
    float thickness = texelFetch(uThickness, t, 0).r / 1.77;
    if (uDebug == 1) { outColour = vec4(n * 0.5 + 0.5, 1.0); return; }
    if (uDebug == 2) { outColour = vec4(vec3(thickness * 5.0), 1.0); return; }
    if (uDebug == 3) { outColour = vec4(vec3(depth / 1.5), 1.0); return; }
    vec3 reflected = reflect(-viewWorld, n);
    vec3 reflection = sky(reflected.y > 0.0 ? reflected : normalize(vec3(reflected.x, 0.02, reflected.z)));
    vec3 colour = vec3(0.0);
    for (int c = 0; c < 3; c++) {
        float nn = uIor[c];
        float r = fresnel(cosI, 1.0, nn);
        // Refraction: the background seen through the liquid, shifted along the surface's slope by an amount that
        // grows with thickness and with the index's excess over one.
        vec2 shift = nEye.xy * thickness * (nn - 1.0) * 1.5 / max(0.05, depth) * uResolution.y * 0.5 / uTanHalf / uResolution;
        ivec2 st = clamp(t + ivec2(shift * uResolution), ivec2(0), size);
        vec4 behind = texelFetch(uBackground, st, 0);
        if (behind.a < depth) behind = back;
        float through = behind[c] * exp(-uAlpha[c] * thickness);
        colour[c] = r * reflection[c] + (1.0 - r) * through;
    }
    outColour = vec4(compand(tonemap(colour)), 1.0);
}
