#include "cycleui.h"
#include <array>
#include <stdio.h>
#include <functional>
#include <imgui_internal.h>
#include <iomanip>
#include <iostream>
#include <map>
#include <set>
#include <string>
#include <variant>
#include <vector>

#include "imgui.h"
#include "implot.h"
#include "ImGuizmo.h"
#include "utilities.h"

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif

namespace ImPlot
{
	struct ScrollingBuffer;
}

unsigned char* cgui_stack = nullptr;
bool cgui_refreshed = false;
char* appName = (char*)"Untitled App";
char* appStat = (char*)"";
int fuck_dbg = 0;

NotifyStateChangedFunc stateCallback;
NotifyWorkspaceChangedFunc global_workspaceCallback;
RealtimeUIFunc realtimeUICallback;
BeforeDrawFunc beforeDraw;

std::map<int, std::vector<unsigned char>> map;
std::vector<unsigned char> v_stack;
std::map<int, point_cloud> pcs;

#define ReadInt *((int*)ptr); ptr += 4
#define ReadStringLen *((int*)ptr)-1;
#define ReadString (char*)(ptr + 4); ptr += *((int*)ptr) + 4
#define ReadStdString std::string(ptr + 4, ptr + 4 + *((int*)ptr)); ptr += *((int*)ptr) + 4
#define ReadBool *((bool*)ptr); ptr += 1
#define ReadByte *((unsigned char*)ptr); ptr += 1
#define ReadFloat *((float*)ptr); ptr += 4
#define ReadArr(type, len) (type*)ptr; ptr += len * sizeof(type);
template<typename T> void Read(T& what, unsigned char*& ptr) { what = *(T*)(ptr); ptr += sizeof(T); }

#define WriteInt32(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=1; pr+=4; *(int*)pr=x; pr+=4;}
#define WriteFloat(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=2; pr+=4; *(float*)pr=x; pr+=4;}
#define WriteDouble(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=3; pr+=4; *(double*)pr=x; pr+=8;}
#define WriteBytes(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=4; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteString(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=5; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteBool(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=6; pr+=4; *(bool*)pr=x; pr+=1;}
#define WriteFloat2(x,y) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=7; pr+=4; *(float*)pr=x; pr+=4;*(float*)pr=y;pr+=4;}

