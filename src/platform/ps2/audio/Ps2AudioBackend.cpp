#include "platform/ps2/audio/Ps2AudioBackend.hpp"

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <sstream>
#include <stdexcept>
#include <utility>

#include <audsrv.h>
#include <kernel.h>
#include <loadfile.h>
#include <sbv_patches.h>
#include <sifrpc.h>

extern "C" {
#define HE_PS2_EXTERN_IRX(_irx) \
    extern unsigned char _irx[]; \
    extern int size_##_irx

    HE_PS2_EXTERN_IRX(audsrv_irx);
    HE_PS2_EXTERN_IRX(libsd_irx);

#undef HE_PS2_EXTERN_IRX
}

namespace helengine::ps2 {
    namespace {
        /// <summary>
        /// Signals the EE semaphore when AUDSRV requests another stream chunk.
        /// </summary>
        /// <param name="argument">Pointer to the backend-owned semaphore id.</param>
        int SignalFillBuffer(void* argument) {
            if (argument == nullptr) {
                return 0;
            }

            int semaphoreId = *reinterpret_cast<int*>(argument);
            if (semaphoreId >= 0) {
                return iSignalSema(semaphoreId);
            }

            return 0;
        }

        /// <summary>
        /// Builds one descriptive runtime error that includes the latest AUDSRV error string when available.
        /// </summary>
        /// <param name="message">Stable prefix for the failure message.</param>
        /// <returns>Runtime error message with AUDSRV context appended when present.</returns>
        std::string BuildAudsrvErrorMessage(const char* message) {
            const char* errorString = audsrv_get_error_string();
            if (errorString == nullptr || errorString[0] == '\0') {
                return message != nullptr ? message : "AUDSRV call failed.";
            }

            return std::string(message != nullptr ? message : "AUDSRV call failed.")
                + " AUDSRV returned: "
                + errorString;
        }
    }

    Ps2AudioBackend::Ps2AudioBackend()
        : NextVoiceId(0),
          BusGainsById(),
          PausedBusIds(),
          ActiveVoice(),
          HasActiveVoice(false),
          FillBufferSemaId(-1),
          StreamChunkBuffer() {
        BusGainsById.emplace("master", 1.0f);
        BusGainsById.emplace("music", 1.0f);
        BusGainsById.emplace("sfx", 1.0f);

        SifInitRpc(0);
        ApplyRpcPatches();
        LoadEmbeddedModule(libsd_irx, size_libsd_irx, "libsd_irx");
        LoadEmbeddedModule(audsrv_irx, size_audsrv_irx, "audsrv_irx");

        int32_t initializeResult = audsrv_init();
        if (initializeResult < 0) {
            throw std::runtime_error(BuildAudsrvErrorMessage("PS2 audio backend failed to initialize AUDSRV."));
        }

        ee_sema_t semaphore = {};
        semaphore.init_count = 0;
        semaphore.max_count = 1;
        semaphore.option = 0;
        FillBufferSemaId = CreateSema(&semaphore);
        if (FillBufferSemaId < 0) {
            audsrv_quit();
            throw std::runtime_error("PS2 audio backend failed to create the AUDSRV fill-buffer semaphore.");
        }

        RegisterFillBufferCallback();
    }

    Ps2AudioBackend::~Ps2AudioBackend() {
        ReleaseActiveVoice();

        if (FillBufferSemaId >= 0) {
            DeleteSema(FillBufferSemaId);
            FillBufferSemaId = -1;
        }

        audsrv_quit();
    }

