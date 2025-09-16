#pragma once
#include "me_impl.h"
#include <fstream>

void me_update_rgba_atlas(sg_image simg, int an, int sx, int sy, int h, int w, const void* data, sg_pixel_format format)
{
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, simg.id);

	_sg_gl_cache_store_texture_binding(0);
	_sg_gl_cache_bind_texture(0, img->gl.target, img->gl.tex[img->cmn.active_slot]);
	GLenum gl_img_target = img->gl.target;
	glTexSubImage3D(gl_img_target, 0,
		sx, sy, an,
		w, h, 1,
		GL_RGBA, GL_UNSIGNED_BYTE,
		// _sg_gl_teximage_format(format), _sg_gl_teximage_type(format),
		data);
	_sg_gl_cache_restore_texture_binding(0);
	_SG_GL_CHECK_ERROR();
}

void updatePartial(sg_buffer buffer, int offset, const sg_range& data)
{
	_sg_buffer_t* buf = _sg_lookup_buffer(&_sg.pools, buffer.id);
	GLenum gl_tgt = _sg_gl_buffer_target(buf->cmn.type);
	GLuint gl_buf = buf->gl.buf[buf->cmn.active_slot];
	SOKOL_ASSERT(gl_buf);
	_SG_GL_CHECK_ERROR();
	_sg_gl_cache_store_buffer_binding(gl_tgt);
	_sg_gl_cache_bind_buffer(gl_tgt, gl_buf);
	glBufferSubData(gl_tgt, offset, (GLsizeiptr)data.size, data.ptr);
	_sg_gl_cache_restore_buffer_binding(gl_tgt);
}

void copyPartial(sg_buffer bufferSrc, sg_buffer bufferDst, int offset)
{
	_sg_buffer_t* buf = _sg_lookup_buffer(&_sg.pools, bufferDst.id);
	_sg_buffer_t* bufsrc = _sg_lookup_buffer(&_sg.pools, bufferSrc.id);
	GLenum gl_tgt = _sg_gl_buffer_target(buf->cmn.type);
	GLuint gl_buf = buf->gl.buf[buf->cmn.active_slot];
	SOKOL_ASSERT(gl_buf);
	_SG_GL_CHECK_ERROR();
	_sg_gl_cache_store_buffer_binding(gl_tgt);
	_sg_gl_cache_bind_buffer(gl_tgt, gl_buf);

	auto src = bufsrc->gl.buf[bufsrc->cmn.active_slot];
	glBindBuffer(GL_COPY_READ_BUFFER, src);

	glCopyBufferSubData(GL_COPY_READ_BUFFER, GL_ARRAY_BUFFER, 0, 0, offset);
	_sg_gl_cache_restore_buffer_binding(gl_tgt);
}

void me_getTexFloats(sg_image img_id, glm::vec4* pixels, int x, int y, int w, int h) {
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, img_id.id);
	SOKOL_ASSERT(img->gl.target == GL_TEXTURE_2D);
	SOKOL_ASSERT(0 != img->gl.tex[img->cmn.active_slot]);

	// GLuint oldFbo = 0;
	static GLuint readFbo = 0;
	if (readFbo == 0) {
		glGenFramebuffers(1, &readFbo);
	}
	//glGetIntegerv(GL_FRAMEBUFFER_BINDING, (GLint*)&oldFbo); just bind 0 is ok.
	glBindFramebuffer(GL_FRAMEBUFFER, readFbo);
	glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, img->gl.tex[img->cmn.active_slot], 0);
	glReadPixels(x, y, w, h, GL_RGBA, GL_FLOAT, pixels);
	glBindFramebuffer(GL_FRAMEBUFFER, 0);
	//sg_reset_state_cache();
	_SG_GL_CHECK_ERROR();
}

