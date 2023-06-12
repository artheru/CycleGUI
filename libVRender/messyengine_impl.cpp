#include <GL/glew.h>

#ifdef _DEBUG
#define SOKOL_DEBUG 
#endif
#define SOKOL_IMPL
#define SOKOL_GLES3

#include "sokol_gfx.h"
#include "sokol_time.h"
#include "sokol_log.h"

#include "cycleui.h"
#include "messyengine.h"
#include <imgui.h>
#include <iomanip>
#include <ios>
#include <iostream>
#include <sstream>
#include <unordered_map>

#include <glm/gtc/matrix_transform.hpp>
#include <glm/gtc/type_ptr.hpp>

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif


Camera* camera;
GroundGrid* grid;

sg_pass_action passAction;
sg_shader point_cloud_simple;
sg_pipeline point_cloud_simple_pip;

#include "shaders/point_cloud_simple.h"

void GLAPIENTRY DebugCallback(GLenum source, GLenum type, GLuint id, GLenum severity, GLsizei length, const GLchar* message, const void* userParam) {
	std::cout << "OpenGL Debug Message: " << message << std::endl;
}

struct gpu_point_cloud
{
	point_cloud pc;
	sg_buffer pcBuf;
	sg_buffer colorBuf;
};
std::unordered_map<std::string, gpu_point_cloud> pointClouds;


Camera::Camera(glm::vec3 stare, float dist, float width, float height, float minDist) : stare(stare), distance(dist), _width(width), _height(height), _minDist(minDist)
{
	Reset(stare, dist);
	Resize(width, height);
}

void Camera::RotateAzimuth(float delta)
{
	Azimuth += delta * rotateSpeed;
	Azimuth = fmod(Azimuth + 2 * M_PI, 2 * M_PI);
	UpdatePosition();
}

void Camera::RotateAltitude(float delta)
{
	Altitude += delta * rotateSpeed;
	Altitude = glm::clamp(Altitude, float(-M_PI_2), float(M_PI_2));
	UpdatePosition();
}

void Camera::PanLeftRight(float delta)
{
	stare += moveRight * delta;
	position += moveRight * delta;
}

void Camera::PanBackForth(float delta)
{
	stare += moveFront * delta;
	position += moveFront * delta;
}

void Camera::ElevateUpDown(float delta)
{
	stare += glm::vec3(0.0f, 0.0f, 1.0f) * delta;
	position += glm::vec3(0.0f, 0.0f, 1.0f) * delta;
}

void Camera::Zoom(float delta)
{
	distance = glm::clamp(distance * (1 + delta), _minDist, std::numeric_limits<float>::max());
	UpdatePosition();
}

void Camera::Reset(glm::vec3 stare, float dist)
{
	this->stare = glm::vec3(stare.x, stare.y, 0.0f);
	distance = dist;
	Azimuth = -M_PI_2;
	Altitude = M_PI_2;
	UpdatePosition();
}

void Camera::Resize(float width, float height)
{
	_width = width;
	_height = height;
	_aspectRatio = _width / _height;
}

glm::mat4 Camera::GetViewMatrix()
{
	return glm::lookAt(position, stare, up);
}

glm::mat4 Camera::GetProjectionMatrix()
{
	if (ProjectionMode == 0) {
		return glm::perspective(glm::radians(_fov), _aspectRatio, 0.0625f, 8192.0f);
	}
	else {
		return glm::ortho(-_width * distance / OrthoFactor, _width * distance / OrthoFactor, -_height * distance / OrthoFactor, _height * distance / OrthoFactor, 1.0f, 100000.0f);
	}
}

void Camera::UpdatePosition()
{
	if (abs(Altitude - M_PI_2) < gap) {
		position = stare + glm::vec3(0.0f, 0.0f, (Altitude > 0 ? distance : -distance));
		glm::vec3 n = glm::vec3(cos(Azimuth), sin(Azimuth), 0.0f);
		moveRight = glm::normalize(glm::cross(glm::vec3(0.0f, 0.0f, 1.0f), n));
		up = moveFront = (Altitude > 0 ? -n : n);
	}
	else {
		position = stare + glm::vec3(
			distance * cos(Altitude) * cos(Azimuth),
			distance * cos(Altitude) * sin(Azimuth),
			distance * sin(Altitude)
		);
		up = glm::vec3(0.0f, 0.0f, 1.0f);
		moveFront = -glm::vec3(cos(Azimuth), sin(Azimuth), 0.0f);
		moveRight = glm::normalize(glm::cross(up, position - stare));
	}
}

