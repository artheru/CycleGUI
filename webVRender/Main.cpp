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
#include "backends/imgui_impl_glfw.h"
#include "backends/imgui_impl_opengl3.h"
#include <iostream>
#include <misc/freetype/imgui_freetype.h>

#include "IconsForkAwesome.h"

GLFWwindow* g_window;
ImVec4 clear_color = ImVec4(0.45f, 0.55f, 0.60f, 1.00f);
bool show_demo_window = true;
bool show_another_window = false;
int g_width;
int g_height;
double g_dpi;





EM_JS(void, logging, (const char* c_str), {
	const str = UTF8ToString(c_str);console.log(str);
});

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

EM_JS(void, processTxt, (char* what, int bufferLen), {
	let r = prompt("input value:", UTF8ToString(what));
	if (r!=null)
		stringToUTF8(r, what, bufferLen);
});

EM_JS(const char*, getHost, (), {
	//var terminalDataUrl = 'ws://' + window.location.host + '/terminal/data';
	var terminalDataUrl = 'ws://' + window.location.host + ':' + window.wsport + '/terminal/data';
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


int frame = 0;
void loop()
{
	int width = canvas_get_width();
	int height = canvas_get_height();
	double dpi = getDevicePixelRatio();

	if (dpi != g_dpi)
	{
		g_dpi = dpi;
		Stylize();
	}

	if (width != g_width || height != g_height)
	{
		g_width = width;
		g_height = height;
		on_size_changed();
	}

	glfwPollEvents();

	int display_w, display_h;
	glfwMakeContextCurrent(g_window);
	glfwGetFramebufferSize(g_window, &display_w, &display_h);
	glViewport(0, 0, display_w, display_h);
	glClear(GL_COLOR_BUFFER_BIT);

	ImGui_ImplOpenGL3_NewFrame();
	ImGui_ImplGlfw_NewFrame();
	ImGui::NewFrame();

	camera->dpi = dpi;
	
	ProcessUIStack();
	DrawWorkspace(display_w, display_h);
	
	// static bool show_demo_window = true;
	// if (show_demo_window)
	    ImGui::ShowDemoWindow(nullptr);

    ImGui::Text("🖐This is some useful text.以及汉字, I1l, 0Oo");
    ImGui::Text(ICON_FK_ADDRESS_BOOK" TEST FK");

	if (ImGui::GetIO().WantTextInput)
	{
		auto id = ImGui::GetActiveID();
		if (id != 0)
		{
			auto ptr = ImGui::GetInputTextState(id);
			processTxt(ptr->TextA.Data, ptr->TextA.Capacity);
			ptr->CurLenA = strlen(ptr->TextA.Data);
			ImGui::MarkItemEdited(id);
			ImGui::ClearActiveID();
			ImGui::GetIO().AddMouseButtonEvent(0, false);
			ImGuiContext& g = *GImGui;
			g.ExternEdit = true;
		}
		//AddInputCharactersUTF8
	}

	if (!testWS())
		goodbye();

	ImGui::Render();


	ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());
	glfwMakeContextCurrent(g_window);

	if (frame ==0)
	{
		notifyLoaded();
	}
	frame += 1;
}


int init_gl()
{
	if (!glfwInit())
	{
		fprintf(stderr, "Failed to initialize GLFW\n");
		return 1;
	}
	
	glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE); // We don't want the old OpenGL
	glfwWindowHint(GLFW_SAMPLES, 4);
    glfwSwapInterval(0); // Enable vsync

	// Open a window and create its OpenGL context
	int canvasWidth = g_width;
	int canvasHeight = g_height;
	g_window = glfwCreateWindow(canvasWidth, canvasHeight, "WebGui Demo", NULL, NULL);
	if (g_window == NULL)
	{
		fprintf(stderr, "Failed to open GLFW window.\n");
		glfwTerminate();
		return -1;
	}
	glfwMakeContextCurrent(g_window); // Initialize GLEW

	glfwSetMouseButtonCallback(g_window, mouse_button_callback);
	glfwSetCursorPosCallback(g_window, cursor_position_callback);
	glfwSetScrollCallback(g_window, scroll_callback);

	return 0;
}


EM_JS(const char*, drawCharProxy, (int codepoint), {
	let uint8Array = drawChar(codepoint);
	if (!uint8Array) return 0;
    var byteCount = uint8Array.length;
    var ptr = Module.asm.malloc(byteCount);
    Module.HEAPU8.set(uint8Array, ptr);
    return ptr;
});