void ActualWorkspaceQueueProcessor(void* wsqueue, viewport_state_t& vstate)
{
	// process workspace:
	auto* ptr = (unsigned char*)wsqueue;
	auto* wstate = &vstate.workspace_state.back();

	int apiN = 0;
	if (&vstate == &ui.viewports[0])
		NotifyWorkspaceUpdated();

	std::function<void()> UIFuns[] = {
		[&] { //0
			auto name = ReadString;
			point_cloud pc;
			pc.isVolatile = ReadBool;
			pc.capacity = ReadInt;
			pc.initN = ReadInt;
			pc.x_y_z_Sz = ReadArr(glm::vec4, pc.initN);
			pc.color = ReadArr(uint32_t, pc.initN);
			pc.position[0] = ReadFloat; //this marco cannot be used as function, it actually consists of several statements(manipulating ptr).
			pc.position[1] = ReadFloat;
			pc.position[2] = ReadFloat;
			pc.quaternion[0] = ReadFloat;
			pc.quaternion[1] = ReadFloat;
			pc.quaternion[2] = ReadFloat;
			pc.quaternion[3] = ReadFloat;
			pc.handleStr = ReadString;

			AddPointCloud(name, pc);
		},
		[&]
		{  //1
			auto name = ReadString;
			auto len = ReadInt;
			//std::cout << "vpnts=" << len << std::endl;
			auto xyzSz = ReadArr(glm::vec4, len);
			auto color = ReadArr(uint32_t, len);

			AppendVolatilePoints(name, len, xyzSz, color);
		},
		[&]
		{  //2
			auto name = ReadString;

			ClearVolatilePoints(name);
		},
		[&]
		{  //3
			auto cls_name = ReadString;
			auto length = ReadInt;
			auto bytes = ReadArr(unsigned char, length); 
			ModelDetail detail;
			detail.center.x = ReadFloat;
			detail.center.y = ReadFloat;
			detail.center.z = ReadFloat;
			detail.rotate.x = ReadFloat;
			detail.rotate.y = ReadFloat;
			detail.rotate.z = ReadFloat;
			detail.rotate.w = ReadFloat;
			detail.scale = ReadFloat;
			detail.color_bias.x = ReadFloat;
			detail.color_bias.y = ReadFloat;
			detail.color_bias.z = ReadFloat;
			detail.contrast = ReadFloat;
			detail.brightness = ReadFloat;
			detail.force_dbl_face = ReadBool;
			detail.normal_shading = ReadFloat;

			LoadModel(cls_name, bytes, length, detail);
		},
		[&]
		{  //4
			auto cls_name = ReadString;
			auto name = ReadString;
			glm::vec3 new_position;
			new_position.x = ReadFloat;
			new_position.y = ReadFloat;
			new_position.z = ReadFloat;
			glm::quat new_quaternion;
			new_quaternion.x = ReadFloat;
			new_quaternion.y = ReadFloat;
			new_quaternion.z = ReadFloat;
			new_quaternion.w = ReadFloat;
		
			PutModelObject(cls_name, name, new_position, new_quaternion);
		},
		[&]
		{  //5 TransformObject.
			auto name = ReadString;
			uint8_t type = ReadByte;
			uint8_t coord = ReadByte;

			glm::vec3 new_position;
			new_position.x = ReadFloat;
			new_position.y = ReadFloat;
			new_position.z = ReadFloat;
			glm::quat new_quaternion;
			new_quaternion.x = ReadFloat;
			new_quaternion.y = ReadFloat;
			new_quaternion.z = ReadFloat;
			new_quaternion.w = ReadFloat;
			auto time = ReadInt;
			
			MoveObject(name, new_position, new_quaternion, time, type, coord);
		},

		[&]
		{  //6
			auto name = ReadString;
			auto selectable = ReadBool;

			SetObjectSelectable(name, selectable);  
		},

		[&]
		{
			//7 : Set Selection.
			ClearSelection();
			auto len = ReadInt;
			for (int i = 0; i < len; ++i)
			{
				auto str = ReadString;
				SetObjectSelected(str);
			}
		},
		[&]
		{
			//8: generate a new stack to select.
			auto id = ReadInt;
			auto str = ReadString;
			BeginWorkspace<select_operation>(id, str, vstate); // default is select.
			
			wstate = &vstate.workspace_state.back();
			((select_operation*)wstate->operation)->painter_data.resize(vstate.disp_area.Size.x * vstate.disp_area.Size.y / 16, 0);
		},
		[&]
		{
			//9 : end operation
			auto str = ReadString;
			auto id = ReadInt;

			auto& ostate = vstate.workspace_state.back();
			if (ostate.name == str && ostate.id==id){
				vstate.pop_workspace_state();
				wstate = &vstate.workspace_state.back();
			}else
			{
				printf("invalid state pop, expected to pop %s(%d), actual api pops %s(%d)", ostate.name.c_str(), ostate.id, str, id);
			}
		},
		[&]
		{
			//10: Guizmo MoveXYZ/RotateXYZ.
			auto id = ReadInt;
			auto str = ReadString;

			if (guizmo_operation* pos_op = dynamic_cast<guizmo_operation*>(wstate->operation); pos_op != nullptr) {
				printf("Problematic guizmo action, current guizmo action is not done yet.");
			}

			BeginWorkspace<guizmo_operation>(id, str, vstate);
			wstate = &vstate.workspace_state.back();
			auto op = (guizmo_operation*)wstate->operation;

			auto realtime = ReadBool;
			auto type = ReadInt; 
			if (type == 0)
				op->mode = gizmo_moveXYZ;
			else if (type == 1)
				op->mode = gizmo_rotateXYZ;

			op->realtime = realtime;
			if (!op->selected_get_center())
			{
				wstate->feedback = operation_canceled;
			}

			// auto nsnaps = ReadInt;
			// for (int i = 0; i < nsnaps; ++i) {
			// 	auto snap = ReadString;
			// 	((guizmo_operation*)wstate->operation)->snaps.push_back(snap);
			// }
		},
		[&]
		{
			// 11： SetAppearance.
			auto useEDL_set = ReadBool;
			if (useEDL_set) {wstate->useEDL = ReadBool;}
			
			auto useSSAO_set = ReadBool;
			if (useSSAO_set) {wstate->useSSAO = ReadBool;}
			
			auto useGround_set = ReadBool;
			if (useGround_set) {wstate->useGround = ReadBool;}
			
			auto useBorder_set = ReadBool;
			if (useBorder_set) {wstate->useBorder = ReadBool;}
			
			auto useBloom_set = ReadBool;
			if (useBloom_set) {wstate->useBloom = ReadBool;}
			
			auto drawGrid_set = ReadBool;
			if (drawGrid_set) {wstate->drawGrid = ReadBool;}
			
			auto drawGuizmo_set = ReadBool;
			if (drawGuizmo_set) {wstate->drawGuizmo = ReadBool;}
			
			auto hover_shine_set = ReadBool;
			if (hover_shine_set) {
				int colorTmp = ReadInt;
				wstate->hover_shine = convertToVec4(colorTmp);
			}
			
			auto selected_shine_set = ReadBool;
			if (selected_shine_set) {
				int colorTmp = ReadInt;
				wstate->selected_shine = convertToVec4(colorTmp);
			}
			
			auto hover_border_color_set = ReadBool;
			if (hover_border_color_set) {
				int colorTmp = ReadInt;
				wstate->hover_border_color = convertToVec4(colorTmp);
			}
			
			auto selected_border_color_set = ReadBool;
			if (selected_border_color_set) {
				int colorTmp = ReadInt;
				wstate->selected_border_color = convertToVec4(colorTmp);
			}
			
			auto world_border_color_set = ReadBool;
			if (world_border_color_set) {
				int colorTmp = ReadInt;
				wstate->world_border_color = convertToVec4(colorTmp);
			}

			// New clipping planes code
            auto clippingPlanes_set = ReadBool;
            if (clippingPlanes_set) {
                auto numPlanes = ReadInt;
                wstate->activeClippingPlanes = numPlanes;
                for (int i = 0; i < numPlanes; i++) {
                    wstate->clippingPlanes[i].center.x = ReadFloat;
                    wstate->clippingPlanes[i].center.y = ReadFloat;
                    wstate->clippingPlanes[i].center.z = ReadFloat;
                    wstate->clippingPlanes[i].direction.x = ReadFloat;
                    wstate->clippingPlanes[i].direction.y = ReadFloat;
                    wstate->clippingPlanes[i].direction.z = ReadFloat;
                }
            }

			auto btf_on_hovering_set = ReadBool;
			if (btf_on_hovering_set) { wstate->btf_on_hovering = ReadBool; }

			auto sun_altitude_set = ReadBool;
			if (sun_altitude_set) {
				vstate.sun_altitude = ReadFloat;
			}
		},
		[&]
		{
			// 12: Draw spot text (UI texts)
			auto name = ReadString;
			auto len = ReadInt;
			// std::cout << "draw spot texts" << len << " on " << name << std::endl;

			ptr = AppendSpotTexts(name, len, ptr);
		},
		[&]
		{
			// 13: Clear spot text.
			auto name = ReadString;
			ClearSpotTexts(name);
		},
		[&]
		{
			//14: Set Camera view.
			vstate.camera.extset = true;
			auto lookAt_set = ReadBool;
			if (lookAt_set) {
				glm::vec3 lookAt;
				{
					lookAt.x = ReadFloat;
					lookAt.y = ReadFloat;
					lookAt.z = ReadFloat;
				}
				vstate.camera.stare = lookAt;
			}
			
			auto azimuth_set = ReadBool;
			if (azimuth_set) {
				vstate.camera.Azimuth = ReadFloat;
			}
			
			auto altitude_set = ReadBool;
			if (altitude_set) {
				vstate.camera.Altitude = ReadFloat;
			}
			
			auto distance_set = ReadBool;
			if (distance_set) {
				vstate.camera.distance = ReadFloat;
			}
			
			auto fov_set = ReadBool;
			if (fov_set) {
				vstate.camera._fov = ReadFloat;
			}
			
			if (lookAt_set || azimuth_set || altitude_set || distance_set) {
				vstate.camera.UpdatePosition();
			}

			auto displayMode_set = ReadBool;
			if (displayMode_set) {
				auto mode = ReadInt;
				switch (mode) {
					case 0: // Normal
						vstate.displayMode = viewport_state_t::DisplayMode::Normal;
						break;
				case 1: // VR
						vstate.displayMode = viewport_state_t::DisplayMode::VR;
						break;
					case 2: // Holography
						vstate.displayMode = viewport_state_t::DisplayMode::EyeTrackedHolography;
						break;
				}
			}
			
			auto world2phy_set = ReadBool;
			if (world2phy_set) {
				grating_params.world2phy = ReadFloat;
			}
			
			auto azimuth_range_set = ReadBool;
			if (azimuth_range_set) {
				vstate.camera.azimuth_range.x = ReadFloat;
				vstate.camera.azimuth_range.y = ReadFloat;
			}
			
			auto altitude_range_set = ReadBool;
			if (altitude_range_set) {
				vstate.camera.altitude_range.x = ReadFloat;
				vstate.camera.altitude_range.y = ReadFloat;
			}
			
			auto xyz_range_set = ReadBool;
			if (xyz_range_set) {
				vstate.camera.UpdatePosition();
				auto x = ReadFloat;
				auto y = ReadFloat;
				auto z = ReadFloat;
				vstate.camera.x_range.x = vstate.camera.position.x - x;
				vstate.camera.x_range.y = vstate.camera.position.x + x;
				vstate.camera.y_range.x = vstate.camera.position.y - y;
				vstate.camera.y_range.y = vstate.camera.position.y + y;
				vstate.camera.z_range.x = vstate.camera.position.z - z;
				vstate.camera.z_range.y = vstate.camera.position.z + z;
			}
			
			auto mmb_freelook_set = ReadBool;
			if (mmb_freelook_set) {
				vstate.camera.mmb_freelook = ReadBool;
			}
		},
		[&]
		{	//15: SetViewCrossSection
			auto namePattern = ReadString;
			auto selectable = ReadBool;

			SetApplyCrossSection(namePattern, selectable);
		},
		[&]
		{  //16: SetSubSelectable.
			auto namePattern = ReadString;
			auto selectable = ReadBool;

			SetObjectSubSelectable(namePattern, selectable);
		},
		[&] { //17：Add Line bunch for temporary draw.
			auto name = ReadString;
			auto len = ReadInt;
			// std::cout << "draw spot texts" << len << " on " << name << std::endl;

			ptr = AppendLines2Bunch(name, len, ptr);
		},
		[&] {
			//18：RemoveNamePattern
			// batch remove object.
			auto name = ReadString;
			RemoveNamePattern(name);
		},
		[&]
		{  //19:  Clear temp lines text.
			auto name = ReadString;
			ClearLineBunch(name);
		},
		[&]
		{  //20: put image
			auto name = ReadString;
			
			auto displayType = ReadByte;
			auto displayH = ReadFloat;
			auto displayW = ReadFloat;
			glm::vec3 new_position;
			new_position.x = ReadFloat;
			new_position.y = ReadFloat;
			new_position.z = ReadFloat;
			glm::quat new_quaternion;
			new_quaternion.x = ReadFloat;
			new_quaternion.y = ReadFloat;
			new_quaternion.z = ReadFloat;
			new_quaternion.w = ReadFloat;
			auto rgbaName = ReadString; //also consider special names: `svg:tiger`

			AddImage(name, displayType << 6, glm::vec2(displayW, displayH), new_position, new_quaternion, rgbaName);
		},
		[&]
		{
			//21: Add line.
			auto name = ReadString;
			auto propstart = ReadString;
			auto propend = ReadString;
			glm::vec3 start;
			start.x = ReadFloat;
			start.y = ReadFloat;
			start.z = ReadFloat;
			glm::vec3 end;
			end.x = ReadFloat;
			end.y = ReadFloat;
			end.z = ReadFloat;
			auto meta = ReadArr(unsigned char, 4);
			glm::uint color = ReadInt;

			auto lineType = ReadInt;
			if (lineType == 0)
			{
				// straight line.
				AddStraightLine(name, {name, propstart, propend, start, end, meta[0], meta[1], meta[2], color});
			} else if (lineType==1)
			{
				// beziercurve.
				auto additionalControlPnts = ReadInt;
				auto v3ptr = ReadArr(glm::vec3, additionalControlPnts);
				
				// Create a vector of control points NOT including start and end points
				std::vector<glm::vec3> controlPoints;
				for (int i = 0; i < additionalControlPnts; i++) {
					controlPoints.push_back(v3ptr[i]);
				}
				
				AddBezierCurve(name, { name, propstart, propend, start, end, meta[0], meta[1], meta[2], color }, controlPoints);

			} else if (lineType==2)
			{
				// vector.
				auto px_len = ReadInt;
				meta[0] = px_len;
				if (meta[0] < 2) meta[0] = 10;
				AddStraightLine(name, { name, propstart, propend, start, end, meta[0], meta[1], meta[2], color });
			}
		},
		[&]
		{  //22: PutRGBA //rgba can also become 9patch.
			auto name = ReadString;
			auto width = ReadInt;
			auto height = ReadInt;
			auto type = ReadInt;

			PutRGBA(name, width, height, type);
		},
		[&]
		{  //23: Update RGBA (internal use)
			auto name = ReadString;
			auto len = ReadInt;
			auto rgba = ReadArr(char, len);

			UpdateRGBA(name, len, rgba);
		},
		[&]
		{
			//24: Stream RGBA
			auto name = ReadString;
			// set the target RGBA into streaming mode.
			SetRGBAStreaming(name);
		},
		[&]
		{
			//25: invalidate RGBA(internal use)
			auto name = ReadString;
			InvalidateRGBA(name);
		}, 
		[&]
		{
			//26: prop gesture interactions.
			auto id = ReadInt;
			auto str = ReadString;
			BeginWorkspace<gesture_operation>(id, str, vstate);
			wstate = &vstate.workspace_state.back();

			// gesture operation preparation:
			ClearSelection();
		},
		[&]
		{
			//27: add button type widget.
			auto name = ReadString;
			auto text = ReadString;
			auto pos = ReadString;
			auto size = ReadString;

			// parse pos/size to vector2.
			glm::vec2 pos_uv, pos_px;
			parsePosition(pos, pos_uv, pos_px);
			glm::vec2 sz_uv, sz_px;
			parsePosition(size, sz_uv, sz_px);

			auto kbd = ReadString;
			auto jstk = ReadString;

			wstate = &vstate.workspace_state.back();
		    if (gesture_operation* d = dynamic_cast<gesture_operation*>(wstate->operation); d != nullptr)
		    {
				auto widget = new button_widget();
				widget->widget_name = name;
				widget->display_text = text;
				widget->center_uv = pos_uv;
				widget->center_px = pos_px;
				widget->sz_uv = sz_uv;
				widget->sz_px = sz_px;
				widget->keyboard_mapping = split(kbd, ',');
				widget->joystick_mapping = split(jstk, ',');
				d->widgets.add(name, widget);
			}
		},
		[&]
		{
			// 28: remove object.
			auto str = ReadString;
			RemoveObject(str);
		},
		[&]
		{
			// 29: add toggle type widget.
			auto name = ReadString;
			auto text = ReadString;
			auto pos = ReadString;
			auto size = ReadString;

			// parse pos/size to vector2.
			glm::vec2 pos_uv, pos_px;
			parsePosition(pos, pos_uv, pos_px);
			glm::vec2 sz_uv, sz_px;
			parsePosition(size, sz_uv, sz_px);
			
			auto kbd = ReadString;
			auto jstk = ReadString;

			wstate = &vstate.workspace_state.back();
		    if (gesture_operation* d = dynamic_cast<gesture_operation*>(wstate->operation); d != nullptr)
		    {
				auto widget = new toggle_widget();
				widget->widget_name = name;
				widget->display_text = text;
				widget->center_uv = pos_uv;
				widget->center_px = pos_px;
				widget->sz_uv = sz_uv;
				widget->sz_px = sz_px;
				widget->keyboard_mapping = split(kbd, ',');
				widget->joystick_mapping = split(jstk, ',');
				d->widgets.add(name, widget);
			}
		},
		[&]
		{
			//30: add throttle type widget.
			auto name = ReadString;
			auto text = ReadString;
			auto pos = ReadString;
			auto size = ReadString;
			auto type = ReadByte; //0:bounceback, 1:dual way, 2:only handle reponse to action, 3:vertical.

			// parse pos/size to vector2.
			glm::vec2 pos_uv, pos_px;
			parsePosition(pos, pos_uv, pos_px);
			glm::vec2 sz_uv, sz_px;
			parsePosition(size, sz_uv, sz_px);
			
			auto kbd = ReadString;
			auto jstk = ReadString;

			wstate = &vstate.workspace_state.back();
		    if (gesture_operation* d = dynamic_cast<gesture_operation*>(wstate->operation); d != nullptr)
		    {
				auto widget = new throttle_widget();
				widget->widget_name = name;
				widget->display_text = text;
				widget->center_uv = pos_uv;
				widget->center_px = pos_px;
				widget->sz_uv = sz_uv;
				widget->sz_px = sz_px;
				widget->onlyHandle = (type & 1);
				widget->dualWay = (type & 2);
				widget->bounceBack = (type & 4); 
				widget->vertical = (type & 8);  // currently not used.
				widget->current_pos = widget->init_pos = widget->dualWay ? 0 : -1;
				widget->keyboard_mapping = split(kbd, ',');
				widget->joystick_mapping = split(jstk, ',');
				d->widgets.add(name, widget);
			}
		},
		[&]
		{
			//31: add stick type widget
			auto name = ReadString;
			auto text = ReadString;
			auto pos = ReadString;
			auto size = ReadString;
			auto type = ReadByte; //0:bounceback.
			auto initX = ReadFloat;
			auto initY = ReadFloat;

			// parse pos/size to vector2.
			glm::vec2 pos_uv, pos_px;
			parsePosition(pos, pos_uv, pos_px);
			glm::vec2 sz_uv, sz_px;
			parsePosition(size, sz_uv, sz_px);
			
			auto kbd = ReadString;
			auto jstk = ReadString;

			wstate = &vstate.workspace_state.back();
		    if (gesture_operation* d = dynamic_cast<gesture_operation*>(wstate->operation); d != nullptr)
		    {
				auto widget = new stick_widget();
				widget->widget_name = name;
				widget->display_text = text;
				widget->center_uv = pos_uv;
				widget->center_px = pos_px;
				widget->sz_uv = sz_uv;
				widget->sz_px = sz_px;
				widget->bounceBack = (type & 2); 
				widget->onlyHandle = (type & 1);
				widget->current_pos = widget->init_pos = glm::vec2(initX, initY);
				widget->keyboard_mapping = split(kbd, ',');
				widget->joystick_mapping = split(jstk, ',');
				d->widgets.add(name, widget);
			}
		},
		[&]
		{
			//32: hide/show object for current workspace uiop.
			auto name = ReadString;
			auto show = ReadBool;
			SetShowHide(name, show);
		},
		[&]
		{
			//33:todo: PutGeometry 
			auto name = ReadString;
			// get geometry type, then put geometry.
		},
		[&] // 34: DefineMesh
		{
			auto clsname = ReadString; 
			auto vertex_count = ReadInt;
			auto positions = ReadArr(float, vertex_count * 3);
			auto color = ReadInt;
			auto smooth = ReadBool;

			// Create mesh data and define mesh
			custom_mesh_data mesh_data{
				.nvtx=vertex_count,
				.positions = (glm::vec3*)positions,
				.color = (unsigned int)color,
				.smooth = smooth
			};
			
			DefineMesh(clsname, mesh_data);
		},
		[&]
		{
			// 35: Make displaying workspace full-screen.
			auto fullscreen = ReadBool;

			if (&vstate != &ui.viewports[0]) return;
			// only main viewport can be full-screen.
			
			// call interface to make full-screen.

			GoFullScreen(fullscreen);
		},
		[&] 
		{
			// 36: GetPosition
			auto id = ReadInt;
			auto str = ReadString;

			auto plane_mode = ReadInt;
			
			BeginWorkspace<positioning_operation>(id, str, vstate);

			auto nsnaps = ReadInt;
			wstate = &vstate.workspace_state.back();
			for (int i = 0; i < nsnaps; ++i) {
				auto snap = ReadString;
				((positioning_operation*)wstate->operation)->snaps.push_back(snap);
			}
			
			// Set the plane mode
			((positioning_operation*)wstate->operation)->mode = plane_mode;
			wstate->useOperationalGrid = true;
		},
		[&]
		{
			// 37: SetMainMenuBar
			auto cid = ReadInt;
			auto show = ReadBool;
			auto whole_offset = ReadInt;
			vstate.showMainMenuBar = show;
			delete[] vstate.mainMenuBarData;
			vstate.mainMenuBarData = new unsigned char[whole_offset];
			memcpy(vstate.mainMenuBarData, ptr, whole_offset);
			ptr += whole_offset;
		},
		[&]
		{
			// 38: SetHoloViewEyePosition
			auto leftx = ReadFloat;
			auto lefty = ReadFloat;
			auto leftz = ReadFloat;
			auto rightx = ReadFloat;
			auto righty = ReadFloat;
			auto rightz = ReadFloat;

			grating_params.left_eye_pos_mm = glm::vec3(leftx, lefty, leftz);
			grating_params.right_eye_pos_mm = glm::vec3(rightx, righty, rightz);
		},
		[&]
		{
			// 39: QueryViewportState
			wstate->queryViewportState = true;
		},
		[&]
		{
			// 40: captureRenderedViewport
			wstate->captureRenderedViewport = true;
		},
		[&]
		{
		    // 41: SetObjectApperance
		    auto names = ReadString;
		    
		    auto bring_to_front_set = ReadBool;
		    bool bring_to_front = false;
		    if (bring_to_front_set) {
		        bring_to_front = ReadBool;
		    }
		    
		    auto shine_color_set = ReadBool;
		    uint32_t shine_color = 0;
		    if (shine_color_set) {
		        shine_color = ReadInt;
		    }
		    
		    auto border_set = ReadBool;
		    bool use_border = false;
		    if (border_set) {
				use_border = ReadBool;
		    }

			auto transparency_set = ReadBool;
			float transparency = 0.0f;
			if (transparency_set) {
				transparency = ReadFloat;
			}

			if (bring_to_front_set)
				BringObjectFront(names, bring_to_front);
			if (shine_color_set)
				SetObjectShine(names, shine_color > 0, shine_color);
			if (border_set)
				SetObjectBorder(names, use_border);
			if (transparency_set)
				SetObjectTransparency(names, transparency);
		},
		[&]
		{
			// 42: SetObjectMoonTo
			
		    auto earth = ReadString;  // The reference object
		    auto moon = ReadString;   // The object to be locked/anchored
			
			glm::vec3 new_position;
			new_position.x = ReadFloat;
			new_position.y = ReadFloat;
			new_position.z = ReadFloat;
			glm::quat new_quaternion;
			new_quaternion.x = ReadFloat;
			new_quaternion.y = ReadFloat;
			new_quaternion.z = ReadFloat;
			new_quaternion.w = ReadFloat;
			AnchorObject(earth, moon, new_position, new_quaternion);
		},
		[&]
		{
			// 43: SetSelectionMode
			auto mode = ReadInt;
    		auto radius = ReadFloat;

    		SetWorkspaceSelectMode((selecting_modes)mode, radius);
		},
		[&]
		{
			// 44: TransformSubObject

			auto objectNamePattern = ReadString;
			auto selectionMode = (int)ReadByte;
			auto subObjectName = ReadString;
			auto subObjectId = ReadInt;
			auto actionMode = (int)ReadByte;
			auto transformType = (int)ReadByte;

			auto tx = ReadFloat;
			auto ty = ReadFloat;
			auto tz = ReadFloat;
			auto rx = ReadFloat;
			auto ry = ReadFloat;
			auto rz = ReadFloat;
			auto rw = ReadFloat;
			auto timeMs = ReadInt;

			TransformSubObject(
				objectNamePattern,
				selectionMode,
				subObjectName,
				subObjectId,
				actionMode,
				transformType,
				glm::vec3(tx, ty, tz),
				glm::quat(rw, rx, ry, rz),
				timeMs
			);
		},
		[&]
		{
			// 45: SetCustomBackgroundShader
			auto shaderCode = ReadString;
			SetCustomBackgroundShader(shaderCode);
		},
		[&]
		{
			// 46: DisableCustomBackgroundShader
			DisableCustomBackgroundShader();
		},
		[&]
		{
			// 47: Follow Mouse operation: click to get position in the world.

			auto id = ReadInt;
			auto str = ReadString;

			// Read the follow mode (0 for GridPlane, 1 for ViewPlane)
			int follow_mode = ReadInt;

			BeginWorkspace<follow_mouse_operation>(id, str, vstate);

			// Set the operation parameters
			auto& wstate = vstate.workspace_state.back();
			auto follow_op = dynamic_cast<follow_mouse_operation*>(wstate.operation);
			
			// Set the follow mode
			follow_op->mode = follow_mode;
			follow_op->real_time = ReadBool;
			follow_op->allow_same_place = ReadBool;

			// Read follower objects
			int follower_count = ReadInt;
			for (int i = 0; i < follower_count; i++) {
				auto obj_name = ReadString;

				// Find the object in the global name map
				auto obj = global_name_map.get(obj_name);
				if (obj && obj->obj) {
					// Add this object to the list of referenced objects
					reference_t::push_list(follow_op->referenced_objects, obj->obj);

					// Store the original translation
					follow_op->original.push_back(obj->obj->target_position);
				}
			}

			// Read start snapping objects
			int start_snap_count = ReadInt;
			for (int i = 0; i < start_snap_count; i++) {
				auto snap_name = ReadString;
				follow_op->snapsStart.push_back(snap_name);
			}

			// Read end snapping objects
			int end_snap_count = ReadInt;
			for (int i = 0; i < end_snap_count; i++) {
				auto snap_name = ReadString;
				follow_op->snapsEnd.push_back(snap_name);
			}

			wstate.useOperationalGrid = true;
		},
		[&]
		{
			// 48: SetHoveringTooltip.
			
		},
		[&]
		{
			//49: PutHandleIcon
			auto name = ReadString;
			glm::vec3 position;
			position.x = ReadFloat;
			position.y = ReadFloat;
			position.z = ReadFloat;
			auto size = ReadFloat;
			auto iconChar = ReadString;
			auto color = ReadInt;
			auto handle_color = ReadInt;  // Add reading handle_color

			handle_icon_info info;
			info.name = name;
			info.position = position;
			info.icon = iconChar;
			info.color = color;
			info.handle_color = handle_color;
			info.size = size;

			AddHandleIcon(name, info);
		},
		[&]
		{
			// 50: SetModelObjectProperty
			auto namePattern = ReadString;
			
			ModelObjectProperties props;
			
			props.baseAnimId_set = ReadBool;
			if (props.baseAnimId_set) {
				props.baseAnimId = ReadInt;
			}
			
			props.nextAnimId_set = ReadBool;
			if (props.nextAnimId_set) {
				props.nextAnimId = ReadInt;
			}
			
			props.material_variant_set = ReadBool;
			if (props.material_variant_set) {
				props.material_variant = ReadInt;
			}
			
			auto team_color_set = ReadBool;
			if (team_color_set) {
				props.team_color = ReadInt;
				props.team_color_set = true;
			}
			
			auto base_stopatend_set = ReadBool;
			if (base_stopatend_set) {
				props.base_stopatend = ReadBool;
				props.base_stopatend_set = true;
			}
			
			auto next_stopatend_set = ReadBool;
			if (next_stopatend_set) {
				props.next_stopatend = ReadBool;
				props.next_stopatend_set = true;
			}
			
			auto animate_asap_set = ReadBool;
			if (animate_asap_set) {
				props.animate_asap = ReadBool;
				props.animate_asap_set = true;
			}
			
			SetModelObjectProperty(namePattern, props);
		},
		[&]
		{
			// 51: SetWorkspacePropDisplayMode
			auto mode = ReadInt;
			auto namePattern = ReadString;
			SetWorkspacePropDisplayMode(mode, namePattern);
		},
		[&]
		{
			// 52: SetSubObjectApperance
		},
		[&]
		{
			// 53: DeclareSVG
			auto name = ReadString;
			auto svgContent = ReadString;

			DeclareSVG(name, svgContent);
		},
		[&]
		{
			// 54: ???
		},
		[&]
		{
			//55: PutTextAlongLine
			auto name = ReadString;
			glm::vec3 start;
			start.x = ReadFloat;
			start.y = ReadFloat;
			start.z = ReadFloat;
			auto dirProp = ReadString;
			glm::vec3 direction;
			direction.x = ReadFloat;
			direction.y = ReadFloat;
			direction.z = ReadFloat;
			auto text = ReadString;
			auto size = ReadFloat;
			auto billboard = ReadBool;
			auto verticalOffset = ReadFloat;
			auto color = ReadInt;
			
			text_along_line_info info;
			info.name = name;
			info.start = start;
			info.direction = direction;
			info.dirProp = dirProp;
			info.text = text;
			info.voff = verticalOffset;
			info.size = size;
			info.bb = billboard;
			info.color = color;

			AddTextAlongLine(name, info);
		},
		[&]
		{
			//56: SetGridAppearance  
			bool pivot_set = ReadBool;
			glm::vec3 pivot = glm::vec3(0);
			if (pivot_set) {
				pivot.x = ReadFloat;
				pivot.y = ReadFloat;
				pivot.z = ReadFloat;
			}

			bool useViewPlane = ReadBool;

			bool unitX_set, unitY_set;
			glm::vec3 unitX = glm::vec3(1, 0, 0), unitY = glm::vec3(0, 1, 0);
			if (useViewPlane)
				SetGridAppearanceByView(pivot_set, pivot);

			unitX_set = ReadBool;
			if (unitX_set) {
				unitX.x = ReadFloat;
				unitX.y = ReadFloat;
				unitX.z = ReadFloat;
			}
			
			unitY_set = ReadBool;
			if (unitY_set) {
				unitY.x = ReadFloat;
				unitY.y = ReadFloat;
				unitY.z = ReadFloat;
			}
			
			SetGridAppearance(pivot_set, pivot, unitX_set, unitX, unitY_set, unitY);
		},
		[&]
		{
			//57: SkyboxImage
			auto width = ReadInt;
			auto height = ReadInt;
			auto dataLength = ReadInt;
			auto wsBtr = ReadArr(char, dataLength);
			
			SetSkyboxImage(width, height, dataLength, wsBtr);
		},
		[&]
		{
			//58: RemoveSkyboxImage
			RemoveSkyboxImage();
		},
		[&]
		{
			//59: SetImGUIStyle
			ImGuiStyle& style = ImGui::GetStyle();
			ImVec4* colors = style.Colors;

			// Read and apply color settings
			auto text_set = ReadBool;
			if (text_set) {
				auto text_color = ReadInt;
				colors[ImGuiCol_Text] = convertToImVec4(text_color);
			}
			
			auto textDisabled_set = ReadBool;
			if (textDisabled_set) {
				auto textDisabled_color = ReadInt;
				colors[ImGuiCol_TextDisabled] = convertToImVec4(textDisabled_color);
			}
			
			auto windowBg_set = ReadBool;
			if (windowBg_set) {
				auto windowBg_color = ReadInt;
				colors[ImGuiCol_WindowBg] = convertToImVec4(windowBg_color);
			}
			
			auto childBg_set = ReadBool;
			if (childBg_set) {
				auto childBg_color = ReadInt;
				colors[ImGuiCol_ChildBg] = convertToImVec4(childBg_color);
			}
			
			auto popupBg_set = ReadBool;
			if (popupBg_set) {
				auto popupBg_color = ReadInt;
				colors[ImGuiCol_PopupBg] = convertToImVec4(popupBg_color);
			}
			
			auto border_set = ReadBool;
			if (border_set) {
				auto border_color = ReadInt;
				colors[ImGuiCol_Border] = convertToImVec4(border_color);
			}
			
			auto borderShadow_set = ReadBool;
			if (borderShadow_set) {
				auto borderShadow_color = ReadInt;
				colors[ImGuiCol_BorderShadow] = convertToImVec4(borderShadow_color);
			}
			
			auto frameBg_set = ReadBool;
			if (frameBg_set) {
				auto frameBg_color = ReadInt;
				colors[ImGuiCol_FrameBg] = convertToImVec4(frameBg_color);
			}
			
			auto frameBgHovered_set = ReadBool;
			if (frameBgHovered_set) {
				auto frameBgHovered_color = ReadInt;
				colors[ImGuiCol_FrameBgHovered] = convertToImVec4(frameBgHovered_color);
			}
			
			auto frameBgActive_set = ReadBool;
			if (frameBgActive_set) {
				auto frameBgActive_color = ReadInt;
				colors[ImGuiCol_FrameBgActive] = convertToImVec4(frameBgActive_color);
			}
			
			auto titleBg_set = ReadBool;
			if (titleBg_set) {
				auto titleBg_color = ReadInt;
				colors[ImGuiCol_TitleBg] = convertToImVec4(titleBg_color);
			}
			
			auto titleBgActive_set = ReadBool;
			if (titleBgActive_set) {
				auto titleBgActive_color = ReadInt;
				colors[ImGuiCol_TitleBgActive] = convertToImVec4(titleBgActive_color);
			}
			
			auto titleBgCollapsed_set = ReadBool;
			if (titleBgCollapsed_set) {
				auto titleBgCollapsed_color = ReadInt;
				colors[ImGuiCol_TitleBgCollapsed] = convertToImVec4(titleBgCollapsed_color);
			}
			
			auto menuBarBg_set = ReadBool;
			if (menuBarBg_set) {
				auto menuBarBg_color = ReadInt;
				colors[ImGuiCol_MenuBarBg] = convertToImVec4(menuBarBg_color);
			}
			
			auto scrollbarBg_set = ReadBool;
			if (scrollbarBg_set) {
				auto scrollbarBg_color = ReadInt;
				colors[ImGuiCol_ScrollbarBg] = convertToImVec4(scrollbarBg_color);
			}
			
			auto scrollbarGrab_set = ReadBool;
			if (scrollbarGrab_set) {
				auto scrollbarGrab_color = ReadInt;
				colors[ImGuiCol_ScrollbarGrab] = convertToImVec4(scrollbarGrab_color);
			}
			
			auto scrollbarGrabHovered_set = ReadBool;
			if (scrollbarGrabHovered_set) {
				auto scrollbarGrabHovered_color = ReadInt;
				colors[ImGuiCol_ScrollbarGrabHovered] = convertToImVec4(scrollbarGrabHovered_color);
			}
			
			auto scrollbarGrabActive_set = ReadBool;
			if (scrollbarGrabActive_set) {
				auto scrollbarGrabActive_color = ReadInt;
				colors[ImGuiCol_ScrollbarGrabActive] = convertToImVec4(scrollbarGrabActive_color);
			}
			
			auto checkMark_set = ReadBool;
			if (checkMark_set) {
				auto checkMark_color = ReadInt;
				colors[ImGuiCol_CheckMark] = convertToImVec4(checkMark_color);
			}
			
			auto sliderGrab_set = ReadBool;
			if (sliderGrab_set) {
				auto sliderGrab_color = ReadInt;
				colors[ImGuiCol_SliderGrab] = convertToImVec4(sliderGrab_color);
			}
			
			auto sliderGrabActive_set = ReadBool;
			if (sliderGrabActive_set) {
				auto sliderGrabActive_color = ReadInt;
				colors[ImGuiCol_SliderGrabActive] = convertToImVec4(sliderGrabActive_color);
			}
			
			auto button_set = ReadBool;
			if (button_set) {
				auto button_color = ReadInt;
				colors[ImGuiCol_Button] = convertToImVec4(button_color);
			}
			
			auto buttonHovered_set = ReadBool;
			if (buttonHovered_set) {
				auto buttonHovered_color = ReadInt;
				colors[ImGuiCol_ButtonHovered] = convertToImVec4(buttonHovered_color);
			}
			
			auto buttonActive_set = ReadBool;
			if (buttonActive_set) {
				auto buttonActive_color = ReadInt;
				colors[ImGuiCol_ButtonActive] = convertToImVec4(buttonActive_color);
			}
			
			auto header_set = ReadBool;
			if (header_set) {
				auto header_color = ReadInt;
				colors[ImGuiCol_Header] = convertToImVec4(header_color);
			}
			
			auto headerHovered_set = ReadBool;
			if (headerHovered_set) {
				auto headerHovered_color = ReadInt;
				colors[ImGuiCol_HeaderHovered] = convertToImVec4(headerHovered_color);
			}
			
			auto headerActive_set = ReadBool;
			if (headerActive_set) {
				auto headerActive_color = ReadInt;
				colors[ImGuiCol_HeaderActive] = convertToImVec4(headerActive_color);
			}
			
			auto separator_set = ReadBool;
			if (separator_set) {
				auto separator_color = ReadInt;
				colors[ImGuiCol_Separator] = convertToImVec4(separator_color);
			}
			
			auto separatorHovered_set = ReadBool;
			if (separatorHovered_set) {
				auto separatorHovered_color = ReadInt;
				colors[ImGuiCol_SeparatorHovered] = convertToImVec4(separatorHovered_color);
			}
			
			auto separatorActive_set = ReadBool;
			if (separatorActive_set) {
				auto separatorActive_color = ReadInt;
				colors[ImGuiCol_SeparatorActive] = convertToImVec4(separatorActive_color);
			}

			// Read and apply style properties
			auto windowPadding_set = ReadBool;
			if (windowPadding_set) {
				auto paddingX = ReadFloat;
				auto paddingY = ReadFloat;
				style.WindowPadding.x = paddingX;
				style.WindowPadding.y = paddingY;
			}
			
			auto framePadding_set = ReadBool;
			if (framePadding_set) {
				auto paddingX = ReadFloat;
				auto paddingY = ReadFloat;
				style.FramePadding.x = paddingX;
				style.FramePadding.y = paddingY;
			}
			
			auto itemSpacing_set = ReadBool;
			if (itemSpacing_set) {
				auto spacingX = ReadFloat;
				auto spacingY = ReadFloat;
				style.ItemSpacing.x = spacingX;
				style.ItemSpacing.y = spacingY;
			}
			
			auto itemInnerSpacing_set = ReadBool;
			if (itemInnerSpacing_set) {
				auto spacingX = ReadFloat;
				auto spacingY = ReadFloat;
				style.ItemInnerSpacing.x = spacingX;
				style.ItemInnerSpacing.y = spacingY;
			}
			
			auto windowRounding_set = ReadBool;
			if (windowRounding_set) {
				auto rounding = ReadFloat;
				style.WindowRounding = rounding;
			}
			
			auto frameRounding_set = ReadBool;
			if (frameRounding_set) {
				auto rounding = ReadFloat;
				style.FrameRounding = rounding;
			}
			
			auto windowBorderSize_set = ReadBool;
			if (windowBorderSize_set) {
				auto borderSize = ReadFloat;
				style.WindowBorderSize = borderSize;
			}
			
			auto frameBorderSize_set = ReadBool;
			if (frameBorderSize_set) {
				auto borderSize = ReadFloat;
				style.FrameBorderSize = borderSize;
			}
			
			auto indentSpacing_set = ReadBool;
			if (indentSpacing_set) {
				auto spacing = ReadFloat;
				style.IndentSpacing = spacing;
			}
			
			auto scrollbarSize_set = ReadBool;
			if (scrollbarSize_set) {
				auto size = ReadFloat;
				style.ScrollbarSize = size;
			}
			
			auto grabMinSize_set = ReadBool;
			if (grabMinSize_set) {
				auto size = ReadFloat;
				style.GrabMinSize = size;
			}
		}
	};
	while (true) {
		auto api = ReadInt;
		if (api == -1) break;
		
		// printf("ws api call:%d\n", api);
		UIFuns[api]();
		//std::cout << "ws api call" << api << std::endl;
		apiN++;
	}
#ifdef __EMSCRIPTEN__
	// if (apiN > 0)
	// 	printf("WS processed %d apis of %dB\n", apiN, (int)(ptr - (unsigned char*)wsqueue));
#endif
	// std::cout << "ws process bytes=" << (int)(ptr - (unsigned char*) wsqueue) << std::endl;
}


