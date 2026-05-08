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
GENERATED_CORE_STAGE_ROOT := $(BUILD_DIR)/generated-core
GENERATED_CORE_STAGE_STAMP := $(GENERATED_CORE_STAGE_ROOT)/.prepared
GENERATED_CORE_STAGE_INPUTS := \
	$(HELENGINE_CORE_CPP_ROOT)/helengine_core_amalgamated.cpp \
	$(HELENGINE_CORE_CPP_ROOT)/IInputBackend.hpp \
	$(HELENGINE_CORE_CPP_ROOT)/RenderManager3D.hpp \
	$(HELENGINE_CORE_CPP_ROOT)/Ps2MaterialAsset.hpp \
	$(HELENGINE_CORE_CPP_ROOT)/runtime/runtime_graphics_renderer_manifest.hpp \
	$(HELENGINE_CORE_CPP_ROOT)/runtime/runtime_graphics_renderer_manifest.cpp
PS2_SOURCES := \
	$(SOURCE_DIR)/main.cpp \
	$(SOURCE_DIR)/platform/ps2/Ps2InputBackend.cpp \
	$(SOURCE_DIR)/platform/ps2/Ps2BootHost.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2FramePlan.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2FramePlanner.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RenderManager3D.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RenderProxy.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RuntimeMaterial.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RuntimeModel.cpp
OBJECTS := \
	$(patsubst $(SOURCE_DIR)/%.cpp,$(BUILD_DIR)/%.o,$(PS2_SOURCES)) \
	$(BUILD_DIR)/generated/runtime/runtime_startup_manifest.o \
	$(BUILD_DIR)/generated/runtime/runtime_code_module_manifest.o \
	$(BUILD_DIR)/generated/helengine_core_amalgamated.o

CXX := mips64r5900el-ps2-elf-g++
STRIP := mips64r5900el-ps2-elf-strip

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
	-lgraph

.PHONY: all clean

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

$(BUILD_DIR)/platform/ps2/Ps2BootHost.o: $(GENERATED_CORE_STAGE_STAMP)
$(BUILD_DIR)/platform/ps2/Ps2InputBackend.o: $(GENERATED_CORE_STAGE_STAMP)
$(BUILD_DIR)/platform/ps2/rendering/%.o: $(GENERATED_CORE_STAGE_STAMP)

$(BUILD_DIR)/generated/helengine_core_amalgamated.o: $(GENERATED_CORE_STAGE_ROOT)/helengine_core_amalgamated.cpp $(GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

$(BUILD_DIR)/generated/runtime/runtime_startup_manifest.o: $(GENERATED_CORE_STAGE_ROOT)/runtime/runtime_startup_manifest.cpp $(GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

$(BUILD_DIR)/generated/runtime/runtime_code_module_manifest.o: $(GENERATED_CORE_STAGE_ROOT)/runtime/runtime_code_module_manifest.cpp $(GENERATED_CORE_STAGE_STAMP)
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

clean:
	@rm -rf $(BUILD_DIR)
