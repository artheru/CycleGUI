// todo: Add textarea support, add font dynamic update from browser support to free the need of "georgia.ttf" font.

#include <stdio.h>

#include <emscripten.h>

#define _SLOG_EMSCRIPTEN
// #include <emscripten/websocket.h>
// EMSCRIPTEN_WEBSOCKET_T ws;

#define GLFW_INCLUDE_ES3
#include <GLES3/gl3.h>
#include <GLFW/glfw3.h>

#include "imgui.h"
#include "imgui_internal.h"
#include "backends/imgui_impl_glfw.h"
#include "backends/imgui_impl_opengl3.h"
#include <iostream>
#include <string.h>
#include <vector>
#include <misc/freetype/imgui_freetype.h>

#include "IconsForkAwesome.h"
#include "implot.h"
#include <implot_internal.h>

GLFWwindow* g_window;
ImVec4 clear_color = ImVec4(0.45f, 0.55f, 0.60f, 1.00f);
bool show_demo_window = true;
bool show_another_window = false;
int g_width;
int g_height;
double g_dpi;


EM_JS(void, logging, (const char* c_str), {
	const str = UTF8ToString(c_str); console.log(str);
	});


enum WebImeResultFlags
{
    WebImeResult_TextChanged  = 1,
    WebImeResult_RequestHide  = 4,
    WebImeResult_TabPressed   = 8,
    WebImeResult_EnterPressed = 16,
};

struct WebImeBridgeState
{
    ImGuiID active_id = 0;
};

static WebImeBridgeState g_WebImeBridge;

EM_JS(void, web_ime_sync, (int id, int type, float x, float y, float w, float h, float line_height, float font_size, float dpi_scale, const char* text, int cursor, int sel_start, int sel_end, float rounding, float pad_x, float pad_y, float bg_r, float bg_g, float bg_b, float bg_a, float text_r, float text_g, float text_b, float text_a), {
    var target = (typeof Module !== 'undefined' && Module.cycleGuiIme) ? Module.cycleGuiIme
                 : (typeof window !== 'undefined' ? window.cycleGuiIme : null);
    if (!target)
        return;
    target.sync(id, type, x, y, w, h, line_height, font_size, dpi_scale, UTF8ToString(text), cursor, sel_start, sel_end, rounding, pad_x, pad_y, bg_r, bg_g, bg_b, bg_a, text_r, text_g, text_b, text_a);
});

EM_JS(int, web_ime_poll, (int id, char* out_text, int out_capacity, int* out_cursor, int* out_sel_start, int* out_sel_end, int* out_flags), {
    var target = (typeof Module !== 'undefined' && Module.cycleGuiIme) ? Module.cycleGuiIme
                 : (typeof window !== 'undefined' ? window.cycleGuiIme : null);
    if (!target)
        return 0;
    const result = target.poll(id);
    if (!result)
        return 0;
    const storedLen = stringToUTF8(result.text || "", out_text, out_capacity);
    Module.HEAP32[out_cursor >> 2] = result.cursor | 0;
	Module.HEAP32[out_sel_start >> 2] = result.selStart | 0;
	Module.HEAP32[out_sel_end >> 2] = result.selEnd | 0;
	Module.HEAP32[out_flags >> 2] = result.flags | 0;
	return storedLen;
});

EM_JS(void, web_ime_hide, (), {
    var target = (typeof Module !== 'undefined' && Module.cycleGuiIme) ? Module.cycleGuiIme
                 : (typeof window !== 'undefined' ? window.cycleGuiIme : null);
    if (target)
        target.hide();
});
static void WebImeApplySelection(ImGuiInputTextState* state, int cursor_cp, int sel_start_cp, int sel_end_cp)
{
    const int text_len_w = state->CurLenW;
    state->Stb.cursor = ImClamp(cursor_cp, 0, text_len_w);
    state->Stb.select_start = ImClamp(sel_start_cp, 0, text_len_w);
    state->Stb.select_end = ImClamp(sel_end_cp, 0, text_len_w);
    state->Stb.has_preferred_x = 0;
}

static void WebImeApplyText(ImGuiInputTextState* state, const char* text_utf8, int text_len_utf8, int cursor_cp, int sel_start_cp, int sel_end_cp)
{
    if (text_utf8 == nullptr)
        text_utf8 = "";

    const int utf8_len = (text_len_utf8 >= 0) ? text_len_utf8 : (int)strlen(text_utf8);

    // Update UTF-8 buffer
    if (state->TextA.Capacity < utf8_len + 1)
        state->TextA.reserve(utf8_len + 1);
    state->TextA.resize(utf8_len + 1);
    if (utf8_len > 0)
        memcpy(state->TextA.Data, text_utf8, (size_t)utf8_len);
    state->TextA[utf8_len] = 0;

    // Update UTF-16/ImWchar buffer
    if (state->TextW.Capacity < utf8_len + 1)
        state->TextW.reserve(utf8_len + 1);
    state->TextW.resize(utf8_len + 1);
    const char* utf8_end = nullptr;
    int new_len_w = ImTextStrFromUtf8(state->TextW.Data, state->TextW.Size, text_utf8, text_utf8 + utf8_len, &utf8_end);
    if (new_len_w < 0)
        new_len_w = 0;
    state->TextW.resize(new_len_w + 1);
    state->TextW[new_len_w] = 0;

    state->CurLenW = new_len_w;
    state->CurLenA = utf8_len;
    state->TextAIsValid = true;
    state->Edited = true;
    state->ExternEdited = true;
    state->CursorFollow = true;
    state->CursorAnimReset();
    WebImeApplySelection(state, cursor_cp, sel_start_cp, sel_end_cp);
    state->ScrollX = 0.0f;

    ImGuiContext& g = *GImGui;
    if (g.InputTextDeactivatedState.ID == state->ID)
    {
        ImGuiInputTextDeactivatedState& deactivated = g.InputTextDeactivatedState;
        deactivated.TextA.resize(state->CurLenA + 1);
        if (state->CurLenA > 0)
            memcpy(deactivated.TextA.Data, state->TextA.Data, (size_t)state->CurLenA);
        deactivated.TextA[state->CurLenA] = 0;
    }
}

