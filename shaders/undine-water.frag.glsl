#version 300 es
// Undine: a pool seen from above. For each pixel the camera ray meets the surface, splits by the exact Fresnel
// equations of the liquid, reflects the sky and refracts per colour channel down to the floor, where the light of
// the lamp is what the caustic map says it is. Absorption over the path in the liquid comes from the liquid's own
// k table. The indices and absorption per metre are the liquid's numbers from Caustikon; nothing here is tuned.
precision highp float;
out vec4 outColour;

uniform vec2 uResolution;
uniform vec3 uEye, uForward, uRight, uUp;
uniform float uTanHalf;

uniform sampler2D uState;      // height in r, metres, over the pool
uniform sampler2D uCaustic;    // lamp light on the floor, one on open floor
uniform float uCausticNorm;
uniform float uSide;           // pool side, metres
uniform float uDepth;          // floor below the rest level, metres
uniform vec3 uIor;             // index at 610, 550, 465 nm
uniform vec3 uAlpha;           // absorption per metre at the same wavelengths
uniform vec3 uLight;           // unit direction the light travels
uniform float uLampStrength;
uniform int uFloor;            // 0 tiles, 1 sand, 2 dark
uniform float uExposure;

float fresnel(float cosI, float n1, float n2) {
    float eta = n1 / n2;
    float sinT2 = eta * eta * (1.0 - cosI * cosI);
    if (sinT2 >= 1.0) return 1.0;
    float cosT = sqrt(1.0 - sinT2);
    float rs = (n1 * cosI - n2 * cosT) / (n1 * cosI + n2 * cosT);
    float rp = (n2 * cosI - n1 * cosT) / (n2 * cosI + n1 * cosT);
    return 0.5 * (rs * rs + rp * rp);
}

float height(vec2 xz) {
    return texture(uState, xz / uSide + 0.5).r;
}

vec3 normalAt(vec2 xz) {
    vec2 texel = 1.0 / vec2(textureSize(uState, 0));
    float cell = uSide * texel.x;
    vec2 uv = xz / uSide + 0.5;
    float hl = texture(uState, uv - vec2(texel.x, 0.0)).r, hr = texture(uState, uv + vec2(texel.x, 0.0)).r;
    float hd = texture(uState, uv - vec2(0.0, texel.y)).r, hu = texture(uState, uv + vec2(0.0, texel.y)).r;
    return normalize(vec3(-(hr - hl) / (2.0 * cell), 1.0, -(hu - hd) / (2.0 * cell)));
}

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

vec3 floorColour(vec2 xz) {
    if (uFloor == 1) {
        float g = 0.55 + 0.12 * (hash(floor(xz * 40.0)) - 0.5) + 0.06 * (hash(floor(xz * 7.0)) - 0.5);
        return vec3(0.86, 0.78, 0.60) * g;
    }
    if (uFloor == 2) return vec3(0.06, 0.07, 0.08);
    // Pool tiles 25 cm with a grout line; two blues so the refraction has edges to bend.
    vec2 tile = xz / 0.25;
    vec2 f = abs(fract(tile) - 0.5);
    float grout = 1.0 - smoothstep(0.44, 0.48, max(f.x, f.y));
    bool dark = mod(floor(tile.x) + floor(tile.y), 2.0) == 0.0;
    vec3 colour = dark ? vec3(0.42, 0.62, 0.72) : vec3(0.82, 0.90, 0.92);
    return mix(vec3(0.75, 0.76, 0.74), colour, grout);
}

vec3 sky(vec3 d) {
    float t = clamp(d.y * 0.5 + 0.5, 0.0, 1.0);
    vec3 s = mix(vec3(0.62, 0.66, 0.72), vec3(0.24, 0.40, 0.70), pow(t, 0.6));
    vec3 toLamp = -uLight;
    float key = max(0.0, dot(d, toLamp));
    s += vec3(1.0, 0.97, 0.90) * uLampStrength * (60.0 * pow(key, 900.0) + 1.5 * pow(key, 12.0));
    return s;
}

vec3 tonemap(vec3 c) {
    c *= uExposure;
    return clamp((c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14), 0.0, 1.0);
}

