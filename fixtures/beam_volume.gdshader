// Visible beam in haze, raymarched through the fixture's projection frustum and shaped by the same mask
// texture as the light. Replaces Godot's volumetric fog for this light, because Godot's fog ignores
// light_projector (CLAUDE.md, open question 2).
//
// Drawn on a box around the frustum: back faces, no depth test, additive. The ray is clipped
// analytically to the frustum and to the opaque scene depth, then sampled `steps` times.
// Not handled yet: occlusion by scenery inside the beam (borders, legs), extinction.
shader_type spatial;
render_mode unshaded, blend_add, cull_front, depth_test_disabled, depth_draw_never, shadows_disabled, fog_disabled;

global uniform float haze_density;

uniform sampler2D mask : filter_linear, repeat_disable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform mat4 world_to_light;       // light looks along -Z, +Y = beam top
uniform float tan_half = 0.25;     // tan of the frustum half angle (= projector edge)
uniform float beam_length = 20.0;
uniform vec3 light_color = vec3(1.0);   // colour * energy
uniform float anisotropy = 0.6;
uniform float gain = 1.0;
uniform int steps = 48;

// Restrict [t0, t1] to the half-space dot(n, o + t d) <= c.
void clip(vec3 n, float c, vec3 o, vec3 d, inout float t0, inout float t1) {
	float dn = dot(n, d);
	float num = c - dot(n, o);
	if (abs(dn) < 1e-6) {
		if (num < 0.0) t1 = -1.0;
	} else if (dn < 0.0) {
		t0 = max(t0, num / dn);
	} else {
		t1 = min(t1, num / dn);
	}
}

float henyey_greenstein(float cos_theta, float g) {
	float g2 = g * g;
	return (1.0 - g2) / (4.0 * PI * pow(1.0 + g2 - 2.0 * g * cos_theta, 1.5));
}

void fragment() {
	float depth = texture(depth_texture, SCREEN_UV).r;
	vec4 scene = INV_VIEW_MATRIX * INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	scene.xyz /= scene.w;

	vec3 to_scene = scene.xyz - CAMERA_POSITION_WORLD;
	float scene_t = length(to_scene);
	vec3 o = (world_to_light * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz;
	vec3 d = (world_to_light * vec4(to_scene / scene_t, 0.0)).xyz;

	float t0 = 0.0;
	float t1 = scene_t;
	clip(vec3(1.0, 0.0, tan_half), 0.0, o, d, t0, t1);
	clip(vec3(-1.0, 0.0, tan_half), 0.0, o, d, t0, t1);
	clip(vec3(0.0, 1.0, tan_half), 0.0, o, d, t0, t1);
	clip(vec3(0.0, -1.0, tan_half), 0.0, o, d, t0, t1);
	clip(vec3(0.0, 0.0, 1.0), -0.05, o, d, t0, t1);
	clip(vec3(0.0, 0.0, -1.0), beam_length, o, d, t0, t1);
	if (t1 <= t0) {
		discard;
	}

	// Interleaved gradient noise hides the banding of the fixed step count.
	float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	float dt = (t1 - t0) / float(steps);
	float sum = 0.0;
	for (int i = 0; i < steps; i++) {
		vec3 p = o + d * (t0 + (float(i) + jitter) * dt);
		vec2 q = p.xy / (-p.z * tan_half);
		float m = textureLod(mask, vec2(q.x, -q.y) * 0.5 + 0.5, 0.0).r;
		float r2 = max(dot(p, p), 0.01);
		sum += m * henyey_greenstein(dot(p, -d) * inversesqrt(r2), anisotropy) / r2;
	}
	ALBEDO = light_color * (haze_density * gain * sum * dt);
}
