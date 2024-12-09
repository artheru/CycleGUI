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

NotifyStateChangedFunc stateCallback;
NotifyWorkspaceChangedFunc workspaceCallback;
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


void ProcessWorkspaceQueue(void* wsqueue)
{
	// process workspace:
	auto* ptr = (unsigned char*)wsqueue;
	auto* wstate = &ui_state.workspace_state.back();

	int apiN = 0;
	AllowWorkspaceData();

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
			BeginWorkspace<select_operation>(id, str); // default is select.
			
			wstate = &ui_state.workspace_state.back();
			((select_operation*)wstate->operation)->painter_data.resize(ui_state.workspace_w * ui_state.workspace_h, 0);
		},
		[&]
		{
			//9 : end operation
			auto str = ReadString;
			auto id = ReadInt;

			auto& ostate = ui_state.workspace_state.back();
			if (ostate.name == str && ostate.id==id){
				ui_state.pop_workspace_state();
				wstate = &ui_state.workspace_state.back();
			}else
			{
				printf("invalid state pop, expected to pop %s(%d), actual api pops %s(%d)", ostate.name, ostate.id, str, id);
			}
		},
		[&]
		{
			//10: Guizmo MoveXYZ/RotateXYZ.
			auto id = ReadInt;
			auto str = ReadString;
			BeginWorkspace<guizmo_operation>(id, str);
			wstate = &ui_state.workspace_state.back();
			auto op = (guizmo_operation*)wstate->operation;

			auto realtime = ReadBool;
			auto type = ReadInt; 
			if (type == 0)
				op->mode = gizmo_moveXYZ;
			else if (type == 1)
				op->mode = gizmo_rotateXYZ;

			op->realtime = realtime;
			op->selected_get_center();
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
			
			auto useCrossSection_set = ReadBool;
			if (useCrossSection_set) {wstate->useCrossSection = ReadBool;}
			
			auto crossSectionPlanePos_set = ReadBool;
			if (crossSectionPlanePos_set) {
				glm::vec3 xyz;
				xyz.x = ReadFloat;
				xyz.y = ReadFloat;
				xyz.z = ReadFloat;
				wstate->crossSectionPlanePos = xyz;
			}
			
			auto clippingDirection_set = ReadBool;
			if (clippingDirection_set) {
				glm::vec3 dir;
				dir.x = ReadFloat;
				dir.y = ReadFloat;
				dir.z = ReadFloat;
				wstate->clippingDirection = dir;
			}

			auto btf_on_hovering_set = ReadBool;
			if (btf_on_hovering_set) { wstate->btf_on_hovering = ReadBool; }
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
			{
				auto lookAt_set = ReadBool;
				if (lookAt_set) {
					glm::vec3 lookAt;
					{
						lookAt.x = ReadFloat;
						lookAt.y = ReadFloat;
						lookAt.z = ReadFloat;
					}
					camera->stare = lookAt;
				}
				
				auto azimuth_set = ReadBool;
				if (azimuth_set) {
					camera->Azimuth = ReadFloat;
				}
				
				auto altitude_set = ReadBool;
				if (altitude_set) {
					camera->Altitude = ReadFloat;
				}
				
				auto distance_set = ReadBool;
				if (distance_set) {
					camera->distance = ReadFloat;
				}
				
				auto fov_set = ReadBool;
				if (fov_set) {
					camera->_fov = ReadFloat;
				}
				
				if (lookAt_set || azimuth_set || altitude_set || distance_set) {
					camera->UpdatePosition();
				}
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
			auto rgbaName = ReadString; //also consider special names: %string(#ffffff,#000000,64px):blahblah....%

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
			}
			if (lineType==1)
			{
				// beziercurve.
				auto additionalControlPnts = ReadInt;
				auto v3ptr = ReadArr(glm::vec3, additionalControlPnts);
			}
		},
		[&]
		{  //22: PutRGBA //rgba can also become 9patch.
			auto name = ReadString;
			auto width = ReadInt;
			auto height = ReadInt;

			PutRGBA(name, width, height);
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
			BeginWorkspace<gesture_operation>(id, str);
			wstate = &ui_state.workspace_state.back();

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

			wstate = &ui_state.workspace_state.back();
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

			wstate = &ui_state.workspace_state.back();
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

			wstate = &ui_state.workspace_state.back();
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

			wstate = &ui_state.workspace_state.back();
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
			//33: PutGeometry
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
		(*obj->references[obj_reference_idx].accessor())[obj->references[obj_reference_idx].offset].obj_reference_idx = obj_reference_idx;
	}
	obj->references.pop_back();
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

