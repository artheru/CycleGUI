// Dear ImGui: standalone example application for GLFW + OpenGL 3, using programmable pipeline
// (GLFW is a cross-platform general purpose library for handling windows, inputs, OpenGL/Vulkan/Metal graphics context creation, etc.)
// If you are new to Dear ImGui, read documentation from the docs/ folder + read the top of imgui.cpp.
// Read online: https://github.com/ocornut/imgui/tree/master/docs
#define NOTIFY_DEBUG

#include <GL/glew.h>
#include "imgui.h"
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

#ifndef __EMSCRIPTEN__
#include <GLFW/glfw3native.h>
#include <Windows.h>
#endif


// [Win32] Our example includes a copy of glfw3.lib pre-compiled with VS2010 to maximize ease of testing and compatibility with old VS compilers.
// To link with VS2010-era libraries, VS2015+ requires linking with legacy_stdio_definitions.lib, which we do using this pragma.
// Your own project should not be affected, as you are likely to link with a newer binary of GLFW that is adequate for your version of Visual Studio.
#if defined(_MSC_VER) && (_MSC_VER >= 1900) && !defined(IMGUI_DISABLE_WIN32_FUNCTIONS)
#pragma comment(lib, "legacy_stdio_definitions")
#endif

// This example can also compile and run with Emscripten! See 'Makefile.emscripten' for details.
#ifdef __EMSCRIPTEN__
#include "emscripten_mainloop_stub.h"
#endif

// Font part:
#include "misc/freetype/imgui_freetype.h"
#include "forkawesome.h"
#include "IconsForkAwesome.h"
#include <format>
#include <fstream>

#include <glm/gtc/random.hpp>
#include "cycleui.h"
#include "messyengine.h"

static void glfw_error_callback(int error, const char* description)
{
    fprintf(stderr, "GLFW Error %d: %s\n", error, description);
}

void LoadFonts(float scale = 1)
{
    ImGuiIO& io = ImGui::GetIO();

#ifdef __EMSCRIPTEN__
    io.Fonts->AddFontDefault();
#else

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
    io.Fonts->AddFontFromMemoryTTF((void*)forkawesome, 219004, 16.0f * scale, &cfg3, ranges3);

}

struct FreeTypeTest
{
    enum FontBuildMode { FontBuildMode_FreeType, FontBuildMode_Stb };

    FontBuildMode   BuildMode = FontBuildMode_FreeType;
    bool            WantRebuild = true;
    float           RasterizerMultiply = 1.0f;
    unsigned int    FreeTypeBuilderFlags = 0;

    // Call _BEFORE_ NewFrame()
    bool PreNewFrame()
    {
        if (!WantRebuild)
            return false;

        ImFontAtlas* atlas = ImGui::GetIO().Fonts;
        for (int n = 0; n < atlas->ConfigData.Size; n++)
            ((ImFontConfig*)&atlas->ConfigData[n])->RasterizerMultiply = RasterizerMultiply;

        // Allow for dynamic selection of the builder. 
        // In real code you are likely to just define IMGUI_ENABLE_FREETYPE and never assign to FontBuilderIO.
#ifdef IMGUI_ENABLE_FREETYPE
        if (BuildMode == FontBuildMode_FreeType)
        {
            atlas->FontBuilderIO = ImGuiFreeType::GetBuilderForFreeType();
            atlas->FontBuilderFlags = FreeTypeBuilderFlags;
        }
#endif
#ifdef IMGUI_ENABLE_STB_TRUETYPE
        if (BuildMode == FontBuildMode_Stb)
        {
            atlas->FontBuilderIO = ImFontAtlasGetBuilderForStbTruetype();
            atlas->FontBuilderFlags = 0;
        }
#endif
        atlas->Build();
        WantRebuild = false;
        return true;
    }