void me_getTexBytes(sg_image img_id, uint8_t* pixels, int x, int y, int w, int h) {
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, img_id.id);
	SOKOL_ASSERT(img->gl.target == GL_TEXTURE_2D);
	SOKOL_ASSERT(0 != img->gl.tex[img->cmn.active_slot]);

	// GLuint oldFbo = 0;
	static GLuint readFbo = 0;
	if (readFbo == 0) {
		glGenFramebuffers(1, &readFbo);
	}
	//glGetIntegerv(GL_FRAMEBUFFER_BINDING, (GLint*)&oldFbo); just bind 0 is ok.
	glBindFramebuffer(GL_FRAMEBUFFER, readFbo);
	glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, img->gl.tex[img->cmn.active_slot], 0);
	glReadPixels(x, y, w, h, GL_RGBA, GL_UNSIGNED_BYTE, pixels);
	glBindFramebuffer(GL_FRAMEBUFFER, 0);
	//sg_reset_state_cache();
	_SG_GL_CHECK_ERROR();
}

void updateTextureW4K(sg_image simg, int objmetah, const void* data, sg_pixel_format format)
{
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, simg.id);

	_sg_gl_cache_store_texture_binding(0);
	_sg_gl_cache_bind_texture(0, img->gl.target, img->gl.tex[img->cmn.active_slot]);
	GLenum gl_img_target = img->gl.target;
	glTexSubImage2D(gl_img_target, 0,
		0, 0,
		4096, objmetah,
		_sg_gl_teximage_format(format), _sg_gl_teximage_type(format),
		data);
	_sg_gl_cache_restore_texture_binding(0);
}

// Update a rectangular region of a 2D texture (RGBA/float formats supported)
void texUpdatePartial(sg_image simg, int x, int y, int w, int h, const void* data, sg_pixel_format format)
{
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, simg.id);
	SOKOL_ASSERT(img && img->gl.target == GL_TEXTURE_2D);
	SOKOL_ASSERT(0 != img->gl.tex[img->cmn.active_slot]);

	_sg_gl_cache_store_texture_binding(0);
	_sg_gl_cache_bind_texture(0, img->gl.target, img->gl.tex[img->cmn.active_slot]);
	GLenum gl_img_target = img->gl.target;
	glTexSubImage2D(gl_img_target, 0,
		x, y,
		w, h,
		_sg_gl_teximage_format(format), _sg_gl_teximage_type(format),
		data);
	_sg_gl_cache_restore_texture_binding(0);
}

void occurences_readout(int w, int h)
{
	struct reading
	{
		GLuint bufReadOccurence;
		GLsync sync;
		int w = -1, h = -1;
	};
	// read graphics_state.TCIN, graphics_state.sprite_render.occurences
	static GLuint readFbo = 0;
	static reading reads[2];
	if (readFbo == 0) {
		glGenFramebuffers(1, &readFbo);
	}
	auto readN = working_viewport->frameCnt % 2;
	auto issueN = 1 - readN;

	//read:
	if (reads[readN].w != -1)
	{
		if (reads[readN].w == w && reads[readN].h == h)
		{
			//valid read:

			//argb occurences:
			glBindBuffer(GL_PIXEL_PACK_BUFFER, reads[readN].bufReadOccurence);
			int ww = ceil(reads[readN].w / 4.0);
			int hh = ceil(reads[readN].h / 4.0);
#ifndef __EMSCRIPTEN__
			auto buf = (const float*)glMapBufferRange(GL_PIXEL_PACK_BUFFER, 0, ww * hh * 4, GL_MAP_READ_BIT);
			// do operations.
			process_argb_occurrence(buf, ww, hh);
			glUnmapBuffer(GL_PIXEL_PACK_BUFFER);
#else
			GLenum waitReturn = glClientWaitSync(reads[readN].sync, GL_SYNC_FLUSH_COMMANDS_BIT, 0);
			glDeleteSync(reads[readN].sync);

			std::vector<float> buf(ww * hh * 4);
			glGetBufferSubData(GL_PIXEL_PACK_BUFFER, 0, ww * hh * sizeof(float), buf.data());
			// do operations
			process_argb_occurrence(buf.data(), ww, hh);
#endif
		}

		glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
		glDeleteBuffers(1, &reads[readN].bufReadOccurence);
	}

	//issue:
	_sg_image_t* o = _sg_lookup_image(&_sg.pools, working_graphics_state->sprite_render.occurences.id);
	auto tex_o = o->gl.tex[o->cmn.active_slot];
	// read argb occurences:
	reads[issueN].w = w;
	reads[issueN].h = h;
	glGenBuffers(1, &reads[issueN].bufReadOccurence);
	glBindBuffer(GL_PIXEL_PACK_BUFFER, reads[issueN].bufReadOccurence);
	int ww = ceil(w / 4.0);
	int hh = ceil(h / 4.0);
	glBufferData(GL_PIXEL_PACK_BUFFER, ww * hh * 4, nullptr, GL_STREAM_READ);
	glBindFramebuffer(GL_FRAMEBUFFER, readFbo);
	glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, tex_o, 0);
	glReadPixels(0, 0, ww, hh, GL_RED, GL_FLOAT, nullptr);

	//clean up
	glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
