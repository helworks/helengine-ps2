/// <summary>
/// Retains the generated runtime file implementation required by native PS2 loaders when managed reachability omits System.IO.File.
/// </summary>
#include "helcpp_config.hpp"

// Managed callers already retain this implementation in the generated unity;
// otherwise this native translation unit owns its definitions, including the
// configured PS2 disc backend rather than a separate host-only replacement.
#if !defined(HE_CPP_REQ_FILE)
#define HE_CPP_REQ_FILE 1
#include "system/io/file.cpp"
#endif
