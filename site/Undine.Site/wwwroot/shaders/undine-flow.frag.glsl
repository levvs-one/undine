#version 300 es
// Undine: a liquid poured onto a table in a room, seen from above the table. For each pixel the camera ray marches
// down to the first surface it meets, the liquid's top where there is liquid and the table where there is none; on
// the liquid it splits by the exact Fresnel equations, reflects the room (a window, a ceiling light, walls) and
// refracts per colour channel through the film to the table beneath, absorbed on the way by the liquid's own k
// table. The stream from the spout is a cylinder of the same liquid. The indices and absorption are the liquid's
// numbers from Caustikon.
precision highp float;
out vec4 outColour;

uniform vec2 uResolution;
uniform vec3 uEye, uForward, uRight, uUp;
uniform float uTanHalf;
uniform sampler2D uState;      // thickness in r, floor in a, over the table
uniform float uSide;           // table side, metres
uniform vec3 uIor;
uniform vec3 uAlpha;
uniform vec3 uLight;
uniform float uLampStrength;
uniform float uExposure;
uniform int uTable;            // 0 wood, 1 stone, 2 dark
uniform vec3 uSpout;           // x, top y, z of the stream, metres; y negative when off
uniform float uSpoutRadius;    // metres, at the spout
uniform float uJetSpeed;       // m/s at the spout: the stream narrows as it falls faster
uniform float uContactAngle;   // radians: the meniscus at the puddle's edge is drawn as its arc

const float AMBIENT = 0.26;
const float THIN = 2e-5;       // below this the film does not count as liquid to the eye

float fresnel(float cosI, float n1, float n2) {
    float eta = n1 / n2;
    float sinT2 = eta * eta * (1.0 - cosI * cosI);
    if (sinT2 >= 1.0) return 1.0;
    float cosT = sqrt(1.0 - sinT2);
    float rs = (n1 * cosI - n2 * cosT) / (n1 * cosI + n2 * cosT);
    float rp = (n2 * cosI - n1 * cosT) / (n2 * cosI + n1 * cosT);
    return 0.5 * (rs * rs + rp * rp);
}

// Bilinear thickness and floor from the unfiltered float texture. The liquid's level (floor + thickness) is what
// is interpolated, not the thickness alone, so a still pool's edge on a sloping dish is a smooth contour rather
// than a sawtooth of cells; the thickness returned is level minus floor.
vec2 filmAt(vec2 xz) {
    ivec2 size = textureSize(uState, 0);
    vec2 p = clamp((xz / uSide + 0.5) * vec2(size) - 0.5, vec2(0.0), vec2(size) - 1.0);
    ivec2 i = ivec2(floor(p));
    vec2 f = p - vec2(i);
    ivec2 j = min(i + 1, size - 1);
    vec4 a = texelFetch(uState, i, 0), b = texelFetch(uState, ivec2(j.x, i.y), 0);
    vec4 c = texelFetch(uState, ivec2(i.x, j.y), 0), d = texelFetch(uState, j, 0);
    vec2 la = vec2(a.r + a.a, a.a), lb = vec2(b.r + b.a, b.a), lc = vec2(c.r + c.a, c.a), ld = vec2(d.r + d.a, d.a);
    vec2 m = mix(mix(la, lb, f.x), mix(lc, ld, f.x), f.y);
    return vec2(max(0.0, m.x - m.y), m.y);
}

float floorAt(vec2 xz) {
    float halfSide = uSide * 0.5;
    if (abs(xz.x) > halfSide || abs(xz.y) > halfSide) return 0.0;
    return filmAt(xz).y;
}

