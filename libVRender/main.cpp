#ifdef _MSC_VER
#define _CRT_SECURE_NO_WARNINGS
#endif

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

#include <implot_internal.h>
#include <set>

#include "cycleui.h"
#include "ImGuizmo.h"
#include "messyengine.h"

#ifdef _WIN32 // For Windows
#define LIBVRENDER_EXPORT __declspec(dllexport)
#else // For Linux and other platforms
#define LIBVRENDER_EXPORT
#endif

std::map<std::string, uint8_t*> buffers;
extern "C" LIBVRENDER_EXPORT void* RegisterStreamingBuffer(const char* name, int length)
{
    return buffers[std::string(name)] = new uint8_t[length];
}
uint8_t* GetStreamingBuffer(std::string name, int length)
{
    return buffers[name];
}

//Display File handler
typedef void(*NotifyDisplay)(const char* filehash, int pid);
NotifyDisplay del_notify_display;
extern "C" LIBVRENDER_EXPORT void RegisterExternDisplayCB(NotifyDisplay handler)
{
    del_notify_display = handler;
}

void ExternDisplay(const char* filehash, int pid, const char* fname) //fname unused.
{
    del_notify_display(filehash, pid);
}


float fscale = 1;
static void glfw_error_callback(int error, const char* description)
{
    fprintf(stderr, "GLFW Error %d: %s\n", error, description);
}


uint8_t appIco[256 * 256 * 4];
int appIcoSz=0;

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

extern "C" { //used for imgui_freetype.cpp patch.
	uint8_t* fallback_text_render(uint32_t codepoint)
	{
        if (codepoint == 0x2b00 && appIcoSz>0)
        {
            ui_state.app_icon.height = ui_state.app_icon.width = 18.0f * fscale;
		    ui_state.app_icon.advanceX = ui_state.app_icon.width + 2;
            ui_state.app_icon.offsetY = -ui_state.app_icon.width *0.85;
            downsample(appIco, appIcoSz, ui_state.app_icon.height, ui_state.app_icon.rgba);
            return (uint8_t*) &ui_state.app_icon;
        }
        return nullptr;
	}
}

void LoadFonts(float scale = 1)
{
    fscale = scale;
    ImGuiIO& io = ImGui::GetIO();

    // ASCII
    ImFont* fontmain = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\CascadiaMono.ttf", 15.0f * scale);

    // Chinese
    static ImFontConfig cfg;
    cfg.MergeMode = true;
    ImFont* font = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\msyh.ttc", 16.0f * scale, &cfg, io.Fonts->GetGlyphRangesChineseFull());
    if (font == NULL)
        font = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\msyh.ttf", 16.0f * scale, &cfg, io.Fonts->GetGlyphRangesChineseFull()); // Windows 7

    static ImWchar ranges3[] = { ICON_MIN_FK, ICON_MAX_FK, 0 };
    static ImFontConfig cfg3;
    cfg3.OversampleH = cfg3.OversampleV = 1;
    cfg3.MergeMode = true;
    //cfg3.FontBuilderFlags |= ImGuiFreeTypeBuilderFlags_LoadColor;
    cfg3.GlyphOffset = ImVec2(0, 2 * scale);
    const int forkawesome_len = 219004;
    void* data = IM_ALLOC(forkawesome_len);
    memcpy(data, forkawesome, forkawesome_len);
    io.Fonts->AddFontFromMemoryTTF(data, forkawesome_len, 16.0f * scale, &cfg3, ranges3);

    // emojis
    static ImWchar ranges2[] = {0x2b00, 0x2b00, 0x1, 0x1FFFF, 0 };
    static ImFontConfig cfg2;
    cfg2.OversampleH = cfg2.OversampleV = 1;
    cfg2.MergeMode = true;
    cfg2.FontBuilderFlags |= ImGuiFreeTypeBuilderFlags_LoadColor;
    cfg2.GlyphOffset = ImVec2(0, 2 * scale);
    io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\seguiemj.ttf", 16.0f*scale, &cfg2, ranges2);

}


// high dpi:
float g_dpiScale = 1.0f;
bool shouldSetFont = false;


bool ScaleUI(float scale)
{
    ImPlotContext& gp = *GImPlot;
    gp.Style.Colormap = ImPlotColormap_Jet;

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
}