GroundGrid::GroundGrid()
{
	// shader glsl version 100
	const GLchar* vertShaderSource = R"glsl(
attribute vec4 position_alpha;

uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;
varying vec3 worldPosition;
varying float major_alpha;

void main()
{
    gl_Position = projectionMatrix * viewMatrix * vec4(position_alpha.xyz, 1.0);
    worldPosition = (vec4(position_alpha.xyz, 1.0)).xyz;
    major_alpha = position_alpha.w;
}
)glsl";

	GLuint vertexShader = glCreateShader(GL_VERTEX_SHADER);
	glShaderSource(vertexShader, 1, &vertShaderSource, nullptr);
	glCompileShader(vertexShader);
	GLint compileStatus;
	glGetShaderiv(vertexShader, GL_COMPILE_STATUS, &compileStatus);

	// For fragment shader, assuming red, green and blue are defined somewhere
	const GLchar* fragShaderSource = R"glsl(
precision mediump float;
varying vec3 worldPosition;
varying float major_alpha;
uniform vec3 starePosition;
uniform float scope;

void main()
{
    float distance = length(worldPosition - starePosition);
    float alpha = 1.0;

    if (distance > scope*0.6) {
        alpha = 1.0 - smoothstep(scope*0.6, scope, distance);
    }

    gl_FragColor = vec4(138.0 / 256.0, 43.0 / 256.0, 226.0 / 256.0, alpha * major_alpha);
}
)glsl";

	GLuint fragmentShader = glCreateShader(GL_FRAGMENT_SHADER);
	glShaderSource(fragmentShader, 1, &fragShaderSource, nullptr);
	glCompileShader(fragmentShader);
	glGetShaderiv(fragmentShader, GL_COMPILE_STATUS, &compileStatus);
	if (compileStatus == GL_FALSE) {
		GLint logLength;
		glGetShaderiv(fragmentShader, GL_INFO_LOG_LENGTH, &logLength);

		// Retrieve the error message
		std::vector<GLchar> log(logLength);
		glGetShaderInfoLog(fragmentShader, logLength, nullptr, log.data());

		// Throw an exception with the error message
		throw std::runtime_error("Shader compilation failed: " + std::string(log.data()));
	}

	programHandle = glCreateProgram();
	glAttachShader(programHandle, vertexShader);
	glAttachShader(programHandle, fragmentShader);
	glLinkProgram(programHandle);
	GLint linkStatus;
	glGetProgramiv(programHandle, GL_LINK_STATUS, &linkStatus);
	if (linkStatus == GL_FALSE) {
		GLint logLength;
		glGetProgramiv(programHandle, GL_INFO_LOG_LENGTH, &logLength);

		// Retrieve the error message
		std::vector<GLchar> log(logLength);
		glGetProgramInfoLog(programHandle, logLength, nullptr, log.data());

		// Throw an exception with the error message
		throw std::runtime_error("Program linking failed: " + std::string(log.data()));
	}

	glDetachShader(programHandle, vertexShader);
	glDetachShader(programHandle, fragmentShader);
	glDeleteShader(fragmentShader);
	glDeleteShader(vertexShader);

	std::vector<std::string> uniformNames = { "projectionMatrix", "viewMatrix", "starePosition", "scope" };
	for (std::string uniformName : uniformNames) {
		int location = glGetUniformLocation(programHandle, uniformName.c_str());
		uniformLocations.insert({ uniformName, location });
	}

	glGenBuffers(1, &vboHandle);
	glBindBuffer(GL_ARRAY_BUFFER, vboHandle);

	glGenVertexArrays(1, &vaoHandle);
	glBindVertexArray(vaoHandle);

	// VAO
	GLint positionLocation = glGetAttribLocation(programHandle, "position_alpha");
	glEnableVertexAttribArray(positionLocation);
	glVertexAttribPointer(positionLocation, 4, GL_FLOAT, GL_FALSE, sizeof(float) * 4, (void*)0);

	glBindBuffer(GL_ARRAY_BUFFER, 0);
	glBindVertexArray(0);
}

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
		if (zeroIndex == decimalIndex+1)
		{
			buffer[decimalIndex]=0;
		}
	}
}

