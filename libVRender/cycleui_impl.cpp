#include "cycleui.h"

#include <array>
#include <stdio.h>
#include <functional>
#include <iomanip>
#include <map>
#include <string>
#include <vector>
#include "imgui.h"

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif

unsigned char* stack = nullptr;
NotifyStateChangedFunc stateCallback;
BeforeDrawFunc beforeDraw;

std::map<int, std::vector<unsigned char>> map;
std::vector<unsigned char> v_stack;
std::map<int, point_cloud> pcs;

#define ReadInt *((int*)ptr); ptr += 4
#define ReadString std::string(ptr + 4, ptr + 4 + *((int*)ptr)); ptr += *((int*)ptr) + 4
#define ReadBool *((bool*)ptr); ptr += 1

#define WriteInt32(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=1; pr+=4; *(int*)pr=x; pr+=4;}
#define WriteFloat(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=2; pr+=4; *(float*)pr=x; pr+=4;}
#define WriteDouble(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=3; pr+=4; *(double*)pr=x; pr+=8;}
#define WriteBytes(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=4; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteString(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=5; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteBool(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=6; pr+=4; *(bool*)pr=x; pr+=1;}


void GenerateStackFromPanelCommands(unsigned char* buffer, int len)
{
	auto ptr = buffer;
	auto plen = ReadInt;

	for (int i = 0; i < plen;++i)
	{
		auto st_ptr = ptr;
		auto pid = ReadInt;
		if (map.find(pid) != map.end())
			map[pid].clear();

		auto name = ReadString;
		auto flag = ReadInt;

		if (flag & 2) //shutdown.
		{
			map.erase(pid);
		}
		else
		{
			auto& bytes = map[pid];
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
				else if (type == 1) //type 1: cache.
				{
					auto len = ReadInt;
					bytes.reserve(bytes.size() + len);
					auto initLen = ReadInt;
					std::copy(ptr, ptr + initLen, std::back_inserter(bytes));
					for (int k = 0; k < len - initLen; ++k)
						bytes.push_back(0);
					ptr += initLen;
				}
			}
			for (int j = 0; j < 4; ++j)
				bytes.push_back(0);
		}
	}

	v_stack.clear();

	int mlen = map.size();
	for (size_t i = 0; i < 4; ++i) {
		v_stack.push_back(((uint8_t*)&mlen)[i]);
	}
	for (const auto& entry : map)
	{
		const auto& bytes = entry.second;
		v_stack.insert(v_stack.end(), bytes.begin(), bytes.end());
	}
	stack = v_stack.data();
}


void ProcessUIStack()
{
	beforeDraw();
	auto ptr = stack;
	if (ptr == nullptr) return; // skip if not initialized.

	auto plen = ReadInt;

	unsigned char buffer[1024];
	auto pr = buffer;
	bool stateChanged = false;
	for (int i = 0; i < plen; ++i)
	{
		auto pid = ReadInt;
		std::array<std::function<void()>, 5> UIFuns = {
			[&] { assert(false); }, // 0: this is not a valid control(cache command)
			[&] //1: text
			{
				auto str = ReadString;
				ImGui::Text(str.c_str());
			},
			[&] // 2: button
			{
				auto cid = ReadInt;
				auto str = ReadString;

				char buttonLabel[256];
				sprintf(buttonLabel, "%s##btn%d", str.c_str(), cid);
				if (ImGui::Button(buttonLabel)) {
					stateChanged = true;
					WriteInt32(1)
				}
			},
			[&] // 3: checkbox
			{
				auto cid = ReadInt;
				auto str = ReadString;
				auto checked = ReadBool;

				char checkboxLabel[256];
				sprintf(checkboxLabel, "%s##checkbox%d", str.c_str(), cid);
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
				char* textBuffer = (char*)ptr;
				ptr += 256;
				ImGui::InputTextWithHint(prompt.c_str(), hint.c_str(), textBuffer, 256);
				WriteString(textBuffer, strlen(textBuffer))
			}
		};
		auto str = ReadString;

		char windowLabel[256];
		sprintf(windowLabel, "%s##pid%d", str.c_str(), pid);
		ImGui::Begin(windowLabel);

		ImGui::PushItemWidth(ImGui::GetFontSize() * -6);
		auto flags = ReadInt;
		if (flags & 1) // freeze.
		{
			ImGui::BeginDisabled(true);
		}
		while (true)
		{
			auto ctype = ReadInt;
			if (ctype == 0) break;
			UIFuns[ctype]();
		}
		if (flags & 1) // freeze.
		{
			ImGui::EndDisabled();
		}
		ImGui::End();
	}
	if (stateChanged)
		stateCallback(buffer, pr - buffer);
}
