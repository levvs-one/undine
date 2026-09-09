#version 300 es
// Undine: a pool seen from the deck. For each pixel the camera ray meets the surface, splits by the exact Fresnel
// equations of the liquid, reflects the sky or the pool's own walls and refracts per colour channel down to the
// floor or a wall, where the light of the lamp is what the caustic map says it is. Absorption over the path in the
// liquid comes from the liquid's own k table. The indices and absorption per metre are the liquid's numbers from
// Caustikon; nothing here is tuned to look like water, it is lit like water.
precision highp float;
out vec4 outColour;

uniform vec2 uResolution;
uniform vec3 uEye, uForward, uRight, uUp;
uniform float uTanHalf;

uniform sampler2D uState;      // height in r, metres, over the pool
uniform sampler2D uCaustic;    // lamp light on the floor plane, one on open floor, over uMargin × the pool
uniform float uCausticNorm;
uniform float uMargin;
uniform float uSide;           // pool side, metres
uniform float uDepth;          // floor below the rest level, metres
uniform float uRim;            // deck above the rest level, metres: the walls are dry up there
uniform vec3 uIor;             // index at 610, 550, 465 nm
uniform vec3 uAlpha;           // absorption per metre at the same wavelengths
uniform vec3 uLight;           // unit direction the light travels
uniform float uLampStrength;
uniform int uFloor;            // 0 tiles, 1 sand, 2 dark
uniform float uExposure;

const float TILE = 0.25;       // metres
const float AMBIENT = 0.24;    // skylight relative to the lamp at full strength

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

float height(vec2 xz) {
    return heightUv(xz / uSide + 0.5);
}

vec3 normalAt(vec2 xz) {
    vec2 texel = 1.0 / vec2(textureSize(uState, 0));
    float cell = uSide * texel.x;
    vec2 uv = xz / uSide + 0.5;
    float hl = heightUv(uv - vec2(texel.x, 0.0)), hr = heightUv(uv + vec2(texel.x, 0.0));
    float hd = heightUv(uv - vec2(0.0, texel.y)), hu = heightUv(uv + vec2(0.0, texel.y));
    return normalize(vec3(-(hr - hl) / (2.0 * cell), 1.0, -(hu - hd) / (2.0 * cell)));
}

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// A checkerboard averaged over a footprint of width w in tile units, so distant tiles go grey instead of shimmering.
float checker(vec2 q, float w) {
    vec2 ww = vec2(max(w, 0.001));
    vec2 i = 2.0 * (abs(fract((q - 0.5 * ww) / 2.0) - 0.5) - abs(fract((q + 0.5 * ww) / 2.0) - 0.5)) / ww;
    return 0.5 - 0.5 * i.x * i.y;
}

// Albedo of the pool lining at a point; w is the width the pixel covers there, in metres.
vec3 lining(vec2 xz, float w) {
    if (uFloor == 1) {
        float fine = 0.12 * (hash(floor(xz * 40.0)) - 0.5) * (1.0 - smoothstep(0.01, 0.05, w));
        float coarse = 0.06 * (hash(floor(xz * 7.0)) - 0.5) * (1.0 - smoothstep(0.07, 0.3, w));
        return vec3(0.86, 0.78, 0.60) * (0.55 + fine + coarse);
    }
    if (uFloor == 2) return vec3(0.06, 0.07, 0.08);
    vec2 q = xz / TILE;
    float wq = w / TILE;
    vec2 f = abs(fract(q) - 0.5);
    float grout = smoothstep(0.455 - wq, 0.455 + wq, max(f.x, f.y));
    vec3 colour = mix(vec3(0.72, 0.88, 0.90), vec3(0.28, 0.60, 0.74), checker(q, wq));
    return mix(colour, vec3(0.72, 0.73, 0.71), grout);
}

// The sky the water reflects is Preetham, Shirley and Smits's analytic daylight (1999): the Perez distributions of
// luminance and CIE chromaticity for a clear sky of turbidity 2.4, from the sun's elevation, converted from xyY to
// linear sRGB; the sun itself is a disc. A low sun reddens the horizon and dims the zenith, and the water's colour
// follows. Ripples show as the reflection slides across the sky's gradient, and as glints when a slope catches the sun.
const float TURBIDITY = 2.4;
const float SKY_SCALE = 0.06;    // kcd/m² of the model to the scene's units, chosen so a noon zenith reads 0.4

float perez(float cosTheta, float gamma, float A, float B, float C, float D, float E) {
    return (1.0 + A * exp(B / max(cosTheta, 0.01))) * (1.0 + C * exp(D * gamma) + E * cos(gamma) * cos(gamma));
}

