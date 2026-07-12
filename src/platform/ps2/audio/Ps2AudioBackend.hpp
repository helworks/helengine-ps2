#pragma once

#include <array>
#include <cstdint>
#include <string>
#include <unordered_map>
#include <unordered_set>

#include "AudioAsset.hpp"
#include "AudioPlaybackRequest.hpp"
#include "IAudioBackend.hpp"

namespace helengine::ps2 {
    /// <summary>
    /// Streams shared Helengine PCM assets through the PS2 AUDSRV bridge.
    /// </summary>
    class Ps2AudioBackend final : public ::IAudioBackend {
    public:
        /// <summary>
        /// Loads the AUDSRV IRX modules, initializes the bridge, and prepares the stream semaphore.
        /// </summary>
        Ps2AudioBackend();

        /// <summary>
        /// Stops the active stream and tears down the AUDSRV bridge.
        /// </summary>
        ~Ps2AudioBackend();

        int32_t Play(::AudioAsset* asset, ::AudioPlaybackRequest* request) override;

        void Stop(int32_t voiceId) override;

        void SetBusGain(std::string busId, float gain) override;

        void SetBusPaused(std::string busId, bool paused) override;

        bool IsPlaying(int32_t voiceId) override;

        void Update() override;

    private:
        static constexpr int32_t StreamChunkByteSize = 2048;
        static constexpr int32_t MaxAudsrvVolume = 100;

        struct ActiveVoiceState {
            int32_t VoiceId;
            const std::uint8_t* EncodedBytes;
            int32_t EncodedByteLength;
            int32_t SourceChannels;
            int32_t FrameByteLength;
            int32_t SourceSampleRate;
            int32_t OutputSampleRate;
            int32_t OutputChannels;
            int32_t OutputFrameByteLength;
            int32_t SourceFrameCount;
            std::int64_t SourcePositionNumerator;
            std::string BusId;
            float BaseGain;
            bool Looping;
            bool Playing;
        };

        static std::string NormalizeBusId(std::string busId);

        static float ClampGain(float gain);

        static int32_t ConvertGainToVolume(float gain);

        static bool UsesPcmEncoding(const std::string& encodingFamilyId);

        static int32_t ResolveAudsrvSampleRate(int32_t sourceSampleRate);

        float ResolveCombinedGain(const std::string& busId, float baseGain) const;

        bool IsBusPaused(const std::string& busId) const;

        void ApplyRpcPatches() const;

        void LoadEmbeddedModule(const void* moduleBytes, int32_t moduleByteLength, const char* moduleId) const;

        void ConfigurePlaybackFormat(int32_t sampleRate, int32_t channels) const;

        void RegisterFillBufferCallback();

        void ApplyActiveVoiceState() const;

        void ResetFillBufferSignalState() const;

        int32_t QueueNextChunk(ActiveVoiceState& voice);

        void ReleaseActiveVoice();

        int32_t NextVoiceId;
        std::unordered_map<std::string, float> BusGainsById;
        std::unordered_set<std::string> PausedBusIds;
        ActiveVoiceState ActiveVoice;
        bool HasActiveVoice;
        int FillBufferSemaId;
        std::array<std::uint8_t, StreamChunkByteSize> StreamChunkBuffer;
    };
}
