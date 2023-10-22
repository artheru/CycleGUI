// Based on Dear ImGui GLFW.

#include <GL/glew.h>
#include "imgui.h"
#include "implot.h"
#include "imgui_impl_glfw.h"
#include "imgui_impl_opengl3.h"
#include <stdio.h>
#define GL_SILENCE_DEPRECATION
#if defined(IMGUI_IMPL_OPENGL_ES2)
#include <GLES2/gl2.h>
#endif
#include <GLFW/glfw3.h> // Will drag system OpenGL headers
#define GLFW_EXPOSE_NATIVE_WIN32
#include <functional>
#include <imgui_internal.h>
#include <iostream>

#ifdef _WIN32
#include <GLFW/glfw3native.h>
#include <Windows.h>
#if defined(_MSC_VER) && (_MSC_VER >= 1900) && !defined(IMGUI_DISABLE_WIN32_FUNCTIONS)
#pragma comment(lib, "legacy_stdio_definitions")
#endif
#endif

#include "misc/freetype/imgui_freetype.h"
#include "forkawesome.h"
#include "IconsForkAwesome.h"
#include <fstream>
#include <glm/gtc/random.hpp>

// CycleUI start:

#include "cycleui.h"
#include "ImGuizmo.h"
#include "messyengine.h"

static void glfw_error_callback(int error, const char* description)
{
    fprintf(stderr, "GLFW Error %d: %s\n", error, description);
}

void LoadFonts(float scale = 1)
{
    ImGuiIO& io = ImGui::GetIO();

#ifdef _WIN32 
    // ASCII
    ImFont* fontmain = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\Georgia.ttf", 15.0f * scale);

    // Chinese
    static ImFontConfig cfg;
    cfg.MergeMode = true;
    ImFont* font = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\msyh.ttc", 16.0f * scale, &cfg, io.Fonts->GetGlyphRangesChineseFull());
    if (font == NULL)
        font = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\msyh.ttf", 16.0f * scale, &cfg, io.Fonts->GetGlyphRangesChineseFull()); // Windows 7

    // emojis
    static ImWchar ranges2[] = { 0x1, 0x1FFFF, 0 };
    static ImFontConfig cfg2;
    cfg2.OversampleH = cfg2.OversampleV = 1;
    cfg2.MergeMode = true;
    cfg2.FontBuilderFlags |= ImGuiFreeTypeBuilderFlags_LoadColor;
    cfg2.GlyphOffset = ImVec2(0, 1 * scale);
    io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\seguiemj.ttf", 16.0f*scale, &cfg2, ranges2);
#endif

    static ImWchar ranges3[] = { ICON_MIN_FK, ICON_MAX_FK, 0 };
    static ImFontConfig cfg3;
    cfg3.OversampleH = cfg3.OversampleV = 1;
    cfg3.MergeMode = true;
    cfg3.FontBuilderFlags |= ImGuiFreeTypeBuilderFlags_LoadColor;
    cfg3.GlyphOffset = ImVec2(0, 1 * scale);
    const int forkawesome_len = 219004;
    void* data = IM_ALLOC(forkawesome_len);
    memcpy(data, forkawesome, forkawesome_len);
    io.Fonts->AddFontFromMemoryTTF(data, forkawesome_len, 16.0f * scale, &cfg3, ranges3);
}


// high dpi:
float g_dpiScale = 1.0f;
bool shouldSetFont = false;


bool ScaleUI(float scale)
{
    shouldSetFont = false;
    ImGuiIO& io = ImGui::GetIO();
    
    ImGui_ImplOpenGL3_DestroyDeviceObjects();

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

    // style.ScaleAllSizes(scale);
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
    colors[ImGuiCol_Button] = ImVec4(0.40f, 0.40f, 0.40f, 0.40f);
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

    io.Fonts->Clear();
    io.Fonts->Flags |= ImFontAtlasFlags_NoBakedLines;

    std::cout << "new scale to " << scale << std::endl;

    LoadFonts(scale);

    return ImGui_ImplOpenGL3_CreateDeviceObjects();
}

GLFWwindow* mainWnd;

#ifdef _WIN32
void HandleDpiChange(HWND hWnd, WPARAM wParam, LPARAM lParam) {
    // Retrieve the new DPI scale factor from the message parameters
    int newDpiX = LOWORD(wParam);
    int newDpiY = HIWORD(wParam);
    g_dpiScale = static_cast<float>(newDpiX) / static_cast<float>(USER_DEFAULT_SCREEN_DPI);
    shouldSetFont = true;
    // Resize the GLFW window to match the new DPI scale
    RECT* rect = reinterpret_cast<RECT*>(lParam);
    //glfwSetWindowSize(window, rect->right - rect->left, rect->bottom - rect->top);


    //std::cout << "New DPI: " << newDpiX << std::endl;
}