EM_JS(void, uploadMsg, (const char* c_str), {
	const str = UTF8ToString(c_str);
	loaderMsg(str);
});

// Linear interpolation downsampling function
void downsample(uint8_t* originalRGBA, int originalSz, int outputSz, uint8_t* outputRGBA) {
    // Scaling ratio between the original and the output
    float scale = (float)originalSz / (float)outputSz;

    for (int y = 0; y < outputSz; y++) {
        for (int x = 0; x < outputSz; x++) {
            // Determine the position in the original image to sample from
            float srcX = x * scale;
            float srcY = y * scale;

            // Calculate the surrounding integer pixel coordinates
            int x0 = (int)floorf(srcX);
            int x1 = x0 + 1;
            int y0 = (int)floorf(srcY);
            int y1 = y0 + 1;

            // Ensure the coordinates are within bounds
            x1 = (x1 >= originalSz) ? originalSz - 1 : x1;
            y1 = (y1 >= originalSz) ? originalSz - 1 : y1;

            // Calculate the interpolation weights
            float dx = srcX - x0;
            float dy = srcY - y0;

            // Compute the pixel index for the output image
            int outputIdx = (y * outputSz + x) * 4;

            // Interpolate the RGBA channels
            for (int c = 0; c < 4; c++) {
                // Get the four neighboring pixels in the original image
                uint8_t p00 = originalRGBA[(y0 * originalSz + x0) * 4 + c];
                uint8_t p01 = originalRGBA[(y0 * originalSz + x1) * 4 + c];
                uint8_t p10 = originalRGBA[(y1 * originalSz + x0) * 4 + c];
                uint8_t p11 = originalRGBA[(y1 * originalSz + x1) * 4 + c];

                // Perform bilinear interpolation
                float interpolatedValue = 
                    (1 - dx) * (1 - dy) * p00 + 
                    dx * (1 - dy) * p01 + 
                    (1 - dx) * dy * p10 + 
                    dx * dy * p11;

                // Set the interpolated value to the output image
                outputRGBA[outputIdx + c] = (uint8_t)(interpolatedValue + 0.5f);
            }
        }
    }
}


EM_JS(int, getIcoSz, (), {
	return favicosz;
});

EM_JS(const char*, getIco, (), {
    var byteCount = favui8arr.length;
    var ptr = Module.asm.malloc(byteCount);
    Module.HEAPU8.set(favui8arr, ptr);
    return ptr;
});

EM_JS(int, getLoadedGlyphsN, (), {
	return loadedGlyphs;
});

extern "C" { //used for imgui_freetype.cpp patch.
	int addedChars = 0;

	void encodeUTF8(char32_t codepoint, char* dest, size_t destSize) {
	    if (codepoint <= 0x7F) {
	        snprintf(dest, destSize, "%c", static_cast<char>(codepoint));
	    } else if (codepoint <= 0x7FF) {
	        snprintf(dest, destSize, "%c%c",
	                 static_cast<char>(0xC0 | (codepoint >> 6)),
	                 static_cast<char>(0x80 | (codepoint & 0x3F)));
	    } else if (codepoint <= 0xFFFF) {
	        snprintf(dest, destSize, "%c%c%c",
	                 static_cast<char>(0xE0 | (codepoint >> 12)),
	                 static_cast<char>(0x80 | ((codepoint >> 6) & 0x3F)),
	                 static_cast<char>(0x80 | (codepoint & 0x3F)));
	    } else if (codepoint <= 0x10FFFF) {
	        snprintf(dest, destSize, "%c%c%c%c",
	                 static_cast<char>(0xF0 | (codepoint >> 18)),
	                 static_cast<char>(0x80 | ((codepoint >> 12) & 0x3F)),
	                 static_cast<char>(0x80 | ((codepoint >> 6) & 0x3F)),
	                 static_cast<char>(0x80 | (codepoint & 0x3F)));
	    }
	}

	uint8_t* fallback_text_render(uint32_t codepoint)
	{
		while (getLoadedGlyphsN() == -1){
			uploadMsg("Prefetching rendered glyphs...");
			emscripten_sleep(100);
		}

        if (codepoint == 0x2b00)
        {
			auto appIcoSz = -1;
			while ((appIcoSz=getIcoSz())==0){
				uploadMsg("Downloading Icon resources");
				emscripten_sleep(100);
			}
			if (appIcoSz == -1) {
				printf("Proceed without app icon.\n");
				return nullptr;
			}
        	uint8_t *appIco = (uint8_t*)getIco();
			
            ui_state.app_icon.height = ui_state.app_icon.width = 18.0f * g_dpi;
		    ui_state.app_icon.advanceX = ui_state.app_icon.width + 2;
            ui_state.app_icon.offsetY = -ui_state.app_icon.width *0.85;

			// already downsampled to 48.
            downsample(appIco, 48, ui_state.app_icon.height, ui_state.app_icon.rgba);
            return (uint8_t*) &ui_state.app_icon;
        }

		addedChars += 1;
		if (addedChars % 500 == 0){
			emscripten_sleep(0);
			char tmp[40] = "Loading glyph:";
			
		    char utf8Char[5] = {0}; // UTF-8 characters can be up to 4 bytes + null terminator
		    encodeUTF8(codepoint, utf8Char, sizeof(utf8Char));

		    // Fill the buffer with "Loading glyph:[/*codepoint character*/]"
		    std::snprintf(tmp, sizeof(tmp), "Building glyph: %s", utf8Char);

			uploadMsg(tmp);
		}
		
		// emscripten: if we have cache, simply use cache.
		
	    // if (loadFromCache(codepoint)) {
	    //     return glyphCache; // Return the cached data
	    // }
		auto ptr = (uint8_t*)drawCharProxy(codepoint);
		// saveToCache(codepoint, ptr);
		return ptr;
	}
}

