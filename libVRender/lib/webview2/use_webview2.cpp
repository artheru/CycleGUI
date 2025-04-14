#include <windows.h>
#include <stdlib.h>
#include <string>
#include <tchar.h>
#include <wrl.h>
#include <wil/com.h>
#include "WebView2.h"

#include "../../cycleui.h"

using namespace Microsoft::WRL;

// Global variables

// The main window class name.
static TCHAR szWindowClass[] = _T("WebViewPanel");

// The string that appears in the application's title bar.
//static TCHAR szTitle[] = _T("WebView Panel");

HINSTANCE hInst;

// Forward declarations of functions included in this code module:
LRESULT CALLBACK WndProc(HWND, UINT, WPARAM, LPARAM);

// Pointer to WebViewController
static wil::com_ptr<ICoreWebView2Controller> webviewController;

// Pointer to WebView window
static wil::com_ptr<ICoreWebView2> webview;

static HWND g_hWnd = NULL;

LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam)
{
	switch (message)
	{
	case WM_SIZE:
		if (webviewController != nullptr) {
			RECT bounds;
			GetClientRect(hWnd, &bounds);
			webviewController->put_Bounds(bounds);
		};
		return 0;
	case WM_DESTROY:
		g_hWnd = NULL;
		webviewController = nullptr;
		webview = nullptr;
		//PostQuitMessage(0);
		return 0;
	default:
		return DefWindowProc(hWnd, message, wParam, lParam);
		break;
	}

	return 0;
}

void showWebPanel(const char* url) {
	// Convert UTF-8 URL to UTF-16
	int wideLength = MultiByteToWideChar(CP_UTF8, 0, url, -1, nullptr, 0);
	std::vector<wchar_t> wideUrl(wideLength);
	MultiByteToWideChar(CP_UTF8, 0, url, -1, wideUrl.data(), wideLength);

	if (g_hWnd != NULL) {
		// If window exists, just navigate to new URL
		webview->Navigate(wideUrl.data());
		ShowWindow(g_hWnd, SW_SHOW);
		SetForegroundWindow(g_hWnd);
		return;
	}

	// Create window class
	WNDCLASSEX wcex = {};
	wcex.cbSize = sizeof(WNDCLASSEX);
	wcex.style = CS_HREDRAW | CS_VREDRAW;
	wcex.lpfnWndProc = WndProc;
	wcex.hInstance = GetModuleHandle(NULL);
	wcex.hCursor = LoadCursor(NULL, IDC_ARROW);
	wcex.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
	wcex.lpszClassName = szWindowClass;

	RegisterClassEx(&wcex);

	// Get screen dimensions
	int screenWidth = GetSystemMetrics(SM_CXSCREEN);
	int screenHeight = GetSystemMetrics(SM_CYSCREEN);
	
	// Calculate window size (80% of screen)
	int windowWidth = (int)(screenWidth * 0.8);
	int windowHeight = (int)(screenHeight * 0.8);
	
	// Calculate window position (center of screen)
	int windowX = (screenWidth - windowWidth) / 2;
	int windowY = (screenHeight - windowHeight) / 2;

	// Create window
	g_hWnd = CreateWindow(
		szWindowClass,
		wideUrl.data(),
		WS_OVERLAPPEDWINDOW,
		windowX, windowY, windowWidth, windowHeight,
		NULL, NULL, GetModuleHandle(NULL), NULL
	);

	if (!g_hWnd) return;

	// Create WebView2 environment
	CreateCoreWebView2EnvironmentWithOptions(nullptr, nullptr, nullptr,
		Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
			[wideUrl](HRESULT result, ICoreWebView2Environment* env) -> HRESULT {
				env->CreateCoreWebView2Controller(g_hWnd,
					Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
						[wideUrl](HRESULT result, ICoreWebView2Controller* controller) -> HRESULT {
							webviewController = controller;
							webviewController->get_CoreWebView2(&webview);
							
							// Set window size and position
							RECT bounds;
							GetClientRect(g_hWnd, &bounds);
							webviewController->put_Bounds(bounds);

							// Navigate to URL
							webview->Navigate(wideUrl.data());
							return S_OK;
						}).Get());
				return S_OK;
			}).Get());

	ShowWindow(g_hWnd, SW_SHOW);
	UpdateWindow(g_hWnd);
}