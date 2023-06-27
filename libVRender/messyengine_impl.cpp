#include "me_impl.h"
#include "groundgrid.hpp"
#include "camera.hpp"
#include "init_impl.hpp"
#include "objects.hpp"

#include "interfaces.hpp"

void GLAPIENTRY DebugCallback(GLenum source, GLenum type, GLuint id, GLenum severity, GLsizei length, const GLchar* message, const void* userParam) {
	std::cout << "OpenGL Debug Message: " << message << std::endl;
}

int lastW, lastH;

void _draw_gltf_shadows(const glm::mat4& vm, const glm::mat4& pm)
{
	for (auto& class_ : classes)
		class_.second->render(vm, pm, true);
}

void _draw_gltf_primitives(const glm::mat4& vm, const glm::mat4& pm)
{
	for (auto& class_ : classes)
	{
		class_.second->render(vm, pm, false);
	}
}

// constants for atmospheric scattering
const float e = 2.71828182845904523536028747135266249775724709369995957;
const float pi = 3.141592653589793238462643383279502884197169;

// wavelength of used primaries, according to preetham
const glm::vec3 lambda = glm::vec3(680E-9, 550E-9, 450E-9);
// this pre-calcuation replaces older TotalRayleigh(vec3 lambda) function:
// (8.0 * pow(pi, 3.0) * pow(pow(n, 2.0) - 1.0, 2.0) * (6.0 + 3.0 * pn)) / (3.0 * N * pow(lambda, vec3(4.0)) * (6.0 - 7.0 * pn))
const glm::vec3 totalRayleigh = glm::vec3(5.804542996261093E-6, 1.3562911419845635E-5, 3.0265902468824876E-5);

// mie stuff
// K coefficient for the primaries
const float v = 4.0;
const glm::vec3 K = glm::vec3(0.686, 0.678, 0.666);
// MieConst = pi * pow( ( 2.0 * pi ) / lambda, vec3( v - 2.0 ) ) * K
const glm::vec3 MieConst = glm::vec3(1.8399918514433978E14, 2.7798023919660528E14, 4.0790479543861094E14);

// earth shadow hack
// cutoffAngle = pi / 1.95;
const float cutoffAngle = 1.6110731556870734;
const float steepness = 1.5;
const float EE = 1000.0;

float sunIntensity(float zenithAngleCos) {
	zenithAngleCos = glm::clamp(zenithAngleCos, -1.0f, 1.0f);
	return EE * glm::max(0.0, 1.0 - pow(e, -((cutoffAngle - acos(zenithAngleCos)) / steepness)));
}

glm::vec3 totalMie(float T) {
	float c = (0.2 * T) * 10E-18;
	return 0.434f * c * MieConst;
}

float rayleigh = 2.63;
float turbidity = 0.1;
float mieCoefficient = 0.016;
float mieDirectionalG = 0.935;
float toneMappingExposure = 0.401;

void _draw_skybox(const glm::mat4& vm, const glm::mat4& pm)
{
	glm::vec3 sunPosition= glm::vec3(1.0f, 0.0f, 0.0f);
	glm::vec3 up = glm::vec3(0.0f, 0.0f, 1.0f);

	ImGui::DragFloat("rayleigh", &rayleigh, 0.01f, 0, 5);
	ImGui::DragFloat("turbidity", &turbidity, 0.01f, 0, 30);
	ImGui::DragFloat("mieCoefficient", &mieCoefficient, 0.0001, 0, 0.1);
	ImGui::DragFloat("mieDirectionalG", &mieDirectionalG, 0.0001, 0, 1);
	ImGui::DragFloat("toneMappingExposure", &toneMappingExposure, 0.001, 0, 1);

	auto vSunDirection = normalize(sunPosition);
	
	auto vSunE = sunIntensity(dot(vSunDirection, up)); // up vector can be obtained from view matrix.

	auto vSunfade = 1.0 - glm::clamp(1.0 - exp((sunPosition.z / 450000.0f)), 0.0, 1.0);

	float rayleighCoefficient = rayleigh - (1.0 * (1.0 - vSunfade));

	// extinction (absorbtion + out scattering)
	// rayleigh coefficients
	auto vBetaR = totalRayleigh * rayleighCoefficient;

	// mie coefficients
	auto vBetaM = totalMie(turbidity) * mieCoefficient;

	glm::mat4 invVm = glm::inverse(vm);
	glm::mat4 invPm = glm::inverse(pm); 

	// edl composing.
	sg_apply_pipeline(graphics_state.skybox.pip);
	sg_apply_bindings(graphics_state.skybox.bind);
	auto sky_fs = sky_fs_t{
		.mieDirectionalG = mieDirectionalG,
		.invVM = invVm,
		.invPM = invPm,
		.vSunDirection = vSunDirection,
		.vSunfade = float(vSunfade),
		.vBetaR = vBetaR,
		.vBetaM = vBetaM,
		.vSunE = vSunE,
		.toneMappingExposure = toneMappingExposure
	};
	sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_window, SG_RANGE(sky_fs));
	sg_draw(0, 4, 1);
}