// The four texels round a point: their bilinear level and floor as filmAt, the thickest of them, and whether any is dry.
vec4 filmEdgeAt(vec2 xz) {
    ivec2 size = textureSize(uState, 0);
    vec2 p = clamp((xz / uSide + 0.5) * vec2(size) - 0.5, vec2(0.0), vec2(size) - 1.0);
    ivec2 i = ivec2(floor(p));
    vec2 f = p - vec2(i);
    ivec2 j = min(i + 1, size - 1);
    vec4 a = texelFetch(uState, i, 0), b = texelFetch(uState, ivec2(j.x, i.y), 0);
    vec4 c = texelFetch(uState, ivec2(i.x, j.y), 0), d = texelFetch(uState, j, 0);
    vec2 la = vec2(a.r + a.a, a.a), lb = vec2(b.r + b.a, b.a), lc = vec2(c.r + c.a, c.a), ld = vec2(d.r + d.a, d.a);
    vec2 m = mix(mix(la, lb, f.x), mix(lc, ld, f.x), f.y);
    float thickest = max(max(a.r, b.r), max(c.r, d.r));
    float dry = min(min(a.r, b.r), min(c.r, d.r)) <= THIN ? 1.0 : 0.0;
    return vec4(max(0.0, m.x - m.y), m.y, thickest, dry);
}

// The liquid's top. At the edge the interpolation ramps the thickness down over one cell; that ramp is reshaped
// into the meniscus a puddle really has there, a circular arc that leaves the floor at the contact angle and
// meets the flat top tangentially: radius H/(1 − cos θ) for a puddle H thick, which for water is a rounded lip about
// four millimetres across. Angles past 90° would overhang, which a height field cannot hold, so they are capped.
float topAt(vec2 xz) {
    float halfSide = uSide * 0.5;
    if (abs(xz.x) > halfSide || abs(xz.y) > halfSide) return 0.0;
    vec4 f = filmEdgeAt(xz);
    float t = f.x;
    if (t <= THIN) return f.y;
    if (f.w > 0.5 && f.z > THIN) {
        float H = f.z;
        float theta = min(uContactAngle, 1.5707963);
        float R = H / (1.0 - cos(theta));
        float sc = R * sin(theta), yc = H - R;
        float s = clamp(t / H, 0.0, 1.0) * sc;
        t = max(0.0, yc + sqrt(max(0.0, R * R - (s - sc) * (s - sc))));
    }
    return f.y + t;
}

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

// The table's albedo; w is the width the pixel covers, metres.
vec3 table(vec2 xz, float w) {
    float fade = 1.0 - smoothstep(0.002, 0.02, w);
    if (uTable == 1) {
        float g = 0.72 + 0.10 * (noise(xz * 90.0) - 0.5) * fade + 0.05 * (noise(xz * 9.0) - 0.5);
        return vec3(0.80, 0.78, 0.74) * g;
    }
    if (uTable == 2) return vec3(0.08, 0.08, 0.09);
    // Wood: boards 12 cm wide along x, grain along the board, a little fine texture that fades with distance.
    float board = floor(xz.y / 0.12);
    float seam = smoothstep(0.0, 0.004 + w, abs(fract(xz.y / 0.12) - 0.5) * 0.12);
    float grain = 0.6 * noise(vec2(xz.x * 14.0 + board * 7.0, xz.y * 110.0)) + 0.4 * noise(vec2(xz.x * 90.0, xz.y * 500.0)) * fade;
    float tone = 0.85 + 0.15 * noise(vec2(board * 3.1, 0.5));
    vec3 wood = mix(vec3(0.40, 0.25, 0.14), vec3(0.58, 0.39, 0.23), grain) * tone;
    return mix(wood * 0.5, wood, seam);
}