bool caseInsensitiveStrStr(const char* haystack, const char* needle) {
	for (const char* h = haystack; *h != '\0'; ++h) {
		const char* hStart = h;
		const char* n = needle;

		while (*n != '\0' && *h != '\0' && tolower(*h) == tolower(*n)) {
			++h;
			++n;
		}

		if (*n == '\0') {
			return true; // Found
		}

		h = hStart; // Reset h to the start for the next iteration
	}
	return false; // Not found
}

void ProcessUIStack()
{
	ImGuiStyle& style = ImGui::GetStyle();

	if (!init_docking)
		SetupDocking();

	// create the main dockspace over the entire editor window
	ImGuiViewport* viewport = ImGui::GetMainViewport();
	ImGui::SetNextWindowPos(viewport->Pos);
	ImGui::SetNextWindowSize(viewport->Size);
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

	unsigned char buffer[1024];
	auto pr = buffer;
	bool stateChanged = false;
	bool wndShown = true;

	cacheBase::untouch();
	
	auto io = ImGui::GetIO();

	// Position
	auto vp = ImGui::GetMainViewport();
	static double sec = 0;
	sec += io.DeltaTime;

	int modalpid = -1;
	for (int i = 0; i < plen; ++i)
	{ 
		auto pid = ReadInt;
		auto str = ReadString;
		auto& mystate = cacheType<wndState>::get()->get_or_create(str);

#ifdef __EMSCRIPTEN__
		auto dpiScale = g_dpi;
#else
		auto dpiScale = mystate.inited < 1 ? vp->DpiScale : mystate.im_wnd->Viewport->DpiScale;
#endif
#define GENLABEL(var,label,prompt) char var[256]; sprintf(var, "%s##%s%d", prompt,label,cid);
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
				ImGui::TextWrapped(prompt);
				ImGui::Indent(style.IndentSpacing / 2);
				ImGui::SetNextItemWidth(-16*dpiScale);

				if (ImGui::InputTextWithHint(tblbl, hint, textBuffer, 256, ImGuiInputTextFlags_EnterReturnsTrue)) {
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
			[&] //7: Searchable Table.
			{
				auto cid = ReadInt;
				auto prompt = ReadString;

				ImGui::SeparatorText(prompt);

				char searcher[256];
				sprintf(searcher, "%s##search", prompt);
				auto skip = ReadInt; //from slot "row" to end.
				auto cols = ReadInt;
				ImGuiTableFlags flags = ImGuiTableFlags_RowBg | ImGuiTableFlags_Borders
					| ImGuiTableFlags_Resizable ;
				ImGui::PushItemWidth(200*dpiScale);

				auto& searchTxt = cacheType<char[256]>::get()->get_or_create(searcher);
				ImGui::InputTextWithHint(searcher, "Search", searchTxt, 256);
				auto searchLen = strlen(searchTxt);

				using VarType = std::variant<int, bool, char*>;

				ImGui::PopItemWidth();
				if (ImGui::BeginTable(prompt, cols, flags))
				{
					for (int i = 0; i < cols; ++i)
					{
						auto header = ReadString;
						ImGui::TableSetupColumn(header);
					}
					ImGui::TableHeadersRow();

					auto rows = ReadInt;
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
								if (searchLen > 0 && caseInsensitiveStrStr(label, searchTxt)) skip_row = false;
							}
							else if (type == 1)
							{	// label with hint
								auto label = ReadString;
								auto hint = ReadString;
								vec.push_back(label);
								vec.push_back(hint);
								if (searchLen > 0 && caseInsensitiveStrStr(label, searchTxt)) skip_row = false;
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
								auto len = ReadInt;
								vec.push_back(len);
								for (int i = 0; i < len; ++i)
								{
									auto init = ReadBool;
									vec.push_back(init);
								}
							}
							else if (type == 5)
							{

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
							}else if (type == 1)
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
								ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(2,2));
								ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(GImGui->Style.ItemInnerSpacing.x/2, GImGui->Style.ItemInnerSpacing.y));

								for (int i = 0; i < len; ++i)
								{
									auto label = std::get<char*>(vec[ii++]);
									char lsbxid[256];
									sprintf(lsbxid, "%s##btng%s_%d", label, prompt, row);
									if (ImGui::SmallButton(lsbxid))
									{
										TableResponseInt(i);
									}
									if (i < len - 1) ImGui::SameLine();
								}
								ImGui::PopStyleVar(3);
							}else if (type ==3)
							{
								// buttons with hint.
								auto len = std::get<int>(vec[ii++]);
								ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 2);
								ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(2,2));
								ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(GImGui->Style.ItemInnerSpacing.x / 3, GImGui->Style.ItemInnerSpacing.y));

								for (int i = 0; i < len; ++i)
								{
									auto label = std::get<char*>(vec[ii++]);
									auto hint = std::get<char*>(vec[ii++]);
									char lsbxid[256];
									sprintf(lsbxid, "%s##btng%s_%d", label, prompt, row);
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
							}else if (type ==4) //checkbox.
							{
								auto len = std::get<int>(vec[ii++]);
								for (int i = 0; i < len; ++i)
								{
									auto init = std::get<bool>(vec[ii++]);
									char lsbxid[256];
									sprintf(lsbxid, "##%s_%d_chk", prompt, row);
									if (ImGui::Checkbox(lsbxid,&init))
									{
										TableResponseBool(init);
									}
								}
							}else if (type ==5) 
							{
								
							}
							else if (type == 6)
							{ // set color, doesn't apply to column.
								auto color = std::get<int>(vec[ii++]);
								ImGui::TableSetBgColor(ImGuiTableBgTarget_RowBg1, color);
								column -= 1;
							}
						}
					}
					ImGui::EndTable();
				}
				else ptr += skip;
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
						ImPlotAxisFlags_NoLabel|ImPlotAxisFlags_NoTickLabels, ImPlotAxisFlags_AutoFit|ImPlotAxisFlags_RangeFit|ImPlotAxisFlags_NoLabel|ImPlotAxisFlags_NoTickLabels);
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
				auto enabled = displayed < ui_state.getMsFromStart() + 1000;
				if (!enabled) ImGui::BeginDisabled(true);
				
				ImGui::PushStyleColor(ImGuiCol_Button, IM_COL32(120, 80, 0, 255));
				if (ImGui::Button(lsbxid))
				{
					ExternDisplay(filehash, pid, fname);
				}
				ImGui::PopStyleColor();
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
					ImGui::SetNextItemWidth(-200);
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
					if (ImGui::Button("\uf1d8") && !sent)
					{
						stateChanged = true;
						WriteString(displayed.inputbuf, strlen(displayed.inputbuf));
						displayed.hint = displayed.inputbuf;
						displayed.inputbuf[0] = 0;
					}
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
				sprintf(searcher, "%s##search", strId);
				auto skip = ReadInt; //from slot "row" to end.
				auto prompt = ReadString;
				if (prompt[0] != '\0') ImGui::SeparatorText(prompt);
				auto enableSearch = ReadBool;
				auto cols = ReadInt;

				ImGuiTableFlags flags = ImGuiTableFlags_RowBg | ImGuiTableFlags_Borders
					| ImGuiTableFlags_Resizable;
				ImGui::PushItemWidth(200 * dpiScale);

				auto& searchTxt = cacheType<char[256]>::get()->get_or_create(searcher);
				if (enableSearch)
					ImGui::InputTextWithHint(searcher, "Search", searchTxt, 256);
				auto searchLen = strlen(searchTxt);

				using VarType = std::variant<int, bool, char*>;

				ImGui::PopItemWidth();
				if (ImGui::BeginTable(strId, cols, flags))
				{
					for (int i = 0; i < cols; ++i)
					{
						auto header = ReadString;
						ImGui::TableSetupColumn(header);
					}
					ImGui::TableHeadersRow();

					auto rows = ReadInt;
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
								auto len = ReadInt;
								vec.push_back(len);
								for (int i = 0; i < len; ++i)
								{
									auto init = ReadBool;
									vec.push_back(init);
								}
							}
							else if (type == 5)
							{

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
								auto len = std::get<int>(vec[ii++]);
								for (int i = 0; i < len; ++i)
								{
									auto init = std::get<bool>(vec[ii++]);
									char lsbxid[256];
									sprintf(lsbxid, "##%s_%d_chk", strId, row);
									if (ImGui::Checkbox(lsbxid, &init))
									{
										TableResponseBool(init);
									}
								}
							}
							else if (type == 5)
							{

							}
							else if (type == 6)
							{ // set color, doesn't apply to column.
								auto color = std::get<int>(vec[ii++]);
								ImGui::TableSetBgColor(ImGuiTableBgTarget_RowBg1, color);
								column -= 1;
							}
						}
					}
					ImGui::EndTable();
				}
				else ptr += skip;
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

				char* preview;
				if (selected >= 0 && selected < items_count) preview = items[selected];
				else
				{
					preview = new char[1];
					preview[0] = '\0';
				}
				
			    if (ImGui::BeginCombo(dropdownLabel, preview))
			    {
			        for (int n = 0; n < items_count; n++)
			        {
			            auto item = items[n];
			            const bool is_selected = (n == selected);
			            if (ImGui::Selectable(item, is_selected))
			            {
			                stateChanged = true;
			                WriteInt32(n);
			            }

			            // Set the initial focus when opening the combo (scrolling + keyboard navigation focus)
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
			}
		};
		//std::cout << "draw " << pid << " " << str << ":"<<i<<"/"<<plen << std::endl;
		// char windowLabel[256];
		// sprintf(windowLabel, "%s##pid%d", str.c_str(), pid);

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
		// Size:
		auto pivot = ImVec2(myPivotX, myPivotY);
		if ((flags & 8) !=0)
		{
			// not resizable
			if ((flags & (16)) != 0) { // autoResized
				window_flags |= ImGuiWindowFlags_AlwaysAutoResize;
				mystate.minH = panelHeight;
				mystate.minW = panelWidth;
				ImGui::SetNextWindowSizeConstraints(ImVec2(0, 0), ImVec2(FLT_MAX, FLT_MAX), StepWndSz, &mystate);
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
			if (mystate.inited < 1) {
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
		mystate.flipper = flipper;

		auto should_block = flags & 1 || mystate.pendingAction || (except.length() > 0) ;
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
			mystate.time_start_interact = ui_state.getMsFromStart();
		}

		mystate.inited += 1;
		mystate.Pos = ImGui::GetWindowPos();
		mystate.Size = ImGui::GetWindowSize();
		mystate.im_wnd = ImGui::GetCurrentWindow();

		if (mystate.pendingAction && mystate.time_start_interact+1000<ui_state.getMsFromStart())
		{
			ImGuiWindow* window = mystate.im_wnd;
	        // Render
			auto radius = 20;
			ImVec2 pos(mystate.Pos.x+mystate.Size.x/2,mystate.Pos.y+mystate.Size.y/2);
			
			// Render
	        window->DrawList->PathClear();
	        
	        int num_segments = 30;
			int time = ui_state.getMsFromStart();
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
	}

	cacheBase::finish();
	cgui_refreshed = false;

	if (modalpid == -1 && no_modal_pids.size() > 0)
		no_modal_pids.pop_back();

	if (stateChanged)
		stateCallback(buffer, pr - buffer);

}

ui_state_t ui_state;
bool initialize()
{
	ui_state.workspace_state.reserve(16);
	ui_state.workspace_state.push_back(workspace_state_desc{.id = 0, .name = "default", .operation = new no_operation});
	ui_state.started_time = std::chrono::high_resolution_clock::now();
	return true;
}
static bool initialized = initialize();

void mouse_button_callback(GLFWwindow* window, int button, int action, int mods)
{
	if (ImGui::GetIO().WantCaptureMouse)
		return;

	auto& wstate = ui_state.workspace_state.back();

	if (action == GLFW_PRESS)
	{
		switch (button)
		{
		case GLFW_MOUSE_BUTTON_LEFT:
			ui_state.mouseLeft = true;
			ui_state.mouseLeftDownFrameCnt = ui_state.frameCnt;
			wstate.operation->pointer_down();
			break;
		case GLFW_MOUSE_BUTTON_MIDDLE:
			ui_state.mouseMiddle = true;
			ui_state.refreshStare = true;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			ui_state.mouseRight = true;
			wstate.operation->canceled();
			break;
		}
	}
	else if (action == GLFW_RELEASE)
	{
		switch (button)
		{
		case GLFW_MOUSE_BUTTON_LEFT:
			ui_state.mouseLeft = false;
			wstate.operation->pointer_up();
			break;
		case GLFW_MOUSE_BUTTON_MIDDLE:
			ui_state.mouseMiddle = false;
			// ui_state.lastClickedMs = ui_state.getMsFromStart();
			// ui_state.clickedMouse = 1;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			// if (wstate.right_click_select && ui_state.selecting && )
			// {
			// 	
			// }
			ui_state.mouseRight = false;
			break;
		}
	}
}


bool widget_definition::isKJHandling()
{
	return ui_state.loopCnt < kj_handle_loop + 1;
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
		if (p) kj_handle_loop = ui_state.loopCnt;
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

void select_operation::pointer_down()
{
	selecting = true;
	if (!ctrl)
		ClearSelection();
	if (selecting_mode == click)
	{
		clickingX = ui_state.mouseX;
		clickingY = ui_state.mouseY;
		// select but not trigger now.
	}
	else if (selecting_mode == drag)
	{
		select_start_x = ui_state.mouseX;
		select_start_y = ui_state.mouseY;
	}
	else if (selecting_mode == paint)
	{
		std::fill(painter_data.begin(), painter_data.end(), 0);
	}
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
			if (abs(ui_state.mouseX - clickingX)<10 && abs(ui_state.mouseY - clickingY)<10)
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


guizmo_operation::~guizmo_operation()
{
	for(int i=0; i < referenced_objects.size();++i)
		referenced_objects[i].remove_from_obj();
	printf("removed reference for guizmo operation.\n");
}

uint64_t ui_state_t::getMsFromStart() {
	return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::high_resolution_clock::now() - started_time).count();
}
template <typename workspaceType>
void BeginWorkspace(int id, std::string state_name)
{
	if (ui_state.workspace_state.size() >= 16)
	{
		printf("workspace operation stack too deep, current depth=%d\n", ui_state.workspace_state.size());
	}

	// effectively eliminate action state.
	_clear_action_state();
	ui_state.workspace_state.push_back(ui_state.workspace_state.back());

	auto& wstate = ui_state.workspace_state.back();
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

	printf("begin workspace %d=%s\n", id, state_name.c_str());
	// assert(wstate.selectables.size() <= 1);
}

void check_the_fuck(me_obj* ptr)
{
	for (int i=0; i<ui_state.workspace_state.size(); ++i)
	{
		for (reference_t& selectable : ui_state.workspace_state[i].selectables)
		{
			assert(selectable.obj != ptr);
		}
		for (reference_t& selectable : ui_state.workspace_state[i].no_cross_section)
		{
			assert(selectable.obj != ptr);
		}
		for (reference_t& selectable : ui_state.workspace_state[i].hidden_objects)
		{
			assert(selectable.obj != ptr);
		}
		for (reference_t& selectable : ui_state.workspace_state[i].sub_selectables)
		{
			assert(selectable.obj != ptr);
		}
	}
}

void ui_state_t::pop_workspace_state()
{
	if (ui_state.workspace_state.size() == 1)
		throw "not allowed to pop default action.";

	auto& wstate = ui_state.workspace_state.back();
	printf("end operation %d:%s\n", wstate.id, wstate.name.c_str());
	ReapplyWorkspaceState();

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

	ui_state.workspace_state.pop_back();
	wstate.operation->destroy();
}

void cursor_position_callback(GLFWwindow* window, double rx, double ry)
{
	if (ImGui::GetIO().WantCaptureMouse)
		return;

	if (!dockingRoot) return;
	auto vp = ImGui::GetMainViewport();
	auto central = ImGui::DockNodeGetRootNode(dockingRoot)->CentralNode;
	auto xpos = rx - central->Pos.x + vp->Pos.x;
	auto ypos = ry - central->Pos.y + vp->Pos.y;

	double deltaX = xpos - ui_state.mouseX;
	double deltaY = ypos - ui_state.mouseY;

	deltaX = ImSign(deltaX) * std::min(100.0, abs(deltaX));
	deltaY = ImSign(deltaY) * std::min(100.0, abs(deltaY));


	//ImGuiDockNode* node = ImGui::DockBuilderGetNode(ImGui::GetID("CycleGUIMainDock"));
	ui_state.mouseX = xpos;// - central->Pos.x + vp->Pos.x;
	ui_state.mouseY = ypos;


	auto& wstate = ui_state.workspace_state.back();
	
		// wstate.operation->pointer_move();
	if (ui_state.mouseLeft)
	{
		// Handle left mouse button dragging (in process ui stack)
	}
	else if (ui_state.mouseMiddle && ui_state.mouseRight)
	{
		camera->Rotate(deltaY * 1.5f, -deltaX);
		
	}
	else if (ui_state.mouseMiddle)
	{
		// Handle middle mouse button dragging
		camera->RotateAzimuth(-deltaX);
		camera->RotateAltitude(deltaY * 1.5f);
	}
	else if (ui_state.mouseRight)
	{
		// Handle right mouse button dragging
		wstate.operation->canceled();
		
		// if pitch exceed certain value, pan on camera coordination.
		auto d = camera->distance * 0.0016f;
		camera->PanLeftRight(-deltaX * d);
		if (abs(camera->Altitude)<M_PI_4)
		{
			auto s = sin(camera->Altitude);
			auto c = cos(camera->Altitude);
			auto fac = 1-s /0.7071;
			camera->ElevateUpDown(deltaY * d * fac);
			camera->PanBackForth(deltaY * d * (1 - fac) - (deltaY * d * fac * s / c));

		}else{
			camera->PanBackForth(deltaY * d);
		}
	}
}

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset)
{
	if (ImGui::GetIO().WantCaptureMouse)
		return;

	// Handle mouse scroll
	if (ui_state.mouseMiddle)
	{
		// move vertically.
		camera->ElevateUpDown(yoffset * 0.1f);
	}
	else {
		// zoom
		camera->Zoom(-yoffset * 0.1f);
	}
}

void key_callback(GLFWwindow* window, int key, int scancode, int action, int mods) {
	// Check if the Ctrl key (left or right) is pressed
	if (key == GLFW_KEY_LEFT_CONTROL || key == GLFW_KEY_RIGHT_CONTROL) {
		if (action == GLFW_PRESS) {
			ui_state.ctrl = true;
		}
		else if (action == GLFW_RELEASE) {
			ui_state.ctrl = false;
		}
	}
	if (key == GLFW_KEY_ESCAPE)
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
	ui_state.touches = touches;
	for (int i = 0; i < ui_state.touches.size(); ++i)
		if (ui_state.prevTouches.find(ui_state.touches[i].id) == ui_state.prevTouches.end())
			ui_state.touches[i].starting = true;
}


//???
void _clear_action_state()
{
	// ui_state.selecting = false;
	auto& wstate = ui_state.workspace_state.back();
	//wstate.action_state = 0;
}