float compand(float c) {
    return c <= 0.0031308 ? 12.92 * c : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
}

// Light at a floor point: the room plus the lamp through the caustic map, absorbed on the way back up.
vec3 floorLit(vec2 xz, float pathMetres, int channel) {
    vec2 uv = xz / uSide + 0.5;
    vec3 lamp = texture(uCaustic, uv).rgb * uCausticNorm;
    vec3 light = vec3(0.35) + uLampStrength * 0.9 * max(0.0, -uLight.y) * lamp;
    vec3 colour = floorColour(xz) * light;
    float t = exp(-uAlpha[channel] * pathMetres);
    return colour * t;
}

void main() {
    vec2 px = gl_FragCoord.xy;
    float aspect = uResolution.x / uResolution.y;
    float sx = (px.x / uResolution.x * 2.0 - 1.0) * uTanHalf * aspect;
    float sy = (px.y / uResolution.y * 2.0 - 1.0) * uTanHalf;
    vec3 dir = normalize(uForward + uRight * sx + uUp * sy);

    if (dir.y >= -1e-4) { outColour = vec4(vec3(compand(tonemap(sky(dir)).r), compand(tonemap(sky(dir)).g), compand(tonemap(sky(dir)).b)), 1.0); return; }
    float t = -uEye.y / dir.y;
    vec3 hit = uEye + dir * t;
    float halfSide = uSide * 0.5;
    if (abs(hit.x) > halfSide || abs(hit.z) > halfSide) {
        // The deck around the pool.
        float edge = max(abs(hit.x), abs(hit.z)) - halfSide;
        vec3 deck = vec3(0.80, 0.77, 0.70) * (0.9 - 0.25 * exp(-edge * 6.0));
        vec3 c = tonemap(deck);
        outColour = vec4(compand(c.r), compand(c.g), compand(c.b), 1.0);
        return;
    }

    // Meet the displaced surface: two corrections toward y = h(x, z) along the ray.
    for (int i = 0; i < 2; i++) {
        float h = height(hit.xz);
        t = (h - uEye.y) / dir.y;
        hit = uEye + dir * t;
    }
    vec3 normal = normalAt(hit.xz);
    float cosI = clamp(-dot(dir, normal), 0.0, 1.0);
    vec3 reflected = reflect(dir, normal);
    vec3 skyColour = sky(reflected);

    vec3 colour = vec3(0.0);
    for (int c = 0; c < 3; c++) {
        float n = uIor[c];
        float r = fresnel(cosI, 1.0, n);
        vec3 inside = refract(dir, normal, 1.0 / n);
        float through = 0.0;
        if (dot(inside, inside) > 0.5 && inside.y < 0.0) {
            // Down to the floor, unless a wall comes first: the pool has four vertical walls in the same tiles.
            float drop = uDepth + hit.y;
            float path = drop / (-inside.y);
            float wall = 1e9;
            if (abs(inside.x) > 1e-6) wall = min(wall, ((inside.x > 0.0 ? halfSide : -halfSide) - hit.x) / inside.x);
            if (abs(inside.z) > 1e-6) wall = min(wall, ((inside.z > 0.0 ? halfSide : -halfSide) - hit.z) / inside.z);
            if (wall < path) {
                vec3 onWall = hit + inside * wall;
                // Wall tiles run along the wall and down it; the lamp reaches them through the water more weakly.
                vec2 wallXz = abs(inside.x) * abs(halfSide - abs(onWall.x)) < abs(inside.z) * abs(halfSide - abs(onWall.z)) ? vec2(onWall.z, onWall.y) : vec2(onWall.x, onWall.y);
                vec3 wallColour = floorColour(wallXz) * (0.35 + uLampStrength * 0.45 * max(0.0, -uLight.y));
                through = wallColour[c] * exp(-uAlpha[c] * wall);
            } else {
                through = floorLit(hit.xz + inside.xz * path, path, c)[c];
            }
        }
        colour[c] = r * skyColour[c] + (1.0 - r) * through;
    }
    colour = tonemap(colour);
    outColour = vec4(compand(colour.r), compand(colour.g), compand(colour.b), 1.0);
}
