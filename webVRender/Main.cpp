// todo: Add textarea support, add font dynamic update from browser support to free the need of "georgia.ttf" font.

#include <stdio.h>

#include <emscripten.h>

#define _SLOG_EMSCRIPTEN
#include <emscripten/websocket.h>
EMSCRIPTEN_WEBSOCKET_T ws;

#define GLFW_INCLUDE_ES3
#include <GLES3/gl3.h>
#include <GLFW/glfw3.h>

#include "imgui.h"
#include "backends/imgui_impl_glfw.h"
#include "backends/imgui_impl_opengl3.h"
#include <iostream>

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

EMSCRIPTEN_KEEPALIVE

EM_JS(double, getDevicePixelRatio, (), { return window.devicePixelRatio || 1 });

// double getDevicePixelRatio()
// {
// 	return EM_ASM_DOUBLE({
// 		return window.devicePixelRatio || 1;
// 		});
// }

// Function called by javascript
EM_JS(void, resizeCanvas, (), {
      js_resizeCanvas();
      });

EM_JS(void, reload, (), {
	location.reload();
	});

EM_JS(const char*, getHost, (), {
	//var terminalDataUrl = 'ws://' + window.location.host + '/terminal/data';
	var terminalDataUrl = 'ws://' + window.location.hostname + ':' + window.wsport + '/terminal/data';
	var length = lengthBytesUTF8(terminalDataUrl) + 1;
	var buffer = _malloc(length);
	stringToUTF8(terminalDataUrl, buffer, length + 1);

	return buffer;
	});


bool testWS()
{
	unsigned short state;
	emscripten_websocket_get_ready_state(ws, &state);
	if (state == 1) {
		return true;
	}
	else if (state > 1) {
		return false;
	}
}

