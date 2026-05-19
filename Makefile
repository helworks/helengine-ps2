PS2DEV ?= /usr/local/ps2dev
PS2SDK ?= $(PS2DEV)/ps2sdk
GSKIT ?= $(PS2DEV)/gsKit
HELENGINE_CORE_CPP_ROOT ?=
PKG_CONFIG ?= pkg-config
GSKIT_CFLAGS := $(shell $(PKG_CONFIG) --cflags gsKit 2>/dev/null)
GSKIT_LIBS := $(shell $(PKG_CONFIG) --libs gsKit 2>/dev/null)

ifeq ($(strip $(HELENGINE_CORE_CPP_ROOT)),)
ifneq ($(strip $(filter-out clean,$(MAKECMDGOALS))),)
$(error HELENGINE_CORE_CPP_ROOT must point at the generated helengine.core C++ output folder)
endif
endif

TARGET := build/helengine_ps2.elf
BUILD_DIR := build
SOURCE_DIR := src
HOST_DEBUGGER_DIR := tools/ps2-host-debugger
HOST_DEBUGGER_BUILD_DIR := $(HOST_DEBUGGER_DIR)/build
HOST_DEBUGGER_TARGET := $(HOST_DEBUGGER_DIR)/bin/ps2-host-debugger.exe
HOST_GENERATED_CORE_STAGE_ROOT := $(HOST_DEBUGGER_BUILD_DIR)/generated-core
HOST_GENERATED_CORE_STAGE_STAMP := $(HOST_GENERATED_CORE_STAGE_ROOT)/.prepared
GENERATED_CORE_STAGE_ROOT := $(BUILD_DIR)/generated-core
GENERATED_CORE_STAGE_STAMP := $(GENERATED_CORE_STAGE_ROOT)/.prepared
GENERATED_CORE_STAGE_INPUTS = $(shell find $(HELENGINE_CORE_CPP_ROOT) -type f | sort)
PS2_SOURCES := \
	$(SOURCE_DIR)/main.cpp \
	$(SOURCE_DIR)/platform/ps2/Ps2InputBackend.cpp \
	$(SOURCE_DIR)/platform/ps2/Ps2BootHost.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2FramePlan.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2FramePlanner.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RenderManager3D.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RenderProxy.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RuntimeMaterial.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RuntimeModel.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuProgramRegistry.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
VU_PROGRAM_SOURCES := \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm
VU_PROGRAM_OBJECTS := \
	$(BUILD_DIR)/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.o \
	$(BUILD_DIR)/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.o
OBJECTS := \
	$(patsubst $(SOURCE_DIR)/%.cpp,$(BUILD_DIR)/%.o,$(PS2_SOURCES)) \
	$(VU_PROGRAM_OBJECTS) \
	$(BUILD_DIR)/generated/runtime/runtime_startup_manifest.o \
	$(BUILD_DIR)/generated/runtime/runtime_scene_catalog_manifest.o \
	$(BUILD_DIR)/generated/runtime/runtime_code_module_manifest.o \
	$(BUILD_DIR)/generated/runtime/runtime_ps2_asset_path_manifest.o \
	$(BUILD_DIR)/generated/helengine_core_amalgamated.o

CXX := mips64r5900el-ps2-elf-g++
STRIP := mips64r5900el-ps2-elf-strip
EE_DVP := dvp-as
HOST_CXX ?= g++

CPPFLAGS := \
	-D_EE \
	-I$(SOURCE_DIR) \
	-I$(PS2SDK)/ee/include \
	-I$(PS2SDK)/common/include \
	-I$(GENERATED_CORE_STAGE_ROOT) \
	$(GSKIT_CFLAGS)

CXXFLAGS := \
	-std=gnu++20 \
	-O2 \
	-Wall \
	-Wextra \
	-Wno-unused-parameter

HOST_CPPFLAGS := \
	-I$(SOURCE_DIR) \
	-I$(HOST_GENERATED_CORE_STAGE_ROOT) \
	-I$(HOST_DEBUGGER_DIR)