#ifdef __EMSCRIPTEN__
	reads[issueN].sync = glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
	glFlush();
#endif
	glBindFramebuffer(GL_FRAMEBUFFER, 0);
}


void InitPlatform()
{
	auto io = ImGui::GetIO();
	io.ConfigInputTrickleEventQueue = false;
	io.ConfigDragClickToInputText = true;

	glewInit();

	// Set OpenGL states
	glClearColor(0.0f, 0.0f, 0.0f, 1.0f);

	sg_desc desc = {
		.buffer_pool_size = 65535,
		.image_pool_size = 65535,
		.pass_pool_size = 512,
		.logger = {.func = slog_func, }
	};
	sg_setup(&desc);

	glEnable(GL_VERTEX_PROGRAM_POINT_SIZE);
	glHint(GL_POINT_SMOOTH_HINT, GL_FASTEST);
	glEnable(GL_POINT_SPRITE);
	glGetError(); //clear errors.
}

bool getTextureWH(sg_image& imgr, int& width, int& height)
{
	// Get texture info from temp_render
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, imgr.id);
	if (!img || img->gl.target != GL_TEXTURE_2D || !img->gl.tex[img->cmn.active_slot]) {
		return false;
	}
	// Get texture dimensions and feed them
	width = img->cmn.width;
	height = img->cmn.height;
	return true;
}

void setFaceCull(bool set)
{
	if (set) glEnable(GL_CULL_FACE);
	else glDisable(GL_CULL_FACE);
}

sg_image genImageFromPlatform(unsigned int platformID)
{
	// Create a temporary sg_image that wraps the OpenGL texture
	return sg_make_image(sg_image_desc{
		.width = 1,
		.height = 1,
		.label = "imgui-temp-texture",
		.gl_textures = {platformID},  // Use the OpenGL texture ID
		});

}