void Stylize()
{
	ImGuiIO& io = ImGui::GetIO();


	// // Setup Dear ImGui style
	ImGuiStyle& style = ImGui::GetStyle();

	style = ImGuiStyle(); // IMPORTANT: ScaleAllSizes will change the original size, so we should reset all style config
	style.FrameBorderSize = 1;
	style.FrameRounding = 6.0f;
	style.WindowPadding = ImVec2(8, 8);
	style.FramePadding = ImVec2(8, 3);
	style.CellPadding = ImVec2(5, 2);
	style.ItemSpacing = ImVec2(12, 5);
	style.ItemInnerSpacing = ImVec2(8, 6);
	style.IndentSpacing = 25.0f;
	style.ScrollbarSize = 15.0f;
	style.GrabMinSize = 19.0f;
	style.SeparatorTextPadding = ImVec2(25, 3);
	style.ScrollbarRounding = 9.0f;
	style.GrabRounding = 6.0f;

	style.ScaleAllSizes(g_dpi);

	ImVec4* colors = ImGui::GetStyle().Colors;
    colors[ImGuiCol_Text] = ImVec4(1.00f, 1.00f, 1.00f, 1.00f);
    colors[ImGuiCol_TextDisabled] = ImVec4(0.50f, 0.50f, 0.50f, 1.00f);
    colors[ImGuiCol_WindowBg] = ImVec4(0.09f, 0.09f, 0.09f, 1.00f);
    colors[ImGuiCol_ChildBg] = ImVec4(0.00f, 0.00f, 0.00f, 0.67f);
    colors[ImGuiCol_PopupBg] = ImVec4(0.07f, 0.05f, 0.07f, 1.00f);
    colors[ImGuiCol_Border] = ImVec4(0.33f, 0.33f, 0.33f, 0.50f);
    colors[ImGuiCol_BorderShadow] = ImVec4(0.00f, 0.00f, 0.00f, 0.00f);
    colors[ImGuiCol_FrameBg] = ImVec4(0.00f, 0.00f, 0.00f, 1.00f);
    colors[ImGuiCol_FrameBgHovered] = ImVec4(0.04f, 0.15f, 0.10f, 1.00f);
    colors[ImGuiCol_FrameBgActive] = ImVec4(0.05f, 0.33f, 0.34f, 1.00f);
    colors[ImGuiCol_TitleBg] = ImVec4(0.04f, 0.04f, 0.04f, 1.00f);
    colors[ImGuiCol_TitleBgActive] = ImVec4(0.55f, 0.12f, 0.46f, 1.00f);
    colors[ImGuiCol_TitleBgCollapsed] = ImVec4(0.00f, 0.00f, 0.00f, 0.47f);
    colors[ImGuiCol_MenuBarBg] = ImVec4(0.12f, 0.11f, 0.31f, 1.00f);
    colors[ImGuiCol_ScrollbarBg] = ImVec4(0.02f, 0.02f, 0.02f, 0.62f);
    colors[ImGuiCol_ScrollbarGrab] = ImVec4(0.31f, 0.31f, 0.31f, 1.00f);
    colors[ImGuiCol_ScrollbarGrabHovered] = ImVec4(0.41f, 0.41f, 0.41f, 1.00f);
    colors[ImGuiCol_ScrollbarGrabActive] = ImVec4(0.51f, 0.51f, 0.51f, 1.00f);
    colors[ImGuiCol_CheckMark] = ImVec4(0.27f, 0.98f, 0.26f, 1.00f);
    colors[ImGuiCol_SliderGrab] = ImVec4(0.41f, 0.31f, 0.31f, 1.00f);
    colors[ImGuiCol_SliderGrabActive] = ImVec4(0.64f, 0.18f, 0.18f, 1.00f);
    colors[ImGuiCol_Button] = ImVec4(0.24f, 0.22f, 0.21f, 1.00f);
    colors[ImGuiCol_ButtonHovered] = ImVec4(0.56f, 0.24f, 0.60f, 1.00f);
    colors[ImGuiCol_ButtonActive] = ImVec4(0.38f, 0.06f, 0.98f, 1.00f);
    colors[ImGuiCol_Header] = ImVec4(0.26f, 0.04f, 0.35f, 1.00f);
    colors[ImGuiCol_HeaderHovered] = ImVec4(0.27f, 0.11f, 0.63f, 1.00f);
    colors[ImGuiCol_HeaderActive] = ImVec4(0.44f, 0.26f, 0.98f, 1.00f);
    colors[ImGuiCol_Separator] = ImVec4(0.43f, 0.43f, 0.50f, 0.50f);
    colors[ImGuiCol_SeparatorHovered] = ImVec4(0.63f, 0.10f, 0.75f, 0.78f);
    colors[ImGuiCol_SeparatorActive] = ImVec4(0.54f, 0.10f, 0.75f, 1.00f);
    colors[ImGuiCol_ResizeGrip] = ImVec4(0.48f, 0.10f, 0.10f, 1.00f);
    colors[ImGuiCol_ResizeGripHovered] = ImVec4(0.98f, 0.26f, 0.26f, 1.00f);
    colors[ImGuiCol_ResizeGripActive] = ImVec4(1.00f, 0.00f, 0.00f, 1.00f);
    colors[ImGuiCol_Tab] = ImVec4(0.35f, 0.12f, 0.12f, 1.00f);
    colors[ImGuiCol_TabHovered] = ImVec4(0.95f, 0.22f, 1.00f, 1.00f);
    colors[ImGuiCol_TabActive] = ImVec4(0.51f, 0.20f, 0.68f, 1.00f);
    colors[ImGuiCol_TabUnfocused] = ImVec4(0.07f, 0.10f, 0.15f, 1.00f);
    colors[ImGuiCol_TabUnfocusedActive] = ImVec4(0.14f, 0.26f, 0.42f, 1.00f);
    colors[ImGuiCol_DockingPreview] = ImVec4(0.26f, 0.59f, 0.98f, 0.70f);
    colors[ImGuiCol_DockingEmptyBg] = ImVec4(0.20f, 0.20f, 0.20f, 1.00f);
    colors[ImGuiCol_PlotLines] = ImVec4(0.61f, 0.61f, 0.61f, 1.00f);
    colors[ImGuiCol_PlotLinesHovered] = ImVec4(1.00f, 0.43f, 0.35f, 1.00f);
    colors[ImGuiCol_PlotHistogram] = ImVec4(0.90f, 0.70f, 0.00f, 1.00f);
    colors[ImGuiCol_PlotHistogramHovered] = ImVec4(1.00f, 0.60f, 0.00f, 1.00f);
    colors[ImGuiCol_TableHeaderBg] = ImVec4(0.19f, 0.19f, 0.20f, 1.00f);
    colors[ImGuiCol_TableBorderStrong] = ImVec4(0.31f, 0.31f, 0.35f, 1.00f);
    colors[ImGuiCol_TableBorderLight] = ImVec4(0.23f, 0.23f, 0.25f, 1.00f);
    colors[ImGuiCol_TableRowBg] = ImVec4(0.00f, 0.00f, 0.00f, 0.00f);
    colors[ImGuiCol_TableRowBgAlt] = ImVec4(1.00f, 1.00f, 1.00f, 0.06f);
    colors[ImGuiCol_TextSelectedBg] = ImVec4(0.26f, 0.59f, 0.98f, 0.35f);
    colors[ImGuiCol_DragDropTarget] = ImVec4(1.00f, 1.00f, 0.00f, 0.90f);
    colors[ImGuiCol_NavHighlight] = ImVec4(0.55f, 0.26f, 0.98f, 1.00f);
    colors[ImGuiCol_NavWindowingHighlight] = ImVec4(1.00f, 1.00f, 1.00f, 0.70f);
    colors[ImGuiCol_NavWindowingDimBg] = ImVec4(0.80f, 0.80f, 0.80f, 0.20f);
    colors[ImGuiCol_ModalWindowDimBg] = ImVec4(0.80f, 0.80f, 0.80f, 0.35f);

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
	// static ImWchar ranges3[] = {ICON_MIN_FK, ICON_MAX_FK, 0};
	static ImFontConfig cfg2;
	cfg2.OversampleH = cfg2.OversampleV = 1;
	cfg2.MergeMode = true;
	cfg2.GlyphOffset = ImVec2(0, 1 * g_dpi);
	io.Fonts->AddFontFromFileTTF("data/forkawesome-webfont.ttf", 16.0f * g_dpi, &cfg2, ranges2);

	// emojis:
	static ImWchar ranges3[]= {
		//app icon
		0x2b00,0x2b00,
		//emojis:
		0x23, 0x23,
	    0x2A, 0x2A,
	    0x30, 0x39,
	    0xA9, 0xA9,
	    0xAE, 0xAE,
	    0x203C, 0x203C,
	    0x2049, 0x2049,
	    0x2122, 0x2122,
	    0x2139, 0x2139,
	    0x2194, 0x2199,
	    0x21A9, 0x21AA,
	    0x231A, 0x231B,
	    0x2328, 0x2328,
	    0x23CF, 0x23CF,
	    0x23E9, 0x23F3,
	    0x23F8, 0x23FA,
	    0x24C2, 0x24C2,
	    0x25AA, 0x25AB,
	    0x25B6, 0x25B6,
	    0x25C0, 0x25C0,
	    0x25FB, 0x25FE,
	    0x2600, 0x2604,
	    0x260E, 0x260E,
	    0x2611, 0x2611,
	    0x2614, 0x2615,
	    0x2618, 0x2618,
	    0x261D, 0x261D,
	    0x2620, 0x2620,
	    0x2622, 0x2623,
	    0x2626, 0x2626,
	    0x262A, 0x262A,
	    0x262E, 0x262F,
	    0x2638, 0x263A,
	    0x2640, 0x2640,
	    0x2642, 0x2642,
	    0x2648, 0x2653,
	    0x265F, 0x2660,
	    0x2663, 0x2663,
	    0x2665, 0x2666,
	    0x2668, 0x2668,
	    0x267B, 0x267B,
	    0x267E, 0x267F,
	    0x2692, 0x2697,
	    0x2699, 0x2699,
	    0x269B, 0x269C,
	    0x26A0, 0x26A1,
	    0x26A7, 0x26A7,
	    0x26AA, 0x26AB,
	    0x26B0, 0x26B1,
	    0x26BD, 0x26BE,
	    0x26C4, 0x26C5,
	    0x26C8, 0x26C8,
	    0x26CE, 0x26CF,
	    0x26D1, 0x26D1,
	    0x26D3, 0x26D4,
	    0x26E9, 0x26EA,
	    0x26F0, 0x26F5,
	    0x26F7, 0x26FA,
	    0x26FD, 0x26FD,
	    0x2702, 0x2702,
	    0x2705, 0x2705,
	    0x2708, 0x270D,
	    0x270F, 0x270F,
	    0x2712, 0x2712,
	    0x2714, 0x2714,
	    0x2716, 0x2716,
	    0x271D, 0x271D,
	    0x2721, 0x2721,
	    0x2728, 0x2728,
	    0x2733, 0x2734,
	    0x2744, 0x2744,
	    0x2747, 0x2747,
	    0x274C, 0x274C,
	    0x274E, 0x274E,
	    0x2753, 0x2755,
	    0x2757, 0x2757,
	    0x2763, 0x2764,
	    0x2795, 0x2797,
	    0x27A1, 0x27A1,
	    0x27B0, 0x27B0,
	    0x27BF, 0x27BF,
	    0x2934, 0x2935,
	    0x2B05, 0x2B07,
	    0x2B1B, 0x2B1C,
	    0x2B50, 0x2B50,
	    0x2B55, 0x2B55,
	    0x3030, 0x3030,
	    0x303D, 0x303D,
	    0x3297, 0x3297,
	    0x3299, 0x3299,
	    0x1F004, 0x1F004,
	    0x1F0CF, 0x1F0CF,
	    0x1F170, 0x1F171,
	    0x1F17E, 0x1F17F,
	    0x1F18E, 0x1F18E,
	    0x1F191, 0x1F19A,
	    0x1F1E6, 0x1F1FF,
	    0x1F201, 0x1F202,
	    0x1F21A, 0x1F21A,
	    0x1F22F, 0x1F22F,
	    0x1F232, 0x1F23A,
	    0x1F250, 0x1F251,
	    0x1F300, 0x1F321,
	    0x1F324, 0x1F393,
	    0x1F396, 0x1F397,
	    0x1F399, 0x1F39B,
	    0x1F39E, 0x1F3F0,
	    0x1F3F3, 0x1F3F5,
	    0x1F3F7, 0x1F4FD,
	    0x1F4FF, 0x1F53D,
	    0x1F549, 0x1F54E,
	    0x1F550, 0x1F567,
	    0x1F56F, 0x1F570,
	    0x1F573, 0x1F57A,
	    0x1F587, 0x1F587,
	    0x1F58A, 0x1F58D,
	    0x1F590, 0x1F590,
	    0x1F595, 0x1F596,
	    0x1F5A4, 0x1F5A5,
	    0x1F5A8, 0x1F5A8,
	    0x1F5B1, 0x1F5B2,
	    0x1F5BC, 0x1F5BC,
	    0x1F5C2, 0x1F5C4,
	    0x1F5D1, 0x1F5D3,
	    0x1F5DC, 0x1F5DE,
	    0x1F5E1, 0x1F5E1,
	    0x1F5E3, 0x1F5E3,
	    0x1F5E8, 0x1F5E8,
	    0x1F5EF, 0x1F5EF,
	    0x1F5F3, 0x1F5F3,
	    0x1F5FA, 0x1F64F,
	    0x1F680, 0x1F6C5,
	    0x1F6CB, 0x1F6D2,
	    0x1F6D5, 0x1F6D7,
	    0x1F6DD, 0x1F6E5,
	    0x1F6E9, 0x1F6E9,
	    0x1F6EB, 0x1F6EC,
	    0x1F6F0, 0x1F6F0,
	    0x1F6F3, 0x1F6FC,
	    0x1F7E0, 0x1F7EB,
	    0x1F7F0, 0x1F7F0,
	    0x1F90C, 0x1F93A,
	    0x1F93C, 0x1F945,
	    0x1F947, 0x1F9FF,
	    0x1FA70, 0x1FA74,
	    0x1FA78, 0x1FA7C,
	    0x1FA80, 0x1FA86,
	    0x1FA90, 0x1FAAC,
	    0x1FAB0, 0x1FABA,
	    0x1FAC0, 0x1FAC5,
	    0x1FAD0, 0x1FAD9,
	    0x1FAE0, 0x1FAE7,
	    0x1FAF0, 0x1FAF6,
	    0x0000 // End of array
	};
	static ImFontConfig cfg3;
	cfg3.OversampleH = cfg3.OversampleV = 1;
	cfg3.MergeMode = true;
    cfg3.FontBuilderFlags |= ImGuiFreeTypeBuilderFlags_LoadColor;
	cfg3.GlyphOffset = ImVec2(0, 1 * g_dpi);
	io.Fonts->AddFontFromFileTTF("data/forkawesome-webfont.ttf", 16.0f * g_dpi, &cfg3, ranges3);
	ImGui_ImplOpenGL3_CreateDeviceObjects();
}