static void CycleGui_UpdateWebIme(double dpi)
{
    ImGuiIO& io = ImGui::GetIO();
    const ImGuiID active_id = ImGui::GetActiveID();
    if (!io.WantTextInput || active_id == 0)
    {
        if (g_WebImeBridge.active_id != 0)
        {
            web_ime_hide();
            g_WebImeBridge.active_id = 0;
        }
        return;
    }

    ImGuiInputTextState* state = ImGui::GetInputTextState(active_id);
    if (state == nullptr)
    {
        if (g_WebImeBridge.active_id != 0)
        {
            web_ime_hide();
            g_WebImeBridge.active_id = 0;
        }
        return;
    }

    g_WebImeBridge.active_id = active_id;

    if (!state->TextAIsValid)
    {
        state->TextA.resize(state->TextW.Size * 4 + 1);
        ImTextStrToUtf8(state->TextA.Data, state->TextA.Size, state->TextW.Data, state->TextW.Data + state->CurLenW);
        state->TextAIsValid = true;
    }

    ImRect frame_bb = state->FrameBB;
    if (frame_bb.GetWidth() <= 0.0f || frame_bb.GetHeight() <= 0.0f)
    {
        const ImVec2 fallback = ImGui::GetItemRectSize();
        frame_bb.Max = ImVec2(frame_bb.Min.x + fallback.x, frame_bb.Min.y + fallback.y);
    }

    const float inv_dpi = (dpi != 0.0) ? (1.0f / (float)dpi) : 1.0f;
    const float x = frame_bb.Min.x * inv_dpi;
    const float y = frame_bb.Min.y * inv_dpi;
    const float w = ImMax(1.0f, frame_bb.GetWidth() * inv_dpi);
    const float h = ImMax(1.0f, frame_bb.GetHeight() * inv_dpi);

    ImGuiContext* ctx = state->Ctx;
    float font_size = ImGui::GetFontSize() * inv_dpi;
    float line_height = font_size;
    if (ctx)
    {
        if (ctx->Font)
            font_size = ctx->Font->FontSize * inv_dpi;
        line_height = ctx->FontSize * inv_dpi;
    }

    int type = 0;
    if (state->Flags & ImGuiInputTextFlags_Password)
        type = 2;
    else if (state->Flags & ImGuiInputTextFlags_Multiline)
        type = 1;

    ImGuiContext& g = *GImGui;
    const ImGuiStyle& style = g.Style;
    float rounding = style.FrameRounding * inv_dpi;
    ImVec2 frame_padding = ImVec2(style.FramePadding.x * inv_dpi, style.FramePadding.y * inv_dpi);
    ImVec4 frame_bg = style.Colors[ImGuiCol_FrameBg];
    if (g.ActiveId == active_id)
        frame_bg = style.Colors[ImGuiCol_FrameBgActive];
    else if (g.HoveredId == active_id)
        frame_bg = style.Colors[ImGuiCol_FrameBgHovered];
    ImVec4 text_col = style.Colors[ImGuiCol_Text];

    web_ime_sync(
        (int)active_id,
        type,
        x, y, w, h,
        line_height,
        font_size,
        (float)dpi,
        state->TextA.Data,
        state->Stb.cursor,
        state->Stb.select_start,
        state->Stb.select_end,
        rounding,
        frame_padding.x,
        frame_padding.y,
        frame_bg.x, frame_bg.y, frame_bg.z, frame_bg.w,
        text_col.x, text_col.y, text_col.z, text_col.w);

    const int buffer_capacity = state->BufCapacityA > 0 ? state->BufCapacityA : ImMax((int)state->TextA.Capacity, 1);
    std::vector<char> new_text(buffer_capacity > 0 ? buffer_capacity : 1);
    int cursor_cp = state->Stb.cursor;
    int sel_start_cp = state->Stb.select_start;
    int sel_end_cp = state->Stb.select_end;
    int result_flags = 0;
    int written_bytes = web_ime_poll((int)active_id, new_text.data(), (int)new_text.size(), &cursor_cp, &sel_start_cp, &sel_end_cp, &result_flags);

    WebImeApplyText(state, new_text.data(), written_bytes, cursor_cp, sel_start_cp, sel_end_cp);

    if (result_flags & WebImeResult_TabPressed)
    {
        io.AddKeyEvent(ImGuiKey_Tab, true);
        io.AddKeyEvent(ImGuiKey_Tab, false);
    }

    if (result_flags & WebImeResult_EnterPressed)
    {
        io.AddKeyEvent(ImGuiKey_Enter, true);
        io.AddKeyEvent(ImGuiKey_Enter, false);
    }

    if (result_flags & WebImeResult_RequestHide)
    {
        ImGui::ClearActiveID();
        g.ExternEdit = true;
        web_ime_hide();
        g_WebImeBridge.active_id = 0;
    }
}

// Function used by c++ to get the size of the html canvas
EM_JS(int, canvas_get_width, (), {
      return Module.canvas.width;
      });