void reference_t::remove_from_obj()
{
	if (obj == nullptr) return;
	// printf("remove %s reference @ %d\n", obj->name.c_str(), obj_reference_idx);
	if (obj_reference_idx<obj->references.size()-1)
	{
		obj->references[obj_reference_idx] = obj->references.back();
		if (obj->references[obj_reference_idx].accessor != nullptr)
			(*obj->references[obj_reference_idx].accessor())[obj->references[obj_reference_idx].offset].obj_reference_idx = obj_reference_idx;
		else
			obj->references[obj_reference_idx].ref->obj_reference_idx = obj_reference_idx;
	}
	obj->references.pop_back();
}

void reference_t::push_list(std::vector<reference_t>& referenced_objects, me_obj* t)
{
    referenced_objects.push_back(reference_t(namemap_t{.obj=t}, t->push_reference([&]() { return &referenced_objects; }, referenced_objects.size())));
}

size_t me_obj::push_reference(std::function<std::vector<reference_t>*()> dr, size_t offset)
{
	auto oidx = references.size();
	references.push_back({ .accessor = dr, .offset = offset });
	// printf("+ reference to `%s` @ %d.\n", this->name.c_str(), oidx);
	return oidx;
}

// todo: deprecate this.
void GenerateStackFromPanelCommands(unsigned char* buffer, int len)
{
	auto ptr = buffer;
	auto plen = ReadInt;

	for (int i = 0; i < plen;++i)
	{
		auto st_ptr = ptr;
		auto pid = ReadInt;

		auto name = ReadString;
		auto flag = ReadInt;

		// GetPanelProperties Magic.
		ptr += 4 * 10;

		int exceptionSz = *(int*)ptr;
		ptr += exceptionSz + 4;

		if ((flag & 2) == 0) //shutdown.
		{
			// std::cout << "shutdown " << pid << std::endl;
			map.erase(pid);
		}
		else
		{
			// std::cout << "show " << pid << ":"<< name << std::endl;
			
			auto& bytes = map[pid];
			bytes.clear();
			bytes.reserve(ptr - st_ptr);
			std::copy(st_ptr, ptr, std::back_inserter(bytes));
			// initialized;

			auto commandLength = ReadInt;
			for (int j = 0; j < commandLength; ++j)
			{
				auto type = ReadInt;
				if (type == 0) //type 0: byte command.
				{
					auto len = ReadInt;
					bytes.reserve(bytes.size() + len);
					std::copy(ptr, ptr + len, std::back_inserter(bytes));
					ptr += len;
				}
				// else if (type == 1) //type 1: cache.
				// {
				// 	auto len = ReadInt;
				// 	bytes.reserve(bytes.size() + len);
				// 	auto initLen = ReadInt;
				// 	std::copy(ptr, ptr + initLen, std::back_inserter(bytes));
				// 	for (int k = 0; k < len - initLen; ++k)
				// 		bytes.push_back(0);
				// 	ptr += initLen;
				// }
			}
			for (int j = 0; j < 4; ++j)
				bytes.push_back(j + 1); //01 02 03 04 as terminal
		}
	} 

	v_stack.clear();

	// number of panels:
	int mlen = map.size();
	//std::cout << "displaying windows(" << mlen <<"):" << std::endl;
	for (size_t i = 0; i < 4; ++i) {
		v_stack.push_back(((uint8_t*)&mlen)[i]);
	}

	// for each panel:
	for (const auto& entry : map)
	{
		const auto& bytes = entry.second;
		v_stack.insert(v_stack.end(), bytes.begin(), bytes.end());
	}
	cgui_stack = v_stack.data();
	cgui_refreshed = true;
}

struct wndState
{
	int inited = 0;
	bool pendingAction = false;
	uint64_t time_start_interact;
	ImVec2 Pos, Size;
	ImGuiWindow* im_wnd;

	// creation params:
	int minH, minW;
	int oneoffid;
	int flipper=0;

	int scycle = -1;
};

std::map<int, wndState> im;

template <typename TType>
struct cacher
{
	bool touched = false;
	TType caching;
};

class cacheBase
{
	static std::vector<cacheBase*> all_cache;
public:
	cacheBase()
	{
		all_cache.push_back(this);
	}
	static void untouch()
	{
		for (const auto& dictionary : all_cache) {
			dictionary->untouch_each();
		}
	}
	static void finish()
	{
		for (const auto& dictionary : all_cache) {
			dictionary->finish_each();
		}
	}

    virtual void untouch_each() = 0;
    virtual void finish_each() = 0;
};
std::vector<cacheBase*> cacheBase::all_cache;

template <typename TType>
class cacheType:cacheBase {
	inline static cacheType* inst = nullptr; // Declaration
	std::map<std::string, cacher<TType>> cache;

public:
	static cacheType* get() {
		if (inst == nullptr) {
			inst = new cacheType(); // Create an instance of cacheType with specific TType
		}
		return inst;
	}


	TType& get_or_create(std::string key) {
		if (cache.find(key) == cache.end()) {
			cache.emplace(key, cacher<TType>{}); // Default-construct TType
			cache[key].touched = true;
		}
		cache[key].touched = true;
		return cache[key].caching;
	}

	bool exist(std::string key) {
		return cache.find(key) != cache.end();
	}

	void untouch_each() override
	{
		for (auto& auto_ : cache)
			auto_.second.touched = false;
	}
	void finish_each() override
	{
	    for (auto it = cache.begin(); it != cache.end();) {
	        if (!it->second.touched) {
	            it = cache.erase(it); // erase returns the next iterator
	        } else {
	            ++it;
	        }
	    }
	}
};


// utility structure for realtime plot
struct ScrollingBuffer {
	bool hold = false;

	int MaxSize;
	int Offset;
	ImVector<ImVec2> Data;
	double latestSec;

	ScrollingBuffer(int max_size = 2000) {
		MaxSize = max_size;
		Offset = 0;
		Data.reserve(MaxSize);
	}
	void AddPoint(float x, float y) {
		if (Data.size() < MaxSize)
			Data.push_back(ImVec2(x, y));
		else {
			Data[Offset] = ImVec2(x, y);
			Offset = (Offset + 1) % MaxSize;
		}
	}
	void Erase() {
		if (Data.size() > 0) {
			Data.shrink(0);
			Offset = 0;
		}
	}
};