EM_JS(void, toClipboard, (const char* what), {
	var str = UTF8ToString(what);
	navigator.clipboard.writeText(str);
	});

static void mySetClipboardText(void* user_data, const char* text)
{
    toClipboard(text);
}

int init_imgui()
{
	// Setup Dear ImGui binding
	IMGUI_CHECKVERSION();
	ImGui::CreateContext();
	auto& io = ImGui::GetIO();
	io.IniFilename = "/offline/imgui.ini";
	io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;     // Enable Keyboard Controls
	io.ConfigFlags |= ImGuiConfigFlags_NavEnableGamepad;      // Enable Gamepad Controls
	io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;         // Enable Docking

	ImGui_ImplGlfw_InitForOpenGL(g_window, true);
	ImGui_ImplOpenGL3_Init();

	// Setup style
	ImGui::StyleColorsDark();

	Stylize();
	resizeCanvas();

	io.SetClipboardTextFn = mySetClipboardText;

	return 0;
}


EM_JS(void, getAppInfo, (char* what, int bufferLen), {
	stringToUTF8(appName, what, bufferLen);
});

EM_JS(void, initPersist, (), {
	syncAll();
});

int init()
{
	init_gl();
	init_imgui();
	getAppInfo(appName, 100);
	initPersist();
	return 0;
}

