#CXX = g++
#CXX = clang++

EXE = libVRender.so
SOURCES = ./libVRender/main.cpp ./libVRender/messyengine_impl.cpp ./libVRender/cycleui_impl.cpp

#imgui

IMGUI_DIR = ./libVRender/lib
SOURCES += $(IMGUI_DIR)/imgui/imgui.cpp $(IMGUI_DIR)/imgui/imgui_demo.cpp $(IMGUI_DIR)/imgui/imgui_draw.cpp $(IMGUI_DIR)/imgui/imgui_tables.cpp $(IMGUI_DIR)/imgui/imgui_widgets.cpp
SOURCES += $(IMGUI_DIR)/backends/imgui_impl_glfw.cpp $(IMGUI_DIR)/backends/imgui_impl_opengl3.cpp
SOURCES += $(IMGUI_DIR)/imgui/ImGuizmo.cpp $(IMGUI_DIR)/imgui/implot_items.cpp $(IMGUI_DIR)/imgui/implot.cpp $(IMGUI_DIR)/imgui/implot_demo.cpp $(IMGUI_DIR)/imgui/misc/freetype/imgui_freetype.cpp 

OBJS = $(addsuffix .o, $(basename $(notdir $(SOURCES))))
UNAME_S := $(shell uname -s)
LINUX_GL_LIBS = -lGL  

CXXFLAGS = -std=c++2a -I$(IMGUI_DIR)/imgui -I$(IMGUI_DIR)/imgui/backends -I$(IMGUI_DIR)/sokol -I$(IMGUI_DIR)
CXXFLAGS += -g -Wall -Wformat
LIBS =

##---------------------------------------------------------------------
## BUILD FLAGS PER PLATFORM
##---------------------------------------------------------------------

ifeq ($(UNAME_S), Linux) #LINUX
	ECHO_MESSAGE = "Linux"
	LIBS += $(LINUX_GL_LIBS) `pkg-config --static --libs glfw3`

	CXXFLAGS += `pkg-config --cflags glfw3`
	CFLAGS = $(CXXFLAGS)
endif

# freetype:
CXXFLAGS += $(shell pkg-config --cflags freetype2)
LIBS += $(shell pkg-config --libs freetype2)


##---------------------------------------------------------------------
## BUILD RULES
##---------------------------------------------------------------------

%.o:./libVRender/%.cpp
	$(CXX) $(CXXFLAGS) -c -o $@ -fPIC $<

%.o:$(IMGUI_DIR)/imgui/%.cpp
	$(CXX) $(CXXFLAGS) -c -o $@ -fPIC $<

%.o:$(IMGUI_DIR)/imgui/backends/%.cpp
	$(CXX) $(CXXFLAGS) -c -o $@ -fPIC $<

%.o:$(IMGUI_DIR)/imgui/misc/freetype/%.cpp
	$(CXX) $(CXXFLAGS) -c -o $@ -fPIC $<


all: $(EXE)
	@echo Build complete for $(ECHO_MESSAGE)

$(EXE): $(OBJS)
	$(CXX) -shared -o $@ $^ $(CXXFLAGS) $(LIBS)

clean:
	rm -f $(EXE) $(OBJS)