SSAOUniforms_t ssao_uniforms{
	.uSampleRadius = 25,
	.uBias = 0.04,
	.uAttenuation = {0.8f,0.2f},
};

void DrawWorkspace(int w, int h)
{
	// draw
	camera->Resize(w, h);
	camera->UpdatePosition();

	auto vm = camera->GetViewMatrix();
	auto pm = camera->GetProjectionMatrix();

	auto pv = pm * vm;

	// Graphics part:
	// Orbital Camera(perspective/orthogonal, height focus controllable), infinite grid, ruler, guizmo
	// toggle: mossaic reflective darkgray ground, blue sky, sun. (screen space reflection)
	// PC: eye-dome, near big far not vanish; PC meshes LOD, degrade to volume distance enough,
	// MatCap Material, Gltf loader, OIT, soft shadow, SSAO
	// objects: volumetric, point cloud, lidar2d-map, line, mesh(gltf), picking via GPU(padded), object border,
	// effect: bloom.
	// maybe: plain bgfx?
	// graphics start:
	// part1: grid
	// part2: point+line pass
	// part3: eye-dome
	// part4: mesh(matcap)
	// part5: point2d map.

	if (lastW!=w ||lastH!=h)
	{
		ResetEDLPass();
		GenPasses(w, h);
	}
	lastW = w;
	lastH = h;
	 
	{
		// first draw point clouds, so edl only reference point's depth => pc_depth.
		sg_begin_pass(graphics_state.pc_primitive.pass, &graphics_state.pc_primitive.pass_action);
		sg_apply_pipeline(point_cloud_simple_pip);
		for (auto& entry : pointClouds)
		{
			// perform some culling.
			const auto& [pc, pcBuf, colorBuf] = entry.second;
			sg_apply_bindings(sg_bindings{ .vertex_buffers = {pcBuf, colorBuf} });
			vs_params_t vs_params{ .mvp = pv * translate(glm::mat4(1.0f), pc.position) * mat4_cast(pc.quaternion) , .dpi = camera->dpi };
			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_vs_params, SG_RANGE(vs_params));
			sg_draw(0, pc.x_y_z_Sz.size(), 1);
		}
		sg_end_pass();

		sg_begin_pass(graphics_state.primitives.pass, &graphics_state.primitives.pass_action);
		_draw_gltf_primitives(vm, pm);
		sg_end_pass();
		 

		
		// draw lines

		// draw gltf. (gpu selecting/selected)

		// === post processing ===
		// ---ssao---
		ssao_uniforms.P = pm;
		ssao_uniforms.iP = glm::inverse(pm);
		ssao_uniforms.iV = glm::inverse(vm);
		ssao_uniforms.cP = camera->position;
		ssao_uniforms.uDepthRange[0] = cam_near;
		ssao_uniforms.uDepthRange[1] = cam_far;
		ImGui::DragFloat("uSampleRadius", &ssao_uniforms.uSampleRadius, 0.1, 0, 100);
		ImGui::DragFloat("uBias", &ssao_uniforms.uBias, 0.003, -0.5, 0.5);
		ImGui::DragFloat2("uAttenuation", ssao_uniforms.uAttenuation, 0.04, -10, 10);
		// ImGui::DragFloat2("uDepthRange", ssao_uniforms.uDepthRange, 0.05, 0, 100);

		sg_begin_pass(graphics_state.ssao.pass, &graphics_state.ssao.pass_action);
		sg_apply_pipeline(graphics_state.ssao.pip);
		sg_apply_bindings(graphics_state.ssao.bindings);
		sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(ssao_uniforms));
		sg_draw(0, 4, 1);
		sg_end_pass();

		sg_begin_pass(graphics_state.ssao.blur_pass, &graphics_state.ssao.pass_action);
		sg_apply_pipeline(graphics_state.kuwahara_blur.pip);
		sg_apply_bindings(graphics_state.ssao.blur_bindings);
		sg_draw(0, 4, 1);
		sg_end_pass();

		// sobel corner (hovering and selected).


		// --- shadow map pass ---
		// sg_begin_pass(graphics_state.shadow_map.pass, &graphics_state.shadow_map.pass_action);
		// _draw_gltf_shadows(vm, pm);
		// sg_apply_pipeline(graphics_state.pc_pip_depth);
		// for (auto& entry : pointClouds)
		// {
		// 	// perform some culling.
		// 	const auto& [pc, pcBuf, colorBuf] = entry.second;
		// 	sg_apply_bindings(sg_bindings{ .vertex_buffers = {pcBuf, colorBuf} });
		// 	vs_params_t vs_params{ .mvp = pv * translate(glm::mat4(1.0f), pc.position) * mat4_cast(pc.quaternion) , .dpi = camera->dpi };
		// 	sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_vs_params, SG_RANGE(vs_params));
		// 	sg_draw(0, pc.x_y_z_Sz.size(), 1);
		// }
		// sg_end_pass();

		// bloom pass

		// --- edl lo-res pass
		sg_begin_pass(graphics_state.edl_lres.pass, &graphics_state.edl_lres.pass_action);
		sg_apply_pipeline(graphics_state.edl_lres_pip);
		sg_apply_bindings(graphics_state.edl_lres.bind);
		depth_blur_params_t edl_params{ .kernelSize = 7, .scale = 1 };
		sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(edl_params));
		sg_draw(0, 4, 1);
		sg_end_pass();
	}
	
	sg_begin_default_pass(&passAction, w, h);
	{
		// sky quad:
		_draw_skybox(vm, pm);

		// ground:
		// draw partial ground plane for shadow casting (shadow computing for plane doesn't need ground's depth on camera view, it can be computed directly.
		std::vector<glm::vec3> ground_instances;
		for (auto& class_ : classes)
			for (auto obj : class_.second->objects)
				ground_instances.push_back(glm::vec3(obj.second.position.x, obj.second.position.y, class_.second->sceneDim.radius));

		if (ground_instances.size() > 0) {
			sg_apply_pipeline(graphics_state.gltf_ground_pip);
			graphics_state.gltf_ground_binding.vertex_buffers[1] = sg_make_buffer(sg_buffer_desc{
				.data = {ground_instances.data(), ground_instances.size() * sizeof(glm::vec3)}
				});
			sg_apply_bindings(graphics_state.gltf_ground_binding);
			gltf_ground_mats_t u{ pm * vm };
			sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(u));
			sg_draw(0, 6, ground_instances.size());
			sg_destroy_buffer(graphics_state.gltf_ground_binding.vertex_buffers[1]);
		}

		// composing (aware of depth) todo: add all other composing.
		sg_apply_pipeline(graphics_state.composer.pip);
		sg_apply_bindings(graphics_state.composer.bind);
		auto wnd = window_t{ .w = float(w), .h = float(h), .pnear = cam_near, .pfar = cam_far };
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_window, SG_RANGE(wnd));
		sg_draw(0, 4, 1);

		// ground reflections.
		// billboards
		
		// checkGLError(__FILE__, __LINE__);
		// // write depth value.
		// _sg_pass_t* pass = _sg_lookup_pass(&_sg.pools, graphics_state.edl_hres.pass.id);
		// auto fbo = pass->gl.fb;
		// glBindFramebuffer(GL_READ_FRAMEBUFFER, fbo);
		// glBindFramebuffer(GL_DRAW_FRAMEBUFFER, 0);
		// glBlitFramebuffer(
		// 	0, 0, w, h,   // Source region (FBO dimensions)
		// 	0, 0, w, h,   // Destination region (window dimensions)
		// 	GL_DEPTH_BUFFER_BIT,   // Buffer mask (depth buffer)
		// 	GL_NEAREST            // Sampling interpolation
		// );
		// checkGLError(__FILE__, __LINE__);

		// grid:
		// todo: grid should be occluded with transparency, compare depth value.
		grid->Draw(*camera);

		// debug:
		std::vector<sg_image> debugArr = {
			graphics_state.primitives.color ,
			graphics_state.primitives.depth ,
			graphics_state.primitives.normal,
			graphics_state.edl_lres.color,
			graphics_state.ssao.image,
			graphics_state.ssao.blur_image};
		sg_apply_pipeline(graphics_state.dbg.pip);
		for (int i=0; i<debugArr.size(); ++i)
		{
			sg_apply_viewport((i/4)*160, (i%4)*120, 160, 120, false);
			graphics_state.dbg.bind.fs_images[SLOT_tex] = debugArr[i];
			sg_apply_bindings(&graphics_state.dbg.bind);
			sg_draw(0, 4, 1);
		}
		sg_apply_viewport(0, 0, w, h, false);
	}
	sg_end_pass();

	sg_commit();