EM_JS(uint8_t*, registerImageStream, (const char* name, int length), {
	const str = UTF8ToString(name);
	console.log("open stream " + str);
    var ptr = Module.asm.malloc(length);
	stream(str, ptr);
    return ptr;
});

std::map<std::string, uint8_t*> buffers;
uint8_t* GetStreamingBuffer(std::string name, int length)
{
	if (buffers.find(name) == buffers.end())
	{
		buffers[name] = registerImageStream(name.c_str(), length);
	}
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

std::vector<uint8_t> remoteWSBytes;
int touchState = 0;
float iTouchDist = -1;
float iX = 0, iY = 0;
extern "C" {
	EMSCRIPTEN_KEEPALIVE void onmessage(uint8_t* data, int length)
	{
		static int type = -1;

		if (type == -1) 
		{
			type = *(int*)data; // next frame is actual data.
		}
		else if (type == 0) 
		{
			GenerateStackFromPanelCommands(data, length); // this function should be in main.cpp....
			//logging("UI data");
			type = -1;
		}
		else if (type == 1)
		{
		    //printf("[%f], WS data sz=%d\n",getJsTime(), length);

			remoteWSBytes.assign(data, data + length);
			
			type = -1;
		}else if (type == 3)
		{
			//test
			printf("[%f], test WS data sz=%d, (%d)\n", getJsTime(), length, data[4]);
		}
	}
	// seems imgui only process one event at a time.
	EMSCRIPTEN_KEEPALIVE void ontouch(int* touches, int length)
	{
		// for (int i = 0; i < length; ++i)
		// 	printf("(%d, %d) ", touches[i * 2], touches[i * 2 + 1]);
		// printf("...\n");
		
		for (int i = 0; i < length; ++i)
		{
			touches[i * 2] = touches[i * 2] * g_dpi;
			touches[i * 2 + 1] = touches[i * 2 + 1] * g_dpi;
		}

	    ImGuiIO& io = ImGui::GetIO();
		auto istate = touchState;
		if (touchState == 0 && length==1)
		{
			// prepare
			io.AddMousePosEvent((float)touches[0], (float)touches[1]);
			cursor_position_callback(nullptr, touches[0], touches[1]);
			touchState = 1;
		}else if (touchState ==1 && length==1){
			// fire for move.
			io.AddMouseButtonEvent(0, true);
			mouse_button_callback(nullptr, 0, GLFW_PRESS, 0);
			touchState = 2;
		}else if (touchState ==1 && length ==0){
			// fire then release.
			io.AddMouseButtonEvent(0, true);
			mouse_button_callback(nullptr, 0, GLFW_PRESS, 0);
			io.AddMouseButtonEvent(0, false);
			mouse_button_callback(nullptr, 0, GLFW_RELEASE, 0);
			touchState = 0;
		}else if (touchState==2 && length ==1) {
			// only move now.
			io.AddMousePosEvent((float)touches[0], (float)touches[1]);
			cursor_position_callback(nullptr, touches[0], touches[1]);
		}else if (touchState ==2 && length==0)
		{
			// released.
			mouse_button_callback(nullptr, 0, GLFW_RELEASE, 0);
			io.AddMouseButtonEvent(0, false);
			touchState = 0;
		}else if ((touchState<=2 || touchState ==9) && length==2)
		{
			// it's right mouse.
			io.AddMouseButtonEvent(1, true);
			io.AddMouseButtonEvent(0, false);
			mouse_button_callback(nullptr, 1, GLFW_PRESS, 0);
			mouse_button_callback(nullptr, 0, GLFW_RELEASE, 0);
			iTouchDist = sqrt((touches[0] - touches[2]) * (touches[0] - touches[2]) + (touches[1] - touches[3]) * (touches[1] - touches[3]));
			iX = (touches[0] + touches[2]) / 2;
			iY = (touches[1] + touches[3]) / 2;
			touchState = 3;
		}else if ((touchState ==3 || touchState==7 || touchState==8) && length==2)
		{
			cursor_position_callback(nullptr, touches[0], touches[1]);
			auto wd = sqrt((touches[0] - touches[2]) * (touches[0] - touches[2]) + (touches[1] - touches[3]) * (touches[1] - touches[3]));
			auto offset = (wd-iTouchDist) / g_dpi*0.07;
			iTouchDist = wd;
			scroll_callback(nullptr, 0, offset);

			// right drag is not available.
			auto jX = (touches[0] + touches[2]) / 2;
			auto jY = (touches[1] + touches[3]) / 2;
			if (touchState == 3){
				if (abs(jX - iX) > 10 || abs(jY - iY) > 10){
					io.AddMouseButtonEvent(1, false); //release right.
					touchState = 7;
				}
			}else if (touchState==7)
			{
				io.AddMouseWheelEvent((float)(jX-iX)*0.04f, (float)(jY-iY)*0.04f);
				iX = jX;
				iY = jY;
			}

		}else if ((touchState ==3 || touchState==7 || touchState==8) && length<2)
		{
			// must end..
			io.AddMouseButtonEvent(1, false);
			mouse_button_callback(nullptr, 1, GLFW_RELEASE, 0);
			touchState = 4;
		}else if (touchState==4 && length==0)
		{
			touchState = 0;
		}else if (touchState<=3 && length==3)
		{
			// it's middle mouse.
			io.AddMouseButtonEvent(0, false);
			mouse_button_callback(nullptr, 0, GLFW_RELEASE, 0);
			io.AddMouseButtonEvent(1, false);
			mouse_button_callback(nullptr, 1, GLFW_RELEASE, 0);
			io.AddMouseButtonEvent(2, true);
			mouse_button_callback(nullptr, 2, GLFW_PRESS, 0);
			touchState = 5;
		}else if (touchState==5 && length==3)
		{
			io.AddMousePosEvent((float)touches[0], (float)touches[1]);
			cursor_position_callback(nullptr, touches[0], touches[1]);
		}else if (touchState ==5 && length<3)
		{
			// must end..
			io.AddMouseButtonEvent(2, false);
			mouse_button_callback(nullptr, 2, GLFW_RELEASE, 0);
			touchState = 6;
		}else if (touchState ==6 && length==0)
		{
			touchState = 0;
		}
	        
		// printf("state=%d->%d\n", istate, touchState);
	}
}

void webBeforeDraw()
{
	// setstackui already done on GenerateStackFromPanelCommands
	if (remoteWSBytes.size()!=0){
		ProcessWorkspaceQueue(remoteWSBytes.data()); // process workspace...
	    // printf("[%f] ws processed\n",getJsTime());
		remoteWSBytes.clear();
		// apiNotice.

		if (!testWS()) return;
		int type = 2;
		js_send_binary((uint8_t*)&type, 4);
		//printf("allow next\n");
	}
}


extern "C" int main(int argc, char** argv)
{
	emscripten_log(EM_LOG_INFO, "Start WEB-based CycleGUI, CompileTime=%s %s", __DATE__, __TIME__);
	// EM_ASM is a macro to call in-line JavaScript code.
	EM_ASM(
		// Make a directory other than '/'
		FS.mkdir('/offline');
		// Then mount with IDBFS type
		FS.mount(IDBFS, {}, '/offline');

		// Then sync
		FS.syncfs(true, function(err) {
            if (err) {
                console.error("Error syncing from IDBFS:", err);
            }
		});
	);


	beforeDraw = webBeforeDraw;
	stateCallback = stateChanger;
	workspaceCallback = workspaceChanger;

	startWS();

	g_width = canvas_get_width();
	g_height = canvas_get_height();
	g_dpi = getDevicePixelRatio();


	if (init() != 0) return 1;
	
	uploadMsg("Compiling shaders...");
	InitGL(g_width, g_height);

#ifdef __EMSCRIPTEN__
	emscripten_set_main_loop(loop, 0, 1);
#endif

	quit();

	return 0;
}