// Function used by c++ to get the size of the html canvas
EM_JS(int, canvas_get_height, (), {
      return Module.canvas.height;
      });

EM_JS(double, getDevicePixelRatio, (), { return dpr || 1 });

// Function called by javascript
EM_JS(void, resizeCanvas, (), {
      js_resizeCanvas();
});

EM_JS(void, reload, (), {
	location.reload();
	});

EM_JS(void, startWS, (), {
	connect2server();
});

EM_JS(float, getJsTime, (), {
	return getTimestampSMS();
});

EM_JS(void, focus, (bool yes), {
	if (yes) proxyinput.focus();
	else proxyinput.blur();
})

EM_JS(const char*, getHost, (), {
	var length = lengthBytesUTF8(terminalDataUrl) + 1;
	var buffer = _malloc(length);
	stringToUTF8(terminalDataUrl, buffer, length + 1);

	return buffer;
	});

EM_JS(void, js_send_binary, (const uint8_t* arr, int length), {
    // Convert C++ array to JavaScript Uint8Array
    var data = new Uint8Array(Module.HEAPU8.subarray(arr, arr + length));
    sendBinaryToServer(data); // Call the JavaScript function
});

EM_JS(bool, testWS, (), {
	return socket;
});

// Bridge to get default imgui.ini content from JS (may be null)
EM_JS(const char*, getDefaultImGUILayoutIni, (), {
    if (defaultImGUILayoutIni == "placeholder2") return 0;
    var length = lengthBytesUTF8(defaultImGUILayoutIni) + 1;
    var buffer = _malloc(length);
    stringToUTF8(defaultImGUILayoutIni, buffer, length);
    return buffer;
});

bool forget = false;
void goodbye()
{
	if (forget) return;
	ImGui::OpenPopup("Connection lost");

    ImVec2 center = ImGui::GetMainViewport()->GetCenter();
    ImGui::SetNextWindowPos(center, ImGuiCond_Appearing, ImVec2(0.5f, 0.5f));
	if (ImGui::BeginPopupModal("Connection lost", NULL, ImGuiWindowFlags_AlwaysAutoResize))
	{
		ImGui::Text("Connection to %s failed. Suggest to reload this page!", getHost());
		ImGui::Separator();
		if (ImGui::Button("Reload"))
		{
			reload();
		}
		ImGui::SameLine();
		if (ImGui::Button("Stay"))
		{
			forget = true;
		}
		ImGui::EndPopup();
	}
}

void on_size_changed()
{
	glfwSetWindowSize(g_window, g_width, g_height);

	ImGui::SetCurrentContext(ImGui::GetCurrentContext());
}

void Stylize();

#include "cycleui.h"

EM_JS(void, trydownload, (const char* filehash, int pid, const char* fname), {
	const fh = UTF8ToString(filehash);
	const fn = UTF8ToString(fname);
	jsDownload(fh, pid, fn)
});
void ExternDisplay(const char* filehash, int pid, const char* fname)
{
	trydownload(filehash, pid, fname);
}

EM_JS(void, notifyLoaded, (), {
	ccmain.style.visibility = "visible";
});


// #define TOC(X) span= std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic).count(); \
// 	ImGui::Text("tic %s=%.2fms, total=%.1fms",X,span*0.001,((float)std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic_st).count())*0.001);\
// 	tic=std::chrono::high_resolution_clock::now();
std::string preparedString("/na");
//#define TOC(X) ;
// #define TOC(X) \
//     span = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic).count(); \
//     staticString += "\nmtic " + std::string(X) + "=" + std::to_string(span * 0.001) + "ms, total=" + std::to_string(((float)std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic_st).count()) * 0.001) + "ms"; \
//     tic = std::chrono::high_resolution_clock::now();

int frame = 0;
void loop()
{
	auto tic=std::chrono::high_resolution_clock::now(), tic_st = tic;
	int span, width = canvas_get_width(), height = canvas_get_height();
	double dpi = getDevicePixelRatio();

	if (dpi != g_dpi) { g_dpi = dpi; Stylize(); }
	if (width != g_width || height != g_height) { g_width = width; g_height = height; on_size_changed(); }

	glfwPollEvents();
	int display_w, display_h;
	glfwMakeContextCurrent(g_window);
	glfwGetFramebufferSize(g_window, &display_w, &display_h);
	glViewport(0, 0, display_w, display_h);
    glScissor(0, 0, display_w, display_h);
	glClear(GL_COLOR_BUFFER_BIT);

	ImGui_ImplOpenGL3_NewFrame();
	ImGui_ImplGlfw_NewFrame();
	ImGui::NewFrame();

	BeforeDrawAny();

	ui.viewports[0].camera.dpi = dpi;

	TOC("prepare_main");
	ProcessUIStack();
	TOC("ui");
	DrawMainWorkspace();
	TOC("drawWS");
	FinalizeFrame();
	// static bool show_demo_window = true;
	// if (show_demo_window)
	// ImGui::ShowDemoWindow(nullptr);

    // ImGui::Text("🖐This is some useful text.以及汉字, I1l, 0Oo");
    // ImGui::Text(ICON_FK_ADDRESS_BOOK" TEST FK");

	CycleGui_UpdateWebIme(g_dpi);

	// ImGui::Text(preparedString.c_str());
	if (!testWS())
		goodbye();

	ImGui::Render();

	TOC("imgui");
	ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());
	TOC("imgui-rendered");

	if (frame++ == 0) notifyLoaded();
	glFinish();

	TOC("fin_loop");
	preparedString = staticString;
	staticString = "--MAIN--\n";

	if (ImGui::GetIO().WantSaveIniSettings) {
		EM_ASM({ FS.syncfs(false, err => err ? console.error("Error syncing FS:", err) : console.log("cache synced to persistent.")); });
		ImGui::GetIO().WantSaveIniSettings = false;
	}
}