HOST_CXXFLAGS := \
	-std=gnu++20 \
	-O0 \
	-g \
	-Wall \
	-Wextra \
	-Wno-unused-parameter

LDFLAGS := \
	-T$(PS2SDK)/ee/startup/linkfile \
	-L$(PS2SDK)/ee/lib \
	-L$(PS2SDK)/common/lib \
	-Wl,-zmax-page-size=128

LDLIBS := \
	-latomic \
	-lstdc++ \
	-lkernel \
	-lpad \
	-ldebug \
	$(GSKIT_LIBS) \
	-lmath3d \
	-ldraw \
	-lpacket2 \
	-ldma \
	-lgraph

HOST_DEBUGGER_SOURCES := \
	$(HOST_DEBUGGER_DIR)/main.cpp \
	$(HOST_DEBUGGER_DIR)/Ps2HostDebugSession.cpp \
	$(HOST_DEBUGGER_DIR)/Ps2HostFileSystem.cpp \
	$(HOST_DEBUGGER_DIR)/Ps2HostRenderManager3D.cpp \
	$(HOST_DEBUGGER_DIR)/Ps2HostRenderManager2D.cpp \
	$(HOST_DEBUGGER_DIR)/Ps2HostInputBackend.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2FramePlan.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2FramePlanner.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RenderProxy.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RuntimeMaterial.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RuntimeModel.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp \
	$(HOST_DEBUGGER_DIR)/HostFile.cpp \
	$(HOST_DEBUGGER_DIR)/HostFileStream.cpp
HOST_DEBUGGER_OBJECTS := \
	$(patsubst $(HOST_DEBUGGER_DIR)/%.cpp,$(HOST_DEBUGGER_BUILD_DIR)/%.o,$(filter $(HOST_DEBUGGER_DIR)/%.cpp,$(HOST_DEBUGGER_SOURCES))) \
	$(patsubst $(SOURCE_DIR)/%.cpp,$(HOST_DEBUGGER_BUILD_DIR)/ps2-runtime/%.o,$(filter $(SOURCE_DIR)/%.cpp,$(HOST_DEBUGGER_SOURCES))) \
	$(HOST_DEBUGGER_BUILD_DIR)/generated/helengine_core_amalgamated.o \
	$(HOST_DEBUGGER_BUILD_DIR)/generated/runtime/runtime_scene_catalog_manifest.o \
	$(HOST_DEBUGGER_BUILD_DIR)/generated/runtime/runtime_ps2_asset_path_manifest.o

.PHONY: all clean ps2-host-debugger

all: $(TARGET)

$(GENERATED_CORE_STAGE_STAMP): $(GENERATED_CORE_STAGE_INPUTS) $(SOURCE_DIR)/app_context_ps2.hpp
	@rm -rf $(GENERATED_CORE_STAGE_ROOT)
	@mkdir -p $(GENERATED_CORE_STAGE_ROOT)
	@cp -R $(HELENGINE_CORE_CPP_ROOT)/. $(GENERATED_CORE_STAGE_ROOT)/
	@cp $(SOURCE_DIR)/app_context_ps2.hpp $(GENERATED_CORE_STAGE_ROOT)/system/app_context.hpp
	@touch $@

$(TARGET): $(OBJECTS)
	@mkdir -p $(dir $@)
	$(CXX) $(LDFLAGS) -o $@ $^ $(LDLIBS)
	$(STRIP) --strip-all $@

$(BUILD_DIR)/%.o: $(SOURCE_DIR)/%.cpp
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

$(BUILD_DIR)/platform/ps2/rendering/vu/programs/%.o: $(SOURCE_DIR)/platform/ps2/rendering/vu/programs/%.vsm
	@mkdir -p $(dir $@)
	$(EE_DVP) $< -o $@