    int32_t Ps2AudioBackend::Play(::AudioAsset* asset, ::AudioPlaybackRequest* request) {
        if (asset == nullptr) {
            throw std::invalid_argument("asset");
        }
        if (asset->SampleRate <= 0) {
            throw std::runtime_error("PS2 audio playback requires one audio asset with a positive sample rate.");
        }
        if (asset->Channels != 1 && asset->Channels != 2) {
            throw std::runtime_error("PS2 audio playback currently supports only mono or stereo 16-bit PCM assets.");
        }
        if (!UsesPcmEncoding(asset->EncodingFamilyId)) {
            throw std::runtime_error("PS2 audio playback currently requires shared PCM cooked assets.");
        }
        if (asset->EncodedBytes == nullptr || asset->EncodedBytes->Length <= 0 || asset->EncodedBytes->Data == nullptr) {
            throw std::runtime_error("PS2 audio playback requires one non-empty encoded payload.");
        }
        if ((asset->EncodedBytes->Length % static_cast<int32_t>(sizeof(std::int16_t) * asset->Channels)) != 0) {
            throw std::runtime_error("PS2 audio playback requires 16-bit PCM frame alignment.");
        }

        const int32_t frameByteLength = static_cast<int32_t>(sizeof(std::int16_t) * asset->Channels);
        const int32_t outputSampleRate = ResolveAudsrvSampleRate(asset->SampleRate);
        const int32_t outputChannels = 2;

        ReleaseActiveVoice();
        ConfigurePlaybackFormat(outputSampleRate, outputChannels);
        RegisterFillBufferCallback();
        ResetFillBufferSignalState();

        ActiveVoiceState voice = {};
        voice.VoiceId = NextVoiceId++;
        voice.EncodedBytes = asset->EncodedBytes->Data;
        voice.EncodedByteLength = asset->EncodedBytes->Length;
        voice.SourceChannels = asset->Channels;
        voice.FrameByteLength = frameByteLength;
        voice.SourceSampleRate = asset->SampleRate;
        voice.OutputSampleRate = outputSampleRate;
        voice.OutputChannels = outputChannels;
        voice.OutputFrameByteLength = static_cast<int32_t>(sizeof(std::int16_t) * outputChannels);
        voice.SourceFrameCount = asset->EncodedBytes->Length / frameByteLength;
        voice.SourcePositionNumerator = 0;
        voice.BusId = NormalizeBusId(
            request != nullptr && !request->BusId.empty()
                ? request->BusId
                : asset->DefaultBusId);
        voice.BaseGain = ClampGain(request != nullptr ? request->Gain : 1.0f);
        voice.Looping = request != nullptr ? request->Loop : asset->DefaultLoop;
        voice.Playing = true;

        const float initialCombinedGain = IsBusPaused(voice.BusId)
            ? 0.0f
            : ResolveCombinedGain(voice.BusId, voice.BaseGain);
        if (audsrv_set_volume(ConvertGainToVolume(initialCombinedGain)) < 0) {
            throw std::runtime_error(BuildAudsrvErrorMessage("PS2 audio playback failed to set the initial AUDSRV volume."));
        }

        const int32_t queuedByteLength = QueueNextChunk(voice);
        if (queuedByteLength <= 0) {
            throw std::runtime_error("PS2 audio playback could not queue the first AUDSRV stream chunk.");
        }

        ActiveVoice = std::move(voice);
        HasActiveVoice = true;
        ApplyActiveVoiceState();
        return ActiveVoice.VoiceId;
    }

    void Ps2AudioBackend::Stop(int32_t voiceId) {
        if (!HasActiveVoice || ActiveVoice.VoiceId != voiceId) {
            return;
        }

        ReleaseActiveVoice();
    }

    void Ps2AudioBackend::SetBusGain(std::string busId, float gain) {
        BusGainsById[NormalizeBusId(std::move(busId))] = ClampGain(gain);
        if (HasActiveVoice) {
            ApplyActiveVoiceState();
        }
    }

    void Ps2AudioBackend::SetBusPaused(std::string busId, bool paused) {
        std::string normalizedBusId = NormalizeBusId(std::move(busId));
        if (paused) {
            PausedBusIds.insert(normalizedBusId);
        } else {
            PausedBusIds.erase(normalizedBusId);
        }

        if (HasActiveVoice) {
            ApplyActiveVoiceState();
        }
    }

    bool Ps2AudioBackend::IsPlaying(int32_t voiceId) {
        return HasActiveVoice && ActiveVoice.VoiceId == voiceId && ActiveVoice.Playing;
    }

    void Ps2AudioBackend::Update() {
        if (!HasActiveVoice || !ActiveVoice.Playing) {
            return;
        }

        ApplyActiveVoiceState();
        if (IsBusPaused(ActiveVoice.BusId)) {
            return;
        }

        while (FillBufferSemaId >= 0 && PollSema(FillBufferSemaId) >= 0) {
            const int32_t queuedByteLength = QueueNextChunk(ActiveVoice);
            if (queuedByteLength <= 0) {
                ReleaseActiveVoice();
                return;
            }
        }
    }