// struct chatbox_double_buffer
// {
// 	std::vector<char> buffer;
// 	int sz;
// 	int ptr = 0;
// 	int read = 0;
// 	int tok;
// 	bool focused;
//
// 	chatbox_double_buffer(int max_size = 4096)
// 	{
// 		buffer.resize(max_size);
// 		sz = max_size / 2;
// 		buffer[0] = buffer[sz] = 0;
// 	}
// 	void push(char* str)
// 	{
// 		auto len = strlen(str);
// 		for(int i=0; i<len; ++i)
// 		{
// 			buffer[ptr] = buffer[ptr + sz] = str[i];
// 			ptr += 1;
// 			if (ptr == sz) ptr = 0;
// 			if (ptr == read) read += 1;
// 			if (read == sz) read = 0;
// 			
// 		}
// 		buffer[ptr] = buffer[ptr + sz] = '\n';
// 		ptr += 1;
// 		if (ptr == sz) ptr = 0;
// 		if (ptr == read) read += 1;
// 		if (read == sz) read = 0;
// 		buffer[ptr] = buffer[ptr + sz] = 0;
// 	}
// 	char* get()
// 	{
// 		return &buffer[read];
// 	}
// 	std::string cached;
// 	void cache()
// 	{
// 		cached = get();
// 	}
// };

struct chatbox_items
{
	std::vector<std::string> buffer, cache;
	int sz;
	int cacheread, cacheptr;
	char inputbuf[512];
	std::string hint;
	bool inputfocus = false;

	chatbox_items(int max_size = 300)
	{
		buffer.resize(max_size);
		sz = max_size;
		inputbuf[0] = 0;
		focused = false;
	}
	int ptr = 0;
	int read = 0; //from read to ptr.
	int tok;
	bool focused;
	void push(char* what)
	{
		buffer[ptr] = what;
		ptr += 1;
		if (ptr == sz) ptr = 0;
		if (ptr == read) read += 1;
		if (read == sz) read = 0;
	}
};

// std::string current_triggering;
bool parse_chord(std::string key) {
	static std::unordered_map<std::string, int> keyMap = {
	    {"space", GLFW_KEY_SPACE},
	    {"left", GLFW_KEY_LEFT},
	    {"right", GLFW_KEY_RIGHT},
	    {"up", GLFW_KEY_UP},
	    {"down", GLFW_KEY_DOWN},
	    {"backspace", GLFW_KEY_BACKSPACE},
	    {"del", GLFW_KEY_DELETE},
	    {"ins", GLFW_KEY_INSERT},
	    {"enter", GLFW_KEY_ENTER},
	    {"tab", GLFW_KEY_TAB},
	    {"esc", GLFW_KEY_ESCAPE},
	    {"pgup", GLFW_KEY_PAGE_UP},
	    {"pgdn", GLFW_KEY_PAGE_DOWN},
	    {"home", GLFW_KEY_HOME},
	    {"end", GLFW_KEY_END},
	    {"pause", GLFW_KEY_PAUSE},
	    {"f1", GLFW_KEY_F1},
	    {"f2", GLFW_KEY_F2},
	    {"f3", GLFW_KEY_F3},
	    {"f4", GLFW_KEY_F4},
	    {"f5", GLFW_KEY_F5},
	    {"f6", GLFW_KEY_F6},
	    {"f7", GLFW_KEY_F7},
	    {"f8", GLFW_KEY_F8},
	    {"f9", GLFW_KEY_F9},
	    {"f10", GLFW_KEY_F10},
	    {"f11", GLFW_KEY_F11},
	    {"f12", GLFW_KEY_F12},
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

    bool ctrl_pressed = !ctrl || (ctrl && (glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_LEFT_CONTROL) == GLFW_PRESS || glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_RIGHT_CONTROL) == GLFW_PRESS));
    bool alt_pressed = !alt || (alt && (glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_LEFT_ALT) == GLFW_PRESS || glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_RIGHT_ALT) == GLFW_PRESS));
    bool shift_pressed = !shift || (shift && (glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_LEFT_SHIFT) == GLFW_PRESS || glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_RIGHT_SHIFT) == GLFW_PRESS));
	bool mainkey_pressed = mainkey != -1 && (glfwGetKey(glfwGetCurrentContext(), mainkey) == GLFW_PRESS);

	if (ctrl_pressed && alt_pressed && shift_pressed && mainkey_pressed) //triggered.
	{
		// if (current_triggering != key)
		// {
		// 	current_triggering = key;
		// 	return true;
		// }
		return true;
	}
    // if (current_triggering == key)
	   //  current_triggering = "";
	return false;
}

ImGuiID leftPanels, rightPanels, dockspace_id;
ImGuiDockNode* dockingRoot;

bool init_docking = false;
void SetupDocking()
{
	init_docking = true;
	dockspace_id = ImGui::GetID("CycleGUIMainDock");

	// ImGui::DockBuilderRemoveNode(dockspace_id); // clear any previous layout
	if ((dockingRoot = ImGui::DockBuilderGetNode(dockspace_id)) != NULL) return;
	ImGui::DockBuilderAddNode(dockspace_id, ImGuiDockNodeFlags_DockSpace);// Add empty node
	dockingRoot = ImGui::DockBuilderGetNode(dockspace_id);

	ImGui::DockBuilderFinish(dockspace_id);
	// ImGui::DockBuilderSetNodeSize(dockspace_id, ImVec2(960, (float)640));
	//
	// ImGuiID dock_id_graph_editor = dockspace_id; // This variable tracks the central node
	// leftPanels = ImGui::DockBuilderSplitNode(dock_id_graph_editor, ImGuiDir_Left, 0.333f, NULL, &dock_id_graph_editor);
	// rightPanels = ImGui::DockBuilderSplitNode(dock_id_graph_editor, ImGuiDir_Right, 0.333f, NULL, &dock_id_graph_editor);
}

std::vector<int> no_modal_pids;

void StepWndSz(ImGuiSizeCallbackData* data)
{
	auto step = 64;
	wndState* minSz = (wndState*)data->UserData;
	auto calX = std::max((int)minSz->minW, (int)(data->CurrentSize.x / step + 0.5f) * step);
	auto calY = std::max((int)minSz->minH, (int)data->CurrentSize.y+10);
	data->DesiredSize = ImVec2(calX, calY);
}

#ifdef __EMSCRIPTEN__
extern double g_dpi;
#endif

void aux_viewport_draw(unsigned char* wsptr, int len);
unsigned char* aux_workspace_ptr;
int aux_workspace_ptr_len;
bool aux_workspace_issued = false;