    // Call to draw UI
    void ShowFontsOptionsWindow()
    {
        ImFontAtlas* atlas = ImGui::GetIO().Fonts;

        ImGui::Begin("FreeType Options");
        ImGui::ShowFontSelector("Fonts");
        WantRebuild |= ImGui::RadioButton("FreeType", (int*)&BuildMode, FontBuildMode_FreeType);
        ImGui::SameLine();
        WantRebuild |= ImGui::RadioButton("Stb (Default)", (int*)&BuildMode, FontBuildMode_Stb);
        WantRebuild |= ImGui::DragInt("TexGlyphPadding", &atlas->TexGlyphPadding, 0.1f, 1, 16);
        WantRebuild |= ImGui::DragFloat("RasterizerMultiply", &RasterizerMultiply, 0.001f, 0.0f, 2.0f);
        ImGui::Separator();

        if (BuildMode == FontBuildMode_FreeType)
        {
#ifndef IMGUI_ENABLE_FREETYPE
            ImGui::TextColored(ImVec4(1.0f, 0.5f, 0.5f, 1.0f), "Error: FreeType builder not compiled!");
#endif
            WantRebuild |= ImGui::CheckboxFlags("NoHinting", &FreeTypeBuilderFlags, ImGuiFreeTypeBuilderFlags_NoHinting);
            WantRebuild |= ImGui::CheckboxFlags("NoAutoHint", &FreeTypeBuilderFlags, ImGuiFreeTypeBuilderFlags_NoAutoHint);
            WantRebuild |= ImGui::CheckboxFlags("ForceAutoHint", &FreeTypeBuilderFlags, ImGuiFreeTypeBuilderFlags_ForceAutoHint);
            WantRebuild |= ImGui::CheckboxFlags("LightHinting", &FreeTypeBuilderFlags, ImGuiFreeTypeBuilderFlags_LightHinting);
            WantRebuild |= ImGui::CheckboxFlags("MonoHinting", &FreeTypeBuilderFlags, ImGuiFreeTypeBuilderFlags_MonoHinting);
            WantRebuild |= ImGui::CheckboxFlags("Bold", &FreeTypeBuilderFlags, ImGuiFreeTypeBuilderFlags_Bold);
            WantRebuild |= ImGui::CheckboxFlags("Oblique", &FreeTypeBuilderFlags, ImGuiFreeTypeBuilderFlags_Oblique);
            WantRebuild |= ImGui::CheckboxFlags("Monochrome", &FreeTypeBuilderFlags, ImGuiFreeTypeBuilderFlags_Monochrome);
        }

        if (BuildMode == FontBuildMode_Stb)
        {
#ifndef IMGUI_ENABLE_STB_TRUETYPE
            ImGui::TextColored(ImVec4(1.0f, 0.5f, 0.5f, 1.0f), "Error: stb_truetype builder not compiled!");
#endif
        }
        ImGui::End();
    }
};

FreeTypeTest freetype_test;

// high dpi:
static bool g_IsUITextureIDValid = false;

bool ScaleUI(float scale)
{
    ImGuiIO& io = ImGui::GetIO();

    g_IsUITextureIDValid = false;
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

float g_dpiScale = 1.0f;
GLFWwindow* mainWnd;

#ifndef __EMSCRIPTEN__
void HandleDpiChange(HWND hWnd, WPARAM wParam, LPARAM lParam) {
    // Retrieve the new DPI scale factor from the message parameters
    int newDpiX = LOWORD(wParam);
    int newDpiY = HIWORD(wParam);
    g_dpiScale = static_cast<float>(newDpiX) / static_cast<float>(USER_DEFAULT_SCREEN_DPI);

    // Resize the GLFW window to match the new DPI scale
    RECT* rect = reinterpret_cast<RECT*>(lParam);
    //glfwSetWindowSize(window, rect->right - rect->left, rect->bottom - rect->top);

    // ScaleUI(g_dpiScale);

    //std::cout << "New DPI: " << newDpiX << std::endl;
}

#ifdef NOTIFY_DEBUG

NOTIFYICONDATA nid;
HMENU hPopupMenu;

#define IDM_TERMINATE 100

LRESULT CALLBACK TrayIconCallback(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (msg == WM_USER + 1)
    {
        if (lParam == WM_LBUTTONDBLCLK)
        {
            glfwShowWindow(mainWnd);
            std::cout << "Clicked" << std::endl;
        }
        else if (lParam == WM_RBUTTONUP)
        {
            POINT cursorPos;
            GetCursorPos(&cursorPos);

            SetForegroundWindow(hwnd);
            TrackPopupMenu(hPopupMenu, TPM_LEFTALIGN | TPM_LEFTBUTTON, cursorPos.x, cursorPos.y, 0, hwnd, NULL);
        }
    }
    else if (msg == WM_CONTEXTMENU)
    {
        POINT cursorPos;
        GetCursorPos(&cursorPos);

        SetForegroundWindow(hwnd);
        TrackPopupMenu(hPopupMenu, TPM_LEFTALIGN | TPM_LEFTBUTTON, cursorPos.x, cursorPos.y, 0, hwnd, NULL);
    }
    else if (msg == WM_COMMAND)
    {
        if (LOWORD(wParam) == IDM_TERMINATE) // ID of the "Terminate" menu item
        {
            exit(0);
            std::cout << "Terminating the program" << std::endl;
        }
    }

    return DefWindowProc(hwnd, msg, wParam, lParam);
}
#endif


LRESULT CALLBACK WindowProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
    switch (uMsg) {
    case WM_DPICHANGED:
        HandleDpiChange(hWnd, wParam, lParam);
        break;
    }
    return CallWindowProc(reinterpret_cast<WNDPROC>(glfwGetWindowUserPointer(mainWnd)), hWnd, uMsg, wParam, lParam);
}