int init_gl()
{
	if (!glfwInit()) { fprintf(stderr, "Failed to initialize GLFW\n"); return 1; }
	
	glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
	glfwWindowHint(GLFW_SAMPLES, 4);
    glfwSwapInterval(0);

	g_window = glfwCreateWindow(g_width, g_height, "WebGui Demo", NULL, NULL);
	if (g_window == NULL) { fprintf(stderr, "Failed to open GLFW window.\n"); glfwTerminate(); return -1; }
	glfwMakeContextCurrent(g_window);

	glfwSetMouseButtonCallback(g_window, mouse_button_callback);
	glfwSetCursorPosCallback(g_window, cursor_position_callback);
	glfwSetScrollCallback(g_window, scroll_callback);
	return 0;
}


EM_JS(const char*, drawCharProxy, (int codepoint), {
	let uint8Array = drawChar(codepoint);
	if (!uint8Array) return 0;
    var byteCount = uint8Array.length;
    var ptr = getModuleAsm().malloc(byteCount);
    Module.HEAPU8.set(uint8Array, ptr);
    return ptr;
});

EM_JS(void, uploadMsg, (const char* c_str), {
	const str = UTF8ToString(c_str);
	loaderMsg(str);
});

// Linear interpolation downsampling function
void downsample(uint8_t* originalRGBA, int originalSz, int outputSz, uint8_t* outputRGBA) {
    float scale = (float)originalSz / (float)outputSz;
    for (int y = 0; y < outputSz; y++) {
        for (int x = 0; x < outputSz; x++) {
            float srcX = x * scale, srcY = y * scale;
            int x0 = (int)floorf(srcX), y0 = (int)floorf(srcY);
            int x1 = (x0 + 1 >= originalSz) ? originalSz - 1 : x0 + 1;
            int y1 = (y0 + 1 >= originalSz) ? originalSz - 1 : y0 + 1;
            float dx = srcX - x0, dy = srcY - y0;
            int outputIdx = (y * outputSz + x) * 4;
            
            for (int c = 0; c < 4; c++) {
                uint8_t p00 = originalRGBA[(y0 * originalSz + x0) * 4 + c];
                uint8_t p01 = originalRGBA[(y0 * originalSz + x1) * 4 + c];
                uint8_t p10 = originalRGBA[(y1 * originalSz + x0) * 4 + c];
                uint8_t p11 = originalRGBA[(y1 * originalSz + x1) * 4 + c];
                outputRGBA[outputIdx + c] = (uint8_t)((1 - dx) * (1 - dy) * p00 + dx * (1 - dy) * p01 + 
                                                      (1 - dx) * dy * p10 + dx * dy * p11 + 0.5f);
            }
        }
    }
}


EM_JS(int, getIcoSz, (), {
	return favicosz;
});

EM_JS(const char*, getIco, (), {
    var byteCount = favui8arr.length;
    var ptr = getModuleAsm().malloc(byteCount);
    Module.HEAPU8.set(favui8arr, ptr);
    return ptr;
});

void GoFullScreen(bool fullscreen){}; //todo: use html capability.

extern "C" { //used for imgui_freetype.cpp patch.
	int addedChars = 0;

	void encodeUTF8(char32_t cp, char* dest, size_t sz) {
	    if (cp <= 0x7F) snprintf(dest, sz, "%c", (char)cp);
	    else if (cp <= 0x7FF) snprintf(dest, sz, "%c%c", (char)(0xC0 | (cp >> 6)), (char)(0x80 | (cp & 0x3F)));
	    else if (cp <= 0xFFFF) snprintf(dest, sz, "%c%c%c", (char)(0xE0 | (cp >> 12)), 
	                                     (char)(0x80 | ((cp >> 6) & 0x3F)), (char)(0x80 | (cp & 0x3F)));
	    else if (cp <= 0x10FFFF) snprintf(dest, sz, "%c%c%c%c", (char)(0xF0 | (cp >> 18)), 
	                                       (char)(0x80 | ((cp >> 12) & 0x3F)), (char)(0x80 | ((cp >> 6) & 0x3F)), (char)(0x80 | (cp & 0x3F)));
	}

	uint8_t* fallback_text_render(uint32_t codepoint)
	{
        if (codepoint == 0x2b00) { // App icon
			auto appIcoSz = -1;
			while ((appIcoSz=getIcoSz())==0) { uploadMsg("Downloading Icon resources"); emscripten_sleep(100); }
			if (appIcoSz == -1) { printf("Proceed without app icon.\n"); return nullptr; }
        	uint8_t *appIco = (uint8_t*)getIco();
            ui.app_icon.height = ui.app_icon.width = 18.0f * g_dpi;
		    ui.app_icon.advanceX = ui.app_icon.width + 2; ui.app_icon.offsetY = -ui.app_icon.width * 0.85;
            downsample(appIco, 48, ui.app_icon.height, ui.app_icon.rgba);
            return (uint8_t*)&ui.app_icon;
        }

		if (++addedChars % 500 == 0) {
			emscripten_sleep(0);
		    char utf8Char[5] = {0}, tmp[40];
		    encodeUTF8(codepoint, utf8Char, sizeof(utf8Char));
		    std::snprintf(tmp, sizeof(tmp), "Building glyph: %s", utf8Char);
			uploadMsg(tmp);
		}
		return (uint8_t*)drawCharProxy(codepoint);
	}
}