unsigned char ui_buffer[1024 * 1024];
void ProcessUIStack()
{
	for (int i = 1; i < MAX_VIEWPORTS; ++i)
		ui.viewports[i].active = ui.viewports[i].assigned = false;
	
	ImGuiStyle& style = ImGui::GetStyle();

	if (!init_docking)
		SetupDocking();

	// create the main dockspace over the entire editor window
	ImGuiViewport* viewport = ImGui::GetMainViewport();
	ImGui::SetNextWindowPos(viewport->WorkPos);
	ImGui::SetNextWindowSize(viewport->WorkSize);
	ImGui::SetNextWindowViewport(viewport->ID);
	ImGui::SetNextWindowBgAlpha(0.0f);

	ImGuiWindowFlags window_flags = ImGuiWindowFlags_NoDocking;
	window_flags |= ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove;
	window_flags |= ImGuiWindowFlags_NoBringToFrontOnFocus | ImGuiWindowFlags_NoNavFocus;

	ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, 0.0f);
	ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize, 0.0f);
	ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(0.0f, 0.0f));
	ImGui::Begin("CycleGUI Dockspace", NULL, window_flags);
	ImGui::PopStyleVar(3);

	ImGuiDockNodeFlags dockspace_flags = ImGuiDockNodeFlags_PassthruCentralNode | ImGuiDockNodeFlags_NoDockingInCentralNode;
	ImGui::DockSpace(dockspace_id, ImVec2(0.0f, 0.0f), dockspace_flags);
	ImGui::End();

	// ui_stack
	beforeDraw();

	auto ptr = cgui_stack;
	if (ptr == nullptr) return; // skip if not initialized.

	auto plen = ReadInt;

	auto pr = ui_buffer;
	bool stateChanged = false;

	cacheBase::untouch();
	
	auto io = ImGui::GetIO();

	// Position
	auto vp = ImGui::GetMainViewport();
	static double sec = 0;
	sec += io.DeltaTime;

	int modalpid = -1;
	for (int i = 0; i < plen; ++i)
	{ 
		bool wndShown = true;

		auto pid = ReadInt;
		auto str = ReadString;
		char wndStr[256];
		sprintf(wndStr, "%s#%d", str, pid);
		auto& mystate = cacheType<wndState>::get()->get_or_create(wndStr);

#ifdef __EMSCRIPTEN__
		auto dpiScale = g_dpi;
#else
		auto dpiScale = mystate.inited < 1 ? vp->DpiScale : mystate.im_wnd->Viewport->DpiScale;
#endif
#define GENLABEL(var,label,prompt) char var[256]; sprintf(var, "%s##%s%d", prompt,label,cid);

		ImGuiWindowFlags window_flags = 0;
		auto flags = ReadInt;

		// consider * scale to overcome highdpi effects.
		auto relPanel = ReadInt;
		auto relPivotX = ReadFloat;
		auto relPivotY = ReadFloat;
		auto myPivotX = ReadFloat;
		auto myPivotY = ReadFloat;
		auto panelWidth = ReadInt;
		auto panelHeight = ReadInt;
		auto panelLeft = ReadInt;
		auto panelTop = ReadInt;

		auto flipper = ReadInt;

		panelWidth *= dpiScale;
		panelHeight *= dpiScale;
		panelLeft *= dpiScale;
		panelTop *= dpiScale;

		auto except = ReadStdString;


		std::function<void()> UIFuns[] = {
			[&]
			{
				assert(false);
			}, // 0: this is not a valid control(cache command)
			[&] //1: text
			{
				auto str = ReadString;
				ImGui::TextWrapped(str);
			},
			[&] // 2: button
			{
				auto cid = ReadInt;
				auto str = ReadString;
				auto shortcut = ReadString;
				auto hintLen = ReadStringLen;
				auto hint = ReadString;

				char buttonLabel[256];
				sprintf(buttonLabel, "%s##btn%d", str, cid);
				if (ImGui::Button(buttonLabel) || parse_chord(shortcut)) {
					stateChanged = true;
					WriteInt32(1)
				}

				if (hintLen > 0 && ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort))
					ImGui::SetTooltip(hint);
			},
			[&] // 3: checkbox
			{
				auto cid = ReadInt;
				auto str = ReadString;
				auto checked = ReadBool;

				char checkboxLabel[256];
				sprintf(checkboxLabel, "%s##checkbox%d", str, cid);
				if (ImGui::Checkbox(checkboxLabel, &checked)) {
					stateChanged = true;
					WriteBool(checked)
				}
			},
			[&] // 4: TextInput
			{
				auto cid = ReadInt;
				auto prompt = ReadString;
				auto hidePrompt = ReadBool;
				auto alwaysReturnString = ReadBool;
				auto hint = ReadString;
				auto defTxt = ReadString;
				auto inputOnShow = ReadBool;

				//ImGui::PushItemWidth(300);
				char tblbl[256];
				sprintf(tblbl, "##%s-tb-%d", prompt, cid);
				using ti = std::tuple<char[256], char[256]>; //get<0>:buffer, get<1>:default.
				auto init = cacheType<ti>::get()->exist(tblbl);
				auto& tiN = cacheType<ti>::get()->get_or_create(tblbl);
				auto& textBuffer = std::get<0>(tiN);
				auto& cacheddef = std::get<1>(tiN);

				if (!init || strcmp(defTxt,cacheddef)){
					memcpy(textBuffer, defTxt, 256);
					memcpy(cacheddef, defTxt, 256);
				}

				// make focus on the text input if possible.
				if (inputOnShow && ImGui::IsWindowAppearing())
					ImGui::SetKeyboardFocusHere();
				if (!hidePrompt) ImGui::TextWrapped(prompt);
				ImGui::Indent(style.IndentSpacing / 2);
				ImGui::SetNextItemWidth(-16*dpiScale);

				bool itwh = ImGui::InputTextWithHint(tblbl, hint, textBuffer, 256, ImGuiInputTextFlags_EnterReturnsTrue);
				if (itwh || alwaysReturnString && ImGui::IsItemActive()) {
					stateChanged = true;
					// patch.
					auto ocid = cid;
					cid += 1;
					WriteBool(true);
					cid = ocid;
				}

				ImGui::Unindent(style.IndentSpacing / 2);
				//ImGui::PopItemWidth();

				WriteString(textBuffer, strlen(textBuffer))
			},
			[&] // 5: Listbox
			{
				auto cid = ReadInt;
				auto prompt = ReadString;
				auto h = ReadInt;
				auto len = ReadInt;
				auto selecting = ReadInt;
				ImGui::SeparatorText(prompt);
				char lsbxid[256];
				sprintf(lsbxid, "%s##listbox", prompt);

				ImGuiWindowFlags window_flags = ImGuiWindowFlags_HorizontalScrollbar;
				if (ImGui::BeginChild(lsbxid, ImVec2(ImGui::GetContentRegionAvail().x, h * ImGui::GetTextLineHeightWithSpacing()+16), true, window_flags))
				{
					for (int n = 0; n < len; n++)
					{
						auto item = ReadString;
						sprintf(lsbxid, "%s##lb%s_%d", item, prompt, n);
						if (ImGui::Selectable(lsbxid, selecting == n)) {
							stateChanged = true;
							selecting = n;
							WriteInt32(n)
						}
						if (selecting == n)
							ImGui::SetItemDefaultFocus();
					}
				}else
				{
					for (int n = 0; n < len; n++)
					{
						auto item = ReadString;
						// do nothing at all
					}
				}
				ImGui::EndChild();
			},
			[&] //6: button group
			{
				auto cid = ReadInt;
				auto promptSz = ReadStringLen;
				auto prompt = ReadString;

				auto flag = ReadInt;
				auto buttons_count = ReadInt;

				if (promptSz > 0)
					ImGui::SeparatorText(prompt);

				float window_visible_x2 = ImGui::GetWindowPos().x + ImGui::GetWindowContentRegionMax().x;
				auto xx = 0;
				for (int n = 0; n < buttons_count; n++)
				{
					auto btn_txt = ReadString;
					char lsbxid[256];
					sprintf(lsbxid, "%s##btng%s_%d", btn_txt, prompt, n);

					auto sz = ImGui::CalcTextSize(btn_txt);
					sz.x += style.FramePadding.x * 2;
					sz.y += style.FramePadding.y * 2;
					
					if (n>0 && (xx + style.ItemSpacing.x + sz.x < window_visible_x2 || (flag & 1) != 0))
						ImGui::SameLine();

					if (ImGui::Button(lsbxid))
					{
						stateChanged = true;
						WriteInt32(n)
					}
					xx = ImGui::GetItemRectMax().x;
				}

			},
			[&]
			{
				// 7: Webview widget
				auto name = ReadString;
				auto url = ReadString;
				auto hintLen = ReadStringLen;
				auto hint = ReadString;

				char txt[256];
				sprintf(txt, "\uf08e %s##wv%s", name, url);

				ImGui::PushStyleColor(ImGuiCol_Button, (ImVec4)ImColor::HSV(4 / 7.0f, 0.6f, 0.6f));
				ImGui::PushStyleColor(ImGuiCol_ButtonHovered, (ImVec4)ImColor::HSV(4 / 7.0f, 0.7f, 0.7f));
				ImGui::PushStyleColor(ImGuiCol_ButtonActive, (ImVec4)ImColor::HSV(4 / 7.0f, 0.8f, 0.8f));

				if (ImGui::Button(txt)) {
					// todo: avoid multiple open.
					showWebPanel(url);
				}
				ImGui::PopStyleColor(3);
				if (hintLen > 0 && ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort))
					ImGui::SetTooltip(hint);
			},
			[&]
			{ // 8 : closing button.
				auto cid = ReadInt;
				if (!wndShown)
				{
					stateChanged = true;
					WriteBool(true);
				}
			},
			[&]
			{
				// 9: realtime plot.
				auto cid = ReadInt;
				auto prompt = ReadString;
				auto value = ReadFloat;
				auto hold = ReadBool;
				
				auto& plotting = cacheType<ScrollingBuffer>::get()->get_or_create(prompt);
				if (!hold && plotting.hold)
					plotting.Erase();
				plotting.hold = hold;

				auto pad = 96 * dpiScale;
				ImGui::PushItemWidth(pad);
				ImGui::BeginGroup();
				{
					auto w = ImGui::CalcTextSize(prompt).x;
					ImGui::SetCursorPosX(ImGui::GetCursorPosX() + pad - w);
					ImGui::Text(prompt);

					char valueTxt[256];
					sprintf(valueTxt, "%.2f%s", value, hold ? " \uf28b" : " \uf144");
					w = ImGui::CalcTextSize(valueTxt).x;
					ImGui::SetCursorPosX(ImGui::GetCursorPosX() + pad - w);
					ImGui::Text(valueTxt);

					auto btns = ReadInt;
					ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 2);
					ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(2,2));
					for (int i=0; i<btns; ++i)
					{
						auto btn_txt = ReadString;
						char lsbxid[256];
						sprintf(lsbxid, "%s##rp%s_%d", btn_txt, prompt, i);
						w = ImGui::CalcTextSize(btn_txt).x;
						ImGui::SetCursorPosX(ImGui::GetCursorPosX() + pad - w - 6*dpiScale);
						if (ImGui::SmallButton(lsbxid))
						{
							stateChanged = true;
							WriteInt32(i);
						}
					}
					ImGui::PopStyleVar(2);

				}
				ImGui::EndGroup();
				ImGui::SameLine(112 * dpiScale);
				if (ImPlot::BeginPlot(prompt, ImVec2(-1, ImGui::GetItemRectSize().y), ImPlotFlags_CanvasOnly)) {
					ImPlot::SetupAxes(nullptr, nullptr, 
						ImPlotAxisFlags_NoLabel|ImPlotAxisFlags_NoTickLabels, ImPlotAxisFlags_AutoFit|ImPlotAxisFlags_NoLabel|ImPlotAxisFlags_NoTickLabels);
					if (plotting.hold)
					{
						ImPlot::SetupAxisLimits(ImAxis_X1, plotting.latestSec - 10, plotting.latestSec, ImGuiCond_Always);
						ImPlot::PlotLine(prompt, &plotting.Data[0].x, &plotting.Data[0].y, plotting.Data.size(), 0, plotting.Offset, 2 * sizeof(float));
					}
					else {
						plotting.AddPoint(sec, value);

						ImPlot::SetupAxisLimits(ImAxis_X1, sec - 10, sec, ImGuiCond_Always);
						ImPlot::PlotLine(prompt, &plotting.Data[0].x, &plotting.Data[0].y, plotting.Data.size(), 0, plotting.Offset, 2 * sizeof(float));

						plotting.latestSec = sec;
					}
					ImPlot::EndPlot();
				} 
			},
			[&]
			{
				// 10: dragfloat.
				auto cid = ReadInt;
				auto prompt = ReadString;

				float* val = (float*)ptr; ptr += 4;
				auto step = ReadFloat;
				auto min_v = ReadFloat;
				auto max_v = ReadFloat;

				if (ImGui::DragFloat(prompt, val, step, min_v, max_v))
				{
					stateChanged = true;
					WriteFloat(*val);
				}
			},
			[&]
			{
				// 11: separator text.
				auto str = ReadString;
				ImGui::SeparatorText(str);
			},
			[&]
			{
				// 12: display file link
				auto displayname = ReadString;
				auto filehash = ReadString;
				auto fname = ReadString;
				char lsbxid[256];
				auto& displayed = cacheType<long long>::get()->get_or_create(filehash);
				sprintf(lsbxid, "\uf0c1 %s", displayname);
				auto enabled = displayed < ui.getMsFromStart() + 1000;
				if (!enabled) ImGui::BeginDisabled(true);
				
				ImGui::PushStyleColor(ImGuiCol_Button, IM_COL32(120, 80, 0, 255));
				ImGui::PushStyleColor(ImGuiCol_ButtonHovered, IM_COL32(140, 90, 0, 255));
				ImGui::PushStyleColor(ImGuiCol_ButtonActive, IM_COL32(180, 100, 10, 255));
				if (ImGui::Button(lsbxid))
				{
					ExternDisplay(filehash, pid, fname);
				}
				ImGui::PopStyleColor(3);

				if (!enabled) ImGui::EndDisabled();
			},
			[&]
			{
				// 13: toggle
				auto cid = ReadInt;
				auto str = ReadString;
				auto checked = ReadBool;

				char checkboxLabel[256];
				sprintf(checkboxLabel, "%s##toggle%d", str, cid);
				auto nv = checked;
				ToggleButton(checkboxLabel, &checked);
				ImGui::SameLine();
				ImGui::AlignTextToFramePadding();
				ImGui::Text(str);
				if (nv!=checked) {
					stateChanged = true;
					WriteBool(checked)
				}
			},
			[&]
			{
				// 14: sameline
				auto spacing = ReadInt;
				ImGui::SameLine(0, spacing);
			},
			[&]
			{
				// 15: chatbox
				auto cid = ReadInt;
				auto prompt = ReadString;
				auto height = ReadInt;
				auto aio = ReadBool;
				auto tok = ReadInt;
				auto lines = ReadInt;

				auto inited = cacheType<chatbox_items>::get()->exist(prompt);
				auto& displayed = cacheType<chatbox_items>::get()->get_or_create(prompt);
				if (!inited)
					displayed.hint = prompt;
				auto changed = tok != displayed.tok;
				displayed.tok = tok;

				for(int i=0; i<lines; ++i)
				{
					auto content = ReadString;
					if (changed)
						displayed.push(content);
				}

				GENLABEL(ilab, "chat", prompt);

				//

	            ImGui::PushStyleVar(ImGuiStyleVar_ChildRounding, 5.0f);
				ImGuiContext& g = *GImGui;
				auto top = ImGui::GetCursorPosY();
				auto previousfocus = displayed.focused;

				ImGui::BeginChild(ilab, ImVec2(0, g.CurrentWindow->Size.y - top - (8+(aio?32:0)) * dpiScale), true, ImGuiWindowFlags_AlwaysVerticalScrollbar);
				displayed.focused = ImGui::IsWindowFocused();
				if (displayed.focused || displayed.inputfocus || previousfocus){
					if (!previousfocus && !displayed.inputfocus){
						displayed.cache = displayed.buffer;
						displayed.cacheread = displayed.read;
						displayed.cacheptr = displayed.ptr;
					}
					for(int p=displayed.cacheread; p!=displayed.cacheptr; )
					{
						ImGui::TextWrapped(displayed.cache[p].c_str());
						p += 1;
						if (p == displayed.sz)p = 0;
					}
				}
				else{
					for(int p=displayed.read; p!=displayed.ptr; )
					{
						ImGui::TextWrapped(displayed.buffer[p].c_str());
						p += 1;
						if (p == displayed.sz)p = 0;
					}
				}
				if (changed && !displayed.focused && !displayed.inputfocus){
					ImGui::SetScrollY(g.CurrentWindow, g.CurrentWindow->ScrollMax.y + 100);
				}

	            ImGui::EndChild();

				if (aio){
					GENLABEL(itml, "chatedit", "");
					ImGui::SetNextItemWidth(-82);
					auto sent = false;
					if (ImGui::InputTextWithHint(itml,displayed.hint.c_str(), displayed.inputbuf, 512, ImGuiInputTextFlags_EnterReturnsTrue))
					{
						sent = true;
						stateChanged = true;
						WriteString(displayed.inputbuf, strlen(displayed.inputbuf));
						displayed.hint = displayed.inputbuf;
						displayed.inputbuf[0] = 0;
						// send data.
					}
					auto txtid = ImGui::GetItemID();
					auto activeid = ImGui::GetActiveID();
					if (previousfocus && !displayed.focused && txtid == activeid)
						displayed.inputfocus = true;
					if (displayed.inputfocus && txtid != activeid)
						displayed.inputfocus = false; //focus transfered to text.
					
					ImGui::SameLine();

					ImGui::PushStyleColor(ImGuiCol_Button, (ImVec4)ImColor::HSV(2 / 7.0f, 0.6f, 0.6f));
					ImGui::PushStyleColor(ImGuiCol_ButtonHovered, (ImVec4)ImColor::HSV(2 / 7.0f, 0.7f, 0.7f));
					ImGui::PushStyleColor(ImGuiCol_ButtonActive, (ImVec4)ImColor::HSV(2 / 7.0f, 0.8f, 0.8f));
					if (ImGui::Button("\uf1d8") && !sent)
					{ 
						stateChanged = true;
						WriteString(displayed.inputbuf, strlen(displayed.inputbuf));
						displayed.hint = displayed.inputbuf;
						displayed.inputbuf[0] = 0;
					}
					ImGui::PopStyleColor(3);

					ImGui::SameLine();
					if (ImGui::Button("\uf103"))
						displayed.inputfocus = false;
				}
				else
					displayed.inputfocus = false;

	            ImGui::PopStyleVar();
				
			},
			[&]
			{
				// 16: bring to front.
				auto cid = ReadInt;
				if (mystate.oneoffid!=cid){
					ImGui::SetWindowFocus();
					printf("bring panel %s to front\n", str);
					mystate.oneoffid = cid;
				}
			},
			[&]
			{
				// 17:???
				static bool show_demo = true;
				ImPlot::ShowDemoWindow(&show_demo);
				ImGui::ShowDemoWindow(&show_demo);
			},
			[&]{
				// 18: Separator
				ImGui::Separator();
			},
			[&]
			{
				// 19: CollapsingHeader
				auto cid = ReadInt;
				auto str = ReadString;
				auto offset = ReadInt;

				char headerLabel[256];
				sprintf(headerLabel, "%s##CollapsingHeader%d", str, cid);
				if (!ImGui::CollapsingHeader(headerLabel, ImGuiTreeNodeFlags_None))
					ptr += offset;
			},
			[&]
			{
				// 20: Table
				auto cid = ReadInt;
				auto strId = ReadString;
				char searcher[256];
				sprintf(searcher, "\uf002##%s-search", strId);
				auto skip = ReadInt; //from slot "row" to end.
				auto title = ReadString;
				auto rows = ReadInt;
				auto height = ReadInt;
				if (title[0] != '\0') ImGui::SeparatorText(title);
				auto enableSearch = ReadBool;
				auto freeze1st = ReadBool;
				auto cols = ReadInt;

				ImGuiTableFlags flags = ImGuiTableFlags_RowBg | ImGuiTableFlags_Borders
					| ImGuiTableFlags_Resizable
					| ImGuiTableFlags_SizingFixedFit;// | ImGuiTableFlags_Sortable;


				auto& searchTxt = cacheType<char[256]>::get()->get_or_create(searcher);
				if (enableSearch){
					ImGui::PushItemWidth(-20 * dpiScale);
					ImGui::InputTextWithHint(searcher, title, searchTxt, 256);
					ImGui::PopItemWidth();
				}
				auto searchLen = strlen(searchTxt);

				using VarType = std::variant<int, bool, char*>;


				auto dispRows = 0;
				if (height > 0) {
					flags |= ImGuiTableFlags_ScrollX | ImGuiTableFlags_ScrollY;
				}
				if (height <= 0)
					height = 0;
				if (ImGui::BeginTable(strId, cols, flags, ImVec2(0, ImGui::GetTextLineHeightWithSpacing()*(height+1))))
				{
					for (int i = 0; i < cols; ++i)
					{
						auto header = ReadString;
						ImGui::TableSetupColumn(header);
					}

					ImGui::TableSetupScrollFreeze(freeze1st ? 1 : 0, 1);

					ImGui::TableHeadersRow();

					// Submit dummy contents
					for (int row = 0; row < rows; row++)
					{
						// check searchTxt
						auto skip_row = true;
						std::vector<VarType> vec;
						for (int column = 0; column < cols; column++)
						{
							auto type = ReadInt;
							vec.push_back(type);
							if (type == 0)
							{	// label
								auto label = ReadString;
								vec.push_back(label);
								if (enableSearch && searchLen > 0 && caseInsensitiveStrStr(label, searchTxt)) skip_row = false;
							}
							else if (type == 1)
							{	// label with hint
								auto label = ReadString;
								auto hint = ReadString;
								vec.push_back(label);
								vec.push_back(hint);
								if (enableSearch && searchLen > 0 && caseInsensitiveStrStr(label, searchTxt)) skip_row = false;
							}
							else if (type == 2) // btn group
							{
								// buttons without hint.
								auto len = ReadInt;
								vec.push_back(len);
								for (int i = 0; i < len; ++i)
								{
									auto label = ReadString;
									vec.push_back(label);
								}
							}
							else if (type == 3)
							{
								// buttons with hint.
								auto len = ReadInt;
								vec.push_back(len);
								for (int i = 0; i < len; ++i)
								{
									auto label = ReadString;
									auto hint = ReadString;
									vec.push_back(label);
									vec.push_back(hint);
								}
							}
							else if (type == 4) //checkbox.
							{
								auto init = ReadBool;
								vec.push_back(init);
							}
							else if (type == 5) //checkbox with hint.
							{
								auto init = ReadBool;
								vec.push_back(init);
								auto hint = ReadString;
								vec.push_back(hint);
							}
							else if (type == 6)
							{ // set color, doesn't apply to column.
								auto color = ReadInt;
								vec.push_back(color);
								column -= 1;
							}
						}

						if (searchLen > 0 && skip_row) continue;

						// actual display
						ImGui::TableNextRow();
						dispRows += 1;
						auto ii = 0;
						for (int column = 0; column < cols; column++)
						{
							ImGui::TableSetColumnIndex(column);
							auto type = std::get<int>(vec[ii++]);
#define TableResponseBool(x) stateChanged=true; char ret[10]; ret[0]=1; *(int*)(ret+1)=row; *(int*)(ret+5)=column; ret[9]=x; WriteBytes(ret, 10);
#define TableResponseInt(x) stateChanged=true; char ret[13]; ret[0]=0; *(int*)(ret+1)=row; *(int*)(ret+5)=column; *(int*)(ret+9)=x; WriteBytes(ret, 13);
							if (type == 0)
							{	// label
								auto label = std::get<char*>(vec[ii++]);
								char hashadded[256];
								snprintf(hashadded, 256, "%s##%d_%d", label, row, column);
								if (ImGui::Selectable(hashadded))
								{
									TableResponseBool(true);
								};
							}
							else if (type == 1)
							{	// label with hint
								auto label = std::get<char*>(vec[ii++]);
								char hashadded[256];
								snprintf(hashadded, 256, "%s##%d_%d", label, row, column);
								auto hint = std::get<char*>(vec[ii++]);
								if (ImGui::Selectable(hashadded))
								{
									TableResponseBool(true);
								};
								if (ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort) && ImGui::BeginTooltip())
								{
									ImGui::PushTextWrapPos(ImGui::GetFontSize() * 35.0f);
									ImGui::TextUnformatted(hint);
									ImGui::PopTextWrapPos();
									ImGui::EndTooltip();
								}
							}else if (type == 2) // btn group
							{
								// buttons without hint.
								auto len = std::get<int>(vec[ii++]);
								
								ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 2);
								ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(2, 2));
								ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(GImGui->Style.ItemInnerSpacing.x / 2, GImGui->Style.ItemInnerSpacing.y));

								for (int i = 0; i < len; ++i)
								{
									auto label = std::get<char*>(vec[ii++]);
									char lsbxid[256];
									sprintf(lsbxid, "%s##btng%s_%d", label, strId, row);
									if (ImGui::SmallButton(lsbxid))
									{
										TableResponseInt(i);
									}
									if (i < len - 1) ImGui::SameLine();
								}
								ImGui::PopStyleVar(3);
							}
							else if (type == 3)
							{
								// buttons with hint.
								auto len = std::get<int>(vec[ii++]);
								ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 2);
								ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(2, 2));
								ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(GImGui->Style.ItemInnerSpacing.x / 3, GImGui->Style.ItemInnerSpacing.y));

								for (int i = 0; i < len; ++i)
								{
									auto label = std::get<char*>(vec[ii++]);
									auto hint = std::get<char*>(vec[ii++]);
									char lsbxid[256];
									sprintf(lsbxid, "%s##btng%s_%d", label, strId, row);
									if (ImGui::SmallButton(lsbxid))
									{
										TableResponseInt(i);
									}
									if (ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort) && ImGui::BeginTooltip())
									{
										ImGui::PushTextWrapPos(ImGui::GetFontSize() * 35.0f);
										ImGui::TextUnformatted(hint);
										ImGui::PopTextWrapPos();
										ImGui::EndTooltip();
									}
									if (i < len - 1) ImGui::SameLine();
								}
								ImGui::PopStyleVar(3);
							}
							else if (type == 4) //checkbox.
							{
								auto init = std::get<bool>(vec[ii++]);
								char lsbxid[256];
								sprintf(lsbxid, "##%s_%d_%d_chk", strId, row, i);
								ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(0, 1)); // reduce vertical padding
								if (ImGui::Checkbox(lsbxid, &init))
								{
									TableResponseBool(init);
								}
								ImGui::PopStyleVar();
							}
							else if (type == 5)
							{
								auto init = std::get<bool>(vec[ii++]);
								auto hint = std::get<char*>(vec[ii++]);
								char lsbxid[256];
								sprintf(lsbxid, "##%s_%d_%d_chk", strId, row, i);
								ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(0, 1)); // reduce vertical padding
								if (ImGui::Checkbox(lsbxid, &init))
								{
									TableResponseBool(init);
								}
								ImGui::PopStyleVar();
								if (ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort) && ImGui::BeginTooltip())
								{
									ImGui::PushTextWrapPos(ImGui::GetFontSize() * 35.0f);
									ImGui::TextUnformatted(hint);
									ImGui::PopTextWrapPos();
									ImGui::EndTooltip();
								}
							}
							else if (type == 6)
							{ // set color, doesn't apply to column.
								auto color = std::get<int>(vec[ii++]);
								ImGui::TableSetBgColor(ImGuiTableBgTarget_RowBg1, color);
								column -= 1;
							}else if (type==7)
							{
								// show a thumbnail.
								auto rgba = ReadString;
								auto pixh = ReadInt;
								auto pixw = ReadInt;

								auto ref = UIUseRGBA(rgba);
								int texid = ref.layerid == -1 ? (int)ImGui::GetIO().Fonts->TexID : (-ref.layerid - 1024);
								auto uv0 = ref.layerid == -1 ? ImVec2(0, 0) : ImVec2(ref.uvStart.x, ref.uvEnd.y);
								auto uv1 = ref.layerid == -1 ? ImVec2(1, 1) : ImVec2(ref.uvEnd.x, ref.uvStart.y);
								char dropdownLabel[256];
								sprintf(dropdownLabel, "%s##image", strId);
								// ImGui::Image((ImTextureID)texid, ImVec2(100, 100), uv1, uv0);
								if (ImPlot::BeginPlot(dropdownLabel, ImVec2(-1, 300), ImPlotFlags_NoLegend | ImPlotFlags_NoMenus | ImPlotFlags_Equal)) {
									static ImVec4 tint(1, 1, 1, 1);
									ImPlot::SetupAxes(nullptr, nullptr,
										ImPlotAxisFlags_NoLabel | ImPlotAxisFlags_NoTickLabels | ImPlotAxisFlags_PanStretch,
										ImPlotAxisFlags_NoLabel | ImPlotAxisFlags_NoTickLabels | ImPlotAxisFlags_PanStretch);
									ImPlot::SetupAxesLimits(0, 1, -1, 1);
									ImPlot::PlotImage(strId, (ImTextureID)texid, ImVec2(0, -ref.height / (float)ref.width / 2), ImVec2(1, ref.height / (float)ref.width / 2), uv1, uv0, tint);

									ImPlot::EndPlot();
								}
							}
						}
					}
					ImGui::EndTable();
					if (searchLen > 0)
					{
						// display info that only XX lines of YY total lines are shown.
						if (ImGui::Button("\uf00d"))
						{
							searchTxt[0] = 0;
						}
						ImGui::SameLine();
						ImGui::Text(" %c \uf0b0 %d/%d", "|/-\\"[(int)(ImGui::GetTime() / 0.05f) & 3], dispRows, rows);
					}
				}
				else 
					ptr += skip;
			},
			[&]
			{
			    // 21: DropdownBox
			    auto cid = ReadInt;
			    auto prompt = ReadString;
			    auto selected = ReadInt;
			    auto items_count = ReadInt;
			    
			    // ImGuiComboFlags flags = 0;
				char dropdownLabel[256];
				sprintf(dropdownLabel, "%s##BeginCombo%d", prompt, cid);

				auto items = std::vector<char*>();
				for (int n = 0; n < items_count; n++)
				{
					auto item = ReadString;
					items.push_back(item);
				}

				char* preview = (char*)""; // never mind, it won't change.
				if (selected >= 0 && selected < items_count) {
					preview = items[selected];
					
					// Process preview to remove all magic strings between < and >
					static char processed_preview[256]; // Static buffer for the processed preview
					int writePos = 0;
					bool insideTag = false;
					
					for (int i = 0; preview[i] != '\0' && writePos < 255; i++) {
						if (preview[i] == '<') {
							insideTag = true;
							continue;
						}
						if (preview[i] == '>') {
							insideTag = false;
							continue;
						}
						if (!insideTag) {
							processed_preview[writePos++] = preview[i];
						}
					}
					processed_preview[writePos] = '\0'; // Ensure null termination
					
					// If we processed any tags, use the processed string
					if (writePos != strlen(preview)) {
						preview = processed_preview;
					}
				}
				
				if (ImGui::BeginCombo(dropdownLabel, preview))
				{
					for (int n = 0; n < items_count; n++)
					{
						auto item = items[n];
						const bool is_selected = (n == selected);
						
						// Parse for thumbnail magic string: <I/i:name>
						bool has_thumbnail = false;
						bool show_in_item = false;
						std::string image_name;
						std::string display_text = item;
						
						if (strlen(item) > 4 && item[0] == '<' && (item[1] == 'I' || item[1] == 'i') && item[2] == ':')
						{
							// Find the closing bracket
							char* end_bracket = strchr(item + 3, '>');
							if (end_bracket != nullptr)
							{
								has_thumbnail = true;
								show_in_item = (item[1] == 'I'); // Show in item if capital I
								
								// Extract the image name
								image_name = std::string(item + 3, end_bracket - (item + 3));
								
								// Extract the display text (after the closing bracket)
								display_text = std::string(end_bracket + 1);
							}
						}
						display_text = display_text + "##cb#" + dropdownLabel;
						// Display the item with or without image
						if (has_thumbnail && show_in_item)
						{
							// Layout with image
							ImGui::BeginGroup();
							
							// Get the thumbnail image
							auto ref = UIUseRGBA(image_name.c_str());
							int texid = ref.layerid == -1 ? (int)ImGui::GetIO().Fonts->TexID : (-ref.layerid - 1024);
							auto uv0 = ref.layerid == -1 ? ImVec2(0, 0) : ImVec2(ref.uvStart.x, ref.uvEnd.y);
							auto uv1 = ref.layerid == -1 ? ImVec2(1, 1) : ImVec2(ref.uvEnd.x, ref.uvStart.y);
							
							// Display image + text side by side
							float height = ImGui::GetTextLineHeight() * 3;
							ImGui::Image((ImTextureID)(intptr_t)texid, ImVec2(height, height), uv0, uv1);
							ImVec2 alignment = ImVec2(.0f,  0.5f);
							ImGui::SameLine();
							ImGui::PushStyleVar(ImGuiStyleVar_SelectableTextAlign, alignment);
							bool selected = ImGui::Selectable(display_text.c_str(), is_selected, ImGuiSelectableFlags_None, ImVec2(0, height));
							ImGui::PopStyleVar();

							ImGui::EndGroup();
							
							if (selected)
							{
								stateChanged = true;
								WriteInt32(n);
							}
						}
						else
						{
							// Regular selectable without image
							if (ImGui::Selectable(display_text.c_str(), is_selected))
							{
								stateChanged = true;
								WriteInt32(n);
							}
							
							// Show image in tooltip if requested
							if (has_thumbnail && !show_in_item && ImGui::IsItemHovered())
							{
								if (ImGui::BeginTooltip())
								{
									auto ref = UIUseRGBA(image_name.c_str());
									int texid = ref.layerid == -1 ? (int)ImGui::GetIO().Fonts->TexID : (-ref.layerid - 1024);
									auto uv0 = ref.layerid == -1 ? ImVec2(0, 0) : ImVec2(ref.uvStart.x, ref.uvStart.y);
									auto uv1 = ref.layerid == -1 ? ImVec2(1, 1) : ImVec2(ref.uvEnd.x, ref.uvEnd.y);
									
									// Display a larger image in the tooltip
									float tooltip_width = 100.0f * GImGui->Style.ItemInnerSpacing.x;
									float aspect_ratio = ref.height / (float)ref.width;
									ImGui::Image((ImTextureID)(intptr_t)texid, 
												ImVec2(tooltip_width, tooltip_width * aspect_ratio), 
												uv0, uv1);
									
									ImGui::EndTooltip();
								}
							}
						}

						// Set the initial focus when opening the combo
						if (is_selected)
							ImGui::SetItemDefaultFocus();
					}
					ImGui::EndCombo();
				}
			},
			[&]
			{
				// 22: RadioButtons
				auto cid = ReadInt;
				auto prompt = ReadString;
				auto selected = ReadInt;
				auto items_count = ReadInt;
				auto same_line = ReadBool;

				auto items = std::vector<char*>();
				for (int n = 0; n < items_count; n++)
				{
					auto item = ReadString;
					items.push_back(item);
				}

				for (int n = 0; n < items_count; n++)
				{
					if (same_line && n > 0) ImGui::SameLine();
					if (ImGui::RadioButton(items[n], &selected, n))
					{
						stateChanged = true;
						WriteInt32(n);
					}
				}
			},
			[&]
			{
				// 23: viewport panel definition.
				auto scycle = ReadInt;
				auto len = ReadInt;
				auto wsBtr = ReadArr(unsigned char, len);
				
				aux_workspace_issued = false;

				auto needProcessWS = mystate.scycle != scycle && mystate.inited;
				if (!needProcessWS) 
					len = 0;

				aux_viewport_draw(wsBtr, len);

				if (needProcessWS)
				{
					mystate.scycle = scycle;
					stateChanged = true;
					auto cid = 1000;
					WriteBool(true);
				}

				if (aux_workspace_issued)
				{
					// use aux_workspace_ptr...
					stateChanged = true;
					auto cid = 999;
					WriteBytes(aux_workspace_ptr, aux_workspace_ptr_len);
				}

			},
			[&]
			{
				// 24: MenuBar
				auto cid = ReadInt;
				auto show = ReadBool;
				std::vector<int> path;

				std::function<void(int)> process = [&ptr, &process, &path, &stateChanged, &pr, &pid, &cid](const int pos) {
					path.push_back(pos);

					auto type = ReadInt;
					auto attr = ReadInt;
					auto label = ReadString;
				
					auto has_action = (attr & (1 << 0)) != 0;
					auto has_shortcut = (attr & (1 << 1)) != 0;
					auto selected = (attr & (1 << 2)) != 0;
					auto enabled = (attr & (1 << 3)) != 0;
				
					char* shortcut = nullptr;
					if (has_shortcut)
					{
						shortcut = ReadString;
					}
				
					if (type == 0)
					{
						if (ImGui::MenuItem(label, shortcut, selected, enabled) && has_action)
						{
							stateChanged = true;
							auto pathLen = (int)path.size();
							auto ret = new int[pathLen + 1];
							ret[0] = pathLen;
							for (int k = 0; k < pathLen; ++k) ret[k + 1] = path[k];
							WriteBytes(ret, (pathLen + 1) * 4);
						}
					}
					else
					{
						auto byte_cnt = ReadInt;
						if (ImGui::BeginMenu(label, enabled))
						{
							auto sub_cnt = ReadInt;
							for (int sub = 0; sub < sub_cnt; sub++) process(sub);
							ImGui::EndMenu();
						}
						else ptr += byte_cnt;
					}

					path.pop_back();
				};

				auto whole_offset = ReadInt;
				if ((flags & (1 << 14)) == 0)
					ptr += whole_offset;
				else
				{
					if (ImGui::BeginMenuBar())
					{
						auto all_cnt = ReadInt;
						for (int cnt = 0; cnt < all_cnt; cnt++)
							process(cnt);
						ImGui::EndMenuBar();
					}
					else ptr += whole_offset;
				}
			},
			[&]
			{
				// 25: Show image RGBA
				auto prompt = ReadString;
				auto rgba = ReadString;

				auto ref = UIUseRGBA(rgba);
				int texid = ref.layerid == -1 ? (int)ImGui::GetIO().Fonts->TexID : (-ref.layerid - 1024);
				auto uv0 = ref.layerid == -1 ? ImVec2(0, 0) : ImVec2(ref.uvStart.x, ref.uvEnd.y);
				auto uv1 = ref.layerid == -1 ? ImVec2(1, 1) : ImVec2(ref.uvEnd.x, ref.uvStart.y);
				char dropdownLabel[256];
				sprintf(dropdownLabel, "%s##image", prompt);
				// ImGui::Image((ImTextureID)texid, ImVec2(100, 100), uv1, uv0);
			    if (ImPlot::BeginPlot(dropdownLabel, ImVec2(-1, 300), ImPlotFlags_NoLegend | ImPlotFlags_NoMenus | ImPlotFlags_Equal)) {
					static ImVec4 tint(1,1,1,1);
					ImPlot::SetupAxes(nullptr, nullptr, 
						ImPlotAxisFlags_NoLabel|ImPlotAxisFlags_NoTickLabels|ImPlotAxisFlags_PanStretch, 
						ImPlotAxisFlags_NoLabel|ImPlotAxisFlags_NoTickLabels|ImPlotAxisFlags_PanStretch);
					ImPlot::SetupAxesLimits(0, 1, -1, 1);
					ImPlot::PlotImage(prompt, (ImTextureID)texid, ImVec2(0, -ref.height / (float)ref.width/2), ImVec2(1, ref.height / (float)ref.width/2), uv0, uv1, tint);
					
			        ImPlot::EndPlot();
			    }
			},
			
			[&]
			{
				// 26: ColorEdit control
				auto cid = ReadInt; // cid is the hash identifier of the prompted widget.
				
				auto prompt = ReadString;
				auto color_value = ReadInt;
				auto flags = ReadInt;
				
				// Convert RGBA8 color to ImGui format (float[4])
				float col[4];
				col[0] = ((color_value & 0x000000FF) >> 0) / 255.0f;  // R
				col[1] = ((color_value & 0x0000FF00) >> 8) / 255.0f;  // G
				col[2] = ((color_value & 0x00FF0000) >> 16) / 255.0f; // B
				col[3] = ((color_value & 0xFF000000) >> 24) / 255.0f; // A
				
				// Set up ImGui ColorEdit flags
				ImGuiColorEditFlags colorFlags = 0;
				if (!(flags & 1)) colorFlags |= ImGuiColorEditFlags_NoAlpha; // Invert flag since we're passing "alphaEnabled"
				
				// Show color picker with ImGui
				bool changed = ImGui::ColorEdit4(prompt, col, colorFlags);
				
				if (changed)
				{
					// Convert back to RGBA8 format
					uint32_t r = (uint32_t)(col[0] * 255.0f);
					uint32_t g = (uint32_t)(col[1] * 255.0f);
					uint32_t b = (uint32_t)(col[2] * 255.0f);
					uint32_t a = (uint32_t)(col[3] * 255.0f);
					
					uint32_t result = (a << 24) | (b << 16) | (g << 8) | r;
					
					// Send result back
					stateChanged = true;
					WriteInt32(result);
				}
			},

			[&]
			{
				// 27: TabButtons.
				auto cid = ReadInt;
				auto prompt = ReadString;
				auto selected = ReadInt;
				auto items_count = ReadInt;

				auto items = std::vector<char*>();
				for (int n = 0; n < items_count; n++)
				{
					auto item = ReadString;
					items.push_back(item);
				}

				if (ImGui::BeginTabBar(prompt, ImGuiTabBarFlags_TabListPopupButton | ImGuiTabBarFlags_FittingPolicyScroll))
				{
					for (int n = 0; n < items_count; n++)
						if (ImGui::BeginTabItem(items[n]))
						{
							if (n != selected)
							{
								stateChanged = true;
								WriteInt32(n);
							}
							ImGui::EndTabItem();
						}					
					ImGui::EndTabBar();
				}
			},
			[&]
			{
				// 28: TextBox
				auto cid = ReadInt;
				auto prompt = ReadString;
				auto content = ReadString;
				auto copyButton = ReadBool;

				char textBoxLabel[256];
				sprintf(textBoxLabel, "%s##textbox%d", prompt, cid);

				ImGui::InputTextMultiline(textBoxLabel, content, strlen(content),
					ImVec2(-FLT_MIN, ImGui::GetTextLineHeight() * 5),
					ImGuiInputTextFlags_ReadOnly);

				if (copyButton && ImGui::Button("\uf0c5 Copy to Clipboard"))
					ImGui::SetClipboardText(content);
			},
			[&]
			{
				// 29: DragVector2
				auto cid = ReadInt;
				auto prompt = ReadString;

				float* valX = (float*)ptr; ptr += 4;
				float* valY = (float*)ptr; ptr += 4;
				auto step = ReadFloat;
				auto min_v = ReadFloat;
				auto max_v = ReadFloat;

				char dragLabel[256];
				sprintf(dragLabel, "%s##dragvec2_%d", prompt, cid);
				
				float values[2] = { *valX, *valY };
				
				if (ImGui::DragFloat2(dragLabel, values, step, min_v, max_v))
				{
					stateChanged = true;
					WriteFloat2(values[0], values[1]);
				}
			},
			[&]
			{
				// 30: image list. horizontally.

				auto cid = ReadInt;
				auto prompt = ReadString;
				auto hide = ReadBool;
				auto h = ReadInt;
				h *= dpiScale;

				auto len = ReadInt;
				auto selecting = ReadInt;
				if (!hide) ImGui::SeparatorText(prompt);
				char lsbxid[256];
				sprintf(lsbxid, "%s##imgls", prompt);

				struct img_cv
				{
					ImTextureID texid;
					ImVec2 wh, uv0, uv1;
					char* display, * hint;
					float left;
					float stride;
					int hintLen;
					float txtleft;
				};
				std::vector<img_cv> imgs;
				float w = 4 * dpiScale;
				auto p = 4 * dpiScale;

				auto fh = ImGui::GetTextLineHeightWithSpacing();
				for (int i=0; i<len; ++i)
				{
					auto img_name = ReadString;
					auto display = ReadString;
					auto hintLen = ReadStringLen;
					auto tooltip = ReadString;
					auto ref = UIUseRGBA(img_name);
					int texid = ref.layerid == -1 ? 0 : (-ref.layerid - 1024);
					auto uv0 = ref.layerid == -1 ? ImVec2(0, 0) : ImVec2(ref.uvStart.x, ref.uvEnd.y);
					auto uv1 = ref.layerid == -1 ? ImVec2(1, 1) : ImVec2(ref.uvEnd.x, ref.uvStart.y);
					float aspect_ratio = ref.height / (float)ref.width;
					auto ww = h / aspect_ratio;
					auto txt_sz = ImGui::CalcTextSize(display, 0, false, std::max(100.0f * dpiScale, ww));
					auto stride = ww;
					auto left = w;
					auto txtleft = left;
					if (txt_sz.x > ww) // text width > img
					{
						stride = txt_sz.x;
						left += (txt_sz.x - ww) / 2;
					}else
					{
						txtleft += (ww - txt_sz.x) / 2;
					}
					fh = std::max(txt_sz.y, fh);
					imgs.push_back({ (ImTextureID)(intptr_t)texid,
						ImVec2(ww, h),
						uv0, uv1, display, tooltip, left, stride, hintLen, txtleft });
					w += stride + p * 3;
				}

				ImVec2 text_size = ImGui::CalcTextSize("N/A");
				if (ImGui::BeginChild(lsbxid, ImVec2(ImGui::GetContentRegionAvail().x, h + fh + 20 * dpiScale +(w< ImGui::GetContentRegionAvail().x ?0: ImGui::GetStyle().ScrollbarSize)),
					true, ImGuiWindowFlags_HorizontalScrollbar))
				{
					for (int n = 0; n < len; n++)
					{
						sprintf(lsbxid, "##ib%s_%d", prompt, n);
						ImGui::SetCursorPos(ImVec2(std::min(imgs[n].left,imgs[n].txtleft), p)); // Reset cursor to image position
						if (ImGui::Selectable(lsbxid, selecting == n, 0, ImVec2(imgs[n].stride+p, h+fh+p))) {
							stateChanged = true;
							selecting = n;
							WriteInt32(n)
						}

						if (imgs[n].hintLen > 0 && ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort))
							ImGui::SetTooltip(imgs[n].hint);

						ImGui::SetCursorPos(ImVec2(imgs[n].left + p, p * 2)); // Reset cursor to image position
						if (imgs[n].texid == (ImTextureID)0)
						{
							// Draw a red cross to indicate texture is not ready
							ImDrawList* draw_list = ImGui::GetWindowDrawList();
							ImVec2 pos = ImGui::GetCursorScreenPos();
							ImVec2 size = imgs[n].wh;
							ImU32 red_color = IM_COL32(255, 0, 0, 255);
							
							// Draw diagonal lines forming an X
							draw_list->AddRect(pos, ImVec2(pos.x + size.x, pos.y + size.y), red_color, 2.0f);
							// draw_list->AddLine(pos, ImVec2(pos.x + size.x, pos.y + size.y), red_color, 2.0f);
							// draw_list->AddLine(ImVec2(pos.x + size.x, pos.y), ImVec2(pos.x, pos.y + size.y), red_color, 2.0f);
							
							// Draw "N/A" text in the middle of the red cross
							ImVec2 text_pos = ImVec2(
								pos.x + (size.x - text_size.x) * 0.5f,
								pos.y + (size.y - text_size.y) * 0.5f
							);
							draw_list->AddText(text_pos, red_color, "N/A");

							// Advance cursor to account for the drawn area
							ImGui::Dummy(size);
						}else
							ImGui::Image(imgs[n].texid, imgs[n].wh, imgs[n].uv0, imgs[n].uv1);

						ImGui::SetCursorPos(ImVec2(imgs[n].txtleft + p, p * 3 + imgs[n].wh.y)); // Reset cursor to image position
						ImGui::PushTextWrapPos(imgs[n].left + p + imgs[n].stride);
						ImGui::TextWrapped(imgs[n].display);

						if (selecting == n)
							ImGui::SetItemDefaultFocus();
					}
					ImGui::EndChild();
				}
			}
		};
		//std::cout << "draw " << pid << " " << str << ":"<<i<<"/"<<plen << std::endl;
		// char windowLabel[256];
		// sprintf(windowLabel, "%s##pid%d", str.c_str(), pid);



		// Size:
		auto pivot = ImVec2(myPivotX, myPivotY);
		if ((flags & 8) !=0)
		{
			// not resizable
			if ((flags & (16)) != 0) { // autoResized
				window_flags |= ImGuiWindowFlags_AlwaysAutoResize;
				mystate.minH = panelHeight;
				mystate.minW = panelWidth;
				ImGui::SetNextWindowSizeConstraints(ImVec2(10, 10), ImVec2(FLT_MAX, FLT_MAX), StepWndSz, &mystate);
			}
			else { // must have initial w/h
				window_flags |= ImGuiWindowFlags_NoResize;
				ImGui::SetNextWindowSize(ImVec2(panelWidth, panelHeight));
			}
		}else
		{
			// initial w/h, maybe read from ini.
			ImGui::SetNextWindowSize(ImVec2(panelWidth, panelHeight), ImGuiCond_FirstUseEver);
		}


		auto fixed = (flags & 32) == 0;
		auto modal = (flags & 128) != 0;
		if (except.length() > 0) modal = false; //no modal for erronous window.

		auto initdocking = (flags >> 9) & 0b111;
		auto dockSplit = (flags & (1 << 12)) != 0;

		if ((flags & (1 << 14)) != 0)
		{
			window_flags |= ImGuiWindowFlags_MenuBar;
		}

		auto pos_cond = ImGuiCond_Appearing;
		if (fixed) {
			window_flags |= ImGuiWindowFlags_NoMove;
			pos_cond = ImGuiCond_Always;
		}
		ImVec2 applyPos, applyPivot = pivot;
		if (relPanel < 0 || im.find(relPanel) == im.end()) {
			applyPos = ImVec2(panelLeft + vp->Pos.x + vp->Size.x * relPivotX, panelTop + vp->Pos.y + vp->Size.y * relPivotY);
		}
		else
		{
			auto& wnd = im[relPanel];
			applyPos = ImVec2(panelLeft + wnd.Pos.x + wnd.Size.x * relPivotX, panelTop + wnd.Pos.y + wnd.Size.y * relPivotY);
		}

		if (modal)
		{
			// window_flags |= ImGuiWindowFlags_NoCollapse;
			// window_flags |= ImGuiDockNodeFlags_NoCloseButton;
			// window_flags |= ImGuiWindowFlags_NoDocking;
			// window_flags |= ImGuiWindowFlags_Modal;
			// sometimes needed, sometimes no.
			//if (relPanel < 0)
			//	applyPos = ImVec2(io.MousePos.x, io.MousePos.y);
			applyPos = ImVec2(vp->Pos.x + vp->Size.x / 2, vp->Pos.y + vp->Size.y / 2);
			applyPivot = ImVec2(0.5, 0.5);
		}

		if (!fixed && !modal && ((initdocking & 4) != 0))
		{
			if (mystate.inited == 0) {
				ImGuiAxis requiredAxis = (initdocking & 1) == 0 ? ImGuiAxis_X : ImGuiAxis_Y;
				int sgn = (initdocking & 2) == 0 ? 1 : -1;

				auto name = str;
				ImGuiID window_id = ImHashStr(name);
				ImGuiWindowSettings* settings = ImGui::FindWindowSettingsByID(window_id);
				if (settings == NULL) {
					ImGuiDockNode* root = ImGui::DockBuilderGetNode(dockspace_id);
					auto central = ImGui::DockNodeGetRootNode(root)->CentralNode;
					auto snode = central;
					auto pnode = central->ParentNode;
					auto docked = false;
					// traverse up central to see if a node has splitted with required direction.
					while (pnode != nullptr)
					{
						if (pnode->SplitAxis == requiredAxis)
						{
							auto othernode = pnode->ChildNodes[0] == snode ? pnode->ChildNodes[1] : pnode->ChildNodes[0];
							if (requiredAxis == ImGuiAxis_X && ImSign(othernode->Pos.x - snode->Pos.x) * sgn > 0 ||
								requiredAxis == ImGuiAxis_Y && ImSign(othernode->Pos.y - snode->Pos.y) * sgn > 0)
							{
								ImGuiID oid;
								if (!dockSplit) {
									// todo: balance windows count for descendant nodes.
									while (!othernode->IsLeafNode())
										othernode = othernode->ChildNodes[0];
									oid = othernode->ID;

								}else
								{
									auto ppos = othernode->Pos;
									while (!othernode->IsLeafNode())
									{
										if (othernode->ChildNodes[0]->Pos.x!=ppos.x || othernode->ChildNodes[0]->Pos.y!=ppos.y)
											othernode = othernode->ChildNodes[0];
										else othernode = othernode->ChildNodes[1];
									}
									if (othernode->Pos.x==ppos.x && othernode->Pos.y==ppos.y)
									{
										ImGuiDir dir;
										if (requiredAxis == ImGuiAxis_X)
											dir = ImGuiDir_Down;
										if (requiredAxis == ImGuiAxis_Y)
											dir = ImGuiDir_Right;
										oid = ImGui::DockBuilderSplitNode(othernode->ID, dir, 0.5f, NULL, NULL);
									}
									else
										oid = othernode->ID;
								}

								ImGui::DockBuilderDockWindow(name, oid);
								docked = true;
								break;
							}
						}
						snode = pnode;
						pnode = pnode->ParentNode;
					}


					if (!docked) {
						ImGuiDir dir;
						if (requiredAxis == ImGuiAxis_X && sgn == 1)
							dir = ImGuiDir_Right;
						if (requiredAxis == ImGuiAxis_X && sgn == -1)
							dir = ImGuiDir_Left;
						if (requiredAxis == ImGuiAxis_Y && sgn == 1)
							dir = ImGuiDir_Up;
						if (requiredAxis == ImGuiAxis_Y && sgn == -1)
							dir = ImGuiDir_Down;

						// ImGuiContext& g = *GImGui;
						// ImGuiDockNode* node = ImGui::DockBuilderGetNode(ImGui::GetID("CycleGUIMainDock"));
						// auto pp = ImGui::DockContextFindNodeByID(&g, central->ID);
						// std::cout << "central id=" << central->ID <<", dlen="<< g.DockContext.Nodes.Data.Size<< ", ptr=" << (int)pp << std::endl;
						// std::cout << "node sz=" << node->Size.x<<"," << node->Size.y << ", pos=" << node->Pos.x<<"," << node->Pos.x << std::endl;

						ImGui::DockBuilderDockWindow(name, ImGui::DockBuilderSplitNode(central->ID, dir, 0.33333f, NULL, NULL));
					}
				}
			}
		}
		else {
			ImGui::SetNextWindowPos(applyPos, pos_cond, applyPivot);
		}

		// Decorators
		if ((flags & 4) == 0)
			window_flags |= ImGuiWindowFlags_NoTitleBar;

		if ((flags & 64) !=0)
		{
			ImGuiWindowClass topmost;
			topmost.ClassId = ImHashStr("TopMost");
			topmost.ViewportFlagsOverrideSet = ImGuiViewportFlags_TopMost | ImGuiViewportFlags_NoAutoMerge;
			window_flags |= ImGuiWindowFlags_NoDocking;

			ImGui::SetNextWindowClass(&topmost);
		}

		bool* p_show = (flags & 256) ? &wndShown : NULL;

		if (modal) {
			if (modalpid != -1)
				no_modal_pids.push_back(modalpid);
			if (std::find(no_modal_pids.begin(),no_modal_pids.end(),pid)== no_modal_pids.end() && modalpid==-1){
				ImGui::OpenPopup(str);
				ImGui::BeginPopupModal(str, p_show, window_flags); // later popup modal should override previous popup modals.
				modalpid = pid;
			}else
				ImGui::Begin(str, p_show, window_flags);
		}
		else
			ImGui::Begin(str, p_show, window_flags);

		//ImGui::PushItemWidth(ImGui::GetFontSize() * -6);
		if (mystate.pendingAction && cgui_refreshed && mystate.flipper!=flipper)
			mystate.pendingAction = false;

		auto should_block = flags & 1 || /*mystate.pendingAction ||*/ (except.length() > 0) ;
		if (should_block) // freeze.
		{
			ImGui::BeginDisabled(true);
		}
		bool beforeLayoutStateChanged = stateChanged;
		while (true)
		{
			auto ctype = ReadInt;
			if (ctype == 0x04030201) break;
			UIFuns[ctype]();
		}
		if (should_block) // freeze.
		{
			ImGui::EndDisabled();
		}

		if (except.length()>0)
		{
			// show message and retry button.
			GImGui->CurrentWindow->DC.CursorPos.x = GImGui->CurrentWindow->Pos.x + 16;
			GImGui->CurrentWindow->DC.CursorPos.y = GImGui->CurrentWindow->Pos.y + 48;
            ImGui::PushStyleColor(ImGuiCol_ChildBg, IM_COL32(128, 0, 0, 200));
            if (ImGui::BeginChild("ResizableChild", ImVec2(-FLT_MIN, -FLT_MIN), true, ImGuiTableFlags_NoSavedSettings)){
				ImGui::Text("UI operation throws error：");
				ImGui::Separator();
				if (ImGui::Button("\uf0c5 Copy Error Message"))
					ImGui::SetClipboardText(except.c_str());
				ImGui::TextWrapped(except.c_str());
	            // ImGui::InputTextMultiline("##source", (char*)except.c_str(), except.length(), ImVec2(-FLT_MIN, ImGui::GetTextLineHeight() * 16), ImGuiInputTextFlags_ReadOnly);
				ImGui::SeparatorText("Actions");
				
				if (mystate.pendingAction)
				{
					ImGui::Text("Feedback...");
				}else{
					// exception handler is on Panel.cs PushState(-2, xxx).
					if (ImGui::Button("Retry"))
					{
						//cid:-2 type int 0.
						*(int*)pr=pid; pr+=4; *(int*)pr=-2; pr+=4; *(int*)pr=1; pr+=4; *(int*)pr=0; pr+=4;
						stateChanged = true;
					}
					ImGui::SameLine();
					ImGui::PushStyleColor(ImGuiCol_Button, IM_COL32(255, 0, 0, 255));
					if (ImGui::Button("Shutdown"))
					{
						// close this cid.
						//cid:-2 type int 1.
						*(int*)pr=pid; pr+=4; *(int*)pr=-2; pr+=4; *(int*)pr=1; pr+=4; *(int*)pr=1; pr+=4;
						stateChanged = true;
					}
					ImGui::PopStyleColor();
				}
				ImGui::EndChild();
			}
            ImGui::PopStyleColor();
		}
		
		if (!beforeLayoutStateChanged && stateChanged)
		{
			mystate.pendingAction = true;
			mystate.time_start_interact = ui.getMsFromStart();
		}

		mystate.inited += 1;
		mystate.Pos = ImGui::GetWindowPos();
		mystate.Size = ImGui::GetWindowSize();
		mystate.im_wnd = ImGui::GetCurrentWindow();

		if (mystate.pendingAction && mystate.time_start_interact+1000<ui.getMsFromStart())
		{
			ImGuiWindow* window = mystate.im_wnd;
	        // Render
			auto radius = 20;
			ImVec2 pos(mystate.Pos.x+mystate.Size.x/2,mystate.Pos.y+mystate.Size.y/2);
			
			// Render
	        window->DrawList->PathClear();
	        
	        int num_segments = 30;
			int time = ui.getMsFromStart();
			int start = abs(ImSin(GImGui->Time)*(num_segments-9));
	        
	        const float a_min = IM_PI*2.0f * ((float)start) / (float)num_segments;
	        const float a_max = IM_PI*2.0f * ((float)num_segments-3) / (float)num_segments;

	        const ImVec2 centre = ImVec2(pos.x, pos.y);
	        
	        for (int i = 0; i < num_segments; i++) {
	            const float a = a_min + ((float)i / (float)num_segments) * (a_max - a_min);
	            window->DrawList->PathLineTo(ImVec2(centre.x + ImCos(a+GImGui->Time*8) * radius,
	                                                centre.y + ImSin(a+GImGui->Time*8) * radius));
	        }

	        window->DrawList->PathStroke((ImU32)0xffffffff, false, 5);
		}
		im.insert_or_assign(pid, mystate);

		if (modalpid == pid)
			ImGui::EndPopup();
		else
			ImGui::End();
		//ImGui::PopStyleVar(1);

		mystate.flipper = flipper;
	}

	cacheBase::finish();
	cgui_refreshed = false;

	if (modalpid == -1 && no_modal_pids.size() > 0)
		no_modal_pids.pop_back();

	if (stateChanged)
		stateCallback(ui_buffer, pr - ui_buffer);

}