// Copy texture array slices using GPU shader (OpenGL direct implementation)
bool CopyTexArr(sg_image src, sg_image dst, int src_slices, int atlas_size)
{
	static bool initialized = false;
	static GLuint shader_program = 0;
	static GLuint vao = 0;
	static GLuint uniform_input_slice = 0;
	static GLuint uniform_atlas_size = 0;
	static GLuint uniform_src_tex = 0;

	// Initialize shader on first use
	if (!initialized) {
		// Vertex shader source - simple fullscreen triangle
		const char* vertex_shader_source = R"(
			#version 330 core
			out vec2 v_uv;
			
			uniform float u_atlas_size;
			
			void main() {
				int idx = gl_VertexID;
				vec2 pos = vec2((idx & 1) * 2.0 - 1.0, (idx & 2) - 1.0);
				gl_Position = vec4(pos, 0.0, 1.0);
				
				// Convert from clip space [-1,1] to texture coordinates [0, atlas_size-1]
				// Clamp to ensure we don't sample outside texture bounds
				v_uv.x = pos.x==-1.0 ? 0.0 : u_atlas_size;
				v_uv.y = pos.y==-1.0 ? 0.0 : u_atlas_size;
				//v_uv = clamp((pos * 0.5 + 0.5) * u_atlas_size, 0.0, u_atlas_size - 1.0);
			}
		)";

		// Fragment shader source
		const char* fragment_shader_source = R"(
			#version 330 core
			in vec2 v_uv;
			out vec4 frag_color;
			
			uniform sampler2DArray u_src_tex;
			uniform int u_input_slice_num;
			
			void main() {
				frag_color = texelFetch(u_src_tex, ivec3(ivec2(v_uv), u_input_slice_num), 0);
			}
		)";

		// Compile vertex shader
		GLuint vertex_shader = glCreateShader(GL_VERTEX_SHADER);
		glShaderSource(vertex_shader, 1, &vertex_shader_source, NULL);
		glCompileShader(vertex_shader);

		// Compile fragment shader
		GLuint fragment_shader = glCreateShader(GL_FRAGMENT_SHADER);
		glShaderSource(fragment_shader, 1, &fragment_shader_source, NULL);
		glCompileShader(fragment_shader);

		// Create and link program
		shader_program = glCreateProgram();
		glAttachShader(shader_program, vertex_shader);
		glAttachShader(shader_program, fragment_shader);
		glLinkProgram(shader_program);

		// Get uniform locations
		uniform_input_slice = glGetUniformLocation(shader_program, "u_input_slice_num");
		uniform_atlas_size = glGetUniformLocation(shader_program, "u_atlas_size");
		uniform_src_tex = glGetUniformLocation(shader_program, "u_src_tex");
		// Create VAO
		glGenVertexArrays(1, &vao);

		// Cleanup
		glDeleteShader(vertex_shader);
		glDeleteShader(fragment_shader);

		initialized = true;
	}

	_sg_image_t* src_img = _sg_lookup_image(&_sg.pools, src.id);
	_sg_image_t* dst_img = _sg_lookup_image(&_sg.pools, dst.id);
	if (!src_img || !dst_img) {
		throw "bad image arr?";
	}

	//glPushDebugGroup(GL_DEBUG_SOURCE_APPLICATION, 0, -1, "CopyTexArr - Atlas Expansion");

	// Use our shader program
	glUseProgram(shader_program);
	glBindVertexArray(vao);

	// Bind source texture array
	glActiveTexture(GL_TEXTURE0);
	glBindTexture(GL_TEXTURE_2D_ARRAY, src_img->gl.tex[src_img->cmn.active_slot]);
	glUniform1i(uniform_src_tex, 0);
	glUniform1f(uniform_atlas_size, atlas_size);
	glDisable(GL_SCISSOR_TEST);
	glViewport(0, 0, atlas_size, atlas_size);

	// Create framebuffer for rendering
	GLuint temp_fbo;
	glGenFramebuffers(1, &temp_fbo);
	glBindFramebuffer(GL_FRAMEBUFFER, temp_fbo);

	// Copy each slice
	for (int slice = 0; slice < src_slices; ++slice) {
		// Set framebuffer to render to destination slice
		glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0,
			dst_img->gl.tex[dst_img->cmn.active_slot], 0, slice);

		// Set which source slice to copy from
		glUniform1i(uniform_input_slice, slice);

		// Render fullscreen quad to copy the entire slice
		glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);
	}

	// Cleanup
	glDeleteFramebuffers(1, &temp_fbo);

	auto a = glGetError();
	if (a != 0) printf("Fucked, err=%d\n", a);
	SOKOL_ASSERT(a == GL_NO_ERROR);
	return true;
}