LRESULT CALLBACK WindowProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
    switch (uMsg) {
    case WM_DPICHANGED:
        HandleDpiChange(hWnd, wParam, lParam);
        break;
    }
    return CallWindowProc(reinterpret_cast<WNDPROC>(glfwGetWindowUserPointer(mainWnd)), hWnd, uMsg, wParam, lParam);
}


#endif

void MainWindowPreventCloseCallback(GLFWwindow* window) {
    glfwHideWindow(window);  // Hide the window instead of destroying it
}


int main();

#ifdef _WIN32 // For Windows
#define LIBVRENDER_EXPORT __declspec(dllexport)
#else // For Linux and other platforms
#define LIBVRENDER_EXPORT
#endif

extern "C" LIBVRENDER_EXPORT void SetUIStack(unsigned char* bytes, int length)
{
    cgui_stack = bytes;
}

// only applicable on main thread, i.e: BeforeDraw
extern "C" LIBVRENDER_EXPORT void UploadWorkspace(void* bytes)
{
    ProcessWorkspaceQueue(bytes);
}

// External function to receive the callback delegate
extern "C" LIBVRENDER_EXPORT void RegisterStateChangedCallback(NotifyStateChangedFunc callback)
{
    stateCallback = callback;
}
// External function to receive the callback delegate
extern "C" LIBVRENDER_EXPORT void RegisterBeforeDrawCallback(BeforeDrawFunc callback)
{
    beforeDraw = callback;
}
// External function to receive the callback delegate
extern "C" LIBVRENDER_EXPORT void RegisterWorkspaceCallback(NotifyWorkspaceChangedFunc callback)
{
    workspaceCallback = callback;
}

extern "C" LIBVRENDER_EXPORT int MainLoop()
{
    main();
    return 1;
}

extern "C" LIBVRENDER_EXPORT void ShowMainWindow()
{
    glfwShowWindow(mainWnd);
}

extern "C" LIBVRENDER_EXPORT void SetWndIcon(unsigned char* bytes, int length)
{
#ifdef _WIN32
    auto offset = LookupIconIdFromDirectoryEx(bytes, true, 0, 0, LR_DEFAULTCOLOR);
    HICON hicon = CreateIconFromResourceEx(bytes + offset, length-offset,
        true, // Set to true if you're creating an icon; false if creating a cursor.
        0x00030000, // Version must be set to 0x00030000.
        0, // Use 0 for the desired width (system default size).
        0, // Use 0 for the desired height (system default size).
        LR_DEFAULTSIZE// Load icon with default color and size.
    );
    auto hwnd = glfwGetWin32Window(mainWnd);
    SendMessage(hwnd, WM_SETICON, ICON_SMALL, (LPARAM)hicon);

	hicon = CreateIconFromResourceEx(bytes + offset, length - offset,
        true, // Set to true if you're creating an icon; false if creating a cursor.
        0x00030000, // Version must be set to 0x00030000.
        0, // Use 0 for the desired width (system default size).
        0, // Use 0 for the desired height (system default size).
        LR_DEFAULTSIZE// Load icon with default color and size.
    );
    SendMessage(hwnd, WM_SETICON, ICON_BIG, (LPARAM)hicon);
#endif

}

std::string windowTitle = "CycleUI Workspace - Compile on " __DATE__ " " __TIME__;

extern "C" LIBVRENDER_EXPORT void SetWndTitle(char* title)
{
    windowTitle = std::string(title);
}

#include "lib/nfd/nfd.h"

// Main code
int main()
{
    glEnable(GL_MULTISAMPLE);

    glfwSetErrorCallback(glfw_error_callback);
    if (!glfwInit())
        return 1;
    
    // GL 3.0 + GLSL 130
    const char* glsl_version = "#version 300 es"; 
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 4);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 6);

	// glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
    glfwWindowHint(GLFW_SCALE_TO_MONITOR, GLFW_TRUE);
    glfwWindowHint(GLFW_SAMPLES, 4);
    // Create window with graphics context

    int initW = 800, initH = 600;

#ifdef _WIN32
    mainWnd = glfwCreateWindow(initW, initH, windowTitle.c_str(), nullptr, nullptr);
#else
    mainWnd = glfwCreateWindow(initW, initH, windowTitle.c_str(), glfwGetPrimaryMonitor(), nullptr);