void MainWindowPreventCloseCallback(GLFWwindow* window) {
    glfwHideWindow(window);  // Hide the window instead of destroying it
}

#endif



int main();


extern "C" __declspec(dllexport) void SetUIStack(unsigned char* bytes, int length)
{
    stack = bytes;
}

// External function to receive the callback delegate
extern "C" __declspec(dllexport) void RegisterStateChangedCallback(NotifyStateChangedFunc callback)
{
    stateCallback = callback;
}
// External function to receive the callback delegate
extern "C" __declspec(dllexport) void RegisterBeforeDrawCallback(BeforeDrawFunc callback)
{
    beforeDraw = callback;
}

extern "C" __declspec(dllexport) int MainLoop()
{
    std::cout << "TEST!" << std::endl;
    main();
    return 1;
}

// Main code
int main()
{
    glEnable(GL_MULTISAMPLE);

    glfwSetErrorCallback(glfw_error_callback);
    if (!glfwInit())
        return 1;

    // Decide GL+GLSL versions
#if defined(IMGUI_IMPL_OPENGL_ES2)
    // GL ES 2.0 + GLSL 100
    const char* glsl_version = "#version 100";
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 2);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 0);
    glfwWindowHint(GLFW_CLIENT_API, GLFW_OPENGL_ES_API);
#elif defined(__APPLE__)
    // GL 3.2 + GLSL 150
    const char* glsl_version = "#version 150";
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 2);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);  // 3.2+ only
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GL_TRUE);            // Required on Mac
#else
    // GL 3.0 + GLSL 130
    const char* glsl_version = "#version 130"; 
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 0);
    //glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);  // 3.2+ only
    //glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GL_TRUE);            // 3.0+ only