void Stylize()
{
    ImPlotContext& gp = *GImPlot;
    gp.Style.Colormap = ImPlotColormap_Spectral;

	ImGuiIO& io = ImGui::GetIO();


	// Setup Dear ImGui style
	ImGuiStyle& style = ImGui::GetStyle();
	style = ImGuiStyle(); // Reset all style config before scaling
	style.FrameBorderSize = 1; style.FrameRounding = 6.0f; style.GrabRounding = 6.0f; style.ScrollbarRounding = 9.0f;
	style.WindowPadding = ImVec2(8, 8); style.FramePadding = ImVec2(8, 3); style.CellPadding = ImVec2(5, 2);
	style.ItemSpacing = ImVec2(12, 5); style.ItemInnerSpacing = ImVec2(8, 6); style.SeparatorTextPadding = ImVec2(25, 3);
	style.IndentSpacing = 25.0f; style.ScrollbarSize = 15.0f; style.GrabMinSize = 19.0f;
	style.AntiAliasedLines = false; // AMD platform won't draw line without this
	style.ScaleAllSizes(g_dpi);

	ImVec4* colors = ImGui::GetStyle().Colors;
    struct {ImGuiCol_ idx; ImVec4 val;} colorMap[] = {
        {ImGuiCol_Text, {1.00f, 1.00f, 1.00f, 1.00f}}, {ImGuiCol_TextDisabled, {0.50f, 0.50f, 0.50f, 1.00f}},
        {ImGuiCol_WindowBg, {0.09f, 0.09f, 0.09f, 1.00f}}, {ImGuiCol_ChildBg, {0.00f, 0.00f, 0.00f, 0.67f}},
        {ImGuiCol_PopupBg, {0.07f, 0.05f, 0.07f, 1.00f}}, {ImGuiCol_Border, {0.33f, 0.33f, 0.33f, 0.50f}},
        {ImGuiCol_BorderShadow, {0.00f, 0.00f, 0.00f, 0.00f}}, {ImGuiCol_FrameBg, {0.00f, 0.00f, 0.00f, 1.00f}},
        {ImGuiCol_FrameBgHovered, {0.04f, 0.15f, 0.10f, 1.00f}}, {ImGuiCol_FrameBgActive, {0.05f, 0.33f, 0.34f, 1.00f}},
        {ImGuiCol_TitleBg, {0.04f, 0.04f, 0.04f, 1.00f}}, {ImGuiCol_TitleBgActive, {0.55f, 0.12f, 0.46f, 1.00f}},
        {ImGuiCol_TitleBgCollapsed, {0.00f, 0.00f, 0.00f, 0.47f}}, {ImGuiCol_MenuBarBg, {0.12f, 0.11f, 0.31f, 1.00f}},
        {ImGuiCol_ScrollbarBg, {0.02f, 0.02f, 0.02f, 0.0f}}, {ImGuiCol_ScrollbarGrab, {0.31f, 0.31f, 0.31f, 1.00f}},
        {ImGuiCol_ScrollbarGrabHovered, {0.41f, 0.41f, 0.41f, 1.00f}}, {ImGuiCol_ScrollbarGrabActive, {0.51f, 0.51f, 0.51f, 1.00f}},
        {ImGuiCol_CheckMark, {0.27f, 0.98f, 0.26f, 1.00f}}, {ImGuiCol_SliderGrab, {0.41f, 0.31f, 0.31f, 1.00f}},
        {ImGuiCol_SliderGrabActive, {0.64f, 0.18f, 0.18f, 1.00f}}, {ImGuiCol_Button, {0.24f, 0.22f, 0.21f, 1.00f}},
        {ImGuiCol_ButtonHovered, {0.56f, 0.24f, 0.60f, 1.00f}}, {ImGuiCol_ButtonActive, {0.38f, 0.06f, 0.98f, 1.00f}},
        {ImGuiCol_Header, {0.26f, 0.04f, 0.35f, 1.00f}}, {ImGuiCol_HeaderHovered, {0.27f, 0.11f, 0.63f, 1.00f}},
        {ImGuiCol_HeaderActive, {0.44f, 0.26f, 0.98f, 1.00f}}, {ImGuiCol_Separator, {0.43f, 0.43f, 0.50f, 0.50f}},
        {ImGuiCol_SeparatorHovered, {0.63f, 0.10f, 0.75f, 0.78f}}, {ImGuiCol_SeparatorActive, {0.54f, 0.10f, 0.75f, 1.00f}},
        {ImGuiCol_ResizeGrip, {0.48f, 0.10f, 0.10f, 1.00f}}, {ImGuiCol_ResizeGripHovered, {0.98f, 0.26f, 1.00f, 1.00f}},
        {ImGuiCol_ResizeGripActive, {1.00f, 0.00f, 0.00f, 1.00f}}, {ImGuiCol_Tab, {0.35f, 0.12f, 0.12f, 1.00f}},
        {ImGuiCol_TabHovered, {0.95f, 0.22f, 1.00f, 1.00f}}, {ImGuiCol_TabActive, {0.51f, 0.20f, 0.68f, 1.00f}},
        {ImGuiCol_TabUnfocused, {0.07f, 0.10f, 0.15f, 1.00f}}, {ImGuiCol_TabUnfocusedActive, {0.14f, 0.26f, 0.42f, 1.00f}},
        {ImGuiCol_DockingPreview, {0.26f, 0.59f, 0.98f, 0.70f}}, {ImGuiCol_DockingEmptyBg, {0.20f, 0.20f, 0.20f, 1.00f}},
        {ImGuiCol_PlotLines, {0.61f, 0.61f, 0.61f, 1.00f}}, {ImGuiCol_PlotLinesHovered, {1.00f, 0.43f, 0.35f, 1.00f}},
        {ImGuiCol_PlotHistogram, {0.90f, 0.70f, 0.00f, 1.00f}}, {ImGuiCol_PlotHistogramHovered, {1.00f, 0.60f, 0.00f, 1.00f}},
        {ImGuiCol_TableHeaderBg, {0.19f, 0.19f, 0.20f, 1.00f}}, {ImGuiCol_TableBorderStrong, {0.31f, 0.31f, 0.35f, 1.00f}},
        {ImGuiCol_TableBorderLight, {0.23f, 0.23f, 0.25f, 1.00f}}, {ImGuiCol_TableRowBg, {0.00f, 0.00f, 0.00f, 0.00f}},
        {ImGuiCol_TableRowBgAlt, {1.00f, 1.00f, 1.00f, 0.06f}}, {ImGuiCol_TextSelectedBg, {0.26f, 0.59f, 0.98f, 0.35f}},
        {ImGuiCol_DragDropTarget, {1.00f, 1.00f, 0.00f, 0.90f}}, {ImGuiCol_NavHighlight, {0.55f, 0.26f, 0.98f, 1.00f}},
        {ImGuiCol_NavWindowingHighlight, {1.00f, 1.00f, 1.00f, 0.70f}}, {ImGuiCol_NavWindowingDimBg, {0.80f, 0.80f, 0.80f, 0.20f}},
        {ImGuiCol_ModalWindowDimBg, {0.80f, 0.80f, 0.80f, 0.35f}}
    };
    for (auto& c : colorMap) colors[c.idx] = c.val;

	// Load Fonts
	io.Fonts->Clear();
	io.Fonts->AddFontFromFileTTF("data/georgia.ttf", 15.0f * g_dpi, 0, io.Fonts->GetGlyphRangesGreek());
	
     static ImWchar ranges2[] = { ICON_MIN_FK, ICON_MAX_FK,
        0x0020, 0x00FF, // Basic Latin + Latin Supplement
        0x2000, 0x206F, // General Punctuation
        0x3000, 0x30FF, // CJK Symbols and Punctuations, Hiragana, Katakana
        0x31F0, 0x31FF, // Katakana Phonetic Extensions
        0xFF00, 0xFFEF, // Half-width characters
        0xFFFD, 0xFFFD, // Invalid
        0x4e00, 0x9FAF, // CJK Ideograms
		0
     	};
	static ImFontConfig cfg2;
	cfg2.OversampleH = cfg2.OversampleV = 1; cfg2.MergeMode = true; cfg2.GlyphOffset = ImVec2(0, 1 * g_dpi);
	io.Fonts->AddFontFromFileTTF("data/forkawesome-webfont.ttf", 16.0f * g_dpi, &cfg2, ranges2);

	// emojis: app icon + standard emoji ranges (compressed)
	static ImWchar ranges3[]= {
		0x2b00,0x2b00, 0x23,0x23, 0x2A,0x2A, 0x30,0x39, 0xA9,0xA9, 0xAE,0xAE,
	    0x203C,0x203C, 0x2049,0x2049, 0x2122,0x2122, 0x2139,0x2139, 0x2194,0x2199, 0x21A9,0x21AA,
	    0x231A,0x231B, 0x2328,0x2328, 0x23CF,0x23CF, 0x23E9,0x23F3, 0x23F8,0x23FA, 0x24C2,0x24C2,
	    0x25AA,0x25AB, 0x25B6,0x25B6, 0x25C0,0x25C0, 0x25FB,0x25FE, 0x2600,0x2604, 0x260E,0x260E,
	    0x2611,0x2611, 0x2614,0x2615, 0x2618,0x2618, 0x261D,0x261D, 0x2620,0x2620, 0x2622,0x2623,
	    0x2626,0x2626, 0x262A,0x262A, 0x262E,0x262F, 0x2638,0x263A, 0x2640,0x2640, 0x2642,0x2642,
	    0x2648,0x2653, 0x265F,0x2660, 0x2663,0x2663, 0x2665,0x2666, 0x2668,0x2668, 0x267B,0x267B,
	    0x267E,0x267F, 0x2692,0x2697, 0x2699,0x2699, 0x269B,0x269C, 0x26A0,0x26A1, 0x26A7,0x26A7,
	    0x26AA,0x26AB, 0x26B0,0x26B1, 0x26BD,0x26BE, 0x26C4,0x26C5, 0x26C8,0x26C8, 0x26CE,0x26CF,
	    0x26D1,0x26D1, 0x26D3,0x26D4, 0x26E9,0x26EA, 0x26F0,0x26F5, 0x26F7,0x26FA, 0x26FD,0x26FD,
	    0x2702,0x2702, 0x2705,0x2705, 0x2708,0x270D, 0x270F,0x270F, 0x2712,0x2712, 0x2714,0x2714,
	    0x2716,0x2716, 0x271D,0x271D, 0x2721,0x2721, 0x2728,0x2728, 0x2733,0x2734, 0x2744,0x2744,
	    0x2747,0x2747, 0x274C,0x274C, 0x274E,0x274E, 0x2753,0x2755, 0x2757,0x2757, 0x2763,0x2764,
	    0x2795,0x2797, 0x27A1,0x27A1, 0x27B0,0x27B0, 0x27BF,0x27BF, 0x2934,0x2935, 0x2B05,0x2B07,
	    0x2B1B,0x2B1C, 0x2B50,0x2B50, 0x2B55,0x2B55, 0x3030,0x3030, 0x303D,0x303D, 0x3297,0x3297,
	    0x3299,0x3299, 0x1F004,0x1F004, 0x1F0CF,0x1F0CF, 0x1F170,0x1F171, 0x1F17E,0x1F17F, 0x1F18E,0x1F18E,
	    0x1F191,0x1F19A, 0x1F1E6,0x1F1FF, 0x1F201,0x1F202, 0x1F21A,0x1F21A, 0x1F22F,0x1F22F, 0x1F232,0x1F23A,
	    0x1F250,0x1F251, 0x1F300,0x1F321, 0x1F324,0x1F393, 0x1F396,0x1F397, 0x1F399,0x1F39B, 0x1F39E,0x1F3F0,
	    0x1F3F3,0x1F3F5, 0x1F3F7,0x1F4FD, 0x1F4FF,0x1F53D, 0x1F549,0x1F54E, 0x1F550,0x1F567, 0x1F56F,0x1F570,
	    0x1F573,0x1F57A, 0x1F587,0x1F587, 0x1F58A,0x1F58D, 0x1F590,0x1F590, 0x1F595,0x1F596, 0x1F5A4,0x1F5A5,
	    0x1F5A8,0x1F5A8, 0x1F5B1,0x1F5B2, 0x1F5BC,0x1F5BC, 0x1F5C2,0x1F5C4, 0x1F5D1,0x1F5D3, 0x1F5DC,0x1F5DE,
	    0x1F5E1,0x1F5E1, 0x1F5E3,0x1F5E3, 0x1F5E8,0x1F5E8, 0x1F5EF,0x1F5EF, 0x1F5F3,0x1F5F3, 0x1F5FA,0x1F64F,
	    0x1F680,0x1F6C5, 0x1F6CB,0x1F6D2, 0x1F6D5,0x1F6D7, 0x1F6DD,0x1F6E5, 0x1F6E9,0x1F6E9, 0x1F6EB,0x1F6EC,
	    0x1F6F0,0x1F6F0, 0x1F6F3,0x1F6FC, 0x1F7E0,0x1F7EB, 0x1F7F0,0x1F7F0, 0x1F90C,0x1F93A, 0x1F93C,0x1F945,
	    0x1F947,0x1F9FF, 0x1FA70,0x1FA74, 0x1FA78,0x1FA7C, 0x1FA80,0x1FA86, 0x1FA90,0x1FAAC, 0x1FAB0,0x1FABA,
	    0x1FAC0,0x1FAC5, 0x1FAD0,0x1FAD9, 0x1FAE0,0x1FAE7, 0x1FAF0,0x1FAF6, 0x0000
	};
	static ImFontConfig cfg3;
	cfg3.OversampleH = cfg3.OversampleV = 1; cfg3.MergeMode = true; 
	cfg3.FontBuilderFlags |= ImGuiFreeTypeBuilderFlags_LoadColor; cfg3.GlyphOffset = ImVec2(0, 1 * g_dpi);
	io.Fonts->AddFontFromFileTTF("data/forkawesome-webfont.ttf", 16.0f * g_dpi, &cfg3, ranges3);
	ImGui_ImplOpenGL3_CreateDeviceObjects();
	EM_ASM({
		FS.syncfs(false, function(err) {
			if (err) {
				console.error("Error syncing FS:", err);
			}
			else {
				console.log("cache synced to persistent.");
			}
		});
	});
}