void goodbye()
{
	if (ImGui::BeginPopupModal("Connection lost", NULL, ImGuiWindowFlags_AlwaysAutoResize))
	{
		ImGui::Text("Connection to %s failed. Suggest to reload this page!", getHost());
		ImGui::Separator();
		if (ImGui::Button("Reload"))
		{
			reload();
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
	//     ImGui::ShowDemoWindow(&show_demo_window);

	if (!testWS())
		goodbye();

	ImGui::Render();


	ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());
	glfwMakeContextCurrent(g_window);
}


int init_gl()
{
	if (!glfwInit())
	{
		fprintf(stderr, "Failed to initialize GLFW\n");
		return 1;
	}
	
	glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE); // We don't want the old OpenGL
	glfwWindowHint(GLFW_SAMPLES, 8);

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
	colors[ImGuiCol_ChildBg] = ImVec4(0.00f, 0.00f, 0.00f, 0.00f);
	colors[ImGuiCol_PopupBg] = ImVec4(0.07f, 0.05f, 0.07f, 1.00f);
	colors[ImGuiCol_Border] = ImVec4(0.33f, 0.33f, 0.33f, 0.50f);
	colors[ImGuiCol_BorderShadow] = ImVec4(0.00f, 0.00f, 0.00f, 0.00f);
	colors[ImGuiCol_FrameBg] = ImVec4(0.00f, 0.00f, 0.00f, 1.00f);
	colors[ImGuiCol_FrameBgHovered] = ImVec4(0.04f, 0.15f, 0.10f, 0.40f);
	colors[ImGuiCol_FrameBgActive] = ImVec4(0.05f, 0.33f, 0.34f, 0.67f);
	colors[ImGuiCol_TitleBg] = ImVec4(0.04f, 0.04f, 0.04f, 1.00f);
	colors[ImGuiCol_TitleBgActive] = ImVec4(0.55f, 0.12f, 0.46f, 1.00f);
	colors[ImGuiCol_TitleBgCollapsed] = ImVec4(0.00f, 0.00f, 0.00f, 0.51f);
	colors[ImGuiCol_MenuBarBg] = ImVec4(0.12f, 0.11f, 0.31f, 1.00f);
	colors[ImGuiCol_ScrollbarBg] = ImVec4(0.02f, 0.02f, 0.02f, 0.53f);
	colors[ImGuiCol_ScrollbarGrab] = ImVec4(0.31f, 0.31f, 0.31f, 1.00f);
	colors[ImGuiCol_ScrollbarGrabHovered] = ImVec4(0.41f, 0.41f, 0.41f, 1.00f);
	colors[ImGuiCol_ScrollbarGrabActive] = ImVec4(0.51f, 0.51f, 0.51f, 1.00f);
	colors[ImGuiCol_CheckMark] = ImVec4(0.27f, 0.98f, 0.26f, 1.00f);
	colors[ImGuiCol_SliderGrab] = ImVec4(0.41f, 0.31f, 0.31f, 1.00f);
	colors[ImGuiCol_SliderGrabActive] = ImVec4(0.64f, 0.18f, 0.18f, 1.00f);
	colors[ImGuiCol_Button] = ImVec4(0.40f, 0.40f, 0.40f, 0.40f);
	colors[ImGuiCol_ButtonHovered] = ImVec4(0.56f, 0.24f, 0.60f, 1.00f);
	colors[ImGuiCol_ButtonActive] = ImVec4(0.38f, 0.06f, 0.98f, 1.00f);
	colors[ImGuiCol_Header] = ImVec4(0.45f, 0.00f, 0.64f, 0.31f);
	colors[ImGuiCol_HeaderHovered] = ImVec4(0.27f, 0.00f, 0.85f, 0.80f);
	colors[ImGuiCol_HeaderActive] = ImVec4(0.44f, 0.26f, 0.98f, 1.00f);
	colors[ImGuiCol_Separator] = ImVec4(0.43f, 0.43f, 0.50f, 0.50f);
	colors[ImGuiCol_SeparatorHovered] = ImVec4(0.63f, 0.10f, 0.75f, 0.78f);
	colors[ImGuiCol_SeparatorActive] = ImVec4(0.54f, 0.10f, 0.75f, 1.00f);
	colors[ImGuiCol_ResizeGrip] = ImVec4(0.98f, 0.26f, 0.26f, 0.20f);
	colors[ImGuiCol_ResizeGripHovered] = ImVec4(0.98f, 0.26f, 0.26f, 0.67f);
	colors[ImGuiCol_ResizeGripActive] = ImVec4(0.98f, 0.26f, 0.26f, 0.95f);
	colors[ImGuiCol_Tab] = ImVec4(0.35f, 0.12f, 0.12f, 0.86f);
	colors[ImGuiCol_TabHovered] = ImVec4(0.95f, 0.22f, 1.00f, 0.80f);
	colors[ImGuiCol_TabActive] = ImVec4(0.51f, 0.20f, 0.68f, 1.00f);
	colors[ImGuiCol_TabUnfocused] = ImVec4(0.07f, 0.10f, 0.15f, 0.97f);
	colors[ImGuiCol_TabUnfocusedActive] = ImVec4(0.14f, 0.26f, 0.42f, 1.00f);
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
	io.Fonts->AddFontFromFileTTF("data/georgia.ttf", 16.0f * g_dpi);
	// static ImFontConfig cfg;
	// cfg.MergeMode = true;
	// ImFont* font = io.Fonts->AddFontFromFileTTF("data/ZCOOLQingKeHuangYou-Regular.ttf", 16.0f * g_dpi, &cfg,
	//                                             io.Fonts->GetGlyphRangesChineseFull());

	static ImWchar ranges3[] = {ICON_MIN_FK, ICON_MAX_FK, 0};
	static ImFontConfig cfg3;
	cfg3.OversampleH = cfg3.OversampleV = 1;
	cfg3.MergeMode = true;
	cfg3.GlyphOffset = ImVec2(0, 1 * g_dpi);
	io.Fonts->AddFontFromFileTTF("data/forkawesome-webfont.ttf", 16.0f * g_dpi, &cfg3, ranges3);

	ImGui_ImplOpenGL3_CreateDeviceObjects();
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


	return 0;
}


int init()
{
	init_gl();
	init_imgui();
	return 0;
}


void quit()
{
	glfwTerminate();
}

