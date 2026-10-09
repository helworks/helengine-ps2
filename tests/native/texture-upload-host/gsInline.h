#pragma once
#include "gsKit.h"
/// Returns a small GIF queue buffer so the actual alpha bind can be checked without issuing hardware commands.
void* gsKit_heap_alloc(GSGLOBAL* global,int qwords,int bytes,int type);
