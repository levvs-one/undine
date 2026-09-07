#version 300 es
// Undine: one radix-2 Stockham pass of a square FFT, along one axis, on a complex RG texture. Run log2(size) passes
// with uSpan = 1, 2, 4, … size/2 per axis; the input in natural order comes out in natural order. Forward with
// uInverse = 0; the inverse conjugates the twiddles and leaves the 1/size² to the caller.
precision highp float;
precision highp int;

uniform sampler2D uInput;
uniform int uSpan;             // size of the sub-transforms already done
uniform int uAxis;             // 0 along x, 1 along y
uniform int uInverse;
out vec2 outValue;

vec2 fetch(ivec2 p, int along) {
    return texelFetch(uInput, uAxis == 0 ? ivec2(along, p.y) : ivec2(p.x, along), 0).rg;
}

void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    int size = uAxis == 0 ? textureSize(uInput, 0).x : textureSize(uInput, 0).y;
    int o = uAxis == 0 ? p.x : p.y;
    int k = o % uSpan;
    int group = o / (2 * uSpan);
    int j = group * uSpan + k;
    vec2 a = fetch(p, j);
    vec2 b = fetch(p, j + size / 2);
    float angle = (uInverse == 1 ? 6.283185307 : -6.283185307) * float(k) / float(2 * uSpan);
    vec2 w = vec2(cos(angle), sin(angle));
    vec2 wb = vec2(w.x * b.x - w.y * b.y, w.x * b.y + w.y * b.x);
    outValue = (o % (2 * uSpan)) < uSpan ? a + wb : a - wb;
}
