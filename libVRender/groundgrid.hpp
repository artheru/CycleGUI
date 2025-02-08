#include "cycleui.h"

void verboseFormatFloatWithTwoDigits(float value, const char* format, char* buffer, int bufferSize)
{
	int numChars = std::snprintf(buffer, bufferSize, format, value);

	if (numChars > 0 && numChars < bufferSize)
	{
		int decimalIndex = -1;
		int zeroIndex = -1;
		bool foundDecimal = false;

		// Find the indices of the decimal point and the first zero after it
		for (int i = 0; i < numChars; ++i)
		{
			if (buffer[i] == '.')
			{
				decimalIndex = i;
				foundDecimal = true;
			}
			else if (buffer[i] == '0' && foundDecimal && zeroIndex == -1)
			{
				zeroIndex = i;
			}
		}

		// Remove trailing zeros if they follow the decimal point
		if (zeroIndex > decimalIndex)
		{
			buffer[zeroIndex] = '\0';
		}
		if (zeroIndex == decimalIndex + 1)
		{
			buffer[decimalIndex] = 0;
		}
	}
}

void GroundGrid::Draw(Camera& cam, disp_area_t disp_area, ImDrawList* dl, glm::mat4 viewMatrix, glm::mat4 projectionMatrix  )
{
	width = cam._width;
	height = cam._height;

	float gz = cam.position.z / (cam.position.z - cam.stare.z) * glm::distance(cam.position, cam.stare);
	auto gstare = gz > 0 ? glm::normalize(cam.stare - cam.position) * gz + cam.position : cam.stare;

	glm::vec3 center(gstare.x, gstare.y, 0);

	float dist = std::abs(cam.position.z);
	float xyd = glm::length(glm::vec2(cam.position.x - gstare.x, cam.position.y - gstare.y));
	float pang = std::atan(xyd / (std::abs(cam.position.z) + 0.00001f)) / M_PI * 180;
	
	if (pang > cam._fov / 2.5)
		dist = std::min(dist, dist / std::cos((pang - cam._fov / 2.5f) / 180 * float(M_PI)));
	dist = std::max(dist, 1.0f);

	float cameraAzimuth = std::fmod(std::abs(cam.Azimuth) + 2 * M_PI, 2 * M_PI);
	center = center + glm::vec3(glm::vec2(gstare - cam.position) * std::cos(cam.Altitude) * powf(cam._fov / 45.0f,1.6) , 0);
	float angle = std::acos(glm::dot(glm::normalize(cam.position - gstare), -glm::vec3(0, 0, 1))) / M_PI * 180;

	int level = 5;

	float rawIndex = std::log((glm::distance(cam.position, gstare) * 0.2f + dist * 0.4f) * cam._fov / 45)/ std::log(level)-0.4;

	float index = std::floor(rawIndex);

	float mainUnit = std::pow(level, index);
	float pctg = rawIndex - index;
	float minorUnit = std::pow(level, index - 1);

	float v = 0.4f;
	float pos = 1 - pctg;
	float _minorAlpha = 0 + (v - 0) * pos;
	float _mainAlpha = _minorAlpha * (1 - v) / v + v;

	float alphaDecay = 1.0f;
	if (std::abs(angle - 90) < 15)
		alphaDecay = 0.05f + 0.95f * std::pow(std::abs(angle - 90) / 15, 3);

	float scope = cam.ProjectionMode == 0
		? std::tan(cam._fov / 2 / 180 * M_PI) * glm::length(cam.position - gstare) * 1.414f * 3
		: cam._width * cam.distance / cam.OrthoFactor;

	auto GenerateGrid = [&](float unit, float maxAlpha, bool isMain, glm::vec3 center) {
		int xEdges = 0;
		int yEdges = 1;
		if (isMain)
		{
			float theta = atan(height / width);
			if ((theta <= cameraAzimuth && cameraAzimuth < M_PI - theta) ||
				(M_PI + theta <= cameraAzimuth && cameraAzimuth < 2 * M_PI - theta))
			{
				xEdges = 0;
				yEdges = 1;
			}
			else
			{
				xEdges = 1;
				yEdges = 0;
			}
		}

		auto getAlpha = [cam](float display, float diminish) {
			float deg = cam.Altitude / M_PI * 180;
			if (deg > display) return 1.0f;
			if (deg > diminish) return (deg - diminish) / (display - diminish);
			return 0.0f;
		};

		float startPos = (int)(floor((center.y - scope) * 100) / (unit * 100)) * unit;
		std::vector<glm::vec4> vLines;
		for (float y = startPos; y <= std::ceil(center.y + scope); y += unit)
		{
			vLines.push_back(glm::vec4(center.x + scope, y, 0, maxAlpha));
			vLines.push_back(glm::vec4(center.x - scope, y, 0, maxAlpha));

			if (!isMain) continue;

			float alpha = yEdges == 1 ? getAlpha(30, 20) : getAlpha(15, 0);

			glm::vec2 p = ConvertWorldToScreen(glm::vec3((center.x - scope) / 2, y, 0), viewMatrix, projectionMatrix, glm::vec2(width, height));
			glm::vec2 q = ConvertWorldToScreen(glm::vec3((center.x + scope) / 2, y, 0), viewMatrix, projectionMatrix, glm::vec2(width, height));

			glm::vec2 intersection;

			if (LineSegCrossBorders(p, q, yEdges, intersection))
			{
				char buf[16];
				verboseFormatFloatWithTwoDigits(y, "y=%.2f", buf, 16);
				ImVec2 textSize = ImGui::CalcTextSize(buf);
				dl->AddText(ImVec2(intersection.x + disp_area.Pos.x - (yEdges==1?textSize.x:0), height - intersection.y + disp_area.Pos.y),
					ImGui::GetColorU32(ImVec4(red * 1.4f, green * 1.5f, blue * 1.3f, alpha)), buf);
			}
		}

		startPos = (int)(std::ceil((center.x - scope) * 100) / (unit * 100)) * unit;
		std::vector<glm::vec4> hLines;
		for (float x = startPos; x <= std::ceil(center.x + scope); x += unit)
		{
			hLines.push_back(glm::vec4(x, center.y + scope, 0, maxAlpha));
			hLines.push_back(glm::vec4(x, center.y - scope, 0, maxAlpha));

			if (!isMain) continue;

			float alpha = xEdges == 1 ? getAlpha(30, 20) : getAlpha(15, 0);

			glm::vec2 p = ConvertWorldToScreen(glm::vec3(x, (center.z - scope) / 2, 0), viewMatrix, projectionMatrix, glm::vec2(width, height));
			glm::vec2 q = ConvertWorldToScreen(glm::vec3(x, (center.z + scope) / 2, 0), viewMatrix, projectionMatrix, glm::vec2(width, height));
			glm::vec2 intersection;
			if (LineSegCrossBorders(p, q, xEdges, intersection))
			{
				char buf[16];
				verboseFormatFloatWithTwoDigits(x, "x=%.2f", buf, 16);
				ImVec2 textSize = ImGui::CalcTextSize(buf);
				dl->AddText(ImVec2(intersection.x + disp_area.Pos.x -(xEdges==1?textSize.x:0), height - intersection.y + disp_area.Pos.y),
					ImGui::GetColorU32(ImVec4(red * 1.4f, green * 1.5f, blue * 1.3f, alpha)), buf);
			}
		}

		vLines.insert(vLines.end(), hLines.begin(), hLines.end());
		return vLines;
	};

	std::vector<glm::vec4> grid0 = GenerateGrid(mainUnit * level, alphaDecay, false, center);
	std::vector<glm::vec4> grid1 = GenerateGrid(mainUnit, _mainAlpha * alphaDecay, true, center);
	std::vector<glm::vec4> grid2 = GenerateGrid(minorUnit, _minorAlpha * alphaDecay, false, center);

	std::vector<glm::vec4> buffer(grid0);
	buffer.insert(buffer.end(), grid1.begin(), grid1.end());
	buffer.insert(buffer.end(), grid2.begin(), grid2.end());

	if (buffer.size()>0){
		sg_apply_pipeline(shared_graphics.grid_pip);
		glLineWidth(1);
		auto buf = sg_make_buffer(sg_buffer_desc{
			.data = sg_range{&buffer[0], buffer.size() * sizeof(glm::vec4)},
			});

		sg_apply_bindings(sg_bindings{
			.vertex_buffers = { buf },
			.fs_images = {working_graphics_state->primitives.depth}
			});
		ground_vs_params_t uniform_vs{ projectionMatrix * viewMatrix };
		sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(uniform_vs));

		ground_fs_params_t uniform_fs{
			.starePosition = center,
			.viewportOffset = glm::vec2(0),
			.scope = scope };
		sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(uniform_fs));

		sg_draw(0, buffer.size(), 1);
		sg_destroy_buffer(buf);
	}
}

