using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CycleGUI.PlatformSpecific.Windows
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;

    public class WindowsTray
    {
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_COMMAND = 0x0111;

        private IntPtr hNotifyWnd, hPopupMenu;
        private Dictionary<int, Action> menuActions = new();

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, NOTIFYICONDATA pnid);

        [DllImport("user32.dll")]
        private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AppendMenu(IntPtr hMenu, MenuFlags uFlags, uint uIDNewItem, string lpNewItem);

        [Flags]
        private enum MenuFlags : uint
        {
            MF_STRING = 0x00000000,
            MF_SEPARATOR = 0x00000800,
            MF_BYCOMMAND = 0x00000000
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public uint uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        public WindowsTray()
        {
        }

        private NOTIFYICONDATA nid;

        public void SetIcon(byte[] iconBytes, string tip)
        {
            int icon_size = 32;
            int offset = LookupIconIdFromDirectoryEx(iconBytes, true, icon_size, icon_size, LR_DEFAULTCOLOR);
            var bmp = iconBytes.Skip(offset).ToArray();
            IntPtr hIcon = CreateIconFromResourceEx(bmp, (uint)bmp.Length,
                true, // Set to true if you're creating an icon; false if creating a cursor.
                0x00030000, // Version must be set to 0x00030000.
                0, // Use 0 for the desired width (system default size).
                0, // Use 0 for the desired height (system default size).
                LR_DEFAULTSIZE// Load icon with default color and size.
            );

            // menuActions = new Dictionary<int, Action>();

            wndClass = new WNDCLASSEX();
            wndClass.cbSize = sizeof_WNDCLASSEX_64;
            wndClass.lpfnWndProc = WndProc;
            wndClass.hInstance = GetModuleHandle(IntPtr.Zero);
            wndClass.lpszClassName = "TrayClass";
            wndClass.hIcon = hIcon;

            uint ret = RegisterClassEx(ref wndClass);
            var name = Process.GetCurrentProcess().ProcessName;
            hNotifyWnd = CreateWindowEx(0, "TrayClass", $"CycleUITray_{name}", 0, 0, 0, 0, 0, (IntPtr)(-3), IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
            var err = Marshal.GetLastWin32Error();
            hPopupMenu = CreatePopupMenu();

            nid = new NOTIFYICONDATA();
            nid.cbSize = Marshal.SizeOf(nid);
            nid.hWnd = hNotifyWnd;
            nid.uID = 0;
            nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            nid.uCallbackMessage = 0x401;
            nid.hIcon = hIcon;
            nid.szTip = tip;
            Shell_NotifyIcon(NIM_ADD, nid);
        }

        public void AddMenuItem(string name, Action action)
        {
            int itemId = menuActions.Count + 1;
            menuActions[itemId] = action;
            AppendMenu(hPopupMenu, MenuFlags.MF_STRING, (uint)itemId, name);
        }

        public void Discard()
        {
            Shell_NotifyIcon(NIM_DELETE, nid);
            DestroyMenu(hPopupMenu);
        }

        private WNDCLASSEX wndClass;
        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == 0x401)
            {
                if ((uint)lParam == WM_LBUTTONDBLCLK)
                {
                    OnDblClick?.Invoke();
                }
                else if ((uint)lParam == WM_RBUTTONUP)
                {
                    POINT point;
                    GetCursorPos(out point);
                    TrackPopupMenu(hPopupMenu, 0x0001, point.X, point.Y, 0, hWnd, IntPtr.Zero);
                }
            }
            else if (msg == WM_COMMAND)
            {
                int menuItemId = (int)wParam;
                if (menuActions.TryGetValue(menuItemId, out Action action))
                {
                    action.Invoke();
                }
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public Action OnDblClick { get; set; }

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_DELETE = 0x00000002;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateIconFromResourceEx(
            byte[] pbIconBits,
            uint cbIconBits,
            bool fIcon,
            uint dwVersion,
            int cxDesired,
            int cyDesired,
            uint uFlags
        );
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int LookupIconIdFromDirectoryEx(
            byte[] presbits,
            bool fIcon,
            int cxIcon,
            int cyIcon,
            uint Flags
        );
        // Constants for the CreateIconFromResourceEx's uFlags parameter.
        private const uint LR_DEFAULTCOLOR = 0x00000000;
        private const uint LR_DEFAULTSIZE = 0x00000040;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(IntPtr lpModuleName);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;                 // The size of the structure (must be set before using the structure).
            public uint style;                  // The window style.
            public WNDPROC lpfnWndProc;          // Pointer to the window procedure (WndProc) function.
            public int cbClsExtra;              // Extra class data (usually set to 0).
            public int cbWndExtra;              // Extra window instance data (usually set to 0).
            public IntPtr hInstance;            // Handle to the application instance.
            public IntPtr hIcon;                // Handle to the class icon.
            public IntPtr hCursor;              // Handle to the class cursor.
            public IntPtr hbrBackground;        // Handle to the class background brush.
            public string lpszMenuName;         // Pointer to a null-terminated string specifying the resource name of the class menu.
            public string lpszClassName;        // Pointer to a null-terminated string specifying the class name.
            public IntPtr hIconSm;              // Handle to a small icon associated with the class.
        }

        // Constants for the wndClass.cbSize field.
        public const int sizeof_WNDCLASSEX = 48; // For 32-bit applications, set this value to 48.
        public const int sizeof_WNDCLASSEX_64 = 80; // For 64-bit applications, set this value to 80.

        public delegate IntPtr WNDPROC(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    }

}