LRESULT CALLBACK WindowProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
	static std::unordered_map<UINT32, touch_state> current_touches;
	switch (uMsg) {

	case WM_DPICHANGED:
		HandleDpiChange(hWnd, wParam, lParam);
		break;

	case WM_POINTERDOWN:
	case WM_POINTERUP:
	case WM_POINTERUPDATE:
	{
        UINT32 pointerId = GET_POINTERID_WPARAM(wParam);
        POINTER_INFO pointerInfo;

        if (GetPointerInfo(pointerId, &pointerInfo) && pointerInfo.pointerType == PT_TOUCH) {
            std::vector<touch_state> touches;
            std::stringstream ss;

            if (uMsg == WM_POINTERUP) {
                current_touches.erase(pointerId);
            } else {
                touch_state ts; // starting is for callback to fill in.
                ts.id = pointerInfo.pointerId;
                ts.touchX = pointerInfo.ptPixelLocation.x;
                ts.touchY = pointerInfo.ptPixelLocation.y;
                current_touches[pointerId] = ts;
            }

            ss << uMsg << "touch[" << current_touches.size() << "]=";
            for (const auto& [id, ts] : current_touches) {
                touches.push_back(ts);
                ss << ts.id << ":" << ts.touchX << "," << ts.touchY << " ";
            }
            // printf("%s\n", ss.str().c_str());
            touch_callback(touches);
        }
        return DefWindowProc(hWnd, uMsg, wParam, lParam);
	}

    case WM_LBUTTONDOWN:
    case WM_RBUTTONDOWN:
    case WM_MBUTTONDOWN:
    case WM_XBUTTONDOWN:
    case WM_LBUTTONUP:
    case WM_RBUTTONUP:
    case WM_MBUTTONUP:
    case WM_XBUTTONUP:
	case WM_MOUSEWHEEL:
	case WM_MOUSEMOVE:
        if (current_touches.size() > 0) return 0; // touch priority higher than mouse.
	}
    return CallWindowProc(reinterpret_cast<WNDPROC>(glfwGetWindowUserPointer(mainWnd)), hWnd, uMsg, wParam, lParam);
}


#endif

void MainWindowPreventCloseCallback(GLFWwindow* window) {
    glfwHideWindow(window);  // Hide the window instead of destroying it
}

std::vector<std::string> split(const std::string& str, char delimiter);
bool parse_chord_global(const std::string& key) {
    static std::unordered_map<std::string, int> keyMap = {
        {"space", VK_SPACE},
        {"left", VK_LEFT},
        {"right", VK_RIGHT},
        {"up", VK_UP},
        {"down", VK_DOWN},
        {"backspace", VK_BACK},
        {"del", VK_DELETE},
        {"ins", VK_INSERT},
        {"enter", VK_RETURN},
        {"tab", VK_TAB},
        {"esc", VK_ESCAPE},
        {"pgup", VK_PRIOR},
        {"pgdn", VK_NEXT},
        {"home", VK_HOME},
        {"end", VK_END},
        {"pause", VK_PAUSE},
        {"f1", VK_F1},
        {"f2", VK_F2},
        {"f3", VK_F3},
        {"f4", VK_F4},
        {"f5", VK_F5},
        {"f6", VK_F6},
        {"f7", VK_F7},
        {"f8", VK_F8},
        {"f9", VK_F9},
        {"f10", VK_F10},
        {"f11", VK_F11},
        {"f12", VK_F12},
    };

    std::vector<std::string> parts = split(key, '+');
    bool ctrl = false, alt = false, shift = false;
    int mainkey = -1;

    for (const std::string& p : parts) {
        if (p == "ctrl") ctrl = true;
        else if (p == "alt") alt = true;
        else if (p == "shift") shift = true;
        else if (keyMap.find(p) != keyMap.end()) mainkey = keyMap[p];
        else if (p.length() == 1) mainkey = toupper(p[0]);
    }

    bool ctrl_pressed = !ctrl || (ctrl && (GetAsyncKeyState(VK_LCONTROL) & 0x8000 || GetAsyncKeyState(VK_RCONTROL) & 0x8000));
    bool alt_pressed = !alt || (alt && (GetAsyncKeyState(VK_LMENU) & 0x8000 || GetAsyncKeyState(VK_RMENU) & 0x8000));
    bool shift_pressed = !shift || (shift && (GetAsyncKeyState(VK_LSHIFT) & 0x8000 || GetAsyncKeyState(VK_RSHIFT) & 0x8000));
    bool mainkey_pressed = mainkey != -1 && (GetAsyncKeyState(mainkey) & 0x8000);

    return (ctrl_pressed && alt_pressed && shift_pressed && mainkey_pressed);
}

int main();