bool GroundGrid::LineSegCrossBorders(glm::vec2 p, glm::vec2 q, int availEdge, glm::vec2& pq)
{
	pq = glm::vec2(0.0f);
	if (std::isnan(p.x) || std::isnan(q.x))
		return false;

	if (availEdge == 0)
	{
		pq = glm::vec2(-p.y * (q.x - p.x) / (q.y - p.y) + p.x + 3, 20);
		if (std::abs(pq.x - lastX) < 50*GImGui->CurrentDpiScale) return false;
		lastX = pq.x;
		return pq.x > 0 && pq.x < width;
	}

	pq = glm::vec2(width - 8, (q.y - p.y) / (q.x - p.x) * (width - p.x) + p.y);
	if (std::abs(pq.y - lastY) < 30*GImGui->CurrentDpiScale) return false;
	lastY = pq.y;
	return pq.y > 0 && pq.y < height;
}

glm::vec2 GroundGrid::ConvertWorldToScreen(glm::vec3 input, glm::mat4 v, glm::mat4 p, glm::vec2 screenSize)
{
	glm::vec4 a = p * v * glm::vec4(input[0], input[1], 0, 1.0f);
	glm::vec3 b = glm::vec3(a) / a.w;
	glm::vec2 c = glm::vec2(b);
	return glm::vec2((c.x * 0.5f + 0.5f) * screenSize.x, (c.y * 0.5f + 0.5f) * screenSize.y);
}
