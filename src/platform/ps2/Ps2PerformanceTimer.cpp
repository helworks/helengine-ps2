#include "platform/ps2/Ps2PerformanceTimer.hpp"

#include <timer.h>

namespace helengine::ps2 {
    std::uint64_t Ps2PerformanceTimer::GetCurrentTicks() {
        return GetTimerSystemTime();
    }

    double Ps2PerformanceTimer::ResolveSeconds(std::uint64_t startTicks, std::uint64_t endTicks) {
        if (endTicks <= startTicks) {
            return 0.0;
        }

        return static_cast<double>(endTicks - startTicks) / static_cast<double>(kBUSCLK);
    }

    double Ps2PerformanceTimer::ResolveMilliseconds(std::uint64_t startTicks, std::uint64_t endTicks) {
        return ResolveSeconds(startTicks, endTicks) * 1000.0;
    }
}
