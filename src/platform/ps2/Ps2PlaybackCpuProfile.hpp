#pragma once

#include <array>
#include <cstdint>
#include <ctime>
#include <string>

namespace helengine::ps2 {
    /// Exclusive outer CPU spans; clipped glyphs retain their existing individual submissions.
    enum class Ps2PlaybackStage : uint8_t {
        UnclippedGlyphRun, ClippedGlyphRun, SpriteQuad, SolidRect,
        ClipTransition, StaticGlyphFastPath, FinalGlyphFlush
    };

    /// Window counters distinguish attempted commands, actual submissions and cold texture uploads.
    enum class Ps2PlaybackCounter : uint8_t {
        BatchedGlyphQuads, ClippedGlyphQuads, SpriteQuads, SolidRects, ClipCommands,
        NonemptyGlyphFlushes, SubmittedPrimitives, UploadedTextures, UploadBytes,
        ResidentTextureChecks
    };

    /// Accumulates coarse playback spans without reading the clock for each glyph.
    class Ps2PlaybackCpuProfile final {
    public:
        /// Borrows the host CPU clock, with injection for deterministic accounting tests.
        using ClockReader = std::clock_t (*)();
        /// Creates fixed-size accounting storage; no render payloads or resource pointers are retained.
        explicit Ps2PlaybackCpuProfile(ClockReader clockReader = std::clock);
        /// Starts tracking counters for one camera's existing outer playback interval.
        void BeginPlayback();
        /// Changes category only when needed, so a contiguous glyph run uses one clock pair.
        void SelectStage(Ps2PlaybackStage stage);
        /// Discards an unsuccessful static-fast-path probe; its cost remains in the outer residual.
        void DiscardStage();
        /// Finishes the last span and stops counting work performed outside playback.
        void EndPlayback();
        /// Counts existing work only during playback; increments perform no clock reads.
        void Count(Ps2PlaybackCounter counter, uint64_t amount = 1);
        /// Reports per-frame stage means, window counters and the outer residual, then resets totals.
        std::string CompleteWindow(uint32_t frames, double playbackMilliseconds);

    private:
        /// Reads and validates the CPU clock against its previous sample, across reporting windows.
        std::clock_t ReadTicks();
        /// Closes the current exclusive span at a validated timestamp.
        void FinishStage(std::clock_t now);
        /// Borrowed clock function; never invoked by the command counter path.
        ClockReader Clock;
        /// Tick totals and segment counts for the seven exclusive categories.
        std::array<uint64_t, 7> Ticks{};
        std::array<uint64_t, 7> Segments{};
        /// Counters are totals for the entire reporting window, not per-frame averages.
        std::array<uint64_t, 10> Counters{};
        /// Start and previous timestamps remain scalar values without dynamic allocation.
        std::clock_t StartTicks = 0;
        std::clock_t PreviousTicks = 0;
        /// Active stage index is used only while HasStage is true.
        std::size_t StageIndex = 0;
        /// Flags distinguish valid zero clock values and camera playback boundaries.
        bool HasReading = false;
        bool HasStage = false;
        bool IsPlaying = false;
    };
}
