#pragma once
#include "me_impl.h"

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


// Permute/shuffle pixels within an atlas slice using GPU shader
bool PermuteAtlasSlice(sg_image atlas, int slice_id, int atlas_size,
	const std::vector<shuffle >& shuffle_ops)
{
	if (shuffle_ops.empty()) return true;

	static bool initialized = false;
	static GLuint shader_program = 0;
	static GLuint vao = 0;
	static GLuint instance_vbo = 0;
	static GLuint uniform_input_slice = 0;
	static GLuint uniform_src_tex = 0;
	static GLuint uniform_atlas_size = 0;

	// Initialize shader on first use
	if (!initialized) {
		// Vertex shader source
		const char* vertex_shader_source = R"(
			#version 330 core
			layout(location = 0) in vec2 a_src_uv0;
			layout(location = 1) in vec2 a_src_uv1;
			layout(location = 2) in vec2 a_dst_uv0;
			layout(location = 3) in vec2 a_dst_uv1;
			
			out vec2 v_src_uv;
			
			uniform float u_atlas_size;
			
			void main() {
				// Use gl_VertexID to determine quad position
				// 0,1,2 = first triangle, 3,4,5 = second triangle
				// Convert to [0,1] quad coordinates
				vec2 position;
				int vid = gl_VertexID % 6;
				if (vid == 0) position = vec2(0.0, 0.0); // bottom-left
				else if (vid == 1) position = vec2(1.0, 0.0); // bottom-right  
				else if (vid == 2) position = vec2(0.0, 1.0); // top-left
				else if (vid == 3) position = vec2(1.0, 0.0); // bottom-right
				else if (vid == 4) position = vec2(1.0, 1.0); // top-right
				else position = vec2(0.0, 1.0); // top-left
				
				// Map to destination region in clip space
				vec2 dst_size = a_dst_uv1 - a_dst_uv0;
				vec2 dst_center = (a_dst_uv0 + a_dst_uv1) * 0.5;
				
				// Convert destination pixel coordinates to clip space [-1,1]
				vec2 clip_pos = (dst_center + (position - 0.5) * dst_size) / (u_atlas_size * 0.5) - 1.0;
				gl_Position = vec4(clip_pos, 0.0, 1.0);
				
				// Interpolate source UV coordinates
				vec2 src_size = a_src_uv1 - a_src_uv0;
				v_src_uv = a_src_uv0 + position * src_size;
			}
		)";

		// Fragment shader source
		const char* fragment_shader_source = R"(
			#version 330 core
			in vec2 v_src_uv;
			out vec4 frag_color;
			
			uniform sampler2DArray u_src_tex;
			uniform int u_input_slice_num;
			
			void main() {
				frag_color = texelFetch(u_src_tex, ivec3(ivec2(v_src_uv), u_input_slice_num), 0);
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
		uniform_src_tex = glGetUniformLocation(shader_program, "u_src_tex");
		uniform_atlas_size = glGetUniformLocation(shader_program, "u_atlas_size");

		// Create VAO and instance VBO
		glGenVertexArrays(1, &vao);
		glGenBuffers(1, &instance_vbo);
		
		glBindVertexArray(vao);
		glBindBuffer(GL_ARRAY_BUFFER, instance_vbo);
		
		// Set up instance attributes (per-instance data)
		// src_uv0 (vec2) - instanced
		glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)0);
		glEnableVertexAttribArray(0);
		glVertexAttribDivisor(0, 1); // 1 = per instance
		
		// src_uv1 (vec2) - instanced  
		glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(2 * sizeof(float)));
		glEnableVertexAttribArray(1);
		glVertexAttribDivisor(1, 1);
		
		// dst_uv0 (vec2) - instanced
		glVertexAttribPointer(2, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(4 * sizeof(float)));
		glEnableVertexAttribArray(2);
		glVertexAttribDivisor(2, 1);
		
		// dst_uv1 (vec2) - instanced
		glVertexAttribPointer(3, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(6 * sizeof(float)));
		glEnableVertexAttribArray(3);
		glVertexAttribDivisor(3, 1);

		// Cleanup
		glDeleteShader(vertex_shader);
		glDeleteShader(fragment_shader);

		initialized = true;
	}

	_sg_image_t* img = _sg_lookup_image(&_sg.pools, atlas.id);
	if (!img) return false;

	// Save current OpenGL state
	GLint old_program;
	glGetIntegerv(GL_CURRENT_PROGRAM, &old_program);
	GLint old_vao;
	glGetIntegerv(GL_VERTEX_ARRAY_BINDING, &old_vao);
	GLint old_fbo;
	glGetIntegerv(GL_FRAMEBUFFER_BINDING, &old_fbo);

	// Use our shader program
	glUseProgram(shader_program);
	glBindVertexArray(vao);

	// Upload instance data
	glBindBuffer(GL_ARRAY_BUFFER, instance_vbo);
	glBufferData(GL_ARRAY_BUFFER, shuffle_ops.size() * sizeof(float) * 8, shuffle_ops.data(), GL_DYNAMIC_DRAW);

	// Bind atlas texture array
	glActiveTexture(GL_TEXTURE0);
	glBindTexture(GL_TEXTURE_2D_ARRAY, img->gl.tex[img->cmn.active_slot]);
	glUniform1i(uniform_src_tex, 0);
	glUniform1i(uniform_input_slice, slice_id);
	glUniform1f(uniform_atlas_size, atlas_size);

	// Create framebuffer for rendering
	GLuint temp_fbo;
	glGenFramebuffers(1, &temp_fbo);
	glBindFramebuffer(GL_FRAMEBUFFER, temp_fbo);
	glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0,
		img->gl.tex[img->cmn.active_slot], 0, slice_id);

	// Set viewport to full atlas
	glViewport(0, 0, atlas_size, atlas_size);

	// Single instanced draw call - 6 vertices per quad, N instances
	glDrawArraysInstanced(GL_TRIANGLES, 0, 6, shuffle_ops.size());

	// Cleanup
	glDeleteFramebuffers(1, &temp_fbo);

	// Restore OpenGL state
	glBindFramebuffer(GL_FRAMEBUFFER, old_fbo);
	glBindVertexArray(old_vao);
	glUseProgram(old_program);

	glFinish();
	_SG_GL_CHECK_ERROR();
	return true;
}