EM_JS(void, toClipboard, (const char* what), {
  var str = UTF8ToString(what);

  if (navigator.clipboard && window.isSecureContext) {
	  // modern, only works under HTTPS
	  navigator.clipboard.writeText(str).catch (function(e) {
		console.warn("Clipboard API failed:", e);
	  });
	}
   else {
	  // fallback for HTTP / insecure context
	  var ta = document.createElement("textarea");
	  ta.value = str;
	  // off-screen
	  ta.style.position = "fixed";
	  ta.style.left = "-9999px";
	  document.body.appendChild(ta);
	  ta.focus();
	  ta.select();
	  try {
		document.execCommand("copy");
	  }
   catch (e) {
  console.error("execCommand copy failed:", e);
}
document.body.removeChild(ta);
}
	});

static void mySetClipboardText(void* user_data, const char* text)
{
    toClipboard(text);
}

int init_imgui()
{
	IMGUI_CHECKVERSION();
	ImGui::CreateContext();
	ImPlot::CreateContext();
	auto& io = ImGui::GetIO();
	io.IniFilename = "/cache/imgui.ini";
	io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard | ImGuiConfigFlags_NavEnableGamepad | ImGuiConfigFlags_DockingEnable;
	ImGui_ImplGlfw_InitForOpenGL(g_window, true);
	ImGui_ImplOpenGL3_Init("#version 300 es");
	ImGui::StyleColorsDark();
	Stylize();
	resizeCanvas();
	io.SetClipboardTextFn = mySetClipboardText;
	return 0;
}