// Helper: dump a 2D array texture slice to BMP for debugging
static void DumpTextureArraySliceBMP(sg_image atlas, int slice_id, int atlas_size, const char* filename)
{
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, atlas.id);
	if (!img) return;

	GLuint tex_id = img->gl.tex[img->cmn.active_slot];
	if (tex_id == 0) return;

	// Create FBO and attach the requested slice
	GLuint fbo = 0;
	glGenFramebuffers(1, &fbo);
	glBindFramebuffer(GL_FRAMEBUFFER, fbo);
	glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, tex_id, 0, slice_id);

	// Read pixels
	std::vector<uint8_t> rgba(atlas_size * atlas_size * 4);
	glReadPixels(0, 0, atlas_size, atlas_size, GL_RGBA, GL_UNSIGNED_BYTE, rgba.data());
	glBindFramebuffer(GL_FRAMEBUFFER, 0);
	glDeleteFramebuffers(1, &fbo);

	// Prepare 24-bit BMP data (BGR, bottom-up)
	int width = atlas_size;
	int height = atlas_size;
	int row_size = width * 3;
	int row_padded = (row_size + 3) & ~3;
	int pixel_data_size = row_padded * height;
	int file_size = 14 + 40 + pixel_data_size;

	std::vector<uint8_t> bmp(file_size);

	// BITMAPFILEHEADER
	bmp[0] = 'B'; bmp[1] = 'M';
	*(uint32_t*)&bmp[2] = file_size;
	*(uint16_t*)&bmp[6] = 0; // reserved1
	*(uint16_t*)&bmp[8] = 0; // reserved2
	*(uint32_t*)&bmp[10] = 54; // offset to pixel data

	// BITMAPINFOHEADER
	*(uint32_t*)&bmp[14] = 40; // header size
	*(int32_t*)&bmp[18] = width;
	*(int32_t*)&bmp[22] = height; // positive for bottom-up
	*(uint16_t*)&bmp[26] = 1; // planes
	*(uint16_t*)&bmp[28] = 24; // bpp
	*(uint32_t*)&bmp[30] = 0; // compression BI_RGB
	*(uint32_t*)&bmp[34] = pixel_data_size;
	*(int32_t*)&bmp[38] = 2835; // 72 DPI
	*(int32_t*)&bmp[42] = 2835; // 72 DPI
	*(uint32_t*)&bmp[46] = 0; // clr used
	*(uint32_t*)&bmp[50] = 0; // clr important

	// Convert RGBA -> BGR with row padding
	for (int y = 0; y < height; ++y) {
		uint8_t* dst_row = &bmp[54 + y * row_padded];
		uint8_t* src_row = &rgba[(height - 1 - y) * width * 4];
		for (int x = 0; x < width; ++x) {
			dst_row[x * 3 + 0] = src_row[x * 4 + 2]; // B
			dst_row[x * 3 + 1] = src_row[x * 4 + 1]; // G
			dst_row[x * 3 + 2] = src_row[x * 4 + 0]; // R
		}
		for (int p = row_size; p < row_padded; ++p) dst_row[p] = 0;
	}

	// Write file
	std::ofstream ofs(filename, std::ios::binary);
	if (ofs.is_open()) {
		ofs.write((const char*)bmp.data(), bmp.size());
		ofs.close();
		printf("Dumped %s\n", filename);
	} else {
		printf("Failed to dump %s\n", filename);
	}
}

