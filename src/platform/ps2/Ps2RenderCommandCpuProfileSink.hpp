#pragma once

#include "IRuntimeCpuProfileSink.hpp"
#include <array>
#include <ctime>
#include <string>

namespace helengine::ps2 {
    /// Accumulates the existing command-builder CPU stages for log-only PS2 benchmarks.
    class Ps2RenderCommandCpuProfileSink final : public ::IRuntimeCpuProfileSink {
    public:
        /// Reads the same CPU clock used by the host's overall build measurement.
        using ClockReader = std::clock_t (*)();

        /// Borrows a clock reader; the injectable reader permits deterministic clock-fault tests.
        explicit Ps2RenderCommandCpuProfileSink(ClockReader clockReader = std::clock);
        /// Surfaces an invalid or regressing clock rather than publishing misleading timings.
        void ClockFault() override;
        /// Returns monotonic CPU microseconds with checked conversion from clock ticks.
        uint64_t ReadMicroseconds() override;
        /// Adds a completed, nonoverlapping command-builder segment and its invocation count.
        void RecordStage(::RuntimeCpuProfileStage stage, uint64_t elapsedMicroseconds) override;
        /// Rejects update profiling: this sink is intentionally bound only to the command builder.
        void RecordUpdateable(::IUpdateable* item, int32_t index, uint32_t typeHash,
            uint32_t ownerSceneEntityId, uint64_t elapsedMicroseconds) override;
        /// Rejects update-type registration so accidental Core-wide instrumentation is visible.
        void RegisterType(::IUpdateable* item, uint32_t typeHash, std::string typeName) override;
        /// Produces stage means, segment counts and the unclassified build residual, then resets the window.
        std::string CompleteWindow(uint32_t frameCount, double totalBuildMilliseconds);

    private:
        /// Clock function borrowed for the lifetime of the sink.
        ClockReader ReadClock;
        /// Last accepted absolute microsecond reading, retained across reporting windows.
        uint64_t LastMicroseconds = 0;
        /// Distinguishes the first reading from a valid zero-valued clock.
        bool HasClockReading = false;
        /// Elapsed microseconds for the eight command-builder stages.
        std::array<uint64_t, 8> StageMicroseconds{};
        /// Number of timed segments per stage; glyph emission is counted once per whole pass.
        std::array<uint64_t, 8> StageSegments{};
    };
}