EM_JS(void, getAppInfo, (char* what, int bufferLen), {
	stringToUTF8(appName, what, bufferLen);
});

// EM_JS(void, initPersist, (), {
// 	syncAll();
// });

void maybeWriteDefaultImGuiIni()
{
	const char* ini = getDefaultImGUILayoutIni();
	if (!ini) return;
	FILE* f = fopen("/cache/imgui.ini", "wb");
	if (f) { fwrite(ini, 1, strlen(ini), f); fclose(f); }
}

int init()
{
	init_gl(); init_imgui(); getAppInfo(appName, 100);
	return 0;
}

// EM_JS(uint8_t*, registerImageStream, (const char* name, int length), {
// 	const str = UTF8ToString(name);
// 	console.log("open stream " + str);
//     var ptr = getModuleAsm().malloc(length);
//     var sb = new SharedArrayBuffer(length);
// 	stream(str, ptr, sb);
//     return ptr;
// });

EM_JS(uint8_t*, registerImageStream, (const char* name, int width, int height), {
	const str = UTF8ToString(name);
	console.log("open stream " + str);
	var ptr = getModuleAsm().malloc(width * height * 4);
	stream(str, ptr, width, height);
    return ptr;
});

EM_JS(void, copyBuffer, (const char* name), {
	js_copyBuffer(UTF8ToString(name));
});