// Permute/shuffle pixels within an atlas slice using GPU shader
bool PermuteAtlasSlice(sg_image atlas, int slice_id, int atlas_size,
	const std::vector<shuffle >& shuffle_ops)
{
	if (shuffle_ops.empty()) return true;

	static bool initialized = false;
	// programs
	static GLuint prog_copy_arr_to_2d = 0;
	static GLuint prog_permute_from_2d = 0;
	// vao and instance vbo for permute
	static GLuint vao_perm = 0;
	static GLuint vbo_instance = 0;
	// uniforms
	static GLint uni_copy_src = -1, uni_copy_layer = -1;
	static GLint uni_copy_atlas_size = -1;
	static GLint uni_perm_cache = -1, uni_perm_atlas_size = -1;
	// cache texture
	static GLuint cache_tex = 0;
	static int cache_size = 0;

	auto ensure_cache = [&](int sz){
		if (cache_tex == 0 || cache_size != sz){
			if (cache_tex != 0) glDeleteTextures(1, &cache_tex);
			glGenTextures(1, &cache_tex);
			glBindTexture(GL_TEXTURE_2D, cache_tex);
#ifdef __EMSCRIPTEN__
			GLint internal_format = GL_RGBA;
#else
			GLint internal_format = GL_RGBA8;
#endif
			glTexImage2D(GL_TEXTURE_2D, 0, internal_format, sz, sz, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
			glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
			glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
			glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
			glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
			cache_size = sz;
		}
	};

	auto build_program = [](const char* vs, const char* fs) -> GLuint {
		GLuint v = glCreateShader(GL_VERTEX_SHADER);
		glShaderSource(v, 1, &vs, NULL);
		glCompileShader(v);
		GLuint f = glCreateShader(GL_FRAGMENT_SHADER);
		glShaderSource(f, 1, &fs, NULL);
		glCompileShader(f);
		GLuint p = glCreateProgram();
		glAttachShader(p, v);
		glAttachShader(p, f);
		glLinkProgram(p);
		glDeleteShader(v);
		glDeleteShader(f);
		return p;
	};

	if (!initialized){
#ifdef __EMSCRIPTEN__
		const char* vs_copy = R"(
			#version 300 es
			precision mediump float;
			void main(){
				int idx = gl_VertexID;
				vec2 pos = vec2((idx & 1) * 2.0 - 1.0, (idx & 2) - 1.0);
				gl_Position = vec4(pos, 0.0, 1.0);
			}
		)";
		const char* fs_copy = R"(
			#version 300 es
			precision mediump float;
			uniform sampler2DArray u_src;
			uniform int u_layer;
			uniform float u_atlas_size;
			out vec4 frag_color;
			void main(){
				ivec2 px = ivec2(gl_FragCoord.xy);
				frag_color = texelFetch(u_src, ivec3(px, u_layer), 0);
			}
		)";
		const char* vs_perm = R"(
			#version 300 es
			precision mediump float;
			layout(location = 0) in vec2 a_src_uv0;
			layout(location = 1) in vec2 a_src_uv1;
			layout(location = 2) in vec2 a_dst_uv0;
			layout(location = 3) in vec2 a_dst_uv1;
			flat out vec2 f_src_uv0;
			flat out vec2 f_dst_uv0;
			uniform float u_atlas_size;
			void main(){
				int vid = gl_VertexID % 6;
				vec2 pos = (vid==0)?vec2(0.0,0.0):(vid==1)?vec2(1.0,0.0):(vid==2)?vec2(0.0,1.0):(vid==3)?vec2(1.0,0.0):(vid==4)?vec2(1.0,1.0):vec2(0.0,1.0);
				vec2 dst_size = a_dst_uv1 - a_dst_uv0;
				vec2 pixel = (a_dst_uv0 + pos * dst_size) / u_atlas_size;
				gl_Position = vec4(pixel * 2.0 - 1.0, 0.0, 1.0);
				f_src_uv0 = a_src_uv0;
				f_dst_uv0 = a_dst_uv0;
			}
		)";
		const char* fs_perm = R"(
			#version 300 es
			precision mediump float;
			flat in vec2 f_src_uv0;
			flat in vec2 f_dst_uv0;
			uniform sampler2D u_cache;
			uniform float u_atlas_size;
			out vec4 frag_color;
			void main(){
				ivec2 dst_px = ivec2(gl_FragCoord.xy);
				ivec2 src_px = ivec2(f_src_uv0 + (vec2(dst_px) - f_dst_uv0));
				frag_color = texelFetch(u_cache, src_px, 0);
			}
		)";
