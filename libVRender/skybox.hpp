
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
	if (ui.displayRenderDebug)
		ImGui::DragFloat("sun", &working_graphics_state->scene_bg.sun_altitude, 0.01f, 0, 1.57);

	glm::vec3 sunPosition = glm::vec3(cos(working_graphics_state->scene_bg.sun_altitude), 0.0f, sin(working_graphics_state->scene_bg.sun_altitude));
	glm::vec3 up = glm::vec3(0.0f, 0.0f, 1.0f);

	//ImGui::DragFloat("rayleigh", &rayleigh, 0.01f, 0, 5);
	//ImGui::DragFloat("turbidity", &turbidity, 0.01f, 0, 30);
	//ImGui::DragFloat("mieCoefficient", &mieCoefficient, 0.0001, 0, 0.1);
	//ImGui::DragFloat("mieDirectionalG", &mieDirectionalG, 0.0001, 0, 1);
	//ImGui::DragFloat("toneMappingExposure", &toneMappingExposure, 0.001, 0, 1);

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

	sg_apply_pipeline(shared_graphics.skybox.pip_sky);
	sg_apply_bindings(shared_graphics.skybox.bind);
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