// Undine: water surface for Godot 4 on a subdivided plane. The height texture comes from your own simulation or from
// Undine's Surface on the CPU (upload Heights as an R32F image each frame). The liquid's constants are in liquids.json.
shader_type spatial;
render_mode cull_back, depth_draw_opaque;

uniform sampler2D height_texture : filter_linear, repeat_disable;   // metres, R channel
uniform sampler2D floor_texture : source_color, filter_linear_mipmap;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float side_metres = 4.0;
uniform float depth_metres = 1.2;
uniform vec3 ior = vec3(1.33273, 1.33468, 1.33867);            // water at 610, 550, 465 nm
uniform vec3 absorb_per_metre = vec3(0.24748, 0.04478, 0.02616); // water, Hale and Querry 1973
uniform vec3 sky_colour : source_color = vec3(0.45, 0.58, 0.78);
uniform float lamp = 1.0;

float fresnel_exact(float cos_i, float n1, float n2) {
    float eta = n1 / n2;
    float sin_t2 = eta * eta * (1.0 - cos_i * cos_i);
    if (sin_t2 >= 1.0) return 1.0;
    float cos_t = sqrt(1.0 - sin_t2);
    float rs = (n1 * cos_i - n2 * cos_t) / (n1 * cos_i + n2 * cos_t);
    float rp = (n2 * cos_i - n1 * cos_t) / (n2 * cos_i + n1 * cos_t);
    return 0.5 * (rs * rs + rp * rp);
}

void vertex() {
    float h = texture(height_texture, UV).r;
    VERTEX.y += h;
    vec2 texel = 1.0 / vec2(textureSize(height_texture, 0));
    float cell = side_metres * texel.x;
    float hl = texture(height_texture, UV - vec2(texel.x, 0.0)).r, hr = texture(height_texture, UV + vec2(texel.x, 0.0)).r;
    float hd = texture(height_texture, UV - vec2(0.0, texel.y)).r, hu = texture(height_texture, UV + vec2(0.0, texel.y)).r;
    NORMAL = normalize(vec3(-(hr - hl) / (2.0 * cell), 1.0, -(hu - hd) / (2.0 * cell)));
}

void fragment() {
    vec3 n = normalize(NORMAL);
    vec3 view_world = normalize((INV_VIEW_MATRIX * vec4(-VIEW, 0.0)).xyz);   // toward the surface, world space
    vec3 n_world = normalize((INV_VIEW_MATRIX * vec4(n, 0.0)).xyz);
    float cos_i = clamp(dot(-view_world, n_world), 0.0, 1.0);
    vec3 colour = vec3(0.0);
    for (int c = 0; c < 3; c++) {
        float r = fresnel_exact(cos_i, 1.0, ior[c]);
        vec3 inside = refract(view_world, n_world, 1.0 / ior[c]);
        float through = 0.0;
        if (dot(inside, inside) > 0.5 && inside.y < 0.0) {
            float path = depth_metres / (-inside.y);
            vec2 floor_uv = UV + inside.xz * path / side_metres;
            through = texture(floor_texture, floor_uv)[c] * exp(-absorb_per_metre[c] * path) * (0.35 + 0.65 * lamp);
        }
        colour[c] = r * sky_colour[c] + (1.0 - r) * through;
    }
    ALBEDO = colour;
    SPECULAR = 0.25;      // Godot's SPECULAR 0.5 = 4% head-on; water's 2% is half that
    ROUGHNESS = 0.02;
    METALLIC = 0.0;
}