#else
		const char* vs_copy = R"(
			#version 330 core
			void main(){
				int idx = gl_VertexID;
				vec2 pos = vec2((idx & 1) * 2.0 - 1.0, (idx & 2) - 1.0);
				gl_Position = vec4(pos, 0.0, 1.0);
			}
		)";
		const char* fs_copy = R"(
			#version 330 core
			uniform sampler2DArray u_src;
			uniform int u_layer;
			uniform float u_atlas_size;
			out vec4 frag_color;
			void main(){
				ivec2 px = ivec2(gl_FragCoord.xy);
				frag_color = texelFetch(u_src, ivec3(px, u_layer), 0);
			}
		)";
		const char* vs_perm = R"(
			#version 330 core
			layout(location = 0) in vec2 a_src_uv0;
			layout(location = 1) in vec2 a_src_uv1;
			layout(location = 2) in vec2 a_dst_uv0;
			layout(location = 3) in vec2 a_dst_uv1;
			flat out vec2 f_src_uv0;
			flat out vec2 f_dst_uv0;
			uniform float u_atlas_size;
			void main(){
				int vid = gl_VertexID % 6;
				vec2 pos = (vid==0)?vec2(0.0,0.0):(vid==1)?vec2(1.0,0.0):(vid==2)?vec2(0.0,1.0):(vid==3)?vec2(1.0,0.0):(vid==4)?vec2(1.0,1.0):vec2(0.0,1.0);
				vec2 dst_size = a_dst_uv1 - a_dst_uv0;
				vec2 pixel = (a_dst_uv0 + pos * dst_size) / u_atlas_size;
				gl_Position = vec4(pixel * 2.0 - 1.0, 0.0, 1.0);
				f_src_uv0 = a_src_uv0;
				f_dst_uv0 = a_dst_uv0;
			}
		)";
		const char* fs_perm = R"(
			#version 330 core
			flat in vec2 f_src_uv0;
			flat in vec2 f_dst_uv0;
			uniform sampler2D u_cache;
			uniform float u_atlas_size;
			out vec4 frag_color;
			void main(){
				ivec2 dst_px = ivec2(gl_FragCoord.xy);
				ivec2 src_px = ivec2(f_src_uv0 + (vec2(dst_px) - f_dst_uv0));
				frag_color = texelFetch(u_cache, src_px, 0);
			}
		)";
