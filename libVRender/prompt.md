first write a cpp project named libVRender in visual studio. do the following:
1. reference imgui, use docking branch for multi-viewport, use font-awesome icons, auto adapt high-dpi screen, compatible with utf8 characters, colorful emojis, allow imes for chinese input.
2. export a function named "startup" to launch the imgui_demo. and a few example windows.
then also write a c sharp project to use the libVRender:
1. a .net 6 winform app, it doesn't show window instead an icon in the notification area.
2. auto call startup in libVRender.
3. double click the notification icon to bringup or re-open imgui_demo window.

#Install
install vcpkg
vcpkg install freetype glm glew