#endif

    if (mainWnd == nullptr)
        return 1;


    glfwMakeContextCurrent(mainWnd);
    glfwSwapInterval(1); // Enable vsync

    // Setup Dear ImGui context
    IMGUI_CHECKVERSION();

    const GLubyte* glVersion = glGetString(GL_VERSION);
    if (glVersion) {
        std::cout << "OpenGL Version: " << glVersion << std::endl;
    }
    else {
        std::cerr << "Failed to get OpenGL version" << std::endl;
    }

    ImGui::CreateContext();
    ImPlot::CreateContext();

    ImGuiIO& io = ImGui::GetIO(); (void)io;
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;     // Enable Keyboard Controls
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableGamepad;      // Enable Gamepad Controls
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;         // Enable Docking
    io.ConfigFlags |= ImGuiConfigFlags_ViewportsEnable;       // Enable Multi-Viewport / Platform Windows

    // temporary fix for viewport dpi change. 
    io.ConfigFlags |= ImGuiConfigFlags_DpiEnableScaleViewports;
    io.ConfigFlags |= ImGuiConfigFlags_DpiEnableScaleFonts;

    io.ConfigViewportsNoDefaultParent = true;
    //io.ConfigViewportsNoAutoMerge = true;
    //io.ConfigViewportsNoTaskBarIcon = true;

    // Setup Dear ImGui style
    ImGui::StyleColorsDark();

    // When viewports are enabled we tweak WindowRounding/WindowBg so platform windows can look identical to regular ones.
    ImGuiStyle& style = ImGui::GetStyle();
    if (io.ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
    {
        style.WindowRounding = 0.0f;
        //style.Colors[ImGuiCol_WindowBg].w = 1.0f;
    }
    
    // Set glfw callback
    glfwSetMouseButtonCallback(mainWnd, mouse_button_callback);
    glfwSetCursorPosCallback(mainWnd, cursor_position_callback);
    glfwSetScrollCallback(mainWnd, scroll_callback);
    glfwSetKeyCallback(mainWnd, key_callback);
    glfwSetWindowCloseCallback(mainWnd, MainWindowPreventCloseCallback);

    // Setup Platform/Renderer backends
    ImGui_ImplGlfw_InitForOpenGL(mainWnd, true);
    ImGui_ImplOpenGL3_Init(glsl_version);

    
    // Warning: the validity of monitor DPI information on Windows depends on the application DPI awareness settings, which generally needs to be set in the manifest or at runtime.
    

#ifdef _WIN32
    auto hwnd = glfwGetWin32Window(mainWnd);
    nfd_owner = hwnd;
    glfwSetWindowUserPointer(mainWnd, reinterpret_cast<void*>(GetWindowLongPtr(hwnd, GWLP_WNDPROC)));
    SetWindowLongPtr(hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(WindowProc));
    int dpiX = GetDpiForWindow(hwnd);
#else
    float x_scale, y_scale;
    glfwGetMonitorContentScale(glfwGetPrimaryMonitor(), &x_scale, &y_scale);
    int dpiX = x_scale;
#endif
    
    std::cout << dpiX << std::endl;
    ScaleUI(static_cast<float>(dpiX) / static_cast<float>(96)); // default dpi=96.

    InitGL(initW, initH);

    while (true)
    {
        int isVisible = glfwGetWindowAttrib(mainWnd, GLFW_VISIBLE);

        int display_w, display_h;
        glfwGetFramebufferSize(mainWnd, &display_w, &display_h);

        glfwSetWindowTitle(mainWnd, windowTitle.c_str());

        glViewport(0, 0, display_w, display_h);
        // glClearColor(clear_color.x * clear_color.w, clear_color.y * clear_color.w, clear_color.z * clear_color.w, clear_color.w);
        glClear(GL_COLOR_BUFFER_BIT);


        if (shouldSetFont)
            ScaleUI(g_dpiScale);

        ImGuizmo::SetOrthographic(camera->ProjectionMode);


        // Start the Dear ImGui frame
        ImGui_ImplOpenGL3_NewFrame();
        ImGui_ImplGlfw_NewFrame();

        ImGui::NewFrame();
        ImGuizmo::BeginFrame();

        glfwPollEvents();
        
        ProcessUIStack();

        camera->dpi = ImGui::GetMainViewport()->DpiScale;

        if (isVisible && display_h > 0 && display_w > 0)
            DrawWorkspace(display_w, display_h);




        // static bool show_demo_window = true;
        // if (show_demo_window)
        //     ImGui::ShowDemoWindow(&show_demo_window);
        //
        // static bool show_plot_demo_window = true;
        // if (show_plot_demo_window)
        //     ImPlot::ShowDemoWindow(&show_plot_demo_window);
        // ImGui::Text("🖐This is some useful text.以及汉字, I1l, 0Oo");
        // ImGui::Text(ICON_FK_ADDRESS_BOOK" TEST FK");

        // Rendering, even though there could be nothing to draw.
        ImGui::Render();
        ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());
        
        if (io.ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
        {
            GLFWwindow* backup_current_context = glfwGetCurrentContext();
            ImGui::UpdatePlatformWindows();
            ImGui::RenderPlatformWindowsDefault();
            glfwMakeContextCurrent(backup_current_context);
        }

        //glFinish();
        glfwSwapBuffers(mainWnd);

        // todo: only redraw on mouse/keyboard or definite redraw event, to save system resources.
    }
}
