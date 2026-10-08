// Beam mask for a profile spot: gate + iris + 4 framing shutters + gobo + focus blur.
// Rendered into a SubViewport by ProfileFixture and projected through the SpotLight3D.
//
// Mask space: p = (0,0) on the beam axis, length(p) = 1 at the gate edge, +y = beam top, +x = beam right,
// both as seen from behind the fixture looking along the beam. Image top = beam top (verified: Godot's
// spot projector maps the texture square onto the cone without flipping, image top = light +Y).
shader_type canvas_item;
render_mode blend_disabled, unshaded;

uniform float gate_radius = 0.9;          // gate circle radius as a fraction of the half texture
uniform float softness = 0.01;            // edge blur (focus), in gate radii
uniform float field_falloff = 0.85;       // relative intensity at the gate edge (flat field = 1)

// Framing shutters: x = top, y = right, z = bottom, w = left (the beam edge each blade cuts).
uniform vec4 shutter_depth = vec4(0.0);   // 0 = out, 1 = to the beam axis, 2 = fully across
uniform vec4 shutter_angle = vec4(0.0);   // blade tilt in degrees, counter-clockwise
uniform float shutter_rotation = 0.0;     // whole shutter assembly, degrees, counter-clockwise

uniform float iris = 1.0;                 // iris diameter relative to the gate (1 = open)

uniform bool gobo_enabled = false;
uniform sampler2D gobo : hint_default_white, filter_linear, repeat_disable;
uniform float gobo_rotation = 0.0;        // degrees, counter-clockwise
uniform float gobo_size = 1.0;            // gobo image width relative to the gate diameter

// Barndoors (PC / fresnel): x = top, y = right, z = bottom, w = left flap; 0 = open, 1 = closed across the lens.
// The flaps sit at the lens, far from any focal plane: their shadow edge is very soft.
uniform vec4 barndoor = vec4(0.0);
uniform float barndoor_rotation = 0.0;    // degrees, counter-clockwise
const float BARNDOOR_SOFT = 0.22;

// PAR: oval field. oval = (1, height / width); the field angle is the width. Rotation of the lamp (bottle).
uniform vec2 oval = vec2(1.0);
uniform float oval_rotation = 0.0;

vec2 rot(vec2 v, float deg) {
	float a = radians(deg);
	float c = cos(a);
	float s = sin(a);
	return vec2(c * v.x - s * v.y, s * v.x + c * v.y);
}

// 1 = light passes, 0 = blocked by the blade that enters from direction `dir`.
float blade(vec2 p, vec2 dir, float depth, float angle, float soft) {
	vec2 n = rot(dir, angle);
	float d = dot(p - dir * (1.0 - depth), n);
	return 1.0 - smoothstep(-soft, soft, d);
}

float gobo_at(vec2 p) {
	vec2 g = rot(p, -gobo_rotation) / gobo_size;
	vec2 uv = vec2(g.x, -g.y) * 0.5 + 0.5;
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) {
		return 0.0;
	}
	return texture(gobo, uv).r;
}

// Defocused gobo: average over a small disc (8 taps + centre).
float gobo_blurred(vec2 p, float soft) {
	float sum = gobo_at(p);
	for (int i = 0; i < 8; i++) {
		float a = float(i) * 0.785398;
		sum += gobo_at(p + vec2(cos(a), sin(a)) * soft);
	}
	return sum / 9.0;
}

void fragment() {
	vec2 p = (UV * 2.0 - 1.0) * vec2(1.0, -1.0) / gate_radius;

	float soft = max(softness, 0.002);
	vec2 po = rot(p, -oval_rotation) / oval;
	float r = length(po);

	float t = 1.0 - smoothstep(1.0 - soft, 1.0 + soft, r);
	t *= 1.0 - smoothstep(iris - soft, iris + soft, r);

	vec2 ps = rot(p, -shutter_rotation);
	t *= blade(ps, vec2(0.0, 1.0), shutter_depth.x, shutter_angle.x, soft);
	t *= blade(ps, vec2(1.0, 0.0), shutter_depth.y, shutter_angle.y, soft);
	t *= blade(ps, vec2(0.0, -1.0), shutter_depth.z, shutter_angle.z, soft);
	t *= blade(ps, vec2(-1.0, 0.0), shutter_depth.w, shutter_angle.w, soft);

	vec2 pb = rot(p, -barndoor_rotation);
	t *= blade(pb, vec2(0.0, 1.0), barndoor.x * 2.0 * (1.0 + BARNDOOR_SOFT) - BARNDOOR_SOFT, 0.0, BARNDOOR_SOFT);
	t *= blade(pb, vec2(1.0, 0.0), barndoor.y * 2.0 * (1.0 + BARNDOOR_SOFT) - BARNDOOR_SOFT, 0.0, BARNDOOR_SOFT);
	t *= blade(pb, vec2(0.0, -1.0), barndoor.z * 2.0 * (1.0 + BARNDOOR_SOFT) - BARNDOOR_SOFT, 0.0, BARNDOOR_SOFT);
	t *= blade(pb, vec2(-1.0, 0.0), barndoor.w * 2.0 * (1.0 + BARNDOOR_SOFT) - BARNDOOR_SOFT, 0.0, BARNDOOR_SOFT);

	if (gobo_enabled) {
		t *= gobo_blurred(p, soft);
	}

	t *= mix(1.0, field_falloff, r * r);
	COLOR = vec4(vec3(t), 1.0);
}
