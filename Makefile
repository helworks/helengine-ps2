PS2DEV ?= /usr/local/ps2dev
PS2SDK ?= $(PS2DEV)/ps2sdk
GSKIT ?= $(PS2DEV)/gsKit
PKG_CONFIG ?= pkg-config
GSKIT_CFLAGS := $(shell $(PKG_CONFIG) --cflags gsKit 2>/dev/null)
GSKIT_LIBS := $(shell $(PKG_CONFIG) --libs gsKit 2>/dev/null)

TARGET := build/helengine_ps2.elf
BUILD_DIR := build
SOURCE_DIR := src
SOURCES := \
	$(SOURCE_DIR)/main.cpp \
	$(SOURCE_DIR)/platform/ps2/Ps2BootHost.cpp
OBJECTS := $(patsubst $(SOURCE_DIR)/%.cpp,$(BUILD_DIR)/%.o,$(SOURCES))

CXX := mips64r5900el-ps2-elf-g++
STRIP := mips64r5900el-ps2-elf-strip

CPPFLAGS := \
	-D_EE \
	-I$(SOURCE_DIR) \
	-I$(PS2SDK)/ee/include \
	-I$(PS2SDK)/common/include \
	$(GSKIT_CFLAGS)

CXXFLAGS := \
	-std=gnu++17 \
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
	-lstdc++ \
	-lkernel \
	$(GSKIT_LIBS) \
	-lmath3d \
	-ldraw \
	-lpacket2 \
	-lgraph

.PHONY: all clean

all: $(TARGET)

$(TARGET): $(OBJECTS)
	@mkdir -p $(dir $@)
	$(CXX) $(LDFLAGS) -o $@ $^ $(LDLIBS)
	$(STRIP) --strip-all $@

$(BUILD_DIR)/%.o: $(SOURCE_DIR)/%.cpp
	@mkdir -p $(dir $@)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -c $< -o $@

clean:
	@rm -rf $(BUILD_DIR)
