// Occlusion map of a lamp (BeamOcclusion): a full-screen pass drawn by the lamp's occlusion camera, which renders only
// the Occluders layer. It turns the scene depth into the linear distance along the beam axis (view -Z), divided by
// `range` (the camera's far plane = the beam's length), into the red channel of the (HDR, linear) occlusion viewport.
// beam_volume.gdshader skips the haze samples behind it.
//
// One quad per lamp, a child of its occlusion camera; it is visible only while that camera renders. Every other camera
// (the audience's, other lamps') collapses it in the vertex shader: only a camera with exactly `occlusion_layers` as its
// cull mask and standing at this quad's position draws it.
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_test_disabled, depth_draw_never, shadows_disabled, fog_disabled, skip_vertex_transform;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform float range = 20.0;
uniform uint occlusion_layers;

void vertex() {
	bool mine = CAMERA_VISIBLE_LAYERS == occlusion_layers
		&& distance(CAMERA_POSITION_WORLD, NODE_POSITION_WORLD) < 0.001;
	// QuadMesh of 2 x 2: VERTEX.xy covers clip space; anyone else gets a degenerate triangle outside it.
	POSITION = mine ? vec4(VERTEX.xy, 0.5, 1.0) : vec4(2.0, 2.0, 2.0, 1.0);
}

void fragment() {
	float depth = texture(depth_texture, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float z = -view.z / view.w;
	ALBEDO = vec3(clamp(z / range, 0.0, 1.0), 0.0, 0.0);
	ALPHA = 1.0;
}