vec3 sky(vec3 d) {
    vec3 toSun = -uLight;
    float T = TURBIDITY;
    float cosTheta = max(d.y, 0.0);
    float thetaS = acos(clamp(toSun.y, 0.0, 1.0));
    float gamma = acos(clamp(dot(d, toSun), -1.0, 1.0));
    // The zenith's luminance and chromaticity for this turbidity and sun.
    float chi = (4.0 / 9.0 - T / 120.0) * (3.14159265 - 2.0 * thetaS);
    float Yz = (4.0453 * T - 4.9710) * tan(chi) - 0.2155 * T + 2.4192;
    float t2 = thetaS * thetaS, t3 = t2 * thetaS;
    float xz = (0.00166 * t3 - 0.00375 * t2 + 0.00209 * thetaS) * T * T + (-0.02903 * t3 + 0.06377 * t2 - 0.03202 * thetaS + 0.00394) * T + (0.11693 * t3 - 0.21196 * t2 + 0.06052 * thetaS + 0.25886);
    float yz = (0.00275 * t3 - 0.00610 * t2 + 0.00317 * thetaS) * T * T + (-0.04214 * t3 + 0.08970 * t2 - 0.04153 * thetaS + 0.00516) * T + (0.15346 * t3 - 0.26756 * t2 + 0.06670 * thetaS + 0.26688);
    // The Perez distributions, each relative to its value at the zenith.
    float Y = Yz * perez(cosTheta, gamma, 0.1787 * T - 1.4630, -0.3554 * T + 0.4275, -0.0227 * T + 5.3251, 0.1206 * T - 2.5771, -0.0670 * T + 0.3703)
                 / perez(1.0, thetaS, 0.1787 * T - 1.4630, -0.3554 * T + 0.4275, -0.0227 * T + 5.3251, 0.1206 * T - 2.5771, -0.0670 * T + 0.3703);
    float x = xz * perez(cosTheta, gamma, -0.0193 * T - 0.2592, -0.0665 * T + 0.0008, -0.0004 * T + 0.2125, -0.0641 * T - 0.8989, -0.0033 * T + 0.0452)
                 / perez(1.0, thetaS, -0.0193 * T - 0.2592, -0.0665 * T + 0.0008, -0.0004 * T + 0.2125, -0.0641 * T - 0.8989, -0.0033 * T + 0.0452);
    float y = yz * perez(cosTheta, gamma, -0.0167 * T - 0.2608, -0.0950 * T + 0.0092, -0.0079 * T + 0.2102, -0.0441 * T - 1.6537, -0.0109 * T + 0.0529)
                 / perez(1.0, thetaS, -0.0167 * T - 0.2608, -0.0950 * T + 0.0092, -0.0079 * T + 0.2102, -0.0441 * T - 1.6537, -0.0109 * T + 0.0529);
    float Yl = max(Y, 0.0) * SKY_SCALE;
    y = max(y, 1e-3);
    vec3 XYZ = vec3(x * Yl / y, Yl, (1.0 - x - y) * Yl / y);
    vec3 s = max(mat3(3.2406, -0.9689, 0.0557, -1.5372, 1.8758, -0.2040, -0.4986, 0.0415, 1.0570) * XYZ, vec3(0.0));
    // The sun's disc (half a degree across) and the bright rim of its aureole the distribution above smooths over.
    float key = max(0.0, dot(d, toSun));
    s += vec3(1.0, 0.96, 0.88) * uLampStrength * (60.0 * smoothstep(0.99985, 0.99997, key) + 1.5 * pow(key, 40.0));
    return s;
}

vec3 tonemap(vec3 c) {
    c *= uExposure;
    return clamp((c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14), 0.0, 1.0);
}

vec3 compand(vec3 c) {
    return mix(12.92 * c, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}

// The caustic map, bilinear from the nearest four texels (the float texture itself is unfiltered).
vec3 lampMap(vec2 xz) {
    ivec2 size = textureSize(uCaustic, 0);
    vec2 p = clamp((xz / (uSide * uMargin) + 0.5) * vec2(size) - 0.5, vec2(0.0), vec2(size) - 1.0);
    ivec2 i = ivec2(floor(p));
    vec2 f = p - vec2(i);
    ivec2 j = min(i + 1, size - 1);
    vec3 a = texelFetch(uCaustic, i, 0).rgb, b = texelFetch(uCaustic, ivec2(j.x, i.y), 0).rgb;
    vec3 c = texelFetch(uCaustic, ivec2(i.x, j.y), 0).rgb, d = texelFetch(uCaustic, j, 0).rgb;
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y) * uCausticNorm;
}