extern "C" LIBVRENDER_EXPORT void SetUIStack(unsigned char* bytes, int length)
{
    cgui_stack = bytes;
	cgui_refreshed = true;
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
    realtimeUICallback = callback; // local terminal use the same callback.
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
    auto CreateAndSetIcon = [](HWND hwnd, unsigned char* bytes, int length, bool isBigIcon) {
        int desiredSize = isBigIcon ? 256 : 32; // 256 for big icon, 32 for small icon
        HICON hicon = NULL;

        // ICONDIR structure
        typedef struct
        {
            WORD idReserved;   // Reserved (must be 0)
            WORD idType;       // Resource type (1 for icons)
            WORD idCount;      // Number of images
        } ICONDIR;

        // ICONDIRENTRY structure
        typedef struct
        {
            BYTE bWidth;       // Width of the image
            BYTE bHeight;      // Height of the image
            BYTE bColorCount;  // Number of colors in the color palette
            BYTE bReserved;    // Reserved (must be 0)
            WORD wPlanes;      // Color Planes
            WORD wBitCount;    // Bits per pixel
            DWORD dwBytesInRes; // Size of the image data
            DWORD dwImageOffset; // Offset of the image data from the beginning of the resource
        } ICONDIRENTRY;

        ICONDIR* iconDir = (ICONDIR*)bytes;
        if (iconDir->idType != 1 || iconDir->idReserved != 0 || iconDir->idCount == 0) {
            // Invalid icon resource
            return hicon;
        }

        ICONDIRENTRY* iconEntries = (ICONDIRENTRY*)(bytes + sizeof(ICONDIR));
        for (int i = 0; i < iconDir->idCount; ++i) {
            ICONDIRENTRY& entry = iconEntries[i];
            int entrySize = entry.dwBytesInRes;
            int entryOffset = entry.dwImageOffset;

            // Load the best matching icon size
            if ((entry.bWidth == desiredSize || entry.bHeight == desiredSize) || 
                (isBigIcon && entry.bWidth >= 48 && entry.bHeight >= 48)) { // Handle scaling
                hicon = CreateIconFromResourceEx(
                    bytes + entryOffset,
                    entrySize,
                    TRUE, // Creating an icon
                    0x00030000, // Version must be set to 0x00030000
                    entry.bWidth, // Use specific size for width
                    entry.bHeight, // Use specific size for height
                    LR_DEFAULTCOLOR | LR_DEFAULTSIZE // Load icon with default color and size
                );

                if (hicon) {
                    SendMessage(hwnd, WM_SETICON, isBigIcon ? ICON_BIG : ICON_SMALL, (LPARAM)hicon);
                    break;
                }
            }
        }

        if (!hicon) {
            // Fallback: Load the first icon in the resource
            ICONDIRENTRY& entry = iconEntries[0];
            hicon = CreateIconFromResourceEx(
                bytes + entry.dwImageOffset,
                entry.dwBytesInRes,
                TRUE, // Creating an icon
                0x00030000, // Version must be set to 0x00030000
                entry.bWidth, // Use specific size for width
                entry.bHeight, // Use specific size for height
                LR_DEFAULTCOLOR | LR_DEFAULTSIZE // Load icon with default color and size
            );

            if (hicon) {
                SendMessage(hwnd, WM_SETICON, isBigIcon ? ICON_BIG : ICON_SMALL, (LPARAM)hicon);
            } else {
                // Handle error (optional)
                // MessageBox(hwnd, "Failed to create icon", "Error", MB_ICONERROR);
            }
        }

        return hicon;
    };

    auto hwnd = glfwGetWin32Window(mainWnd);
    if (hwnd) {
        HICON hiconSmall = CreateAndSetIcon(hwnd, bytes, length, false);
        HICON hiconBig = CreateAndSetIcon(hwnd, bytes, length, true);

        // Cleanup icons when they are no longer needed (e.g., on application close)
        // DestroyIcon(hiconSmall);
        // DestroyIcon(hiconBig);
    }
#endif
}


extern "C" LIBVRENDER_EXPORT void SetAppIcon(unsigned char* rgba, int sz)
{
    appIcoSz = sz;
    for (int i=0; i<sz*sz*4; ++i)
		appIco[i] = rgba[i];
}


std::string windowTitle = "CycleUI Workspace - Compile on " __DATE__ " " __TIME__;

extern "C" LIBVRENDER_EXPORT void SetWndTitle(char* title)
{
    windowTitle = std::string(title);
    appName = (char*)windowTitle.c_str();
}

#include "lib/nfd/nfd.h"

// Main code
std::string preparedString("/na");
std::string staticString(""); // Static string to append text

// #define TOC(X) \
//     span = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic).count(); \
//     staticString += "\nmtic " + std::string(X) + "=" + std::to_string(span * 0.001) + "ms, total=" + std::to_string(((float)std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic_st).count()) * 0.001) + "ms"; \
//     tic = std::chrono::high_resolution_clock::now();
#define TOC(X) ;

