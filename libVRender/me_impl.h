#pragma once
#include <GL/glew.h>
#include <GL/glu.h>

#ifdef _DEBUG
#define SOKOL_DEBUG
#define USE_DBG_UI
#define SOKOL_TRACE_HOOKS
#endif
#define SOKOL_IMPL
#define SOKOL_GLES3


#define NOMINMAX

// note: sokol_gfx is modified to allow passing depth texture
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


// ███    ███ ███████         ██   ██ ███████  █████  ██████  ███████ ██████  ███████
// ████  ████ ██              ██   ██ ██      ██   ██ ██   ██ ██      ██   ██ ██
// ██ ████ ██ █████           ███████ █████   ███████ ██   ██ █████   ██████  ███████
// ██  ██  ██ ██              ██   ██ ██      ██   ██ ██   ██ ██      ██   ██      ██
// ██      ██ ███████ ███████ ██   ██ ███████ ██   ██ ██████  ███████ ██   ██ ███████

#include "shaders/point_cloud_simple.h"
#include "shaders/ground_plane.h"
#include "shaders/dbg.h"
#include "shaders/depth_blur.h"

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif

static struct {
	struct {
		sg_image color, depth;
		sg_pass pass;
		sg_pass_action pass_action;
		sg_bindings bind;
	} edl_lres;

	struct {
		sg_image color, depth;
		sg_pass pass;
		sg_pass_action pass_action;
	} edl_hres; // draw points, doesn't need binding.

	sg_pipeline edl_lres_pip;

	struct {
		sg_pass_action pass_action;
		sg_pipeline pip;
		sg_bindings bind;
	} edl_composer;

	sg_buffer quad_vertices;

	struct
	{
		sg_pipeline pip;
		sg_bindings bind;
	} dbg;

	struct
	{
		sg_pipeline pip;
		sg_bindings bind;
	} skybox;
} graphics_state;

Camera* camera;
GroundGrid* grid;

sg_pass_action passAction;

sg_pipeline point_cloud_simple_pip;
sg_pipeline point_cloud_composer_pip;

sg_shader ground_shader;
sg_pipeline ground_pip;


void GenEDLPasses(int w, int h);
void ResetEDLPass();


struct gpu_point_cloud
{
	point_cloud pc;
	sg_buffer pcBuf;
	sg_buffer colorBuf;
};
std::unordered_map<std::string, gpu_point_cloud> pointClouds;


EM_JS(void, melog, (const char* c_str), {
	const str = UTF8ToString(c_str);console.log(str);
	});

void checkGLError(const char* file, int line)
{
	GLenum error = glGetError();
	if (error != GL_NO_ERROR) {
		// const char* errorString = reinterpret_cast<const char*>(gluErrorString(error));
#ifdef __EMSCRIPTEN__
		char buf[2048];
		sprintf(buf, "GL Error at %s(%d): %d", file, line, error);
		melog(buf);
#else
		std::cerr << "OpenGL error at " << file << ":" << line << " - " << error << std::endl;
#endif
	}
}