ui_state_t ui;
grating_param_t grating_params;

bool initialize()
{
	ui.started_time = std::chrono::high_resolution_clock::now();
	return true;
}

static bool initialized = initialize();

int getInterestedViewport(GLFWwindow* window)
{
	// select the active workspace.
	int stackpos = 999;
	int ret = 0;
	ImGuiContext& g = *ImGui::GetCurrentContext();
    for (int i = 1; i < MAX_VIEWPORTS; ++i) {
        auto& viewport = ui.viewports[i];
        if (!viewport.active) continue; // Skip inactive viewports
		if (window != nullptr && viewport.imguiWindow->Viewport->PlatformHandle != window) continue;
		auto windowPos = ui.viewports[i].imguiWindow->Pos;
		auto windowSize = ui.viewports[i].imguiWindow->Size;
        bool isInside = ui.mouseX >= windowPos.x && ui.mouseX <= windowPos.x + windowSize.x &&
                        ui.mouseY >= windowPos.y && ui.mouseY <= windowPos.y + windowSize.y;
		if (!isInside) continue;
		int mystackpos = 999;
		for (int j = 0; j < g.Windows.Size; j++)
	    {
	        if (g.Windows[j] == viewport.imguiWindow)
	        {
				if (j<stackpos)
				{
					stackpos = j;
					ret = i;
				}
				break;
	        }
	    }
	}
	return ret;
}