void draw()
{
    auto tic = std::chrono::high_resolution_clock::now();
    auto tic_st = tic;
    int span;

    int isVisible = glfwGetWindowAttrib(mainWnd, GLFW_VISIBLE);

    int display_w, display_h;
    glfwGetFramebufferSize(mainWnd, &display_w, &display_h);

    glfwSetWindowTitle(mainWnd, windowTitle.c_str());

    // glViewport(0, 0, display_w, display_h);
    glScissor(0, 0, display_w, display_h);
    // glClearColor(clear_color.x * clear_color.w, clear_color.y * clear_color.w, clear_color.z * clear_color.w, clear_color.w);
    glClear(GL_COLOR_BUFFER_BIT);


    if (shouldSetFont)
        ScaleUI(g_dpiScale);

    ImGuizmo::SetOrthographic(camera->ProjectionMode);


    // Start the Dear ImGui frame
    ImGui_ImplOpenGL3_NewFrame();
    ImGui_ImplGlfw_NewFrame();

    if (ImGui::GetPlatformIO().Monitors.Size == 0) goto skip;
    ImGui::NewFrame();
    ImGuizmo::BeginFrame();

    // ImGui::Text("tic=%f,%f,%f", toc1, toc2, toc3);
    // auto tic=std::chrono::high_resolution_clock::now();

    TOC("prepare_main");
    ProcessUIStack();
    TOC("ui");

    // toc1 = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic).count();
    camera->dpi = ImGui::GetMainViewport()->DpiScale;

    if (isVisible && display_h > 0 && display_w > 0)
        DrawWorkspace(display_w, display_h);
    else
        ProcessBackgroundWorkspace();
    TOC("drawWS");
    // toc2 = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic).count();




    // static bool show_demo_window = true;
    // if (show_demo_window)
    //     ImGui::ShowDemoWindow(&show_demo_window);
    //
    // static bool show_plot_demo_window = true;
    // if (show_plot_demo_window)
    //     ImPlot::ShowDemoWindow(&show_plot_demo_window);
    // ImGui::Text("🖐This is some useful text.以及汉字, I1l, 0Oo");
    // ImGui::Text(ICON_FK_ADDRESS_BOOK" TEST FK");

// ImGui::Text(preparedString.c_str());
    // Rendering, even though there could be nothing to draw.
    ImGui::Render();
    TOC("imgui");
    ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());
    TOC("imgui-ow");

    if (ImGui::GetIO().ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
    {
        GLFWwindow* backup_current_context = glfwGetCurrentContext();
        ImGui::UpdatePlatformWindows();
        ImGui::RenderPlatformWindowsDefault();
        glfwMakeContextCurrent(backup_current_context);
    }
    TOC("imgui_fin");
    // toc3 = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic).count();
    //glFinish();
skip:
    glfwSwapBuffers(mainWnd);

    TOC("fin_loop");
    preparedString = staticString;
    staticString = "--MAIN--\n";
    // todo: only redraw on mouse/keyboard or definite redraw event, to save system resources.
}

void move_resize_callback(GLFWwindow* window, int width, int height)
{
    glViewport(0, 0, width, height);
    draw();
}

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
    printf("%s\n", windowTitle.c_str());
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
    glfwSetWindowPosCallback(mainWnd, move_resize_callback);
    glfwSetFramebufferSizeCallback(mainWnd, move_resize_callback);

    // Setup Platform/Renderer backends
    ImGui_ImplGlfw_InitForOpenGL(mainWnd, true);
    ImGui_ImplOpenGL3_Init(glsl_version);

    
    // Warning: the validity of monitor DPI information on Windows depends on the application DPI awareness settings, which generally needs to be set in the manifest or at runtime.
    

#ifdef _WIN32
    auto hwnd = glfwGetWin32Window(mainWnd);
 //    seems not working.// no gestures at all.
	GESTURECONFIG gc[] = {0,0,GC_ALLGESTURES};
	UINT uiGcs = 1;
	BOOL bResult = SetGestureConfig(hwnd, 0, uiGcs, gc, sizeof(GESTURECONFIG));  

    nfd_owner = hwnd;
    glfwSetWindowUserPointer(mainWnd, reinterpret_cast<void*>(GetWindowLongPtr(hwnd, GWLP_WNDPROC)));
    SetWindowLongPtr(hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(WindowProc));
    //RegisterPointerInputTarget(hwnd, PT_TOUCH);


    int dpiX = GetDpiForWindow(hwnd);
#else
    float x_scale, y_scale;
    glfwGetMonitorContentScale(glfwGetPrimaryMonitor(), &x_scale, &y_scale);
    int dpiX = x_scale;
#endif
    
    std::cout << dpiX << std::endl;
    ScaleUI(static_cast<float>(dpiX) / static_cast<float>(96)); // default dpi=96.

    InitGL(initW, initH);

    // double toc1=0,toc2=0,toc3=0;
    while (true)
    {
        glfwPollEvents();
        draw();
    }
}
