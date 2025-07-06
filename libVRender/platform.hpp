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