std::map<std::string, uint8_t*> buffers;
uint8_t* GetStreamingBuffer(std::string name, int width, int height)
{
	if (buffers.find(name) == buffers.end())
	{
		buffers[name] = registerImageStream(name.c_str(), width, height);
	}
	// copyBuffer(name.c_str());
    return buffers[name];
}


void quit()
{
	glfwTerminate();
}

std::function<void(unsigned char*, int)> delegator;

void stateChanger(unsigned char* stateChange, int bytes)
{
	if (!testWS()) return;
	int type = 0;
	js_send_binary((uint8_t*)&type, 4);
	js_send_binary(stateChange, bytes);
};

void workspaceChanger(unsigned char* wsChange, int bytes)
{
	if (!testWS()) return;
	int type = 1;
	js_send_binary((uint8_t*)&type, 4);
	js_send_binary(wsChange, bytes);
};


EM_JS(bool, isWSSent, (), {
	return sent;
});

bool useRealtimeUI = false;
std::chrono::time_point<std::chrono::steady_clock> ticRealtimeUI;
void realtimeUI(unsigned char* wsChange, int bytes)
{
	if (!testWS() || !isWSSent()) return; //do not queue realtime ui.

	useRealtimeUI = true;
	ticRealtimeUI = std::chrono::high_resolution_clock::now();
	int type = 3;
	js_send_binary((uint8_t*)&type, 4);
	js_send_binary(wsChange, bytes);
};

std::vector<uint8_t> remoteWSBytes;
int touchState = 0;
float iTouchDist = -1;
float iX = 0, iY = 0;
std::string appStatStr;
extern "C" {
	EMSCRIPTEN_KEEPALIVE void onmessage(uint8_t* data, int length)
	{
		static int type = -1;
		if (type == -1) {
			auto htype = *(int*)data;
			if (htype == 2) {
				auto latency = 0.001 * std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - ticRealtimeUI).count();
				char stattmp[50]; sprintf(stattmp, "\uf1eb%.1fms", latency);
				appStatStr = stattmp; appStat = (char*)appStatStr.c_str();
			} else type = htype;
		} else if (type == 0) { GenerateStackFromPanelCommands(data, length); type = -1; }
		else if (type == 1) { remoteWSBytes.assign(data, data + length); type = -1; }
	}
	// seems imgui only process one event at a time.
	EMSCRIPTEN_KEEPALIVE void ontouch(int* touches, int length)
	{
		std::vector<touch_state> touchls;
		for (int i = 0; i < length; ++i)
			touchls.push_back(touch_state{ .id = touches[0 + i * 3],.touchX = (float)(touches[1 + i * 3]*g_dpi),.touchY = (float)(touches[2 + i * 3]*g_dpi) });
		touch_callback(touchls);
	}
}

void webBeforeDraw()
{
	if (remoteWSBytes.size()) {
		ProcessWorkspaceQueue(remoteWSBytes.data());
		remoteWSBytes.clear();
		int type = 2;
		js_send_binary((uint8_t*)&type, 4);
	}
}


// Global flag accessed by both JS and C++.
volatile int fsSyncedFlag = 0;

// This function is called from JavaScript when FS.syncfs is finished.
extern "C" EMSCRIPTEN_KEEPALIVE void onFSSynced() {
	fsSyncedFlag = 1;
}

#include "../libVRender/generated_version.h"
extern "C" int main(int argc, char** argv)
{
	emscripten_log(EM_LOG_INFO, "Start WEB-based CycleGUI, CompileTime@ %s %s:v%x", __DATE__, __TIME__, LIB_VERSION);
	// EM_ASM is a macro to call in-line JavaScript code.

	EM_ASM(
		FS.mkdir('/cache');
		FS.mount(IDBFS, {}, '/cache');
		FS.syncfs(true, err => { if (err) console.error("Error syncing from IDBFS:", err); _onFSSynced(); });
	);
	while (!fsSyncedFlag) emscripten_sleep(100);

	// Write default ini if provided, before imgui reads it
	maybeWriteDefaultImGuiIni();

	beforeDraw = webBeforeDraw;
	stateCallback = stateChanger;
	global_workspaceCallback = workspaceChanger;
	realtimeUICallback = realtimeUI;

	startWS();

	g_width = canvas_get_width();
	g_height = canvas_get_height();
	g_dpi = getDevicePixelRatio();


	if (init() != 0) return 1;
	
	uploadMsg("Compiling shaders...");
	InitGraphics();
    initialize_viewport(0, g_width, g_height);

	uploadMsg("Initialized...");
#ifdef __EMSCRIPTEN__
	emscripten_set_main_loop(loop, 0, 1);
#endif

	quit();

	return 0;
}

// JavaScript bridge for showing H5 window
EM_JS(void, js_show_h5_window, (const char* url), {
    window.showH5Window(UTF8ToString(url));
});

// Function to show web panel
void showWebPanel(const char* url) {
    js_show_h5_window(url);
}