$(BUILD_DIR)/platform/ps2/Ps2BootHost.o: $(GENERATED_CORE_STAGE_STAMP)
$(BUILD_DIR)/platform/ps2/Ps2InputBackend.o: $(GENERATED_CORE_STAGE_STAMP)
$(BUILD_DIR)/platform/ps2/rendering/%.o: $(GENERATED_CORE_STAGE_STAMP)

$(BUILD_DIR)/generated/helengine_core_amalgamated.o: $(GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp $(GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

$(BUILD_DIR)/generated/runtime/runtime_startup_manifest.o: $(GENERATED_CORE_STAGE_ROOT)/runtime/runtime_startup_manifest.cpp $(GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

$(BUILD_DIR)/generated/runtime/runtime_scene_catalog_manifest.o: $(GENERATED_CORE_STAGE_ROOT)/runtime/runtime_scene_catalog_manifest.cpp $(GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

$(BUILD_DIR)/generated/runtime/runtime_code_module_manifest.o: $(GENERATED_CORE_STAGE_ROOT)/runtime/runtime_code_module_manifest.cpp $(GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

$(BUILD_DIR)/generated/runtime/runtime_ps2_asset_path_manifest.o: $(GENERATED_CORE_STAGE_ROOT)/runtime/runtime_ps2_asset_path_manifest.cpp $(GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

$(HOST_DEBUGGER_TARGET): $(HOST_DEBUGGER_OBJECTS)
	@mkdir -p $(dir $@)
	$(HOST_CXX) $(HOST_CXXFLAGS) -o $@ $^

$(HOST_GENERATED_CORE_STAGE_STAMP): $(shell find $(HELENGINE_CORE_CPP_ROOT) -type f | sort)
	@rm -rf $(HOST_GENERATED_CORE_STAGE_ROOT)
	@mkdir -p $(HOST_GENERATED_CORE_STAGE_ROOT)
	@cp -R $(HELENGINE_CORE_CPP_ROOT)/. $(HOST_GENERATED_CORE_STAGE_ROOT)/
	@grep -v 'system/io/file-stream.cpp' $(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp | grep -v 'system/io/file.cpp' > $(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp.tmp
	@mv $(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp.tmp $(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp
	@touch $@

$(HOST_DEBUGGER_BUILD_DIR)/%.o: $(HOST_DEBUGGER_DIR)/%.cpp $(HOST_GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(HOST_CXX) $(HOST_CPPFLAGS) $(HOST_CXXFLAGS) -c $< -o $@

$(HOST_DEBUGGER_BUILD_DIR)/ps2-runtime/%.o: $(SOURCE_DIR)/%.cpp $(HOST_GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(HOST_CXX) $(HOST_CPPFLAGS) $(HOST_CXXFLAGS) -c $< -o $@

$(HOST_DEBUGGER_BUILD_DIR)/generated/helengine_core_amalgamated.o: $(HOST_GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(HOST_CXX) $(HOST_CPPFLAGS) $(HOST_CXXFLAGS) -c $(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp -o $@

$(HOST_DEBUGGER_BUILD_DIR)/generated/runtime/runtime_ps2_asset_path_manifest.o: $(HOST_GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(HOST_CXX) $(HOST_CPPFLAGS) $(HOST_CXXFLAGS) -c $(HOST_GENERATED_CORE_STAGE_ROOT)/runtime/runtime_ps2_asset_path_manifest.cpp -o $@

$(HOST_DEBUGGER_BUILD_DIR)/generated/runtime/runtime_scene_catalog_manifest.o: $(HOST_GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(HOST_CXX) $(HOST_CPPFLAGS) $(HOST_CXXFLAGS) -c $(HOST_GENERATED_CORE_STAGE_ROOT)/runtime/runtime_scene_catalog_manifest.cpp -o $@

ps2-host-debugger: $(HOST_DEBUGGER_TARGET)

clean:
	@rm -rf $(BUILD_DIR)
	@rm -rf $(HOST_DEBUGGER_BUILD_DIR) $(HOST_DEBUGGER_DIR)/bin