void GroundGrid::Draw(Camera& cam)
{
	width = cam._width;
	height = cam._height;

	glm::mat4 viewMatrix = cam.GetViewMatrix();
	glm::mat4 projectionMatrix = cam.GetProjectionMatrix();
	
	glm::vec3 center(cam.stare.x, cam.stare.y, 0);
	float dist = std::abs(cam.position.z);
	float xyd = glm::length(glm::vec2(cam.position.x - cam.stare.x, cam.position.y - cam.stare.y));
	float pang = std::atan(xyd / (std::abs(cam.position.z) + 0.00001f)) / M_PI * 180;

	if (pang > cam._fov / 2.5)
		dist = std::min(dist, dist / std::cos((pang - cam._fov / 2.5f) / 180 * float(M_PI)));
	dist = std::max(dist, 1.0f);

	float cameraAzimuth = std::fmod(std::abs(cam.Azimuth) + 2 * M_PI, 2 * M_PI);
	float angle = std::acos(glm::dot(glm::normalize(cam.position - cam.stare), -glm::vec3(0, 0, 1))) / M_PI * 180;

	int level = 5;

	float rawIndex = std::log(glm::distance(cam.position, cam.stare) * 0.2f + dist * 0.4f) / std::log(level);

	// Using std::sprintf
	char bs[50];
	sprintf(bs, "rawIndex=%f", rawIndex);
	ImGui::Text(bs);

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
		              ? std::tan(cam._fov / 2 / 180 * M_PI) * glm::length(cam.position - cam.stare) * 1.414f * 3
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

			float alpha = yEdges == 1 ? getAlpha(45, 30) : getAlpha(15, 0);

			glm::vec2 p = ConvertWorldToScreen(glm::vec3((center.x - scope) / 2, y, 0), viewMatrix, projectionMatrix, glm::vec2(width, height));
			glm::vec2 q = ConvertWorldToScreen(glm::vec3((center.x + scope) / 2, y, 0), viewMatrix, projectionMatrix, glm::vec2(width, height));

			glm::vec2 intersection;
			if (LineSegCrossBorders(p, q, yEdges, intersection))
			{
				char buf[16];
				verboseFormatFloatWithTwoDigits(y,"y=%.2f", buf, 16);
				ImVec2 textSize = ImGui::CalcTextSize(buf);
				auto pos = ImGui::GetMainViewport()->Pos;
				ImGui::GetBackgroundDrawList()->AddText(ImVec2(intersection.x + pos.x - textSize.x, height - intersection.y + pos.y),
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

			float alpha = xEdges == 1 ? getAlpha(45, 30) : getAlpha(15, 0);

			glm::vec2 p = ConvertWorldToScreen(glm::vec3(x, (center.z - scope) / 2, 0), viewMatrix, projectionMatrix, glm::vec2(width, height));
			glm::vec2 q = ConvertWorldToScreen(glm::vec3(x, (center.z + scope) / 2, 0), viewMatrix, projectionMatrix, glm::vec2(width, height));
			glm::vec2 intersection;
			if (LineSegCrossBorders(p, q, xEdges, intersection))
			{
				char buf[16];
				verboseFormatFloatWithTwoDigits(x,"x=%.2f", buf, 16);
				auto pos = ImGui::GetMainViewport()->Pos;
				ImGui::GetBackgroundDrawList()->AddText(ImVec2(intersection.x + pos.x, height - intersection.y + pos.y),
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

	glBindBuffer(GL_ARRAY_BUFFER, vboHandle);
	glBindVertexArray(vaoHandle);

	int size = buffer.size() * sizeof(glm::vec4);
	glBufferData(GL_ARRAY_BUFFER, size, &buffer[0], GL_DYNAMIC_DRAW);

	glUseProgram(programHandle);

	glLineWidth(1);
	// glBlendEquation(GL_FUNC_ADD);
	glEnable(GL_BLEND);
	glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
	glDisable(GL_DEPTH_TEST);

	glUniformMatrix4fv(uniformLocations["projectionMatrix"], 1, GL_FALSE, &projectionMatrix[0][0]);
	glUniformMatrix4fv(uniformLocations["viewMatrix"], 1, GL_FALSE, &viewMatrix[0][0]);
	glUniform3fv(uniformLocations["starePosition"], 1, &cam.stare[0]);
	glUniform1f(uniformLocations["scope"], scope);

	glDrawArrays(GL_LINES, 0, buffer.size());
}

bool GroundGrid::LineSegCrossBorders(glm::vec2 p, glm::vec2 q, int availEdge, glm::vec2& pq)
{
	pq = glm::vec2(0.0f);
	if (std::isnan(p.x) || std::isnan(q.x))
		return false;

	if (availEdge == 0)
	{
		pq = glm::vec2(-p.y * (q.x - p.x) / (q.y - p.y) + p.x + 3, 20);
		return pq.x > 0 && pq.x < width;
	}

	pq = glm::vec2(width - 8, (q.y - p.y) / (q.x - p.x) * (width - p.x) + p.y);
	if (std::abs(pq.y - lastY) < 30) return false;
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

void DrawWorkspace(int w, int h)
{
	// draw
	camera->Resize(w, h);
	camera->UpdatePosition();

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

	// todo: next week, get point cloud+line+eye dome done.
	// we all use "#version 300 es";
	// pass1: draw all objects
	// pass2: 

	sg_begin_default_pass(&passAction, w, h);

	sg_apply_pipeline(point_cloud_simple_pip);

	auto vm = camera->GetViewMatrix();
	auto pm = camera->GetProjectionMatrix();

	auto pv = pm * vm;
	
	for (auto &entry: pointClouds)
	{
		const auto& gpu = entry.second;

		if (GLenum error = glGetError()) {
			std::cerr << " looping OpenGL Error: " << error << std::endl;
		}
		sg_bindings sokolBindings = { };
		sokolBindings.vertex_buffers[0] = gpu.pcBuf;
		sokolBindings.vertex_buffers[1] = gpu.colorBuf;
		sg_apply_bindings(&sokolBindings);

		if (GLenum error = glGetError()) {
			std::cerr << " binding OpenGL Error: " << error << std::endl;
		}
		auto modelMatrix = glm::translate(glm::mat4(1.0f), gpu.pc.position);
		modelMatrix *= glm::mat4_cast(gpu.pc.quaternion);
		vs_params_t vs_params = { .mvp = pv * modelMatrix };
		// sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_vs_params, SG_RANGE(vs_params));

		if (GLenum error = glGetError()) {
			std::cerr << " before draw OpenGL Error: " << error << std::endl;
		}
		// glDrawArrays(GL_POINTS, 0, 1000);
		// sg_draw(0, gpu.pc.x_y_z_Sz.size(), 1);
		if (GLenum error = glGetError()) {
			std::cerr << "after draw OpenGL Error: " << error << std::endl;
		}
	}
	sg_end_pass();
	sg_commit();

	if (GLenum error = glGetError()) {
		std::cerr << "after draw OpenGL Error: " << error << std::endl;
	}

	grid->Draw(*camera);
	// todo: grid should be occluded with transparency.
}

bool _mouseLeftPressed = false;
bool _mouseMiddlePressed = false;
bool _mouseRightPressed = false;
double _lastX = 0.0;
double _lastY = 0.0;

void mouse_button_callback(GLFWwindow* window, int button, int action, int mods)
{
	if (ImGui::GetIO().WantCaptureMouse)
		return;

	if (action == GLFW_PRESS)
	{
		switch (button)
		{
		case GLFW_MOUSE_BUTTON_LEFT:
			_mouseLeftPressed = true;
			break;
		case GLFW_MOUSE_BUTTON_MIDDLE:
			_mouseMiddlePressed = true;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			_mouseRightPressed = true;
			break;
		}
	}
	else if (action == GLFW_RELEASE)
	{
		switch (button)
		{
		case GLFW_MOUSE_BUTTON_LEFT:
			_mouseLeftPressed = false;
			break;
		case GLFW_MOUSE_BUTTON_MIDDLE:
			_mouseMiddlePressed = false;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			_mouseRightPressed = false;
			break;
		}
	}
}

void cursor_position_callback(GLFWwindow* window, double xpos, double ypos)
{
	if (ImGui::GetIO().WantCaptureMouse)
		return;

	double deltaX = xpos - _lastX;
	double deltaY = ypos - _lastY;
	_lastX = xpos;
	_lastY = ypos;

	if (_mouseLeftPressed)
	{
		// Handle left mouse button dragging
		// currently nothing.
	}
	else if (_mouseMiddlePressed)
	{
		// Handle middle mouse button dragging
		camera->RotateAzimuth(-deltaX);
		camera->RotateAltitude(deltaY * 1.5f);
	}
	else if (_mouseRightPressed)
	{
		// Handle right mouse button dragging
		auto d = camera->distance * 0.0016f;
		camera->PanLeftRight(-deltaX * d);
		camera->PanBackForth(deltaY * d);
	}
}

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset)
{
	if (ImGui::GetIO().WantCaptureMouse)
		return;

	// Handle mouse scroll

	camera->Zoom(-yoffset * 0.1f);
}

void AddPointCloud(std::string name, point_cloud what)
{
	auto it = pointClouds.find(name);
	if (it != pointClouds.end())
		pointClouds.erase(it);

	gpu_point_cloud gbuf;
	gbuf.pc = what;
	sg_buffer_desc vbufDesc = {
		.data = { what.x_y_z_Sz.data(), what.x_y_z_Sz.size()*sizeof(glm::vec4)},
	};
	gbuf.pcBuf = sg_make_buffer(&vbufDesc);
	sg_buffer_desc cbufDesc = {
		.data = { what.color.data(), what.color.size() * sizeof(glm::vec4) },
	};
	gbuf.colorBuf = sg_make_buffer(&cbufDesc);
	pointClouds[name] = gbuf;

	std::cout << "Added point cloud '" << name << "'" << std::endl;
}

void RemovePointCloud(std::string name) {
	auto it = pointClouds.find(name);
	if (it != pointClouds.end()) {
		pointClouds.erase(it);
		std::cout << "Removed point cloud '" << name << "'" << std::endl;
	}
	else {
		std::cout << "Point cloud '" << name << "' not found" << std::endl;
	}
}

void ModifyPointCloud(std::string name, glm::vec3 new_position, glm::quat new_quaternion) {
	auto it = pointClouds.find(name);
	if (it != pointClouds.end()) {
		auto& gpu = it->second;
		gpu.pc.position = new_position;
		gpu.pc.quaternion = new_quaternion;
		std::cout << "Modified point cloud '" << name << "'" << std::endl;
	}
	else {
		std::cout << "Point cloud '" << name << "' not found" << std::endl;
	}
}


void InitGL(int w, int h)
{
	// Enable KHR_debug extension
	if (GLEW_KHR_debug) {
		glEnable(GL_DEBUG_OUTPUT);
		glEnable(GL_DEBUG_OUTPUT_SYNCHRONOUS);
		glDebugMessageCallback(DebugCallback, nullptr);
	}

	glewInit();

	// Set OpenGL states
	glClearColor(0.0f, 0.0f, 0.0f, 1.0f);


	sg_desc desc = {
		.logger = {.func = slog_func, } };
	sg_setup(&desc);

	// glEnable(GL_DEPTH_TEST);
	// glDepthFunc(GL_LEQUAL);
	// glEnable(GL_VERTEX_PROGRAM_POINT_SIZE);
	// glEnable(GL_ALPHA_TEST);
	// glEnable(GL_BLEND);
	//
	// glHint(GL_POINT_SMOOTH_HINT, GL_FASTEST);
	// glEnable(GL_POINT_SPRITE);
	// glDisable(GL_POINT_SMOOTH);

	camera = new Camera(glm::vec3(0.0f, 0.0f, 0.0f), 10, w, h, 0.2);

	// Shader program
	point_cloud_simple = sg_make_shader(point_cloud_simple_shader_desc(sg_query_backend()));

	// Pipeline state object
	point_cloud_simple_pip = sg_make_pipeline(sg_pipeline_desc {
		.shader = point_cloud_simple,
		.layout = {
			.buffers = { {.stride = 32}},
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3,  },
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT4 },
			},
		},
		.depth = {
			.compare = SG_COMPAREFUNC_LESS_EQUAL,
			.write_enabled = true,
		},
		.primitive_type = SG_PRIMITIVETYPE_POINTS,
		.index_type = SG_INDEXTYPE_NONE,
	});
		
	// Pass action
	passAction = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 1.0f } } }
	};

	grid = new GroundGrid();
}

