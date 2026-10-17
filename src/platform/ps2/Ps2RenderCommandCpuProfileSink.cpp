#include "platform/ps2/Ps2RenderCommandCpuProfileSink.hpp"
#include <cmath>
#include <limits>
#include <stdexcept>

namespace helengine::ps2 {
    Ps2RenderCommandCpuProfileSink::Ps2RenderCommandCpuProfileSink(ClockReader clockReader)
        : ReadClock(clockReader) {
        if (ReadClock == nullptr) {
            throw std::invalid_argument("PS2 command profiling requires a clock reader.");
        }
    }

    void Ps2RenderCommandCpuProfileSink::ClockFault() {
        throw std::runtime_error("PS2 command profiling clock is invalid or non-monotonic.");
    }

    uint64_t Ps2RenderCommandCpuProfileSink::ReadMicroseconds() {
        const std::clock_t ticks = ReadClock();
        if (ticks == static_cast<std::clock_t>(-1) || ticks < 0) {
            ClockFault();
        }
        const uint64_t tickCount = static_cast<uint64_t>(ticks);
        const uint64_t seconds = tickCount / CLOCKS_PER_SEC;
        const uint64_t remainder = tickCount % CLOCKS_PER_SEC;
        if (seconds > std::numeric_limits<uint64_t>::max() / 1000000u
            || remainder > std::numeric_limits<uint64_t>::max() / 1000000u) {
            ClockFault();
        }
        const uint64_t wholeMicroseconds = seconds * 1000000u;
        const uint64_t fractionalMicroseconds = remainder * 1000000u / CLOCKS_PER_SEC;
        if (wholeMicroseconds > std::numeric_limits<uint64_t>::max() - fractionalMicroseconds) {
            ClockFault();
        }
        const uint64_t microseconds = wholeMicroseconds + fractionalMicroseconds;
        if (HasClockReading && microseconds < LastMicroseconds) {
            ClockFault();
        }
        LastMicroseconds = microseconds;
        HasClockReading = true;
        return microseconds;
    }

    void Ps2RenderCommandCpuProfileSink::RecordStage(::RuntimeCpuProfileStage stage, uint64_t elapsedMicroseconds) {
        const int index = static_cast<int>(stage) - static_cast<int>(::RuntimeCpuProfileStage::CommandListSetup);
        if (index < 0 || index >= static_cast<int>(StageMicroseconds.size())) {
            throw std::invalid_argument("PS2 command profiling received a non-command stage.");
        }
        if (StageMicroseconds[index] > std::numeric_limits<uint64_t>::max() - elapsedMicroseconds
            || StageSegments[index] == std::numeric_limits<uint64_t>::max()) {
            throw std::overflow_error("PS2 command profiling accumulator overflow.");
        }
        StageMicroseconds[index] += elapsedMicroseconds;
        StageSegments[index]++;
    }

    void Ps2RenderCommandCpuProfileSink::RecordUpdateable(::IUpdateable*, int32_t, uint32_t, uint32_t, uint64_t) {
        throw std::logic_error("PS2 command profiling must not be attached to Core updates.");
    }

    void Ps2RenderCommandCpuProfileSink::RegisterType(::IUpdateable*, uint32_t, std::string) {
        throw std::logic_error("PS2 command profiling does not register update types.");
    }

    std::string Ps2RenderCommandCpuProfileSink::CompleteWindow(uint32_t frameCount, double totalBuildMilliseconds) {
        if (frameCount == 0 || !std::isfinite(totalBuildMilliseconds) || totalBuildMilliseconds < 0.0) {
            throw std::invalid_argument("PS2 command profiling requires a nonempty reporting window.");
        }
        static_assert(static_cast<int>(::RuntimeCpuProfileStage::CommandListClipping) == 11);
        static_assert(static_cast<int>(::RuntimeCpuProfileStage::CommandListSprites) == 12);
        static_assert(static_cast<int>(::RuntimeCpuProfileStage::CommandListTextLayout) == 13);
        static_assert(static_cast<int>(::RuntimeCpuProfileStage::CommandListTextEffectsSetup) == 14);
        static_assert(static_cast<int>(::RuntimeCpuProfileStage::CommandListGlyphEmission) == 15);
        static_assert(static_cast<int>(::RuntimeCpuProfileStage::CommandListRoundedRects) == 16);
        static_assert(static_cast<int>(::RuntimeCpuProfileStage::CommandListTrailingClips) == 17);
        static constexpr const char* names[] = {
            "setup", "clip", "sprites", "textLayout", "textEffects", "glyphEmission", "roundedRects", "trailingClips"
        };
        std::string summary = "draw2d build split frames=" + std::to_string(frameCount);
        double measuredMilliseconds = 0.0;
        for (size_t index = 0; index < StageMicroseconds.size(); index++) {
            const double milliseconds = static_cast<double>(StageMicroseconds[index]) / 1000.0;
            measuredMilliseconds += milliseconds;
            summary += " " + std::string(names[index]) + "Ms=" + std::to_string(milliseconds / frameCount)
                + " " + names[index] + "Segments=" + std::to_string(StageSegments[index]);
        }
        summary += " totalMeasuredMs=" + std::to_string(measuredMilliseconds / frameCount)
            + " buildMs=" + std::to_string(totalBuildMilliseconds / frameCount)
            + " traversalAndInstrumentationMs=" + std::to_string((totalBuildMilliseconds - measuredMilliseconds) / frameCount);
        StageMicroseconds.fill(0);
        StageSegments.fill(0);
        return summary;
    }
}