#ifdef _DEBUG
	// if (ImGui::BeginMainMenuBar()) {
	// 	if (ImGui::BeginMenu("sokol-gfx")) {
	// 		ImGui::MenuItem("Capabilities", 0, &sg_imgui.caps.open);
	// 		ImGui::MenuItem("Buffers", 0, &sg_imgui.buffers.open);
	// 		ImGui::MenuItem("Images", 0, &sg_imgui.images.open);
	// 		ImGui::MenuItem("Shaders", 0, &sg_imgui.shaders.open);
	// 		ImGui::MenuItem("Pipelines", 0, &sg_imgui.pipelines.open);
	// 		ImGui::MenuItem("Passes", 0, &sg_imgui.passes.open);
	// 		ImGui::MenuItem("Calls", 0, &sg_imgui.capture.open);
	// 		ImGui::EndMenu();
	// 	}
	// 	ImGui::EndMainMenuBar();
	// }
	// sg_imgui_draw(&sg_imgui);
#endif
}



#define SOKOL_IMGUI_NO_SOKOL_APP
#include "sokol_imgui.h"
void InitGL(int w, int h)
{

	lastW = w;
	lastH = h;
	glewInit();
	// Enable KHR_debug extension
	// if (GLEW_KHR_debug) {
	// 	glEnable(GL_DEBUG_OUTPUT);
	// 	glEnable(GL_DEBUG_OUTPUT_SYNCHRONOUS);
	// 	glDebugMessageCallback(DebugCallback, nullptr);
	// }


	// Set OpenGL states
	glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
	
	sg_desc desc = {
		.buffer_pool_size = 65535,
		.image_pool_size = 65535,
		.logger = {.func = slog_func, },
	};
	sg_setup(&desc);

#ifdef _DEBUG
	// const sg_imgui_desc_t desc_IMGUI = { };
	// sg_imgui_init(&sg_imgui, &desc_IMGUI);
	// // setup the sokol-imgui utility header
	// simgui_desc_t simgui_desc = { };
	// simgui_desc.sample_count = 1;
	// simgui_setup(&simgui_desc);
#endif

	glEnable(GL_VERTEX_PROGRAM_POINT_SIZE);
	// glEnable(GL_BLEND);
	// glBlendEquation(GL_FUNC_ADD);
	// glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
	glHint(GL_POINT_SMOOTH_HINT, GL_FASTEST);
	//glEnable(GL_POINT_SMOOTH);
	glEnable(GL_POINT_SPRITE);

	checkGLError(__FILE__, __LINE__);

	camera = new Camera(glm::vec3(0.0f, 0.0f, 0.0f), 10, w, h, 0.2);
	grid = new GroundGrid();

	// a vertex buffer
	float quadVertice[] = {
		// positions            colors
		-1.0f, -1.0f,
		 1.0f, -1.0f,
		-1.0f,  1.0f,
		 1.0f,  1.0f,
	};
	graphics_state.quad_vertices = sg_make_buffer(sg_buffer_desc{
		.data = SG_RANGE(quadVertice),
		.label = "composer-quad-vertices"
		});

	init_skybox_renderer();
	init_messy_renderer();
	init_gltf_render();


	// Pass action
	passAction = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 1.0f } } }
	};

	int shadow_resolution = 1024;
	sg_image_desc sm_desc = {
		.render_target = true,
		.width = shadow_resolution,
		.height = shadow_resolution,
		.pixel_format = SG_PIXELFORMAT_R32F,
	};
	sg_image shadow_map = sg_make_image(&sm_desc);
	sm_desc.pixel_format = SG_PIXELFORMAT_DEPTH_STENCIL; // for depth test.
	sg_image depthTest = sg_make_image(&sm_desc);
	graphics_state.shadow_map = {
		.shadow_map = shadow_map, .depthTest = depthTest,
		.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = { {.image = shadow_map}, },
			.depth_stencil_attachment = {.image = depthTest},
		}),
		.pass_action = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f } } },
			.depth = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE },
			.stencil = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE }
		},
	};

	GenPasses(w, h);
}