    std::string Ps2AudioBackend::NormalizeBusId(std::string busId) {
        if (busId.empty()) {
            return "master";
        }

        std::transform(
            busId.begin(),
            busId.end(),
            busId.begin(),
            [](unsigned char value) {
                return static_cast<char>(std::tolower(value));
            });
        return busId;
    }

    float Ps2AudioBackend::ClampGain(float gain) {
        if (!(gain >= 0.0f) || gain != gain) {
            return 0.0f;
        }

        return std::clamp(gain, 0.0f, 1.0f);
    }

    int32_t Ps2AudioBackend::ConvertGainToVolume(float gain) {
        return static_cast<int32_t>(ClampGain(gain) * static_cast<float>(MaxAudsrvVolume));
    }

    bool Ps2AudioBackend::UsesPcmEncoding(const std::string& encodingFamilyId) {
        return encodingFamilyId == "pcm"
            || encodingFamilyId == "pcm-streamed"
            || encodingFamilyId == "pcm-buffered";
    }

    int32_t Ps2AudioBackend::ResolveAudsrvSampleRate(int32_t sourceSampleRate) {
        if (sourceSampleRate <= 0) {
            throw std::runtime_error("PS2 audio playback requires one positive source sample rate.");
        }

        if (sourceSampleRate >= 44100) {
            return 44100;
        }

        return 22050;
    }

    float Ps2AudioBackend::ResolveCombinedGain(const std::string& busId, float baseGain) const {
        float masterGain = 1.0f;
        auto masterGainIterator = BusGainsById.find("master");
        if (masterGainIterator != BusGainsById.end()) {
            masterGain = masterGainIterator->second;
        }

        float busGain = 1.0f;
        auto busGainIterator = BusGainsById.find(busId);
        if (busGainIterator != BusGainsById.end()) {
            busGain = busGainIterator->second;
        }

        return ClampGain(masterGain * busGain * baseGain);
    }

    bool Ps2AudioBackend::IsBusPaused(const std::string& busId) const {
        return PausedBusIds.contains("master") || PausedBusIds.contains(busId);
    }

    void Ps2AudioBackend::ApplyRpcPatches() const {
        if (sbv_patch_enable_lmb() < 0) {
            throw std::runtime_error("PS2 audio backend failed to apply sbv_patch_enable_lmb.");
        }
        if (sbv_patch_disable_prefix_check() < 0) {
            throw std::runtime_error("PS2 audio backend failed to apply sbv_patch_disable_prefix_check.");
        }
        if (sbv_patch_fileio() < 0) {
            throw std::runtime_error("PS2 audio backend failed to apply sbv_patch_fileio.");
        }
    }

    void Ps2AudioBackend::LoadEmbeddedModule(const void* moduleBytes, int32_t moduleByteLength, const char* moduleId) const {
        if (moduleBytes == nullptr || moduleByteLength <= 0) {
            std::ostringstream message;
            message
                << "PS2 audio backend received one invalid embedded IRX module: "
                << (moduleId != nullptr ? moduleId : "unknown")
                << " ptr=0x"
                << std::hex
                << reinterpret_cast<std::uintptr_t>(moduleBytes)
                << std::dec
                << " size="
                << moduleByteLength;
            throw std::runtime_error(message.str());
        }

        int moduleResult = 0;
        int32_t executeResult = SifExecModuleBuffer(
            const_cast<void*>(moduleBytes),
            moduleByteLength,
            0,
            nullptr,
            &moduleResult);
        if (executeResult < 0 || moduleResult < 0) {
            throw std::runtime_error(std::string("PS2 audio backend failed to load embedded IRX module: ") + (moduleId != nullptr ? moduleId : "unknown"));
        }
    }

    void Ps2AudioBackend::ConfigurePlaybackFormat(int32_t sampleRate, int32_t channels) const {
        audsrv_fmt_t format = {};
        format.bits = 16;
        format.freq = sampleRate;
        format.channels = channels;
        if (audsrv_set_format(&format) < 0) {
            throw std::runtime_error(BuildAudsrvErrorMessage("PS2 audio playback failed to configure the AUDSRV stream format."));
        }
    }