bool widget_definition::isKJHandling()
{
	return ui.loopCnt < kj_handle_loop + 1;
}


bool parse_chord_global(const std::string& key);
void widget_definition::process_keyboardjoystick()
{
	keyboard_press.clear();
	for (int i = 0; i < keyboard_mapping.size(); ++i){
#ifndef __EMSCRIPTEN__
		auto p=parse_chord_global(keyboard_mapping[i]);
#else
		auto p=parse_chord(keyboard_mapping[i]);
#endif
		keyboard_press.push_back(p);

		// if at least one key bound is pressed, this widget is KJ handling.
		if (p) kj_handle_loop = ui.loopCnt;
	}

	// todo: do joysticks:
	keyboardjoystick_map();
	previouslyKJHandled = isKJHandling();
}

void gesture_operation::pointer_down()
{
	// trigger is any widget need down attention.

}

void gesture_operation::pointer_move()
{
}

void gesture_operation::pointer_up()
{
}

void select_operation::pointer_move()
{
}

void select_operation::pointer_up()
{
	if (selecting)
	{
		selecting = false;
		if (selecting_mode == click)
		{
			if (abs(ui.mouseX - clickingX)<10 && abs(ui.mouseY - clickingY)<10)
			{
				// trigger, (postponed to draw workspace)
				extract_selection = true;
			}
		}else
		{
			extract_selection = true;
		}
	}
}