#endif
    glfwWindowHint(GLFW_SCALE_TO_MONITOR, GLFW_TRUE);
    glfwWindowHint(GLFW_SAMPLES, 8);
    // Create window with graphics context

    int initW = 800, initH = 600;
    mainWnd = glfwCreateWindow(initW, initH, "libVRender", nullptr, nullptr);

    if (mainWnd == nullptr)
        return 1;


    glfwMakeContextCurrent(mainWnd);
    glfwSwapInterval(1); // Enable vsync

    // Setup Dear ImGui context
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
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
    // ImGui::StyleColorsLight();

    // When viewports are enabled we tweak WindowRounding/WindowBg so platform windows can look identical to regular ones.
    ImGuiStyle& style = ImGui::GetStyle();
    if (io.ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
    {
        style.WindowRounding = 0.0f;
        style.Colors[ImGuiCol_WindowBg].w = 1.0f;
    }

#ifndef __EMSCRIPTEN__
    // Set glfw callback
    glfwSetMouseButtonCallback(mainWnd, mouse_button_callback);
    glfwSetCursorPosCallback(mainWnd, cursor_position_callback);
    glfwSetScrollCallback(mainWnd, scroll_callback);

    // Setup Platform/Renderer backends
    ImGui_ImplGlfw_InitForOpenGL(mainWnd, true);
    ImGui_ImplOpenGL3_Init(glsl_version);

    auto hwnd = glfwGetWin32Window(mainWnd);
    glfwSetWindowUserPointer(mainWnd, reinterpret_cast<void*>(GetWindowLongPtr(hwnd, GWLP_WNDPROC)));
    SetWindowLongPtr(hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(WindowProc));
    int dpiX = GetDpiForWindow(hwnd);
    std::cout << dpiX << std::endl;
    ScaleUI(static_cast<float>(dpiX) / static_cast<float>(USER_DEFAULT_SCREEN_DPI));
#else
    ScaleUI(1);
#endif

#ifndef __EMSCRIPTEN__
#ifdef NOTIFY_DEBUG
    // Create a hidden window
    HINSTANCE hInstance = GetModuleHandle(NULL);
    
    HWND hwndTray;
    WNDCLASSEX wndClass = { 0 };
    wndClass.cbSize = sizeof(WNDCLASSEX);
    wndClass.lpfnWndProc = TrayIconCallback;
    wndClass.hInstance = hInstance;
    wndClass.lpszClassName = L"TrayIconWindowClass";
    
    RegisterClassEx(&wndClass);
    hwndTray = CreateWindowEx(0, wndClass.lpszClassName, L"Tray Icon Window", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, hInstance, NULL);
    
    
    ZeroMemory(&nid, sizeof(nid));
    // Set up the notification icon data
    nid.cbSize = sizeof(nid);
    nid.hWnd = hwndTray;
    nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    nid.uCallbackMessage = WM_USER + 1;
    nid.hIcon = LoadIcon(NULL, IDI_APPLICATION);  // Icon for the tray
    wcscpy_s(nid.szTip, L"Notification Icon");
    Shell_NotifyIcon(NIM_ADD, &nid);
    
    hPopupMenu = CreatePopupMenu();
    AppendMenu(hPopupMenu, MF_STRING, IDM_TERMINATE, L"Terminate");
    // Update the tray icon with the menu
    Shell_NotifyIcon(NIM_SETVERSION, &nid);
#endif
#endif
    
    // Our state
    bool show_demo_window = true;
    bool show_another_window = false;
    ImVec4 clear_color = ImVec4(0.45f, 0.55f, 0.60f, 1.00f);

    // Main loop
#ifdef __EMSCRIPTEN__
    // For an Emscripten build we are disabling file-system access, so let's not attempt to do a fopen() of the imgui.ini file.
    // You may manually call LoadIniSettingsFromMemory() to load settings from your own storage.
    io.IniFilename = nullptr;
    EMSCRIPTEN_MAINLOOP_BEGIN
#else
    glfwSetWindowCloseCallback(mainWnd, MainWindowPreventCloseCallback);

    InitGL(initW, initH);


    while (true)
#endif
    {

        if (freetype_test.PreNewFrame())
        {
            // REUPLOAD FONT TEXTURE TO GPU
            ImGui_ImplOpenGL3_DestroyDeviceObjects();
            ImGui_ImplOpenGL3_CreateDeviceObjects();
        }


        int display_w, display_h;
        glfwGetFramebufferSize(mainWnd, &display_w, &display_h);
        glViewport(0, 0, display_w, display_h);
        // glClearColor(clear_color.x * clear_color.w, clear_color.y * clear_color.w, clear_color.z * clear_color.w, clear_color.w);
        glClear(GL_COLOR_BUFFER_BIT);


        // Start the Dear ImGui frame
        ImGui_ImplOpenGL3_NewFrame();
        ImGui_ImplGlfw_NewFrame();

        g_IsUITextureIDValid = true;
        ImGui::NewFrame();
        // Poll and handle events (inputs, window resize, etc.)
        // You can read the io.WantCaptureMouse, io.WantCaptureKeyboard flags to tell if dear imgui wants to use your inputs.
        // - When io.WantCaptureMouse is true, do not dispatch mouse input data to your main application, or clear/overwrite your copy of the mouse data.
        // - When io.WantCaptureKeyboard is true, do not dispatch keyboard input data to your main application, or clear/overwrite your copy of the keyboard data.
        // Generally you may always pass all inputs to dear imgui, and hide them from your application based on those two flags.


        glfwPollEvents();


        ProcessUIStack();

        camera->dpi = ImGui::GetMainViewport()->DpiScale;
        DrawWorkspace(display_w, display_h);

        // 1. Show the big demo window (Most of the sample code is in ImGui::ShowDemoWindow()! You can browse its code to learn more about Dear ImGui!).
        if (show_demo_window)
            ImGui::ShowDemoWindow(&show_demo_window);

        // 2. Show a simple window that we create ourselves. We use a Begin/End pair to create a named window.
        {
            static float f = 0.0f;
            static int counter = 0;

            ImGui::Begin("TEST");
            ImGui::Text("mouse hover: %s, %s, %d", ui_state.mousePointingType.c_str(), ui_state.mousePointingInstance.c_str(), ui_state.mousePointingSubId);
            
            if (ImGui::Button("Set selector=click"))
                SetWorkspaceSelectMode(click);
            ImGui::SameLine(0, 5);
            if (ImGui::Button("drag"))
                SetWorkspaceSelectMode(drag);
            ImGui::SameLine(0, 5);
            if (ImGui::Button("drag*"))
                SetWorkspaceSelectMode(multi_drag_click);
            ImGui::SameLine(0, 5);
            if (ImGui::Button("painter(r=100)"))
                SetWorkspaceSelectMode(paint, 100);

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
                    pc.x_y_z_Sz.push_back(glm::vec4(dx * 2, dy * 2+2, -dz * 2+1, (5.0 * i) / N + 1));
                    pc.color.push_back(glm::vec4(1, 1 - float(i) / N, 1 - float(i) / N, 1));
                }
                for (int i = 0; i < N; ++i)
                {
                    pc.x_y_z_Sz.push_back(glm::vec4(float(i/100)/40, (i%100)/50.0f, (float)i/1000, 4));
                    pc.color.push_back(glm::vec4(1, 1 - float(i) / N, 1 - float(i) / N, 1));
                }
                AddPointCloud("test", pc);

                // point cloud doesn't support border.
                SetObjectShine("test", glm::vec3(0, 1, 0), 1.0, "hover");
                SetObjectShine("test", glm::vec3(0, 1, 1), 1.0, "selected");
                BringObjectFront("test", "hover");
                SetObjectBehaviour("test", "sub_selectable");
            }

            static bool test;
            static bool loaded=false;
            static float h = 15;
            if (ImGui::Button("Load a lot point cloud!"))
            {
                std::ifstream file("D:\\corpus\\static_point_cloud\\geoslam\\Hotel_Southampton.laz.bin", std::ios::binary);

                file.seekg(0, std::ios::end);
                std::streampos fileSize = file.tellg();
                file.seekg(0, std::ios::beg);
                int n = fileSize / 32;

                point_cloud pc;
                pc.x_y_z_Sz.resize(n);
                pc.color.resize(n);
                for (int i = 0; i < n; i+=1) {
                    file.read((char*)&pc.x_y_z_Sz[i], 16);
                    file.read((char*)&pc.color[i], 16);
                    //pc.color[i] = glm::vec4(1.0f);
                    pc.color[i] /= 65535;
                    pc.color[i].a = 1;
                }
                pc.position = glm::vec3(0, 0, 15);

                file.close();
                AddPointCloud("bigpc", pc);
                loaded = true;
                if (loaded)
                {
                    ImGui::DragFloat("height", &h, 0.02, -15, 15);
                    ModifyPointCloud("bigpc", glm::vec3(0.0f, 0.0f, h), glm::identity<glm::quat>());
                }
            }
            if (ImGui::Button("Load models!"))
            {

                std::ifstream file("xqe.glb", std::ios::binary | std::ios::ate);

                if (!file.is_open()) {
                    std::cerr << "Failed to open the file." << std::endl;
                    return 1;
                }

                // Get the file size
                std::streampos fileSize = file.tellg();
                file.seekg(0, std::ios::beg);

                // Allocate memory for the file content
                unsigned char* buffer = new unsigned char[fileSize];

                // Read the file content into the buffer
                if (!file.read(reinterpret_cast<char*>(buffer), fileSize)) {
                    std::cerr << "Failed to read the file." << std::endl;
                    delete[] buffer;
                    return 1;
                }

                // Close the file
                file.close();

                //LoadModel("flamingo", buffer, fileSize, ModelDetail{ glm::vec3(0,0,0.7), 3 });
                LoadModel("xqe", buffer, fileSize, ModelDetail{ glm::vec3(-1,0,-0.2), glm::angleAxis(glm::radians(180.0f),glm::vec3(1.0f,0.0,0.0)) , 0.001f });
                //LoadModel("xqe", buffer, fileSize, ModelDetail{ glm::vec3(0,0,-5.5), glm::angleAxis(90.0f,glm::vec3(1.0f,0.0,0.0)) ,2 ,0.01f }); // rotate 90 around x is typical.

                PutModelObject("xqe", "xqe1", glm::zero<glm::vec3>(), glm::identity<glm::quat>());
                //SetObjectBehaviour("xqe1", "selectable");
                //SetObjectBorder("xqe1", glm::vec3(1, 1, 1), "hover");
                //SetObjectBorder("xqe1", glm::vec3(1, 0, 0), "selected");

                SetWorkspaceShine(glm::vec3(1, 0, 1), 1.0);

                SetObjectShineOnHover("xqe1");
                BringObjectFrontOnHover("xq1");

                PutModelObject("xqe", "xqe2", glm::vec3(10, 0, 0), glm::angleAxis(glm::radians(90.0f), glm::vec3(0.0f, 0.0f, 1.0f)));
                //SetObjectShine("xqe2", glm::vec3(1, 0, 0), 1.0, "selected");
                //SetObjectBehaviour("xqe2", "sub_selectable");
                SetSubObjectShineOnHover("xqe2");
                BringSubObjectFrontOnHover("xq2");
                //SetObjectBorder("xqe2", glm::vec3(1, 0, 0), "hover_sub");


            }
            if (ImGui::Button("Many"))
            {
                for (int i = 0; i < 100; ++i) {
                    float angle = glm::linearRand(0.0f, glm::two_pi<float>());

                    // Create a quaternion that rotates around the z-axis
                    glm::quat rotationQuat = glm::angleAxis(angle, glm::vec3(0.0f, 0.0f, 1.0f));

                    // Generate a random vec2 in the XY plane
                    glm::vec2 randomVec2 = glm::diskRand(50.0f);
                    PutModelObject("xqe", std::format("f{}",i).c_str(), glm::vec3(randomVec2,0), rotationQuat);
                }
            }
            ImGui::Text("🖐This is some useful text.以及汉字, I1l, 0Oo");               // Display some text (you can use a format strings too)
            ImGui::Text(std::format("stare={},{},{}", camera->stare[0], camera->stare[1], camera->stare[2]).c_str());
            ImGui::Text(std::format("pos={},{},{}", camera->position[0], camera->position[1], camera->position[2]).c_str());

        	ImGui::Text(ICON_FK_ADDRESS_BOOK" TEST FK");
            ImGui::Checkbox("Demo Window", &show_demo_window);      // Edit bools storing our window open/close state
            ImGui::Checkbox("Another Window", &show_another_window);
            ToggleButton("试一试呀", &test);
            ImGui::SliderFloat("float", &f, 0.0f, 1.0f);            // Edit 1 float using a slider from 0.0f to 1.0f
            ImGui::ColorEdit3("clear color", (float*)&clear_color); // Edit 3 floats representing a color

            if (ImGui::Button("Button"))                            // Buttons return true when clicked (most widgets return true when edited/activated)
                counter++;
            ImGui::SameLine();
            ImGui::Text("counter = %d", counter);

            ImGui::Text("Application average %.3f ms/frame (%.1f FPS)", 1000.0f / io.Framerate, io.Framerate);
            ImGui::End();
        }

        //// 3. Show another simple window.
        //if (show_another_window)
        //{
        //    ImGui::Begin("Another Window", &show_another_window);   // Pass a pointer to our bool variable (the window will have a closing button that will clear the bool when clicked)
        //    ImGui::Text("Hello from another window!");
        //    if (ImGui::Button("Close Me"))
        //        show_another_window = false;
        //    ImGui::End();
        //}
        //
        //// 4. freetype test.
        //freetype_test.ShowFontsOptionsWindow();

        // Rendering, even though there could be nothing to draw.
        ImGui::Render();
        ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());

        // Update and Render additional Platform Windows
        // (Platform functions may change the current OpenGL context, so we save/restore it to make it easier to paste this code elsewhere.
        //  For this specific demo app we could also call glfwMakeContextCurrent(window) directly)
        if (io.ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
        {
            GLFWwindow* backup_current_context = glfwGetCurrentContext();
            ImGui::UpdatePlatformWindows();
            ImGui::RenderPlatformWindowsDefault();
            glfwMakeContextCurrent(backup_current_context);
        }

        glfwSwapBuffers(mainWnd);
        // todo: only redraw on mouse/keyboard or definite redraw event, to save system resources.
    }
#ifdef __EMSCRIPTEN__
    EMSCRIPTEN_MAINLOOP_END;
#endif

    // Cleanup
    ImGui_ImplOpenGL3_Shutdown();
    ImGui_ImplGlfw_Shutdown();
    ImGui::DestroyContext();

    glfwDestroyWindow(mainWnd);
    glfwTerminate();

    return 0;
}