// Light on the floor at xz: skylight, shaded in the corners, plus the lamp through the caustic map.
vec3 floorLight(vec2 xz) {
    float halfSide = uSide * 0.5;
    vec2 gap = halfSide - abs(xz);
    float ao = 1.0 - 0.4 * (exp(-gap.x / 0.35) + exp(-gap.y / 0.35));
    return vec3(AMBIENT * ao) + uLampStrength * max(0.0, -uLight.y) * lampMap(xz);
}

// Light on a wall point under water: the beam of the lamp that passes it would land on the floor plane further on;
// the map there, scaled from the floor's incidence to the wall's, is the light on the wall.
vec3 wallLight(vec3 p, vec3 wallNormal, vec3 lampInside) {
    float halfSide = uSide * 0.5;
    float fromFloor = p.y + uDepth;
    float along = wallNormal.x != 0.0 ? halfSide - abs(p.z) : halfSide - abs(p.x);
    float ao = 1.0 - 0.4 * (exp(-fromFloor / 0.35) + exp(-along / 0.35));
    float cosWall = max(0.0, dot(-lampInside, wallNormal));
    float cosFloor = max(1e-3, -lampInside.y);
    // Back up the beam to where it crossed the rest plane: over the deck means the deck's shadow.
    vec2 entry = p.xz - lampInside.xz * (p.y / lampInside.y);
    bool shadowed = abs(entry.x) > halfSide || abs(entry.y) > halfSide;
    float t = fromFloor / cosFloor;
    vec2 landing = p.xz + lampInside.xz * t;
    bool mapped = abs(landing.x) < halfSide * uMargin && abs(landing.y) < halfSide * uMargin;
    vec3 focus = mapped ? lampMap(landing) : vec3(1.0);
    vec3 lamp = cosWall > 0.0 && !shadowed ? focus * (cosWall / cosFloor) : vec3(0.0);
    return vec3(AMBIENT * ao) + uLampStrength * max(0.0, -uLight.y) * lamp;
}

// A wall point above the waterline: dry tiles lit by the sky and the lamp, a wet band where the water laps.
vec3 dryWall(vec3 p, vec3 wallNormal, float w) {
    vec2 xz = wallNormal.x != 0.0 ? vec2(p.z, p.y) : vec2(p.x, p.y);
    vec3 albedo = lining(xz, w);
    float wet = 1.0 - 0.35 * (1.0 - smoothstep(0.0, 0.03, p.y));
    float direct = max(0.0, dot(-uLight, wallNormal));
    vec3 light = vec3(AMBIENT * 1.4) + uLampStrength * vec3(1.0, 0.97, 0.92) * direct;
    return albedo * wet * light;
}

// The deck around the pool: coping stones along the rim, then paving; lit by the sky and the lamp directly.
vec3 deck(vec3 hit, float w) {
    float halfSide = uSide * 0.5;
    float edge = max(abs(hit.x), abs(hit.z)) - halfSide;
    float grain = 0.05 * (hash(floor(hit.xz * 30.0)) - 0.5) * (1.0 - smoothstep(0.02, 0.08, w));
    vec3 stone = vec3(0.62, 0.58, 0.52) * (1.0 + grain);
    vec3 coping = vec3(0.78, 0.75, 0.69);
    vec3 albedo = mix(coping, stone, smoothstep(0.28, 0.32, edge));
    float seamDistance = min(abs(fract(hit.x) - 0.5), abs(fract(hit.z) - 0.5));
    float seam = 1.0 - 0.25 * (1.0 - smoothstep(0.0, 0.012 + w, seamDistance)) * step(edge, 0.30);
    float lip = 1.0 - 0.3 * exp(-edge / 0.02);
    vec3 light = vec3(AMBIENT * 1.6) + uLampStrength * vec3(1.0, 0.97, 0.92) * max(0.0, -uLight.y);
    return albedo * seam * lip * light;
}

// Where a ray from p along d leaves the pool footprint, and the wall it leaves through.
float exitWall(vec3 p, vec3 d, out vec3 wallNormal) {
    float halfSide = uSide * 0.5;
    float best = 1e9;
    wallNormal = vec3(0.0);
    if (abs(d.x) > 1e-6) {
        float tx = ((d.x > 0.0 ? halfSide : -halfSide) - p.x) / d.x;
        if (tx < best) { best = tx; wallNormal = vec3(d.x > 0.0 ? -1.0 : 1.0, 0.0, 0.0); }
    }
    if (abs(d.z) > 1e-6) {
        float tz = ((d.z > 0.0 ? halfSide : -halfSide) - p.z) / d.z;
        if (tz < best) { best = tz; wallNormal = vec3(0.0, 0.0, d.z > 0.0 ? -1.0 : 1.0); }
    }
    return best;
}