std::function<void(unsigned char*, int)> delegator;
int socket;

void stateChanger(unsigned char* stateChange, int bytes)
{
	if (!testWS()) return;
	int type = 0;
	emscripten_websocket_send_binary(socket, &type, 4);
	emscripten_websocket_send_binary(socket, stateChange, bytes);
};

void workspaceChanger(unsigned char* wsChange, int bytes)
{
	if (!testWS()) return;
	int type = 1;
	emscripten_websocket_send_binary(socket, &type, 4);
	emscripten_websocket_send_binary(socket, wsChange, bytes);
};

EM_BOOL onopen(int eventType, const EmscriptenWebSocketOpenEvent* websocketEvent, void* userData)
{
	//debug("initializd");
	socket = websocketEvent->socket;
	return EM_TRUE;
}

EM_BOOL onwserror(int eventType, const EmscriptenWebSocketErrorEvent* websocketEvent, void* userData)
{
	//debug("onerror");
	return EM_TRUE;
}

EM_BOOL onclose(int eventType, const EmscriptenWebSocketCloseEvent* websocketEvent, void* userData)
{
	//debug("onclose");
	return EM_TRUE;
}

std::string generateMemoryString(const std::vector<unsigned char>& vec)
{
	std::string memoryString;
	char buffer[3]; // Buffer for the two-digit hexadecimal value and null terminator

	for (const auto& element : vec)
	{
		sprintf(buffer, "%02X", element);
		memoryString += buffer;
		memoryString += " ";
	}

	return memoryString;
}

std::vector<uint8_t> remoteWSBytes;

EM_BOOL onmessage(int eventType, const EmscriptenWebSocketMessageEvent* websocketEvent, void* userData)
{
	static int type = -1;
	//debug("onmessage");
	if (type == -1) 
	{
		type = *(int*)websocketEvent->data; // next frame is actual data.
	}
	else if (type == 0) 
	{
		GenerateStackFromPanelCommands(websocketEvent->data, websocketEvent->numBytes); // this function should be in main.cpp....
		//logging("UI data");
		type = -1;
	}
	else if (type == 1)
	{
		remoteWSBytes.assign(websocketEvent->data, websocketEvent->data + websocketEvent->numBytes);
		//logging("WS data");
		type = -1;
	}
	//debug(generateMemoryString(v_stack).c_str());
	return EM_TRUE;
}

// Create WebSocket connection
void CreateWebSocket(const char* wsUrl)
{
	if (!emscripten_websocket_is_supported())
	{
		//debug("NOT supported ws?");
	}
	//debug("Start WS");
	EmscriptenWebSocketCreateAttributes ws_attrs = {
		wsUrl,
		NULL,
		EM_TRUE
	};

	EMSCRIPTEN_WEBSOCKET_T ws = emscripten_websocket_new(&ws_attrs);
	emscripten_websocket_set_onopen_callback(ws, NULL, onopen);
	emscripten_websocket_set_onerror_callback(ws, NULL, onwserror);
	emscripten_websocket_set_onclose_callback(ws, NULL, onclose);
	emscripten_websocket_set_onmessage_callback(ws, NULL, onmessage);

	//debug("Complete WS");
}


void webBeforeDraw()
{
	// setstackui already done on GenerateStackFromPanelCommands
	if (remoteWSBytes.size()!=0)
		ProcessWorkspaceQueue(remoteWSBytes.data()); // process workspace...
	remoteWSBytes.clear();


	// apiNotice.
	if (!testWS()) return;
	int type = 2;
	emscripten_websocket_send_binary(socket, &type, 4);
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
			// Error
		});
	);


	beforeDraw = webBeforeDraw;
	stateCallback = stateChanger;
	workspaceCallback = workspaceChanger;
	CreateWebSocket(getHost());

	g_width = canvas_get_width();
	g_height = canvas_get_height();
	g_dpi = getDevicePixelRatio();


	if (init() != 0) return 1;
	
	InitGL(g_width, g_height);

#ifdef __EMSCRIPTEN__
	emscripten_set_main_loop(loop, 0, 1);
#endif

	quit();

	return 0;
}
