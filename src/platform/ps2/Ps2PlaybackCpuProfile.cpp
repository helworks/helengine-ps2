#include "platform/ps2/Ps2PlaybackCpuProfile.hpp"

#include <cmath>
#include <limits>
#include <stdexcept>
#include <utility>

namespace helengine::ps2 {
    Ps2PlaybackCpuProfile::Ps2PlaybackCpuProfile(ClockReader clockReader) : Clock(clockReader) {
        if (Clock == nullptr) throw std::invalid_argument("PS2 playback profiling requires a CPU clock.");
    }

    std::clock_t Ps2PlaybackCpuProfile::ReadTicks() {
        const std::clock_t now = Clock();
        // EE clock_t is unsigned; clock() still reports failure with the all-ones sentinel.
        if (now == static_cast<std::clock_t>(-1) || std::cmp_less(now, 0)
            || (HasReading && now < PreviousTicks)) {
            throw std::runtime_error("PS2 playback CPU clock is invalid or regressed.");
        }
        PreviousTicks = now;
        HasReading = true;
        return now;
    }

    void Ps2PlaybackCpuProfile::BeginPlayback() {
        if (IsPlaying || HasStage) throw std::logic_error("PS2 playback profiling intervals cannot overlap.");
        IsPlaying = true;
    }

    void Ps2PlaybackCpuProfile::FinishStage(std::clock_t now) {
        if (!HasStage) return;
        const uint64_t elapsed = static_cast<uint64_t>(now - StartTicks);
        if (Ticks[StageIndex] > std::numeric_limits<uint64_t>::max() - elapsed
            || Segments[StageIndex] == std::numeric_limits<uint64_t>::max()) {
            throw std::overflow_error("PS2 playback stage accounting overflowed.");
        }
        Ticks[StageIndex] += elapsed;
        ++Segments[StageIndex];
        HasStage = false;
    }

    void Ps2PlaybackCpuProfile::SelectStage(Ps2PlaybackStage stage) {
        const std::size_t index = static_cast<std::size_t>(stage);
        if (!IsPlaying || index >= Ticks.size()) throw std::logic_error("Invalid PS2 playback stage interval.");
        if (HasStage && index == StageIndex) return;
        const std::clock_t now = ReadTicks();
        FinishStage(now);
        StageIndex = index;
        StartTicks = now;
        HasStage = true;
    }

    void Ps2PlaybackCpuProfile::DiscardStage() {
        if (!IsPlaying || !HasStage) throw std::logic_error("No PS2 playback probe is active.");
        HasStage = false;
    }

    void Ps2PlaybackCpuProfile::EndPlayback() {
        if (!IsPlaying) throw std::logic_error("No PS2 playback interval is active.");
        if (HasStage) FinishStage(ReadTicks());
        IsPlaying = false;
    }

    void Ps2PlaybackCpuProfile::Count(Ps2PlaybackCounter counter, uint64_t amount) {
        if (!IsPlaying) return;
        const std::size_t index = static_cast<std::size_t>(counter);
        if (index >= Counters.size()) throw std::invalid_argument("Invalid PS2 playback counter.");
        if (Counters[index] > std::numeric_limits<uint64_t>::max() - amount) {
            throw std::overflow_error("PS2 playback command accounting overflowed.");
        }
        Counters[index] += amount;
    }

    std::string Ps2PlaybackCpuProfile::CompleteWindow(uint32_t frames, double playbackMilliseconds) {
        if (frames == 0 || IsPlaying || HasStage || !std::isfinite(playbackMilliseconds) || playbackMilliseconds < 0) {
            throw std::logic_error("Invalid PS2 playback profiling window.");
        }
        static const char* stageNames[] = { "unclippedGlyphRun", "clippedGlyphRun", "spriteQuad", "solidRect",
            "clipTransition", "staticGlyphFastPath", "finalGlyphFlush" };
        static const char* counterNames[] = { "batchedGlyphQuads", "clippedGlyphQuads", "spriteQuads", "solidRects",
            "clipCommands", "nonemptyGlyphFlushes", "submittedPrimitives", "uploadedTextures", "uploadBytes", "residentTextureChecks" };
        std::string result = "draw2d playback split frames=" + std::to_string(frames);
        double measured = 0;
        for (std::size_t index = 0; index < Ticks.size(); ++index) {
            const double milliseconds = static_cast<double>(Ticks[index]) * 1000.0 / CLOCKS_PER_SEC / frames;
            measured += milliseconds;
            result += " " + std::string(stageNames[index]) + "Ms=" + std::to_string(milliseconds);
            result += " " + std::string(stageNames[index]) + "Segments=" + std::to_string(Segments[index]);
        }
        if (measured > playbackMilliseconds + 0.000001) {
            throw std::logic_error("PS2 playback exclusive spans exceed the measured outer interval.");
        }
        result += " totalMeasuredMs=" + std::to_string(measured);
        result += " playbackMs=" + std::to_string(playbackMilliseconds);
        result += " dispatchAndInstrumentationMs=" + std::to_string(playbackMilliseconds - measured);
        for (std::size_t index = 0; index < Counters.size(); ++index) {
            result += " " + std::string(counterNames[index]) + "=" + std::to_string(Counters[index]);
        }
        Ticks.fill(0);
        Segments.fill(0);
        Counters.fill(0);
        return result;
    }
}