    void Ps2AudioBackend::RegisterFillBufferCallback() {
        if (FillBufferSemaId < 0) {
            throw std::runtime_error("PS2 audio backend cannot register the AUDSRV fill-buffer callback without one valid semaphore.");
        }

        int32_t callbackResult = audsrv_on_fillbuf(StreamChunkByteSize, SignalFillBuffer, &FillBufferSemaId);
        if (callbackResult < 0) {
            throw std::runtime_error(BuildAudsrvErrorMessage("PS2 audio playback failed to register the AUDSRV fill-buffer callback."));
        }
    }

    void Ps2AudioBackend::ApplyActiveVoiceState() const {
        if (!HasActiveVoice) {
            return;
        }

        const float combinedGain = IsBusPaused(ActiveVoice.BusId)
            ? 0.0f
            : ResolveCombinedGain(ActiveVoice.BusId, ActiveVoice.BaseGain);
        if (audsrv_set_volume(ConvertGainToVolume(combinedGain)) < 0) {
            throw std::runtime_error(BuildAudsrvErrorMessage("PS2 audio playback failed to update the AUDSRV volume."));
        }
    }

    void Ps2AudioBackend::ResetFillBufferSignalState() const {
        if (FillBufferSemaId < 0) {
            return;
        }

        while (PollSema(FillBufferSemaId) >= 0) {
        }
    }

    int32_t Ps2AudioBackend::QueueNextChunk(ActiveVoiceState& voice) {
        int32_t queuedByteLength = 0;
        while (queuedByteLength + voice.OutputFrameByteLength <= StreamChunkByteSize) {
            if (voice.SourceFrameCount <= 0
                || voice.FrameByteLength <= 0
                || voice.OutputFrameByteLength <= 0
                || voice.OutputSampleRate <= 0) {
                break;
            }

            std::int64_t sourceFrameIndex = voice.SourcePositionNumerator / voice.OutputSampleRate;
            while (sourceFrameIndex >= voice.SourceFrameCount) {
                if (!voice.Looping) {
                    sourceFrameIndex = -1;
                    break;
                }

                voice.SourcePositionNumerator -= static_cast<std::int64_t>(voice.SourceFrameCount) * voice.OutputSampleRate;
                sourceFrameIndex = voice.SourcePositionNumerator / voice.OutputSampleRate;
            }

            if (sourceFrameIndex < 0) {
                break;
            }

            const std::uint8_t* sourceFrameBytes = voice.EncodedBytes + (static_cast<int32_t>(sourceFrameIndex) * voice.FrameByteLength);
            if (voice.SourceChannels == 1 && voice.OutputChannels == 2) {
                const std::int16_t monoSample = *reinterpret_cast<const std::int16_t*>(sourceFrameBytes);
                std::int16_t* outputSamples = reinterpret_cast<std::int16_t*>(StreamChunkBuffer.data() + queuedByteLength);
                outputSamples[0] = monoSample;
                outputSamples[1] = monoSample;
            } else {
                std::memcpy(
                    StreamChunkBuffer.data() + queuedByteLength,
                    sourceFrameBytes,
                    static_cast<std::size_t>(voice.OutputFrameByteLength));
            }

            queuedByteLength += voice.OutputFrameByteLength;
            voice.SourcePositionNumerator += voice.SourceSampleRate;
        }

        if (queuedByteLength <= 0) {
            voice.Playing = false;
            return 0;
        }

        if (audsrv_wait_audio(queuedByteLength) < 0) {
            throw std::runtime_error(BuildAudsrvErrorMessage("PS2 audio playback failed while waiting for AUDSRV stream space."));
        }

        const int32_t playResult = audsrv_play_audio(
            reinterpret_cast<const char*>(StreamChunkBuffer.data()),
            queuedByteLength);
        if (playResult < 0) {
            throw std::runtime_error(BuildAudsrvErrorMessage("PS2 audio playback failed while queueing one AUDSRV stream chunk."));
        }
        if (playResult != queuedByteLength) {
            throw std::runtime_error("PS2 audio playback queued one partial AUDSRV stream chunk unexpectedly.");
        }

        return queuedByteLength;
    }

    void Ps2AudioBackend::ReleaseActiveVoice() {
        if (HasActiveVoice) {
            audsrv_stop_audio();
        }

        ActiveVoice = {};
        HasActiveVoice = false;
        ResetFillBufferSignalState();
    }
}