// The room the table stands in, by direction: a pale ceiling with a soft panel light overhead, walls, and a bright
// window where the lamp comes from. Reflections of these are what make a still liquid read as liquid.
vec3 sky(vec3 d) {
    vec3 toLamp = -uLight;
    vec3 walls = vec3(0.62, 0.60, 0.57) * (0.75 + 0.25 * clamp(d.y * 2.0 + 0.5, 0.0, 1.0));
    vec3 ceiling = vec3(0.80, 0.79, 0.77);
    float up = smoothstep(0.25, 0.45, d.y);
    // The panel: a rectangle overhead, bright and soft-edged.
    float panel = smoothstep(0.86, 0.90, d.y) * (1.0 - smoothstep(0.30, 0.36, abs(d.x))) * (1.0 - smoothstep(0.20, 0.26, abs(d.z)));
    vec3 s = mix(walls, ceiling, up) + vec3(3.0, 2.95, 2.85) * panel;
    // The window, in the lamp's direction: a frame of 2 by 3 panes with daylight behind, so its reflection has
    // edges for the liquid to bend.
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

// The room's floor beyond the table, far below.
vec3 roomFloor(vec3 o, vec3 d) {
    float t = (-0.75 - o.y) / d.y;
    vec3 p = o + d * t;
    float tile = mod(floor(p.x / 0.3) + floor(p.z / 0.3), 2.0);
    vec3 c = mix(vec3(0.20, 0.19, 0.18), vec3(0.26, 0.25, 0.23), tile);
    float fog = exp(-t * 0.35);
    return mix(vec3(0.30, 0.29, 0.28), c * (AMBIENT * 2.0 + 0.3 * uLampStrength), fog);
}

vec3 tonemap(vec3 c) {
    c *= uExposure;
    return clamp((c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14), 0.0, 1.0);
}

vec3 compand(vec3 c) {
    return mix(12.92 * c, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}

// The lamp on the table: direct light, with the stream's shadow.
vec3 tableLight(vec2 xz) {
    float direct = max(0.0, -uLight.y);
    if (uSpout.y > 0.0) {
        // Where the stream stands between this point and the lamp.
        vec3 toLamp = -uLight;
        vec3 p = vec3(xz.x, floorAt(xz), xz.y);
        float t = (uSpout.y - p.y) / max(1e-3, toLamp.y);
        vec2 closest = p.xz + toLamp.xz * clamp(dot(uSpout.xz - p.xz, toLamp.xz) / max(1e-6, dot(toLamp.xz, toLamp.xz)), 0.0, t);
        float d = length(closest - uSpout.xz);
        direct *= smoothstep(uSpoutRadius, uSpoutRadius * 1.6, d);
    }
    return vec3(AMBIENT) + uLampStrength * vec3(1.0, 0.97, 0.92) * direct;
}

// March the ray to the first surface (liquid top or table); returns the distance, or a negative when none.
float march(vec3 o, vec3 d, float far) {
    float t = 0.0;
    float lastAbove = 0.0;
    float step = 0.004;
    for (int i = 0; i < 160; i++) {
        vec3 p = o + d * t;
        float s = topAt(p.xz);
        if (p.y < s) {
            // Bisect between the last point above and this one below.
            float lo = lastAbove, hi = t;
            for (int j = 0; j < 8; j++) {
                float mid = 0.5 * (lo + hi);
                vec3 q = o + d * mid;
                if (q.y < topAt(q.xz)) hi = mid; else lo = mid;
            }
            return hi;
        }
        lastAbove = t;
        // Steps grow with height above the table, shrink near it.
        step = max(0.002, min(0.03, (p.y - s) * 0.5));
        t += step;
        if (t > far) break;
    }
    return -1.0;
}

vec3 normalAt(vec2 xz) {
    float e = 1.5 * uSide / float(textureSize(uState, 0).x);
    float l = topAt(xz - vec2(e, 0.0)), r = topAt(xz + vec2(e, 0.0));
    float d = topAt(xz - vec2(0.0, e)), u = topAt(xz + vec2(0.0, e));
    return normalize(vec3(-(r - l) / (2.0 * e), 1.0, -(u - d) / (2.0 * e)));
}

vec3 floorNormal(vec2 xz) {
    float e = uSide / float(textureSize(uState, 0).x);
    float l = floorAt(xz - vec2(e, 0.0)), r = floorAt(xz + vec2(e, 0.0));
    float d = floorAt(xz - vec2(0.0, e)), u = floorAt(xz + vec2(0.0, e));
    return normalize(vec3(-(r - l) / (2.0 * e), 1.0, -(u - d) / (2.0 * e)));
}

// The stream's radius at a height: it falls freely, v² = v₀² + 2g·drop, and carries the same flow at every
// height, so r = r₀·√(v₀/v); a stream from a tap visibly narrows on the way down.
float streamRadiusAt(float y) {
    float v0 = max(uJetSpeed, 0.05);
    float v2 = v0 * v0 + 2.0 * 9.80665 * max(0.0, uSpout.y - y);
    return uSpoutRadius * sqrt(v0 / sqrt(v2));
}

// The stream from the spout down to the surface: the ray's entry into its narrowing body, or negative, and the
// normal there. Marched within the spout's own cylinder, then bisected.
float stream(vec3 o, vec3 d, out vec3 n) {
    n = vec3(0.0);
    if (uSpout.y <= 0.0) return -1.0;
    vec2 oc = o.xz - uSpout.xz;
    float a = dot(d.xz, d.xz), b = 2.0 * dot(oc, d.xz), c = dot(oc, oc) - uSpoutRadius * uSpoutRadius;
    float disc = b * b - 4.0 * a * c;
    if (disc < 0.0 || a < 1e-8) return -1.0;
    float t0 = max(0.0, (-b - sqrt(disc)) / (2.0 * a)), t1 = (-b + sqrt(disc)) / (2.0 * a);
    if (t1 <= 0.0) return -1.0;
    float bottom = topAt(uSpout.xz);
    float tPrev = t0, tHit = -1.0;
    for (int i = 0; i <= 16; i++) {
        float t = mix(t0, t1, float(i) / 16.0);
        vec3 p = o + d * t;
        if (p.y <= uSpout.y && p.y >= bottom && length(p.xz - uSpout.xz) < streamRadiusAt(p.y)) {
            float lo = tPrev, hi = t;
            for (int j = 0; j < 6; j++) {
                float m = 0.5 * (lo + hi);
                vec3 q = o + d * m;
                if (q.y <= uSpout.y && q.y >= bottom && length(q.xz - uSpout.xz) < streamRadiusAt(q.y)) hi = m; else lo = m;
            }
            tHit = hi;
            break;
        }
        tPrev = t;
    }
    if (tHit < 0.0) return -1.0;
    vec3 p = o + d * tHit;
    vec2 dd = p.xz - uSpout.xz;
    float r = max(streamRadiusAt(p.y), 1e-6);
    // dr/dy of the profile above, for the normal's tilt: the body widens towards the spout.
    float v0 = max(uJetSpeed, 0.05);
    float v2 = v0 * v0 + 2.0 * 9.80665 * max(0.0, uSpout.y - p.y);
    float drdy = uSpoutRadius * sqrt(v0) * 9.80665 / (2.0 * pow(v2, 1.25));
    n = normalize(vec3(dd.x / r, -drdy, dd.y / r));
    return tHit;
}

vec3 shadeTable(vec3 p, float w) {
    vec3 n = floorNormal(p.xz);
    float direct = max(0.0, dot(n, -uLight)) / max(1e-3, -uLight.y);
    return table(p.xz, w) * tableLight(p.xz) * mix(1.0, direct, 0.7);
}

// A wet table is darker and richer: light that the table scatters back is partly trapped in the film by total
// internal reflection and scattered again, so less leaves and what leaves has been coloured twice.
vec3 wetTable(vec3 p, float w, float n) {
    vec3 dry = table(p.xz, w);
    float trapped = 1.0 - 1.0 / (n * n);
    vec3 albedo = dry * (1.0 - trapped) / (1.0 - trapped * dry);
    vec3 nrm = floorNormal(p.xz);
    float direct = max(0.0, dot(nrm, -uLight)) / max(1e-3, -uLight.y);
    return albedo * tableLight(p.xz) * mix(1.0, direct, 0.7);
}

void main() {
    vec2 px = gl_FragCoord.xy;
    float aspect = uResolution.x / uResolution.y;
    float sx = (px.x / uResolution.x * 2.0 - 1.0) * uTanHalf * aspect;
    float sy = (px.y / uResolution.y * 2.0 - 1.0) * uTanHalf;
    vec3 dir = normalize(uForward + uRight * sx + uUp * sy);
    float pixelAngle = 2.0 * uTanHalf / uResolution.y;

    if (dir.y >= -1e-4) { outColour = vec4(compand(tonemap(sky(dir))), 1.0); return; }
    float halfSide = uSide * 0.5;
    // Where the ray crosses the table's plane: beyond the table it goes on down to the room's floor.
    float tPlane = -uEye.y / dir.y;
    vec3 onPlane = uEye + dir * tPlane;
    if (abs(onPlane.x) > halfSide + 0.02 || abs(onPlane.z) > halfSide + 0.02) {
        outColour = vec4(compand(tonemap(roomFloor(uEye, dir))), 1.0);
        return;
    }
    float far = tPlane * 1.5 + 0.5;
    float t = march(uEye, dir, far);
    vec3 streamNormal;
    float ts = stream(uEye, dir, streamNormal);
    vec3 colour;
    if (ts > 0.0 && (t < 0.0 || ts < t)) {
        // The stream: glassy, showing what is behind it bent by the liquid's index.
        vec3 p = uEye + dir * ts;
        float cosI = clamp(-dot(dir, streamNormal), 0.0, 1.0);
        vec3 reflected = reflect(dir, streamNormal);
        vec3 skyColour = sky(reflected.y > 0.0 ? reflected : normalize(vec3(reflected.x, 0.05, reflected.z)));
        colour = vec3(0.0);
        for (int c = 0; c < 3; c++) {
            float n = uIor[c];
            float r = fresnel(cosI, 1.0, n);
            vec3 inside = refract(dir, streamNormal, 1.0 / n);
            vec3 outDir = refract(inside, -streamNormal, n);
            if (dot(outDir, outDir) < 0.5) outDir = inside;
            float radiusHere = streamRadiusAt(p.y);
            vec3 exitPoint = p + inside * (2.0 * radiusHere * cosI);
            float tb = march(exitPoint, outDir, far);
            float behind;
            if (tb > 0.0) {
                vec3 q = exitPoint + outDir * tb;
                float w = (ts + tb) * pixelAngle;
                vec2 f = filmAt(q.xz);
                behind = (f.x > THIN ? wetTable(q, w, n) : shadeTable(q, w))[c];
            } else behind = sky(outDir)[c];
            behind *= exp(-uAlpha[c] * 2.0 * radiusHere);
            colour[c] = r * skyColour[c] + (1.0 - r) * behind;
        }
        outColour = vec4(compand(tonemap(colour)), 1.0);
        return;
    }
    if (t < 0.0) { outColour = vec4(compand(tonemap(roomFloor(uEye, dir))), 1.0); return; }
    vec3 hit = uEye + dir * t;
    vec2 film = filmAt(hit.xz);
    bool wet = film.x > THIN && abs(hit.x) < halfSide && abs(hit.z) < halfSide;
    float w = t * pixelAngle / max(0.05, -dir.y);
    if (!wet) {
        outColour = vec4(compand(tonemap(shadeTable(hit, w))), 1.0);
        return;
    }
    vec3 normal = normalAt(hit.xz);
    float cosI = clamp(-dot(dir, normal), 0.0, 1.0);
    vec3 reflected = reflect(dir, normal);
    if (reflected.y < 0.01) reflected = normalize(vec3(reflected.x, 0.01, reflected.z));
    vec3 skyColour = sky(reflected);
    colour = vec3(0.0);
    for (int c = 0; c < 3; c++) {
        float n = uIor[c];
        float r = fresnel(cosI, 1.0, n);
        vec3 inside = refract(dir, normal, 1.0 / n);
        float through;
        if (dot(inside, inside) > 0.5 && inside.y < 0.0) {
            // Down through the film to the table: thin, so the floor under the entry point, shifted by the slant.
            float path = film.x / max(0.05, -inside.y);
            vec2 onTable = hit.xz + inside.xz * path;
            float wt = (t + path) * pixelAngle / (n * max(0.05, -inside.y));
            through = wetTable(vec3(onTable.x, floorAt(onTable), onTable.y), wt, n)[c] * exp(-uAlpha[c] * path * 2.0);
        } else through = skyColour[c];
        colour[c] = r * skyColour[c] + (1.0 - r) * through;
    }
    outColour = vec4(compand(tonemap(colour)), 1.0);
}