void main() {
    vec2 px = gl_FragCoord.xy;
    float aspect = uResolution.x / uResolution.y;
    float sx = (px.x / uResolution.x * 2.0 - 1.0) * uTanHalf * aspect;
    float sy = (px.y / uResolution.y * 2.0 - 1.0) * uTanHalf;
    vec3 dir = normalize(uForward + uRight * sx + uUp * sy);
    float pixelAngle = 2.0 * uTanHalf / uResolution.y;
    float halfSide = uSide * 0.5;

    if (dir.y >= -1e-4) { outColour = vec4(compand(tonemap(sky(dir))), 1.0); return; }
    // The deck plane first: outside the pool it is the deck, inside the ray drops past the dry walls to the water.
    float tDeck = (uRim - uEye.y) / dir.y;
    vec3 onDeck = uEye + dir * tDeck;
    if (tDeck > 0.0 && (abs(onDeck.x) > halfSide || abs(onDeck.z) > halfSide)) {
        float w = tDeck * pixelAngle / max(0.05, -dir.y);
        outColour = vec4(compand(tonemap(deck(onDeck, w))), 1.0);
        return;
    }
    float t = -uEye.y / dir.y;
    vec3 hit = uEye + dir * t;
    // Meet the displaced surface: corrections toward y = h(x, z) along the ray.
    for (int i = 0; i < 3; i++) {
        float h = height(hit.xz);
        t = (h - uEye.y) / dir.y;
        hit = uEye + dir * t;
    }
    if (abs(hit.x) > halfSide || abs(hit.z) > halfSide) {
        // The ray met a wall above the water before reaching the surface.
        vec3 wallNormal;
        float tw = exitWall(onDeck, dir, wallNormal) + tDeck;
        vec3 onWall = uEye + dir * tw;
        float w = tw * pixelAngle / max(0.05, abs(dot(dir, wallNormal)));
        outColour = vec4(compand(tonemap(dryWall(onWall, wallNormal, w))), 1.0);
        return;
    }

    vec3 normal = normalAt(hit.xz);
    float cosI = clamp(-dot(dir, normal), 0.0, 1.0);
    vec3 reflected = reflect(dir, normal);
    if (reflected.y < 0.002) reflected = normalize(vec3(reflected.x, 0.002, reflected.z));
    // The reflection: a dry wall of the pool when the ray meets one below the deck level, the sky otherwise.
    vec3 reflectedColour;
    {
        vec3 wallNormal;
        float tw = exitWall(hit, reflected, wallNormal);
        vec3 onWall = hit + reflected * tw;
        if (onWall.y < uRim) {
            float w = (t + tw) * pixelAngle / max(0.05, abs(dot(reflected, wallNormal)));
            reflectedColour = dryWall(onWall, wallNormal, w);
        } else {
            reflectedColour = sky(reflected);
        }
    }
    vec3 lampInside = refract(uLight, vec3(0.0, 1.0, 0.0), 1.0 / uIor.y);

    vec3 colour = vec3(0.0);
    for (int c = 0; c < 3; c++) {
        float n = uIor[c];
        float r = fresnel(cosI, 1.0, n);
        vec3 inside = refract(dir, normal, 1.0 / n);
        float through = 0.0;
        if (dot(inside, inside) > 0.5 && inside.y < 0.0) {
            // Down to the floor, unless a wall comes first: four vertical walls in the same lining.
            float drop = uDepth + hit.y;
            float path = drop / (-inside.y);
            vec3 wallNormal;
            float wall = exitWall(hit, inside, wallNormal);
            // The footprint of the pixel where the ray lands, widened by the grazing angle; refraction narrows it by n.
            if (wall < path) {
                vec3 onWall = hit + inside * wall;
                float w = (t + wall) * pixelAngle / (n * max(0.05, abs(dot(inside, wallNormal))));
                vec2 wallXz = wallNormal.x != 0.0 ? vec2(onWall.z, onWall.y) : vec2(onWall.x, onWall.y);
                through = (lining(wallXz, w) * wallLight(onWall, wallNormal, lampInside))[c] * exp(-uAlpha[c] * wall);
            } else {
                vec2 onFloor = hit.xz + inside.xz * path;
                float w = (t + path) * pixelAngle / (n * max(0.05, -inside.y));
                through = (lining(onFloor, w) * floorLight(onFloor))[c] * exp(-uAlpha[c] * path);
            }
        } else {
            through = reflectedColour[c];
        }
        colour[c] = r * reflectedColour[c] + (1.0 - r) * through;
    }
    outColour = vec4(compand(tonemap(colour)), 1.0);
}