#endif
		prog_copy_arr_to_2d = build_program(vs_copy, fs_copy);
		prog_permute_from_2d = build_program(vs_perm, fs_perm);

		// uniform locations
		uni_copy_src = glGetUniformLocation(prog_copy_arr_to_2d, "u_src");
		uni_copy_layer = glGetUniformLocation(prog_copy_arr_to_2d, "u_layer");
		uni_copy_atlas_size = glGetUniformLocation(prog_copy_arr_to_2d, "u_atlas_size");
		uni_perm_cache = glGetUniformLocation(prog_permute_from_2d, "u_cache");
		uni_perm_atlas_size = glGetUniformLocation(prog_permute_from_2d, "u_atlas_size");

		// geometry
		glGenVertexArrays(1, &vao_perm);
		glGenBuffers(1, &vbo_instance);
		glBindVertexArray(vao_perm);
		glBindBuffer(GL_ARRAY_BUFFER, vbo_instance);
		glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)0);
		glEnableVertexAttribArray(0);
		glVertexAttribDivisor(0, 1);
		glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(2 * sizeof(float)));
		glEnableVertexAttribArray(1);
		glVertexAttribDivisor(1, 1);
		glVertexAttribPointer(2, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(4 * sizeof(float)));
		glEnableVertexAttribArray(2);
		glVertexAttribDivisor(2, 1);
		glVertexAttribPointer(3, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(6 * sizeof(float)));
		glEnableVertexAttribArray(3);
		glVertexAttribDivisor(3, 1);

		initialized = true;
	}

	_sg_image_t* img = _sg_lookup_image(&_sg.pools, atlas.id);
	if (!img) return false;

	// Save GL state
	GLint old_program; glGetIntegerv(GL_CURRENT_PROGRAM, &old_program);
	GLint old_vao; glGetIntegerv(GL_VERTEX_ARRAY_BINDING, &old_vao);
	GLint old_fbo; glGetIntegerv(GL_FRAMEBUFFER_BINDING, &old_fbo);

	// // Debug info
	// printf("PermuteAtlasSlice: slice=%d, ops=%d, atlas=%d\n", slice_id, (int)shuffle_ops.size(), atlas_size);
	// for (int i = 0; i < (int)shuffle_ops.size(); ++i) {
	// 	const float* p = (const float*)(&shuffle_ops[i]);
	// 	printf("  op[%d]: src=(%.0f,%.0f)-(%.0f,%.0f) dst=(%.0f,%.0f)-(%.0f,%.0f)\n",
	// 		i, p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]);
	// }
	//
	// // Dumps
	// static int dump_seq = 0;
	// char before_name[128];
	// sprintf(before_name, "permute_before_slice%d_%d.bmp", slice_id, dump_seq);
	// DumpTextureArraySliceBMP(atlas, slice_id, atlas_size, before_name);

	// Ensure cache
	ensure_cache(atlas_size);

	// Pass 1: copy current slice to cache 2D
	{
		GLuint fbo = 0;
		glGenFramebuffers(1, &fbo);
		glBindFramebuffer(GL_FRAMEBUFFER, fbo);
		glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, cache_tex, 0);
		glViewport(0, 0, atlas_size, atlas_size);
		glDisable(GL_SCISSOR_TEST);
		glUseProgram(prog_copy_arr_to_2d);
		glActiveTexture(GL_TEXTURE0);
		glBindTexture(GL_TEXTURE_2D_ARRAY, img->gl.tex[img->cmn.active_slot]);
		if (uni_copy_src >= 0) glUniform1i(uni_copy_src, 0);
		if (uni_copy_layer >= 0) glUniform1i(uni_copy_layer, slice_id);
		if (uni_copy_atlas_size >= 0) glUniform1f(uni_copy_atlas_size, (float)atlas_size);
		glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);
		glDeleteFramebuffers(1, &fbo);
	}

	// Pass 2: permute from cache -> array slice
	{
		// upload instance data
		glBindVertexArray(vao_perm);
		glBindBuffer(GL_ARRAY_BUFFER, vbo_instance);
		glBufferData(GL_ARRAY_BUFFER, shuffle_ops.size() * sizeof(float) * 8, shuffle_ops.data(), GL_DYNAMIC_DRAW);

		GLuint fbo = 0;
		glGenFramebuffers(1, &fbo);
		glBindFramebuffer(GL_FRAMEBUFFER, fbo);
		glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, img->gl.tex[img->cmn.active_slot], 0, slice_id);
		glViewport(0, 0, atlas_size, atlas_size);
		glDisable(GL_SCISSOR_TEST);
		glUseProgram(prog_permute_from_2d);
		glActiveTexture(GL_TEXTURE0);
		glBindTexture(GL_TEXTURE_2D, cache_tex);
		if (uni_perm_cache >= 0) glUniform1i(uni_perm_cache, 0);
		if (uni_perm_atlas_size >= 0) glUniform1f(uni_perm_atlas_size, (float)atlas_size);
		glDrawArraysInstanced(GL_TRIANGLES, 0, 6, (GLsizei)shuffle_ops.size());
		glDeleteFramebuffers(1, &fbo);
	}

	// Restore state
	glBindFramebuffer(GL_FRAMEBUFFER, old_fbo);
	glBindVertexArray(old_vao);
	glUseProgram(old_program);

	glFinish();
	_SG_GL_CHECK_ERROR();

	// Dump after
	// char after_name[128];
	// sprintf(after_name, "permute_after_slice%d_%d.bmp", slice_id, dump_seq);
	// DumpTextureArraySliceBMP(atlas, slice_id, atlas_size, after_name);
	// dump_seq++;

	return true;
}

