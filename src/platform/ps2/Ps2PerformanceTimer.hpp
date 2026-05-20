#pragma once

#include <cstdint>

namespace helengine::ps2 {
    class Ps2PerformanceTimer final {
    public:
        static std::uint64_t GetCurrentTicks();

        static double ResolveSeconds(std::uint64_t startTicks, std::uint64_t endTicks);

        static double ResolveMilliseconds(std::uint64_t startTicks, std::uint64_t endTicks);
    };
}
