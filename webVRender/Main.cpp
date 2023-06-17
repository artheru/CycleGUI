#include <stdio.h>

#ifdef __EMSCRIPTEN__
#include <emscripten.h>
#endif

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
	// 1. Show a simple window.
	// Tip: if we don't call ImGui::Begin()/ImGui::End() the widgets automatically appears in a window called "Debug".
	{
		static float f = 0.0f;
		static int counter = 0;
		ImGui::Text("+grid+pc+del+sky"); // Display some text (you can use a format string too)
		if (ImGui::Button("Test point cloud!"))
		{
			point_cloud pc;
			auto N = 16000;
			for (int i = 0; i < N; ++i) {
				float rho = 3.883222077450933 * i;
				float sphi = 1 - 2 * (i + 0.5f) / N;
				float cphi = std::sqrt(1 - sphi * sphi);
				float dx = std::cos(rho) * cphi;
				float dy = std::sin(rho) * cphi;
				float dz = sphi;
				pc.x_y_z_Sz.push_back(glm::vec4(dx * 3, dy * 3 + 2, -dz * 3 + 1, (5.0 * i) / N + 1));
				pc.color.push_back(glm::vec4(1, 1 - float(i) / N, 1 - float(i) / N, 1));
			}
			AddPointCloud(std::string("test"), pc);
		}

		ImGui::Text("🖐This is some useful text.以及汉字, I1l, 0Oo");
		// Display some text (you can use a format strings too)
		ImGui::Text(ICON_FK_ADDRESS_BOOK" TEST FK");
		static bool test = false;
		ToggleButton("试一试呀", &test);
		ImGui::SliderFloat("float", &f, 0.0f, 1.0f); // Edit 1 float using a slider from 0.0f to 1.0f
		ImGui::ColorEdit3("clear color", (float*)&clear_color); // Edit 3 floats representing a color

		ImGui::Checkbox("Demo Window", &show_demo_window); // Edit bools storing our windows open/close state
		ImGui::Checkbox("Another Window", &show_another_window);

		if (ImGui::Button("Button"))
			// Buttons return true when clicked (NB: most widgets return true when edited/activated)
			counter++;
		ImGui::SameLine();
		ImGui::Text("counter = %d", counter);

		ImGui::Text("Application average %.3f ms/frame (%.1f FPS)", 1000.0f / ImGui::GetIO().Framerate,
		            ImGui::GetIO().Framerate);
	}

	// 2. Show another simple window. In most cases you will use an explicit Begin/End pair to name your windows.
	if (show_another_window)
	{
		ImGui::Begin("Another Window", &show_another_window);
		ImGui::Text("Hello from another window!");
		if (ImGui::Button("Close Me"))
			show_another_window = false;
		ImGui::End();
	}

	// 3. Show the ImGui demo window. Most of the sample code is in ImGui::ShowDemoWindow(). Read its code to learn more about Dear ImGui!
	if (show_demo_window)
	{
		ImGui::SetNextWindowPos(ImVec2(650, 20), ImGuiCond_FirstUseEver);
		// Normally user code doesn't need/want to call this because positions are saved in .ini file anyway. Here we just want to make the demo initial state a bit more friendly!
		ImGui::ShowDemoWindow(&show_demo_window);
	}

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
	io.Fonts->AddFontFromFileTTF("data/ZCOOLQingKeHuangYou-Regular.ttf", 16.0f * g_dpi);
	static ImFontConfig cfg;
	cfg.MergeMode = true;
	ImFont* font = io.Fonts->AddFontFromFileTTF("data/ZCOOLQingKeHuangYou-Regular.ttf", 16.0f * g_dpi, &cfg,
	                                            io.Fonts->GetGlyphRangesChineseFull());

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

#include <emscripten/websocket.h>

EMSCRIPTEN_WEBSOCKET_T ws;


std::function<void(unsigned char*, int)> delegator;
int socket;

void stateChanger(unsigned char* stateChange, int bytes)
{
	emscripten_websocket_send_binary(socket, stateChange, bytes);
};

EM_BOOL onopen(int eventType, const EmscriptenWebSocketOpenEvent* websocketEvent, void* userData)
{
	//debug("initializd");
	socket = websocketEvent->socket;
	return EM_TRUE;
}

EM_BOOL onerror(int eventType, const EmscriptenWebSocketErrorEvent* websocketEvent, void* userData)
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


EM_BOOL onmessage(int eventType, const EmscriptenWebSocketMessageEvent* websocketEvent, void* userData)
{
	//debug("onmessage");
	GenerateStackFromPanelCommands(websocketEvent->data, websocketEvent->numBytes);
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
	emscripten_websocket_set_onerror_callback(ws, NULL, onerror);
	emscripten_websocket_set_onclose_callback(ws, NULL, onclose);
	emscripten_websocket_set_onmessage_callback(ws, NULL, onmessage);

	//debug("Complete WS");
}

void webBeforeDraw()
{
	// setstackui
}

EM_JS(const char*, getHost, (), {
      var terminalDataUrl = 'ws://' + window.location.host + '/terminal/data';
      var length = lengthBytesUTF8(terminalDataUrl) + 1;
      var buffer = _malloc(length);
      stringToUTF8(terminalDataUrl, buffer, length + 1);

      return buffer;
      });


extern "C" int main(int argc, char** argv)
{
	logging("Start WEB-based CycleUI");

	beforeDraw = webBeforeDraw;
	stateCallback = stateChanger;
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