void follow_mouse_operation::destroy()
{
	for (int i = 0; i < referenced_objects.size(); ++i)
		referenced_objects[i].remove_from_obj();
	// printf("removed reference for follow mouse operation.\n");
}

guizmo_operation::~guizmo_operation()
{
	for(int i=0; i < referenced_objects.size();++i)
		referenced_objects[i].remove_from_obj();
	// printf("removed reference for guizmo operation.\n");
}

float viewport_state_t::mouseX()
{
	return ui.mouseX - disp_area.Pos.x;
	return imguiWindow == nullptr ? ui.mouseX - disp_area.Pos.x : ui.mouseX - imguiWindow->DC.CursorStartPos.x;
}

float viewport_state_t::mouseY()
{
	return ui.mouseY - disp_area.Pos.y;
	return imguiWindow == nullptr ? ui.mouseY - disp_area.Pos.y : ui.mouseY - imguiWindow->DC.CursorStartPos.y;
}


uint64_t ui_state_t::getMsFromStart() {
	return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::high_resolution_clock::now() - started_time).count();
}
float ui_state_t::getMsGraphics() {
	return (float)((std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::high_resolution_clock::now() - started_time).count()) & 0x7fffff);
}

template <typename workspaceType>
void BeginWorkspace(int id, std::string state_name, viewport_state_t& viewport)
{
	if (viewport.workspace_state.size() >= 16)
	{
		printf("workspace operation stack too deep, current depth=%d\n", viewport.workspace_state.size());
	}

	// effectively eliminate action state.
	_clear_action_state();
	viewport.workspace_state.push_back(viewport.workspace_state.back());

	auto& wstate = viewport.workspace_state.back();
	wstate.id = id;
	wstate.name = state_name;
	wstate.operation = new workspaceType();

	// Remove null pointers and add references for all containers
	auto process_container = [](std::vector<reference_t>& container) {
		container.erase(
			std::remove_if(container.begin(), container.end(),
				[](const namemap_t& ref) { return ref.obj == nullptr; }
			),
			container.end()
		);
		for (size_t i = 0; i < container.size(); i++) {
			container[i].obj_reference_idx = container[i].obj->push_reference([&container]() { return &container; }, i);
		}
	};

	process_container(wstate.no_cross_section);
	process_container(wstate.hidden_objects);
	process_container(wstate.selectables);
	process_container(wstate.sub_selectables);

	printf("begin workspace %d=%s on vp %d\n", id, state_name.c_str(), &viewport-ui.viewports);
}

void destroy_state(viewport_state_t* state)
{
	auto& wstate = state->workspace_state.back();
	// pop should modify me_obj's reference
	auto remove_refs = [](std::vector<reference_t>& container) {
		for (auto& tr : container) 
			if (tr.obj!=nullptr)
				tr.remove_from_obj();
	};
	
	remove_refs(wstate.no_cross_section);
	remove_refs(wstate.hidden_objects);
	remove_refs(wstate.selectables); 
	remove_refs(wstate.sub_selectables);

	state->workspace_state.pop_back();
	wstate.operation->destroy();
}

void viewport_state_t::pop_workspace_state()
{
	if (workspace_state.size() == 1)
		throw "not allowed to pop default action.";

	auto& wstate = workspace_state.back();
	printf("end operation %d:%s\n", wstate.id, wstate.name.c_str());
	
	DeapplyWorkspaceState();
	destroy_state(this);

	ReapplyWorkspaceState();
}


int test_rmpan = 0;
void mouse_button_callback(GLFWwindow* window, int button, int action, int mods)
{
	// todo: if 
	if (ImGui::GetIO().WantCaptureMouse && (window!=nullptr && ImGui::GetMainViewport()->PlatformHandle == window)) //if nullptr it's touch.
		return;
	
	if (action == GLFW_PRESS)
	{
		if (!ui.mouseTriggered){
			// select the active workspace.
			ImGuiContext& g = *ImGui::GetCurrentContext();
			ui.mouseCaptuingViewport = getInterestedViewport(window);
		}
		
		auto& wstate = ui.viewports[ui.mouseCaptuingViewport].workspace_state.back();
		switch_context(ui.mouseCaptuingViewport);

		switch (button)
		{
		case GLFW_MOUSE_BUTTON_LEFT:
			ui.mouseLeft = true;
			ui.mouseLeftDownLoopCnt = ui.loopCnt;
			wstate.operation->pointer_down();
			break;
		case GLFW_MOUSE_BUTTON_MIDDLE:
			ui.mouseMiddle = true;
			ui.viewports[ui.mouseCaptuingViewport].refreshStare = true;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			ui.mouseRight = true;
			test_rmpan = 0;
			// wstate.operation->canceled();
			break;
		}
		ui.viewports[ui.mouseCaptuingViewport].camera.extset = false;
	}
	else if (action == GLFW_RELEASE)
	{
		auto& wstate = ui.viewports[ui.mouseCaptuingViewport].workspace_state.back();
		switch_context(ui.mouseCaptuingViewport);
		switch (button)
		{
		case GLFW_MOUSE_BUTTON_LEFT:
			ui.mouseLeft = false;
			wstate.operation->pointer_up();
			break;
		case GLFW_MOUSE_BUTTON_MIDDLE:
			ui.mouseMiddle = false;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			ui.mouseRight = false;
			if (test_rmpan < 3)
				wstate.operation->canceled();
			break;
		}
		ui.mouseTriggered = false;
	}
}

void cursor_position_callback(GLFWwindow* window, double rx, double ry)
{
	// auto vp = ImGui::GetMainViewport();
	// auto central = ImGui::DockNodeGetRootNode(dockingRoot)->CentralNode;

	int xpos=0, ypos=0;
	if (window!=nullptr)
		glfwGetWindowPos(window, &xpos, &ypos);
	xpos += rx;
	ypos += ry;

	double deltaX = xpos - ui.mouseX;
	double deltaY = ypos - ui.mouseY;

	deltaX = ImSign(deltaX) * std::min(100.0, abs(deltaX));
	deltaY = ImSign(deltaY) * std::min(100.0, abs(deltaY));

	//ImGuiDockNode* node = ImGui::DockBuilderGetNode(ImGui::GetID("CycleGUIMainDock"));
	ui.mouseX = xpos;// - central->Pos.x + vp->Pos.x;
	ui.mouseY = ypos;


	if (ui.mouseCaptuingViewport==0){
		if (ImGui::GetIO().WantCaptureMouse)
			return;
		if (!dockingRoot) return;
	}

	auto& wstate = ui.viewports[ui.mouseCaptuingViewport].workspace_state.back();
	switch_context(ui.mouseCaptuingViewport);
	auto camera = &ui.viewports[ui.mouseCaptuingViewport].camera;
		// wstate.operation->pointer_move(); ???
	if (ui.mouseMiddle || ui.mouseRight) {
		if (camera->extset) return; //break current camera manipulation operation.
		if (ui.mouseMiddle && ui.mouseRight)
		{
			camera->Rotate(deltaY * 1.5f, -deltaX);
		}
		else if (ui.mouseMiddle)
		{
			// Handle middle mouse button dragging
			if (camera->mmb_freelook) {
				// Free look mode - rotate around current position
				camera->Rotate(deltaY * 1.5f, -deltaX);
			}
			else {
				// Traditional orbital controls
				camera->RotateAzimuth(-deltaX);
				camera->RotateAltitude(deltaY * 1.5f);
			}
		}
		else if (ui.mouseRight)
		{
			// Handle right mouse button dragging
			// wstate.operation->canceled();
			test_rmpan += abs(deltaX) + abs(deltaY);

			// if pitch exceed certain value, pan on camera coordination.
			auto d = camera->distance * 0.0016f;
			camera->PanLeftRight(-deltaX * d);
			if (abs(camera->Altitude) < M_PI_4)
			{
				auto s = sin(camera->Altitude);
				auto c = cos(camera->Altitude);
				auto fac = 1 - s / 0.7071;
				camera->ElevateUpDown(deltaY * d * fac);
				camera->PanBackForth(deltaY * d * (1 - fac) - (deltaY * d * fac * s / c));

			}
			else {
				camera->PanBackForth(deltaY * d);
			}
		}
	}
	else
	{
		// invoke pointer move operation if no camera operation is performed
		wstate.operation->pointer_move();
	}
}

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset)
{
	auto iv = getInterestedViewport(window);
	if (iv==0 && ImGui::GetIO().WantCaptureMouse)
		return;

	auto camera = &ui.viewports[ui.mouseCaptuingViewport].camera;
	// Handle mouse scroll
	if (ui.mouseMiddle)
	{
		// go ahead.
		ui.viewports[iv].camera.GoFrontBack(yoffset * camera->distance * 0.1f);
	}
	else {
		// zoom
		if (camera->mmb_freelook)
			ui.viewports[iv].camera.GoFrontBack(yoffset * camera->distance * 0.1f);
		else
			ui.viewports[iv].camera.Zoom(-yoffset * 0.1f);
	}
}

void key_callback(GLFWwindow* window, int key, int scancode, int action, int mods) {
	// Check if the Ctrl key (left or right) is pressed
	if (key == GLFW_KEY_LEFT_CONTROL || key == GLFW_KEY_RIGHT_CONTROL) {
		if (action == GLFW_PRESS) {
			ui.ctrl = true;
		}
		else if (action == GLFW_RELEASE) {
			ui.ctrl = false;
		}
	}	if (key == GLFW_KEY_ESCAPE)
	{
		if (action == GLFW_PRESS)
		{
			// if not submitted, reset.
			_clear_action_state();
		}
	}
}

void touch_callback(std::vector<touch_state> touches)
{
	ui.touches = touches;
	for (int i = 0; i < ui.touches.size(); ++i)
		if (ui.prevTouches.find(ui.touches[i].id) == ui.prevTouches.end())
			ui.touches[i].starting = true;
}


//???
void _clear_action_state()
{

}


// Store original handlers
struct WindowCallbacks {
    GLFWmousebuttonfun mouseButtonCallback = nullptr;
    GLFWcursorposfun cursorPosCallback = nullptr;
    GLFWscrollfun scrollCallback = nullptr;
    GLFWkeyfun keyCallback = nullptr;
};

// Map to store callbacks for each window
std::unordered_map<GLFWwindow*, WindowCallbacks> windowCallbacks;

// Wrapper functions that call both our handlers and original handlers
void mouse_button_callback_wrapper(GLFWwindow* window, int button, int action, int mods) {
    mouse_button_callback(window, button, action, mods);
    if (windowCallbacks[window].mouseButtonCallback)
        windowCallbacks[window].mouseButtonCallback(window, button, action, mods);
}

void cursor_position_callback_wrapper(GLFWwindow* window, double rx, double ry) {
    cursor_position_callback(window, rx, ry);
    if (windowCallbacks[window].cursorPosCallback)
        windowCallbacks[window].cursorPosCallback(window, rx, ry);
}

void scroll_callback_wrapper(GLFWwindow* window, double xoffset, double yoffset) {
    scroll_callback(window, xoffset, yoffset);
    if (windowCallbacks[window].scrollCallback)
        windowCallbacks[window].scrollCallback(window, xoffset, yoffset);
}

void key_callback_wrapper(GLFWwindow* window, int key, int scancode, int action, int mods) {
    key_callback(window, key, scancode, action, mods);
    if (windowCallbacks[window].keyCallback)
        windowCallbacks[window].keyCallback(window, key, scancode, action, mods);
}

void mount_window_handlers(GLFWwindow* window) {
	// in case the window is not prepared yet.
	if (!window) return;

    // Check if handlers are already mounted for this window
    if (windowCallbacks.find(window) != windowCallbacks.end()) {
        return; // Already mounted
    }

    // Store existing callbacks
    WindowCallbacks callbacks;
    callbacks.mouseButtonCallback = glfwSetMouseButtonCallback(window, mouse_button_callback_wrapper);
    callbacks.cursorPosCallback = glfwSetCursorPosCallback(window, cursor_position_callback_wrapper); 
    callbacks.scrollCallback = glfwSetScrollCallback(window, scroll_callback_wrapper);
    callbacks.keyCallback = glfwSetKeyCallback(window, key_callback_wrapper);
    windowCallbacks[window] = callbacks;
}

void aux_workspace_notify(unsigned char* news, int length)
{
	aux_workspace_issued = true;
	aux_workspace_ptr = news;
	aux_workspace_ptr_len = length;
}
void aux_viewport_draw(unsigned char* wsptr, int len) {
    ImVec2 contentRegion = ImGui::GetContentRegionAvail();
	if (contentRegion.x < 64) contentRegion.x = 64;
	if (contentRegion.y < 64) contentRegion.y = 64;
	ImVec2 contentPos = ImGui::GetCursorScreenPos();    // Position (top-left corner)

	auto im_wnd = ImGui::GetCurrentWindowRead();
    float contentWidth = contentRegion.x;
    float contentHeight = contentRegion.y;
    
    GLFWwindow* imguiWindow = (GLFWwindow*)ImGui::GetCurrentWindowRead()->Viewport->PlatformHandle;
    
    // Mount handlers if not already mounted
    mount_window_handlers(imguiWindow);

	auto vid = 0;
	for(int i=1; i<MAX_VIEWPORTS; ++i)
	{
		if (im_wnd == ui.viewports[i].imguiWindow)
		{
			// found the id, use it to draw.
			switch_context(vid=i);
			break;
		}
	}
	if (vid==0)
	{
		// not initialized, find the first unassigned viewport.
		for(int i=1; i<MAX_VIEWPORTS; ++i)
		{
			if (!ui.viewports[i].assigned)
			{
				// found it. use this.
				if (!ui.viewports[i].graphics_inited)
					initialize_viewport(i, contentWidth, contentHeight);
				
				switch_context(vid=i);
				ui.viewports[i].imguiWindow = im_wnd;
				ui.viewports[i].clear();
				break;
			}
		}
	}

	if (vid == 0) 
		throw "not enough viewports";

	ui.viewports[vid].assigned = true;

	if (len>0){
		DBG("vp %d process %d\n", vid, len);
		ActualWorkspaceQueueProcessor(wsptr, ui.viewports[vid]);
	}

	if (imguiWindow==nullptr || !glfwGetWindowAttrib(imguiWindow, GLFW_VISIBLE))
	{
		ui.viewports[vid].active = false;
		// still need to process queue like backgroud workspace.
		return;
	}
	
	ui.viewports[vid].active = true;
	draw_viewport(disp_area_t{.Size = {(int)contentWidth,(int)contentHeight}, .Pos = {(int)contentPos.x, (int)contentPos.y}}, vid);
}