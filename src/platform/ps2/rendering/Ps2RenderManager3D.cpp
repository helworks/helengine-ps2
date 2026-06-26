#include "platform/ps2/rendering/Ps2RenderManager3D.hpp"

#include <algorithm>
#include <cmath>
#include <ctime>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <malloc.h>
#include <memory>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>

#include <dma.h>
#include <debug.h>
#include <draw.h>
#include <gsKit.h>
#include <packet2.h>

#include "ContentManager.hpp"
#include "AssetSerializer.hpp"
#include "Ps2AssetSerializer.hpp"
#include "CameraComponent.hpp"
#include "Core.hpp"
#include "DirectionalLightComponent.hpp"
#include "Entity.hpp"
#include "IDrawable3D.hpp"
#include "ModelAsset.hpp"
#include "ObjectManager.hpp"
#include "PlatformMaterialAsset.hpp"
#include "Ps2ModelAsset.hpp"
#include "Ps2TextureAsset.hpp"
#include "Ps2MaterialAsset.hpp"
#include "Ps2MaterialLightingMode.hpp"
#include "MaterialBlendMode.hpp"
#include "RenderTarget.hpp"
#include "runtime/runtime_ps2_asset_path_manifest.hpp"
#include "float2.hpp"
#include "float3.hpp"
#include "platform/ps2/rendering/Ps2FramePlan.hpp"
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"
#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"
#include "runtime/finally.hpp"
#include "runtime/native_cast.hpp"
#include "system/io/file.hpp"
#include "system/io/path.hpp"
#include "float4.hpp"
#include "float4x4.hpp"

namespace helengine::ps2 {
    namespace {
        std::string ResolvePs2CookedAssetOpenPath(const std::string& path) {
            if (path.empty()) {
                return path;
            }

            const char* physicalPath = he_get_runtime_ps2_asset_physical_path(path.c_str());
            if (physicalPath != nullptr && physicalPath[0] != '\0') {
                return physicalPath;
            }

            std::string normalizedPath = path;
            std::replace(normalizedPath.begin(), normalizedPath.end(), '/', '\\');
            if (normalizedPath.rfind("cdrom0:", 0) == 0) {
                if (normalizedPath.size() < 2u || normalizedPath.compare(normalizedPath.size() - 2u, 2u, ";1") != 0) {
                    normalizedPath += ";1";
                }

                return normalizedPath;
            }

            const bool isCookedDiscRelativePath = normalizedPath.rfind("COOKED\\", 0) == 0 || normalizedPath.rfind("\\COOKED\\", 0) == 0;
            if (!isCookedDiscRelativePath) {
                return path;
            }

            if (normalizedPath.rfind("\\", 0) == 0) {
                normalizedPath.erase(0, 1);
            }
            if (normalizedPath.size() < 2u || normalizedPath.compare(normalizedPath.size() - 2u, 2u, ";1") != 0) {
                normalizedPath += ";1";
            }

            return std::string("cdrom0:\\") + normalizedPath;
        }

        void AppendDiagnosticToHostBootLog(const std::string& message) {
            const char* candidatePaths[] = {
                "host:ps2_bootlog.txt",
                "host0:ps2_bootlog.txt"
            };

            for (const char* candidatePath : candidatePaths) {
                FILE* file = std::fopen(candidatePath, "a");
                if (file == nullptr) {
                    continue;
                }

                std::fwrite(message.c_str(), 1u, message.size(), file);
                std::fwrite("\n", 1u, 1u, file);
                std::fflush(file);
                std::fclose(file);
                return;
            }
        }

        constexpr double LightingScale = 191.0;
        constexpr double LightingBias = 64.0;
        constexpr bool EnableFlatColorDiagnostics = false;
        constexpr bool EnableLightingOnlyDiagnostics = false;
        constexpr bool EnableSingleProxyDiagnostics = false;
        constexpr bool EnableLegacyCpuOpaquePathDiagnostics = false;
        constexpr std::size_t SingleProxyDiagnosticIndex = 1;
        constexpr float HdrGlowScale = 1.08f;
        constexpr float HdrGlowScaleVariance = 0.08f;
        constexpr float HdrGlowDepthBias = 0.0005f;
        constexpr float HdrGlowDepthBiasVariance = 0.0003f;
        constexpr std::uint8_t HdrGlowThreshold = 0xC0;
        constexpr std::uint8_t HdrGlowHotThreshold = 0xD8;
        constexpr std::uint8_t HdrGlowExposureCeiling = 0xE0;
        constexpr float HdrGlowBoostMinimum = 0.35f;
        constexpr float HdrGlowBoostVariance = 0.45f;
        constexpr float CameraFieldOfViewRadians = 0.785398185f;
        constexpr float MinimumClipW = 0.0001f;
        constexpr float MinimumNearPlaneEpsilon = 0.00001f;
        constexpr std::uint8_t AlphaTestCutoff = 0x80;
        constexpr bool EnableVuDispatchBypassDiagnostics = false;
        constexpr bool EnableVuDirectGifDispatchDiagnostics = false;
        constexpr bool EnableVuDirectGifHelperTriangleDiagnostics = false;
        constexpr bool EnableVuSingleTrianglePayloadDiagnostics = false;
        constexpr std::uint32_t TexturedVuSubmitDiagnosticLogLimit = 16u;
        constexpr float VuDirectGifDiagnosticTriangleAX = 211.843231f;
        constexpr float VuDirectGifDiagnosticTriangleAY = 332.156738f;
        constexpr float VuDirectGifDiagnosticTriangleBX = 211.843231f;
        constexpr float VuDirectGifDiagnosticTriangleBY = 115.843239f;
        constexpr float VuDirectGifDiagnosticTriangleCX = 428.156738f;
        constexpr float VuDirectGifDiagnosticTriangleCY = 332.156738f;
        constexpr std::uint32_t VuDirectGifDiagnosticTriangleZ = 0x007FFFFFu;
        const ::float3 DefaultForward(0.0f, 0.0f, -1.0f);
        const ::float3 DefaultUp(0.0f, 1.0f, 0.0f);

        double ResolveMillisecondsFromClockTicks(std::clock_t startTicks, std::clock_t endTicks) {
            if (endTicks <= startTicks) {
                return 0.0;
            }

            return (static_cast<double>(endTicks - startTicks) / static_cast<double>(CLOCKS_PER_SEC)) * 1000.0;
        }

        ::float4 ResolvePixelViewport(::CameraComponent* camera, const int2& windowSize) {
            if (camera == nullptr) {
                throw std::invalid_argument("Camera must be provided.");
            }
            if (windowSize.X <= 0 || windowSize.Y <= 0) {
                throw std::invalid_argument("Window size must be greater than zero.");
            }

            const ::float4 viewport = camera->get_Viewport();
            double offsetX = viewport.X;
            double offsetY = viewport.Y;
            double width = viewport.Z;
            double height = viewport.W;
            if (width <= 1.0 && height <= 1.0) {
                offsetX *= static_cast<double>(windowSize.X);
                offsetY *= static_cast<double>(windowSize.Y);
                width *= static_cast<double>(windowSize.X);
                height *= static_cast<double>(windowSize.Y);
            }

            return ::float4(
                static_cast<float>(offsetX),
                static_cast<float>(offsetY),
                static_cast<float>(width),
                static_cast<float>(height));
        }

        void LogVuDirectGifDiagnostics(const std::string& message) {
            scr_printf("[helengine-ps2] %s\n", message.c_str());
            std::printf("[helengine-ps2] %s\n", message.c_str());
            std::fflush(stdout);
        }

        std::uint64_t BuildVuDirectGifDiagnosticPosition(float screenX, float screenY) {
            const std::int32_t gsX = static_cast<std::int32_t>((2048.0f + screenX) * 16.0f);
            const std::int32_t gsY = static_cast<std::int32_t>((2048.0f + screenY) * 16.0f);
            return GS_SETREG_XYZ2(gsX, gsY, VuDirectGifDiagnosticTriangleZ);
        }

        struct Ps2ClipVertex {
            ::float3 ViewPosition;
            ::float2 TexCoord;
            std::uint8_t Red;
            std::uint8_t Green;
            std::uint8_t Blue;
            std::uint8_t Alpha;
        };

        struct Ps2HdrGlowTriangle {
            float ScreenAX;
            float ScreenAY;
            float ScreenAZ;
            float ScreenBX;
            float ScreenBY;
            float ScreenBZ;
            float ScreenCX;
            float ScreenCY;
            float ScreenCZ;
            ::float2 TexCoordA;
            ::float2 TexCoordB;
            ::float2 TexCoordC;
            std::uint64_t ColorA;
            std::uint64_t ColorB;
            std::uint64_t ColorC;
            float GlowStrength;
            GSTEXTURE* Texture;
            bool UseTexture;
        };

        std::unordered_map<std::string, GSTEXTURE*> TextureRecords;
        std::vector<Ps2HdrGlowTriangle> DeferredHdrGlowTriangles;

        bool IsLiveEntity(::Entity* entity) {
            return entity != nullptr && !entity->get_IsDisposed();
        }

        const helengine::ps2::Ps2RenderProxy* ResolveRenderableProxyByIndex(const helengine::ps2::Ps2FramePlan& plan, std::size_t proxyIndex) {
            const std::vector<const helengine::ps2::Ps2RenderProxy*>* lists[] = {
                &plan.OpaqueWorld,
                &plan.OpaqueDynamic,
                &plan.AlphaWorld,
                &plan.AlphaDynamic
            };

            std::size_t currentIndex = 0;
            for (const std::vector<const helengine::ps2::Ps2RenderProxy*>* list : lists) {
                if (list == nullptr) {
                    continue;
                }

                for (const helengine::ps2::Ps2RenderProxy* proxy : *list) {
                    if (proxy != nullptr && currentIndex++ == proxyIndex) {
                        return proxy;
                    }
                }
            }

            return nullptr;
        }

        std::uint64_t ResolveDiagnosticProxyColor(const helengine::ps2::Ps2RenderProxy& proxy) {
            static constexpr std::uint64_t DiagnosticPalette[] = {
                GS_SETREG_RGBAQ(0xD0, 0x40, 0x40, 0x80, 0x00),
                GS_SETREG_RGBAQ(0x40, 0xD0, 0x40, 0x80, 0x00),
                GS_SETREG_RGBAQ(0x40, 0x70, 0xD0, 0x80, 0x00),
                GS_SETREG_RGBAQ(0xD0, 0xC0, 0x40, 0x80, 0x00),
                GS_SETREG_RGBAQ(0x40, 0xC0, 0xC0, 0x80, 0x00),
                GS_SETREG_RGBAQ(0xC0, 0x40, 0xC0, 0x80, 0x00),
                GS_SETREG_RGBAQ(0xD0, 0x80, 0x40, 0x80, 0x00),
                GS_SETREG_RGBAQ(0xD0, 0xD0, 0xD0, 0x80, 0x00)
            };

            ::IDrawable3D* drawable = proxy.GetDrawable();
            ::Entity* parent = drawable != nullptr ? drawable->get_Parent() : nullptr;
            const void* diagnosticSource = parent != nullptr
                ? static_cast<const void*>(parent)
                : static_cast<const void*>(drawable);
            const std::uintptr_t diagnosticKey = reinterpret_cast<std::uintptr_t>(diagnosticSource);
            const std::size_t paletteIndex = std::hash<std::uintptr_t>{}(diagnosticKey) % (sizeof(DiagnosticPalette) / sizeof(DiagnosticPalette[0]));
            return DiagnosticPalette[paletteIndex];
        }

        std::uint8_t ResolveColorComponent(std::uint64_t color, int shift) {
            return static_cast<std::uint8_t>((color >> shift) & 0xFFu);
        }

        std::uint64_t PackColor(std::uint8_t red, std::uint8_t green, std::uint8_t blue, std::uint8_t alpha) {
            return GS_SETREG_RGBAQ(red, green, blue, alpha, 0x00);
        }

        float ResolveGlowStrengthFromColor(std::uint64_t color) {
            const std::uint8_t red = ResolveColorComponent(color, 0);
            const std::uint8_t green = ResolveColorComponent(color, 8);
            const std::uint8_t blue = ResolveColorComponent(color, 16);
            const std::uint8_t brightestChannel = std::max({ red, green, blue });
            if (brightestChannel <= HdrGlowThreshold) {
                return 0.0f;
            }

            const float numerator = static_cast<float>(brightestChannel - HdrGlowThreshold);
            const float denominator = static_cast<float>(HdrGlowHotThreshold - HdrGlowThreshold);
            const float normalizedStrength = denominator <= 0.0f ? 0.0f : std::clamp(numerator / denominator, 0.0f, 1.0f);
            return normalizedStrength * normalizedStrength;
        }

        std::uint64_t BoostHdrGlowColor(std::uint64_t color, float glowStrength) {
            const float boostStrength = std::clamp(HdrGlowBoostMinimum + (glowStrength * HdrGlowBoostVariance), 0.0f, 1.0f);
            const auto boostChannel = [boostStrength](std::uint8_t value) {
                const double boosted = static_cast<double>(value) + ((255.0 - static_cast<double>(value)) * static_cast<double>(boostStrength));
                return static_cast<std::uint8_t>(std::clamp(std::lround(boosted), 0l, static_cast<long>(HdrGlowExposureCeiling)));
            };

            return GS_SETREG_RGBAQ(
                boostChannel(ResolveColorComponent(color, 0)),
                boostChannel(ResolveColorComponent(color, 8)),
                boostChannel(ResolveColorComponent(color, 16)),
                ResolveColorComponent(color, 24),
                0x00);
        }

        Ps2ClipVertex CreateClipVertex(const ::float3& viewPosition, const ::float2& texCoord, std::uint64_t color) {
            Ps2ClipVertex vertex;
            vertex.ViewPosition = viewPosition;
            vertex.TexCoord = texCoord;
            vertex.Red = ResolveColorComponent(color, 0);
            vertex.Green = ResolveColorComponent(color, 8);
            vertex.Blue = ResolveColorComponent(color, 16);
            vertex.Alpha = ResolveColorComponent(color, 24);
            return vertex;
        }

        ::float2 ResolveGsTextureCoordinate(const ::float2& normalizedTexCoord, const GSTEXTURE* texture) {
            if (texture == nullptr) {
                return ::float2(0.0f, 0.0f);
            }

            return ::float2(
                normalizedTexCoord.X * static_cast<float>(texture->Width),
                normalizedTexCoord.Y * static_cast<float>(texture->Height));
        }

        std::uint8_t InterpolateComponent(std::uint8_t start, std::uint8_t end, float amount) {
            const double blended = static_cast<double>(start) + ((static_cast<double>(end) - static_cast<double>(start)) * static_cast<double>(amount));
            return static_cast<std::uint8_t>(std::clamp(std::lround(blended), 0l, 255l));
        }

        Ps2ClipVertex InterpolateClipVertex(const Ps2ClipVertex& start, const Ps2ClipVertex& end, float amount) {
            Ps2ClipVertex vertex;
            vertex.ViewPosition.X = start.ViewPosition.X + ((end.ViewPosition.X - start.ViewPosition.X) * amount);
            vertex.ViewPosition.Y = start.ViewPosition.Y + ((end.ViewPosition.Y - start.ViewPosition.Y) * amount);
            vertex.ViewPosition.Z = start.ViewPosition.Z + ((end.ViewPosition.Z - start.ViewPosition.Z) * amount);
            vertex.TexCoord.X = start.TexCoord.X + ((end.TexCoord.X - start.TexCoord.X) * amount);
            vertex.TexCoord.Y = start.TexCoord.Y + ((end.TexCoord.Y - start.TexCoord.Y) * amount);
            vertex.Red = InterpolateComponent(start.Red, end.Red, amount);
            vertex.Green = InterpolateComponent(start.Green, end.Green, amount);
            vertex.Blue = InterpolateComponent(start.Blue, end.Blue, amount);
            vertex.Alpha = InterpolateComponent(start.Alpha, end.Alpha, amount);
            return vertex;
        }

        void ClipTriangleAgainstNearPlane(const Ps2ClipVertex& first, const Ps2ClipVertex& second, const Ps2ClipVertex& third, float nearPlaneDistance, std::vector<Ps2ClipVertex>& clippedVertices) {
            clippedVertices.clear();

            const float nearPlaneZ = -nearPlaneDistance;
            Ps2ClipVertex previous = third;
            bool previousInside = previous.ViewPosition.Z <= nearPlaneZ;
            const Ps2ClipVertex vertices[3] = { first, second, third };

            for (const Ps2ClipVertex& current : vertices) {
                bool currentInside = current.ViewPosition.Z <= nearPlaneZ;
                if (currentInside != previousInside) {
                    const float denominator = current.ViewPosition.Z - previous.ViewPosition.Z;
                    if (std::abs(denominator) > MinimumNearPlaneEpsilon) {
                        const float amount = (nearPlaneZ - previous.ViewPosition.Z) / denominator;
                        clippedVertices.push_back(InterpolateClipVertex(previous, current, amount));
                    }
                }
                if (currentInside) {
                    clippedVertices.push_back(current);
                }

                previous = current;
                previousInside = currentInside;
            }
        }

        int ResolveGsPixelStorageMode(::Ps2TexturePixelStorageMode mode) {
            if (mode == ::Ps2TexturePixelStorageMode::PsmCt32) {
                return GS_PSM_CT32;
            } else if (mode == ::Ps2TexturePixelStorageMode::PsmT8) {
                return GS_PSM_T8;
            } else if (mode == ::Ps2TexturePixelStorageMode::PsmT4) {
                return GS_PSM_T4;
            }

            throw std::runtime_error("Unsupported PS2 texture pixel storage mode.");
        }

        int ResolveClutWidth(::Ps2TextureAsset* data) {
            if (data == nullptr || data->PaletteData == nullptr || data->PaletteData->Length <= 0) {
                return 0;
            }

            int paletteEntryCount = data->PaletteData->Length / 4;
            if (paletteEntryCount <= 16) {
                return 8;
            }

            return 16;
        }

        int ResolveClutHeight(::Ps2TextureAsset* data) {
            if (data == nullptr || data->PaletteData == nullptr || data->PaletteData->Length <= 0) {
                return 0;
            }

            int paletteEntryCount = data->PaletteData->Length / 4;
            if (paletteEntryCount <= 16) {
                return 2;
            }

            return 16;
        }

        GSTEXTURE* BuildTextureFromAsset(GSGLOBAL* gsGlobal, ::Ps2TextureAsset* data) {
            if (gsGlobal == nullptr || data == nullptr || data->PixelData == nullptr || data->PixelData->Length <= 0 || data->Width <= 0 || data->Height <= 0) {
                return nullptr;
            }

            GSTEXTURE* texture = new GSTEXTURE();
            texture->Width = data->Width;
            texture->Height = data->Height;
            texture->PSM = ResolveGsPixelStorageMode(data->PixelStorageMode);
            texture->ClutPSM = ResolveGsPixelStorageMode(data->ClutPixelStorageMode);
            texture->Clut = nullptr;
            texture->VramClut = 0;
            texture->Filter = GS_FILTER_NEAREST;
            texture->ClutStorageMode = GS_CLUT_STORAGE_CSM1;
            texture->Mem = static_cast<u32*>(memalign(128, static_cast<std::size_t>(data->PixelData->Length)));
            if (texture->Mem == nullptr) {
                delete texture;
                return nullptr;
            }

            std::memcpy(texture->Mem, data->PixelData->Data, static_cast<std::size_t>(data->PixelData->Length));
            if (data->PaletteData != nullptr && data->PaletteData->Length > 0) {
                texture->Clut = static_cast<u32*>(memalign(128, static_cast<std::size_t>(data->PaletteData->Length)));
                if (texture->Clut == nullptr) {
                    free(texture->Mem);
                    delete texture;
                    return nullptr;
                }

                std::memcpy(texture->Clut, data->PaletteData->Data, static_cast<std::size_t>(data->PaletteData->Length));
            }
            texture->Vram = gsKit_vram_alloc(
                gsGlobal,
                gsKit_texture_size(texture->Width, texture->Height, texture->PSM),
                GSKIT_ALLOC_USERBUFFER);
            if (texture->Vram == GSKIT_ALLOC_ERROR) {
                free(texture->Clut);
                free(texture->Mem);
                delete texture;
                return nullptr;
            }
            if (texture->Clut != nullptr) {
                texture->VramClut = gsKit_vram_alloc(
                    gsGlobal,
                    gsKit_texture_size(ResolveClutWidth(data), ResolveClutHeight(data), GS_PSM_CT32),
                    GSKIT_ALLOC_USERBUFFER);
                if (texture->VramClut == GSKIT_ALLOC_ERROR) {
                    free(texture->Clut);
                    free(texture->Mem);
                    delete texture;
                    return nullptr;
                }
            }

            return texture;
        }

        GSTEXTURE* ResolveTexture(GSGLOBAL* gsGlobal, const std::string& textureRelativePath) {
            if (textureRelativePath.empty()) {
                return nullptr;
            }

            auto textureIt = TextureRecords.find(textureRelativePath);
            if (textureIt != TextureRecords.end()) {
                return textureIt->second;
            }

            ::Core* core = ::Core::get_Instance();
            if (core == nullptr || core->get_ContentManager() == nullptr) {
                return nullptr;
            }

            const std::string resolvedTexturePath = ResolvePs2CookedAssetOpenPath(textureRelativePath);
            ::FileStream* stream = nullptr;
            try {
                stream = ::File::OpenRead(resolvedTexturePath);
            } catch (const std::exception& exception) {
                throw std::runtime_error(
                    std::string("ResolveTexture failed relativePath='")
                    + textureRelativePath
                    + "' resolvedPath='"
                    + resolvedTexturePath
                    + "' inner="
                    + exception.what());
            }
            [[maybe_unused]] auto streamGuard = he_cpp_make_scope_exit([stream]() {
                if (stream != nullptr) {
                    stream->Dispose();
                }
            });
            ::Asset* asset = ::Ps2AssetSerializer::Deserialize(stream);
            ::Ps2TextureAsset* textureAsset = he_cpp_try_cast<::Ps2TextureAsset>(asset);
            GSTEXTURE* texture = BuildTextureFromAsset(gsGlobal, textureAsset);
            if (texture == nullptr) {
                return nullptr;
            }

            gsKit_texture_upload(gsGlobal, texture);
            TextureRecords.emplace(textureRelativePath, texture);
            return texture;
        }

        void DrawHdrGlowPass(GSGLOBAL* gsGlobal) {
            if (gsGlobal == nullptr || DeferredHdrGlowTriangles.empty()) {
                DeferredHdrGlowTriangles.clear();
                return;
            }

            gsKit_set_test(gsGlobal, GS_ATEST_OFF);

            gsKit_set_primalpha(gsGlobal, GS_SETREG_ALPHA(0, 2, 2, 1, 0x80), 0);
            gsGlobal->PrimAlphaEnable = GS_SETTING_ON;
            for (const Ps2HdrGlowTriangle& triangle : DeferredHdrGlowTriangles) {
                const float glowScale = HdrGlowScale + (triangle.GlowStrength * HdrGlowScaleVariance);
                const float glowDepthBias = HdrGlowDepthBias + (triangle.GlowStrength * HdrGlowDepthBiasVariance);
                const float centerX = (triangle.ScreenAX + triangle.ScreenBX + triangle.ScreenCX) / 3.0f;
                const float centerY = (triangle.ScreenAY + triangle.ScreenBY + triangle.ScreenCY) / 3.0f;
                const float glowAX = centerX + ((triangle.ScreenAX - centerX) * glowScale);
                const float glowAY = centerY + ((triangle.ScreenAY - centerY) * glowScale);
                const float glowBX = centerX + ((triangle.ScreenBX - centerX) * glowScale);
                const float glowBY = centerY + ((triangle.ScreenBY - centerY) * glowScale);
                const float glowCX = centerX + ((triangle.ScreenCX - centerX) * glowScale);
                const float glowCY = centerY + ((triangle.ScreenCY - centerY) * glowScale);
                const float glowAZ = std::max(0.0f, triangle.ScreenAZ - glowDepthBias);
                const float glowBZ = std::max(0.0f, triangle.ScreenBZ - glowDepthBias);
                const float glowCZ = std::max(0.0f, triangle.ScreenCZ - glowDepthBias);
                const std::uint64_t glowColorA = BoostHdrGlowColor(triangle.ColorA, triangle.GlowStrength);
                const std::uint64_t glowColorB = BoostHdrGlowColor(triangle.ColorB, triangle.GlowStrength);
                const std::uint64_t glowColorC = BoostHdrGlowColor(triangle.ColorC, triangle.GlowStrength);

                if (triangle.UseTexture && triangle.Texture != nullptr) {
                    const ::float2 glowTexCoordA = ResolveGsTextureCoordinate(triangle.TexCoordA, triangle.Texture);
                    const ::float2 glowTexCoordB = ResolveGsTextureCoordinate(triangle.TexCoordB, triangle.Texture);
                    const ::float2 glowTexCoordC = ResolveGsTextureCoordinate(triangle.TexCoordC, triangle.Texture);
                    gsKit_prim_triangle_goraud_texture_3d(
                        gsGlobal,
                        triangle.Texture,
                        glowAX, glowAY, glowAZ, glowTexCoordA.X, glowTexCoordA.Y,
                        glowBX, glowBY, glowBZ, glowTexCoordB.X, glowTexCoordB.Y,
                        glowCX, glowCY, glowCZ, glowTexCoordC.X, glowTexCoordC.Y,
                        glowColorA, glowColorB, glowColorC);
                } else {
                    gsKit_prim_triangle_gouraud_3d(
                        gsGlobal,
                        glowAX, glowAY, glowAZ,
                        glowBX, glowBY, glowBZ,
                        glowCX, glowCY, glowCZ,
                        glowColorA, glowColorB, glowColorC);
                }
            }

            if (gsGlobal->ZBuffering == GS_SETTING_ON) {
                gsKit_set_test(gsGlobal, GS_ZTEST_ON);
            } else {
                gsKit_set_test(gsGlobal, GS_ZTEST_OFF);
            }

            DeferredHdrGlowTriangles.clear();
        }
    }

    Ps2RenderManager3D::Ps2RenderManager3D()
        : FramePlanner(),
          VuOpaqueBatchBuilder(),
          VuProgramRegistry(),
          VuVifPacketBuilder(),
          VuGifStateEncoder(),
          UseLegacyCpuOpaquePath(EnableLegacyCpuOpaquePathDiagnostics),
          HdrEnabled(false),
          GsGlobal(nullptr),
          Proxies(),
          PendingReleasedMaterials(),
          PendingReleasedModels(),
          LastProxyCount(0),
          LastOpaqueWorldCount(0),
          LastOpaqueDynamicCount(0),
          LastAlphaWorldCount(0),
          LastAlphaDynamicCount(0),
          LastClipRejectCount(0),
          LastProjectionRejectCount(0),
          LastCullRejectCount(0),
          LastSubmittedTriangleCount(0),
          LastVuBatchDispatchCount(0),
          LastVuTriangleVertexCount(0),
          LastVuPacketByteCount(0),
            LastVuRejectedMissingMaterialCount(0),
            LastVuRejectedMissingModelCount(0),
            LastVuRejectedMissingPackedModelCount(0),
            LastVuPacketPhase(0),
            LastProxySyncMilliseconds(0.0),
            LastFramePlanMilliseconds(0.0),
            LastVuBatchBuildMilliseconds(0.0),
            LastVuWaitMilliseconds(0.0),
            LastVuSubmitMilliseconds(0.0),
            LastVuPacketEncodeMilliseconds(0.0),
            LastVuTriangleSetupMilliseconds(0.0),
            LastVuPacketAssemblyMilliseconds(0.0),
            LastVuTrianglePrepMilliseconds(0.0),
            LastVuTriangleEmitMilliseconds(0.0),
            LastVuTriangleLightingMilliseconds(0.0),
            LastVuTrianglePayloadFillMilliseconds(0.0),
            LastResolvedViewport(),
          LastSubmittedScreenBounds(),
          LastSubmittedTriangleBoundsA(),
          LastSubmittedTriangleBoundsB(),
          LastSubmittedTriangleVertexA0(),
          LastSubmittedTriangleVertexA1(),
          LastSubmittedTriangleVertexA2(),
          LastSubmittedTriangleVertexB0(),
          LastSubmittedTriangleVertexB1(),
          LastSubmittedTriangleVertexB2() {
    }

    ::RuntimeMaterial* Ps2RenderManager3D::BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset) {
        if (materialAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked platform material asset is required.");
        }

        Ps2RuntimeMaterial* runtimeMaterial = new Ps2RuntimeMaterial();
        runtimeMaterial->LoadFromCooked(materialAsset);
        return runtimeMaterial;
    }

    ::RuntimeMaterial* Ps2RenderManager3D::BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset) {
        if (materialAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked material asset is required.");
        }

        Ps2RuntimeMaterial* runtimeMaterial = new Ps2RuntimeMaterial();
        runtimeMaterial->LoadFromCooked(materialAsset);
        return runtimeMaterial;
    }

    ::RuntimeMaterial* Ps2RenderManager3D::BuildMaterialFromCooked(std::string cookedAssetPath) {
        if (cookedAssetPath.empty()) {
            throw std::invalid_argument("PS2 cooked material path is required.");
        }

        const std::string resolvedCookedAssetPath = ResolvePs2CookedAssetOpenPath(cookedAssetPath);
        ::FileStream* stream = nullptr;
        try {
            stream = ::File::OpenRead(resolvedCookedAssetPath);
        } catch (const std::exception& exception) {
            throw std::runtime_error(
                std::string("BuildMaterialFromCooked failed path='")
                + cookedAssetPath
                + "' resolvedPath='"
                + resolvedCookedAssetPath
                + "' inner="
                + exception.what());
        }
        [[maybe_unused]] auto streamGuard = he_cpp_make_scope_exit([stream]() {
            if (stream != nullptr) {
                stream->Dispose();
            }
        });
        ::Asset* asset = ::Ps2AssetSerializer::Deserialize(stream);
        ::Ps2MaterialAsset* cookedMaterialAsset = he_cpp_try_cast<::Ps2MaterialAsset>(asset);
        if (cookedMaterialAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked material payload did not deserialize as Ps2MaterialAsset.");
        }

        return BuildMaterialFromCooked(cookedMaterialAsset);
    }

    ::RuntimeModel* Ps2RenderManager3D::BuildModelFromCooked(std::string cookedAssetPath) {
        if (cookedAssetPath.empty()) {
            throw std::invalid_argument("PS2 cooked model path is required.");
        }

        const std::string resolvedCookedAssetPath = ResolvePs2CookedAssetOpenPath(cookedAssetPath);
        ::FileStream* modelStream = nullptr;
        try {
            modelStream = ::File::OpenRead(resolvedCookedAssetPath);
        } catch (const std::exception& exception) {
            throw std::runtime_error(
                std::string("BuildModelFromCooked failed path='")
                + cookedAssetPath
                + "' resolvedPath='"
                + resolvedCookedAssetPath
                + "' inner="
                + exception.what());
        }
        [[maybe_unused]] auto modelStreamGuard = he_cpp_make_scope_exit([modelStream]() {
            if (modelStream != nullptr) {
                modelStream->Dispose();
            }
        });
        ::Asset* modelAssetBase = ::Ps2AssetSerializer::Deserialize(modelStream);
        ::Ps2ModelAsset* modelAsset = he_cpp_try_cast<::Ps2ModelAsset>(modelAssetBase);
        if (modelAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked model payload did not deserialize as Ps2ModelAsset.");
        }

        Ps2RuntimeModel* runtimeModel = new Ps2RuntimeModel();
        runtimeModel->LoadFromCooked(modelAsset);
        return runtimeModel;
    }

    ::RuntimeModel* Ps2RenderManager3D::BuildModelFromRaw(::ModelAsset* data) {
        if (data == nullptr) {
            throw std::invalid_argument("PS2 raw model data is required.");
        }

        Ps2RuntimeModel* runtimeModel = new Ps2RuntimeModel();
        if (EnableVuSingleTrianglePayloadDiagnostics) {
            runtimeModel->LoadFromRawWithoutPackedMesh(data);
        } else {
            runtimeModel->LoadFromRaw(data);
        }
        return runtimeModel;
    }

    void Ps2RenderManager3D::FlushReleasedAssets() {
        for (::RuntimeMaterial* material : PendingReleasedMaterials) {
            if (material == nullptr) {
                continue;
            }

            material->Dispose();
            delete material;
        }

        PendingReleasedMaterials.clear();

        for (::RuntimeModel* model : PendingReleasedModels) {
            if (model == nullptr) {
                continue;
            }

            model->Dispose();
            delete model;
        }

        PendingReleasedModels.clear();
    }

    void Ps2RenderManager3D::ReleaseMaterial(::RuntimeMaterial* material) {
        if (material == nullptr) {
            throw std::invalid_argument("PS2 runtime material release requires one material.");
        }

        if (std::find(PendingReleasedMaterials.begin(), PendingReleasedMaterials.end(), material) != PendingReleasedMaterials.end()) {
            return;
        }

        PendingReleasedMaterials.push_back(material);
    }

    void Ps2RenderManager3D::ReleaseModel(::RuntimeModel* model) {
        if (model == nullptr) {
            throw std::invalid_argument("PS2 runtime model release requires one model.");
        }

        if (std::find(PendingReleasedModels.begin(), PendingReleasedModels.end(), model) != PendingReleasedModels.end()) {
            return;
        }

        PendingReleasedModels.push_back(model);
    }

    void Ps2RenderManager3D::Draw() {
        LastProxyCount = 0;
        LastOpaqueWorldCount = 0;
        LastOpaqueDynamicCount = 0;
        LastAlphaWorldCount = 0;
        LastAlphaDynamicCount = 0;
        LastClipRejectCount = 0;
        LastProjectionRejectCount = 0;
        LastCullRejectCount = 0;
        LastSubmittedTriangleCount = 0;
        LastVuBatchDispatchCount = 0;
        LastVuTriangleVertexCount = 0;
        LastVuPacketByteCount = 0;
        LastVuRejectedMissingMaterialCount = 0;
        LastVuRejectedMissingModelCount = 0;
        LastVuRejectedMissingPackedModelCount = 0;
        LastVuPacketPhase = 0;
        LastProxySyncMilliseconds = 0.0;
        LastFramePlanMilliseconds = 0.0;
        LastVuBatchBuildMilliseconds = 0.0;
        LastVuPacketEncodeMilliseconds = 0.0;
        LastVuTriangleSetupMilliseconds = 0.0;
        LastVuPacketAssemblyMilliseconds = 0.0;
        LastVuTrianglePrepMilliseconds = 0.0;
        LastVuTriangleEmitMilliseconds = 0.0;
        LastVuTriangleLightingMilliseconds = 0.0;
        LastVuTrianglePayloadFillMilliseconds = 0.0;
        LastResolvedViewport = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        LastSubmittedScreenBounds = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        LastSubmittedTriangleBoundsA = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        LastSubmittedTriangleBoundsB = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        LastSubmittedTriangleVertexA0 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        LastSubmittedTriangleVertexA1 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        LastSubmittedTriangleVertexA2 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        LastSubmittedTriangleVertexB0 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        LastSubmittedTriangleVertexB1 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        LastSubmittedTriangleVertexB2 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        PublishPerformanceOverlayMetrics();

        if (GsGlobal == nullptr) {
            return;
        }

        ::CameraComponent* camera = nullptr;
        try {
            camera = GetActiveCamera();
        } catch (const std::exception& exception) {
            scr_printf("[helengine-ps2] draw3d stage=GetActiveCamera exception=%s\n", exception.what());
            std::printf("[helengine-ps2] draw3d stage=GetActiveCamera exception=%s\n", exception.what());
            std::fflush(stdout);
            throw;
        }
        if (camera == nullptr || camera->get_Parent() == nullptr) {
            return;
        }

        const int2 windowSize = get_MainWindowSize();
        if (windowSize.X <= 0 || windowSize.Y <= 0) {
            return;
        }

        ::float4 viewport = ResolvePixelViewport(camera, windowSize);
        LastResolvedViewport = viewport;
        if (viewport.Z <= 0.0f || viewport.W <= 0.0f) {
            return;
        }

        ::float3 cameraPosition;
        ::float4 cameraOrientation;
        try {
            cameraPosition = camera->get_Parent()->get_Position();
            cameraOrientation = camera->get_Parent()->get_Orientation();
        } catch (const std::exception& exception) {
            scr_printf("[helengine-ps2] draw3d stage=ResolveCameraTransform exception=%s\n", exception.what());
            std::printf("[helengine-ps2] draw3d stage=ResolveCameraTransform exception=%s\n", exception.what());
            std::fflush(stdout);
            throw;
        }
        ::float3 cameraForward = ::float4::RotateVector(DefaultForward, cameraOrientation);
        ::float3 cameraUp = ::float4::RotateVector(DefaultUp, cameraOrientation);
        ::float3 cameraTarget = cameraPosition + cameraForward;
        ::float4x4 view;
        ::float4x4::CreateLookAt__ref0_ref1_ref2_out3(cameraPosition, cameraTarget, cameraUp, view);

        ::float4x4 projection;
        ::float4x4::CreatePerspectiveFieldOfView__out4(
            CameraFieldOfViewRadians,
            viewport.Z / viewport.W,
            camera->get_NearPlaneDistance(),
            camera->get_FarPlaneDistance(),
            projection);

        DeferredHdrGlowTriangles.clear();
        ApplyDepthState(GsGlobal->ZBuffering == GS_SETTING_ON);
        const std::clock_t proxySyncStartTicks = std::clock();
        try {
            RebuildProxies();
        } catch (const std::exception& exception) {
            scr_printf("[helengine-ps2] draw3d stage=RebuildProxies exception=%s\n", exception.what());
            std::printf("[helengine-ps2] draw3d stage=RebuildProxies exception=%s\n", exception.what());
            std::fflush(stdout);
            throw;
        }
        const std::clock_t proxySyncEndTicks = std::clock();
        LastProxySyncMilliseconds = ResolveMillisecondsFromClockTicks(proxySyncStartTicks, proxySyncEndTicks);
        LastProxyCount = Proxies.size();
        const std::clock_t framePlanStartTicks = std::clock();
        Ps2FramePlan plan = FramePlanner.Build(Proxies);
        const std::clock_t framePlanEndTicks = std::clock();
        LastFramePlanMilliseconds = ResolveMillisecondsFromClockTicks(framePlanStartTicks, framePlanEndTicks);
        LastOpaqueWorldCount = plan.OpaqueWorld.size();
        LastOpaqueDynamicCount = plan.OpaqueDynamic.size();
        LastAlphaWorldCount = plan.AlphaWorld.size();
        LastAlphaDynamicCount = plan.AlphaDynamic.size();

        if (GsGlobal->ZBuffering == GS_SETTING_ON) {
            SortAlphaProxies(plan.AlphaWorld, cameraPosition, cameraForward);
            SortAlphaProxies(plan.AlphaDynamic, cameraPosition, cameraForward);

            if (EnableSingleProxyDiagnostics) {
                const Ps2RenderProxy* firstProxy = ResolveRenderableProxyByIndex(plan, SingleProxyDiagnosticIndex);
                if (firstProxy != nullptr) {
                    DrawOpaqueProxyLegacy(*firstProxy, view, projection, viewport, camera->get_NearPlaneDistance());
                }

                DrawHdrGlowPass(GsGlobal);
                PublishPerformanceOverlayMetrics();
                return;
            }

            if (UseLegacyCpuOpaquePath) {
                for (const Ps2RenderProxy* proxy : plan.OpaqueWorld) {
                    if (proxy != nullptr) {
                        DrawOpaqueProxyLegacy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
                    }
                }

                for (const Ps2RenderProxy* proxy : plan.OpaqueDynamic) {
                    if (proxy != nullptr) {
                        DrawOpaqueProxyLegacy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
                    }
                }
            } else {
                RenderOpaqueWithVuPath(plan, view, projection, viewport, camera->get_NearPlaneDistance());
                PublishPerformanceOverlayMetrics();
            }

            for (const Ps2RenderProxy* proxy : plan.AlphaWorld) {
                if (proxy != nullptr) {
                    DrawAlphaProxy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
                }
            }

            for (const Ps2RenderProxy* proxy : plan.AlphaDynamic) {
                if (proxy != nullptr) {
                    DrawAlphaProxy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
                }
            }

            DrawHdrGlowPass(GsGlobal);
            PublishPerformanceOverlayMetrics();
            return;
        }

        DrawSoftwareDepthPass(
            plan,
            view,
            projection,
            viewport,
            camera->get_NearPlaneDistance(),
            cameraPosition,
            cameraForward);
        DrawHdrGlowPass(GsGlobal);
        PublishPerformanceOverlayMetrics();
    }

    void Ps2RenderManager3D::PublishPerformanceOverlayMetrics() const {
        if (::Core::get_Instance() == nullptr) {
            return;
        }

        ::Core::get_Instance()->SetPerformanceOverlayMetrics(
            true,
            LastVuTriangleSetupMilliseconds,
            LastVuTrianglePrepMilliseconds,
            LastVuTriangleEmitMilliseconds,
            LastVuPacketAssemblyMilliseconds,
            LastVuTriangleLightingMilliseconds,
            LastVuTrianglePayloadFillMilliseconds,
            static_cast<int>(LastSubmittedTriangleCount),
            static_cast<int>(LastVuBatchDispatchCount));
    }

    void Ps2RenderManager3D::RenderOpaqueWithVuPath(const Ps2FramePlan& plan, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance) {
        const std::clock_t vuBatchBuildStartTicks = std::clock();
        std::vector<Ps2VuOpaqueBatch> batches = VuOpaqueBatchBuilder.Build(plan);
        const std::clock_t vuBatchBuildEndTicks = std::clock();
        LastVuBatchBuildMilliseconds = ResolveMillisecondsFromClockTicks(vuBatchBuildStartTicks, vuBatchBuildEndTicks);
        LastVuWaitMilliseconds = 0.0;
        LastVuSubmitMilliseconds = 0.0;
        ::float3 lightDirection = DefaultForward;
        TryResolveDirectionalLightDirection(lightDirection);
        LastVuBatchDispatchCount = batches.size();
        LastVuRejectedMissingMaterialCount = VuOpaqueBatchBuilder.GetLastRejectedMissingMaterialCount();
        LastVuRejectedMissingModelCount = VuOpaqueBatchBuilder.GetLastRejectedMissingModelCount();
        LastVuRejectedMissingPackedModelCount = VuOpaqueBatchBuilder.GetLastRejectedMissingPackedModelCount();
        for (const Ps2VuOpaqueBatch& batch : batches) {
            if (batch.Proxy == nullptr) {
                continue;
            }

            const std::clock_t vuInitialWaitStartTicks = std::clock();
            dma_channel_wait(DMA_CHANNEL_VIF1, 0);
            const std::clock_t vuInitialWaitEndTicks = std::clock();
            LastVuWaitMilliseconds += ResolveMillisecondsFromClockTicks(vuInitialWaitStartTicks, vuInitialWaitEndTicks);
            ::float4x4 world = BuildWorldMatrix(*batch.Proxy);
            GSTEXTURE* batchTexture = nullptr;
            int batchTextureWidth = 0;
            int batchTextureHeight = 0;
            if (batch.Material != nullptr && batch.Material->HasTextureRelativePath()) {
                batchTexture = ResolveTexture(GsGlobal, batch.Material->GetTextureRelativePath());
                if (batchTexture != nullptr) {
                    batchTextureWidth = static_cast<int>(batchTexture->Width);
                    batchTextureHeight = static_cast<int>(batchTexture->Height);
                }
            }

            VuGifStateEncoder.EncodeOpaqueState(batch, GsGlobal);
            VuVifPacketBuilder.Reset();
            const std::clock_t vuPacketEncodeStartTicks = std::clock();
            VuVifPacketBuilder.AddOpaqueBatch(
                batch,
                world,
                view,
                projection,
                viewport,
                nearPlaneDistance,
                lightDirection,
                GsGlobal,
                batchTexture,
                batchTextureWidth,
                batchTextureHeight);
            packet2_t* packet = VuVifPacketBuilder.GetPacket();
            const std::clock_t vuPacketEncodeEndTicks = std::clock();
            LastVuPacketEncodeMilliseconds += ResolveMillisecondsFromClockTicks(vuPacketEncodeStartTicks, vuPacketEncodeEndTicks);
            LastVuTriangleSetupMilliseconds += VuVifPacketBuilder.GetLastTriangleSetupMilliseconds();
            LastVuPacketAssemblyMilliseconds += VuVifPacketBuilder.GetLastPacketAssemblyMilliseconds();
            LastVuTrianglePrepMilliseconds += VuVifPacketBuilder.GetLastTrianglePrepMilliseconds();
            LastVuTriangleEmitMilliseconds += VuVifPacketBuilder.GetLastTriangleEmitMilliseconds();
            LastVuTriangleLightingMilliseconds += VuVifPacketBuilder.GetLastTriangleLightingMilliseconds();
            LastVuTrianglePayloadFillMilliseconds += VuVifPacketBuilder.GetLastTrianglePayloadFillMilliseconds();
            if (packet == nullptr) {
                continue;
            }

            LastVuTriangleVertexCount += static_cast<std::size_t>(batch.Model->GetTriangleVertexCount());
            LastVuPacketByteCount += VuVifPacketBuilder.GetPacketByteCount();
            LastVuPacketPhase = VuVifPacketBuilder.GetLastCompletedPhase();
            LastSubmittedTriangleCount += VuVifPacketBuilder.GetSubmittedTriangleCount();
            if (VuVifPacketBuilder.GetSubmittedTriangleCount() > 0u) {
                if (LastSubmittedTriangleCount == VuVifPacketBuilder.GetSubmittedTriangleCount()) {
                    LastSubmittedScreenBounds = VuVifPacketBuilder.GetSubmittedScreenBounds();
                    LastSubmittedTriangleBoundsA = VuVifPacketBuilder.GetSubmittedTriangleBoundsA();
                    LastSubmittedTriangleVertexA0 = VuVifPacketBuilder.GetSubmittedTriangleVertexA0();
                    LastSubmittedTriangleVertexA1 = VuVifPacketBuilder.GetSubmittedTriangleVertexA1();
                    LastSubmittedTriangleVertexA2 = VuVifPacketBuilder.GetSubmittedTriangleVertexA2();
                    LastSubmittedTriangleBoundsB = VuVifPacketBuilder.GetSubmittedTriangleBoundsB();
                    LastSubmittedTriangleVertexB0 = VuVifPacketBuilder.GetSubmittedTriangleVertexB0();
                    LastSubmittedTriangleVertexB1 = VuVifPacketBuilder.GetSubmittedTriangleVertexB1();
                    LastSubmittedTriangleVertexB2 = VuVifPacketBuilder.GetSubmittedTriangleVertexB2();
                } else {
                    const ::float4 batchBounds = VuVifPacketBuilder.GetSubmittedScreenBounds();
                    LastSubmittedScreenBounds.X = std::min(LastSubmittedScreenBounds.X, batchBounds.X);
                    LastSubmittedScreenBounds.Y = std::min(LastSubmittedScreenBounds.Y, batchBounds.Y);
                    LastSubmittedScreenBounds.Z = std::max(LastSubmittedScreenBounds.Z, batchBounds.Z);
                    LastSubmittedScreenBounds.W = std::max(LastSubmittedScreenBounds.W, batchBounds.W);
                }
            }
            const bool useDirectGifDispatchDiagnostics = EnableVuDirectGifDispatchDiagnostics;
            if (EnableVuDispatchBypassDiagnostics) {
                LastVuPacketPhase = 100;
            } else if (useDirectGifDispatchDiagnostics) {
                packet2_t* gifPacket = nullptr;
                std::size_t gifPacketByteCount = 0;
                if (EnableVuDirectGifHelperTriangleDiagnostics) {
                    gifPacket = packet2_create(32, P2_TYPE_NORMAL, P2_MODE_NORMAL, false);
                    if (gifPacket != nullptr) {
                        prim_t prim = {};
                        prim.type = PRIM_TRIANGLE;
                        prim.shading = PRIM_SHADE_FLAT;
                        prim.mapping = 0;
                        prim.fogging = 0;
                        prim.blending = 0;
                        prim.antialiasing = 0;
                        prim.mapping_type = PRIM_MAP_ST;
                        prim.colorfix = PRIM_UNFIXED;
                        color_t diagnosticColor = {};
                        diagnosticColor.r = 0xD0;
                        diagnosticColor.g = 0x40;
                        diagnosticColor.b = 0x40;
                        diagnosticColor.a = 0x80;
                        packet2_update(gifPacket, draw_prim_start(gifPacket->base, 0, &prim, &diagnosticColor));
                        packet2_add_u64(gifPacket, BuildVuDirectGifDiagnosticPosition(VuDirectGifDiagnosticTriangleAX, VuDirectGifDiagnosticTriangleAY));
                        packet2_add_u64(gifPacket, BuildVuDirectGifDiagnosticPosition(VuDirectGifDiagnosticTriangleBX, VuDirectGifDiagnosticTriangleBY));
                        packet2_add_u64(gifPacket, BuildVuDirectGifDiagnosticPosition(VuDirectGifDiagnosticTriangleCX, VuDirectGifDiagnosticTriangleCY));
                        packet2_pad128(gifPacket, 0);
                        packet2_update(gifPacket, draw_prim_end(gifPacket->next, 1, static_cast<u64>(GIF_REG_XYZ2) << 0));
                        packet2_update(gifPacket, draw_finish(gifPacket->next));
                        gifPacketByteCount = static_cast<std::size_t>(packet2_get_qw_count(gifPacket)) * 16u;
                    }
                } else {
                    const std::vector<std::uint8_t>& gifPacketBytes = VuVifPacketBuilder.GetGifPacketBytes();
                    if (!gifPacketBytes.empty()) {
                        gifPacket = packet2_create(static_cast<std::uint16_t>(gifPacketBytes.size() / 16u), P2_TYPE_NORMAL, P2_MODE_NORMAL, 0);
                        if (gifPacket != nullptr) {
                            std::memcpy(gifPacket->base, gifPacketBytes.data(), gifPacketBytes.size());
                            packet2_advance_next(gifPacket, gifPacketBytes.size());
                            gifPacketByteCount = gifPacketBytes.size();
                        }
                    }
                }

                if (gifPacket != nullptr) {
                    LastVuPacketPhase = 101;
                    dma_channel_wait(DMA_CHANNEL_GIF, 0);
                    dma_channel_send_packet2(gifPacket, DMA_CHANNEL_GIF, true);
                    dma_channel_wait(DMA_CHANNEL_GIF, 0);
                    LastVuPacketPhase = 102;
                    packet2_free(gifPacket);
                }
            } else {
                LastVuPacketPhase = 201;
                const std::clock_t vuSubmitStartTicks = std::clock();
                dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);
                LastVuPacketPhase = 202;
                const std::clock_t vuSubmitEndTicks = std::clock();
                LastVuSubmitMilliseconds += ResolveMillisecondsFromClockTicks(vuSubmitStartTicks, vuSubmitEndTicks);
                LastVuPacketPhase = 203;
                if (batch.Textured) {
                    static std::uint32_t texturedSubmitDiagnosticCount = 0u;
                    if (texturedSubmitDiagnosticCount < TexturedVuSubmitDiagnosticLogLimit) {
                        texturedSubmitDiagnosticCount++;
                        AppendDiagnosticToHostBootLog(
                            std::string("[helengine-ps2] textured-vu submit")
                            + " texture=" + (batch.Material != nullptr && batch.Material->HasTextureRelativePath() ? batch.Material->GetTextureRelativePath() : std::string("none"))
                            + " submitted=" + std::to_string(VuVifPacketBuilder.GetSubmittedTriangleCount())
                            + " vifBytes=" + std::to_string(VuVifPacketBuilder.GetPacketByteCount())
                            + " phase=" + std::to_string(VuVifPacketBuilder.GetLastCompletedPhase())
                            + " waitMs=" + std::to_string(LastVuWaitMilliseconds)
                            + " submitMs=" + std::to_string(LastVuSubmitMilliseconds));
                    }
                }
            }
            (void)viewport;
            (void)nearPlaneDistance;
        }
    }

    ::float4x4 Ps2RenderManager3D::BuildWorldMatrix(const Ps2RenderProxy& proxy) const {
        ::IDrawable3D* drawable = proxy.GetDrawable();
        if (drawable == nullptr) {
            return ::float4x4::get_Identity();
        }

        ::Entity* parent = drawable->get_Parent();
        if (!IsLiveEntity(parent)) {
            return ::float4x4::get_Identity();
        }

        ::float3 parentScale = parent->get_Scale();
        ::float4 parentOrientation = parent->get_Orientation();
        ::float3 parentPosition = parent->get_Position();
        ::float4x4 scaleMatrix;
        ::float4x4 scaleRotationMatrix;
        ::float4x4 rotationMatrix;
        ::float4x4 translationMatrix;
        ::float4x4 worldMatrix;
        ::float4x4::CreateScale__out3(parentScale.X, parentScale.Y, parentScale.Z, scaleMatrix);
        ::float4x4::CreateFromQuaternion__ref0_out1(parentOrientation, rotationMatrix);
        ::float4x4::CreateTranslation__ref0_out1(parentPosition, translationMatrix);
        ::float4x4::Multiply__ref0_ref1_out2(scaleMatrix, rotationMatrix, scaleRotationMatrix);
        ::float4x4::Multiply__ref0_ref1_out2(scaleRotationMatrix, translationMatrix, worldMatrix);
        return worldMatrix;
    }

    void Ps2RenderManager3D::SetGsGlobal(GSGLOBAL* gsGlobal) {
        GsGlobal = gsGlobal;
    }

    std::size_t Ps2RenderManager3D::GetLastProxyCount() const {
        return LastProxyCount;
    }

    std::size_t Ps2RenderManager3D::GetLastOpaqueWorldCount() const {
        return LastOpaqueWorldCount;
    }

    std::size_t Ps2RenderManager3D::GetLastOpaqueDynamicCount() const {
        return LastOpaqueDynamicCount;
    }

    std::size_t Ps2RenderManager3D::GetLastAlphaWorldCount() const {
        return LastAlphaWorldCount;
    }

    std::size_t Ps2RenderManager3D::GetLastAlphaDynamicCount() const {
        return LastAlphaDynamicCount;
    }

    std::size_t Ps2RenderManager3D::GetLastClipRejectCount() const {
        return LastClipRejectCount;
    }

    std::size_t Ps2RenderManager3D::GetLastProjectionRejectCount() const {
        return LastProjectionRejectCount;
    }

    std::size_t Ps2RenderManager3D::GetLastCullRejectCount() const {
        return LastCullRejectCount;
    }

    std::size_t Ps2RenderManager3D::GetLastSubmittedTriangleCount() const {
        return LastSubmittedTriangleCount;
    }

    std::size_t Ps2RenderManager3D::GetLastVuBatchDispatchCount() const {
        return LastVuBatchDispatchCount;
    }

    std::size_t Ps2RenderManager3D::GetLastVuTriangleVertexCount() const {
        return LastVuTriangleVertexCount;
    }

    std::size_t Ps2RenderManager3D::GetLastVuPacketByteCount() const {
        return LastVuPacketByteCount;
    }

    std::size_t Ps2RenderManager3D::GetLastVuRejectedMissingMaterialCount() const {
        return LastVuRejectedMissingMaterialCount;
    }

    std::size_t Ps2RenderManager3D::GetLastVuRejectedMissingModelCount() const {
        return LastVuRejectedMissingModelCount;
    }

    std::size_t Ps2RenderManager3D::GetLastVuRejectedMissingPackedModelCount() const {
        return LastVuRejectedMissingPackedModelCount;
    }

    std::uint32_t Ps2RenderManager3D::GetLastVuPacketPhase() const {
        return LastVuPacketPhase;
    }

    double Ps2RenderManager3D::GetLastProxySyncMilliseconds() const {
        return LastProxySyncMilliseconds;
    }

    double Ps2RenderManager3D::GetLastFramePlanMilliseconds() const {
        return LastFramePlanMilliseconds;
    }

    double Ps2RenderManager3D::GetLastVuBatchBuildMilliseconds() const {
        return LastVuBatchBuildMilliseconds;
    }

    double Ps2RenderManager3D::GetLastVuWaitMilliseconds() const {
        return LastVuWaitMilliseconds;
    }

    double Ps2RenderManager3D::GetLastVuSubmitMilliseconds() const {
        return LastVuSubmitMilliseconds;
    }

    double Ps2RenderManager3D::GetLastVuPacketEncodeMilliseconds() const {
        return LastVuPacketEncodeMilliseconds;
    }

    double Ps2RenderManager3D::GetLastVuTriangleSetupMilliseconds() const {
        return LastVuTriangleSetupMilliseconds;
    }

    double Ps2RenderManager3D::GetLastVuPacketAssemblyMilliseconds() const {
        return LastVuPacketAssemblyMilliseconds;
    }

    double Ps2RenderManager3D::GetLastVuTrianglePrepMilliseconds() const {
        return LastVuTrianglePrepMilliseconds;
    }

    double Ps2RenderManager3D::GetLastVuTriangleEmitMilliseconds() const {
        return LastVuTriangleEmitMilliseconds;
    }

    bool Ps2RenderManager3D::IsUsingLegacyCpuOpaquePath() const {
        return UseLegacyCpuOpaquePath;
    }

    ::float4 Ps2RenderManager3D::GetLastResolvedViewport() const {
        return LastResolvedViewport;
    }

    ::float4 Ps2RenderManager3D::GetLastSubmittedScreenBounds() const {
        return LastSubmittedScreenBounds;
    }

    ::float4 Ps2RenderManager3D::GetLastSubmittedTriangleBoundsA() const {
        return LastSubmittedTriangleBoundsA;
    }

    ::float4 Ps2RenderManager3D::GetLastSubmittedTriangleBoundsB() const {
        return LastSubmittedTriangleBoundsB;
    }

    ::float4 Ps2RenderManager3D::GetLastSubmittedTriangleVertexA0() const {
        return LastSubmittedTriangleVertexA0;
    }

    ::float4 Ps2RenderManager3D::GetLastSubmittedTriangleVertexA1() const {
        return LastSubmittedTriangleVertexA1;
    }

    ::float4 Ps2RenderManager3D::GetLastSubmittedTriangleVertexA2() const {
        return LastSubmittedTriangleVertexA2;
    }

    ::float4 Ps2RenderManager3D::GetLastSubmittedTriangleVertexB0() const {
        return LastSubmittedTriangleVertexB0;
    }

    ::float4 Ps2RenderManager3D::GetLastSubmittedTriangleVertexB1() const {
        return LastSubmittedTriangleVertexB1;
    }

    ::float4 Ps2RenderManager3D::GetLastSubmittedTriangleVertexB2() const {
        return LastSubmittedTriangleVertexB2;
    }

    void Ps2RenderManager3D::DrawOpaqueProxy(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance) {
        const Ps2RuntimeModel* model = proxy.GetModel();
        const Ps2RuntimeMaterial* material = proxy.GetMaterial();
        ::IDrawable3D* drawable = proxy.GetDrawable();
        if (model == nullptr || material == nullptr || drawable == nullptr) {
            return;
        }

        const std::vector<::float3>& positions = model->GetPositions();
        const std::vector<::float3>& normals = model->GetNormals();
        const std::vector<::float2>& texCoords = model->GetTexCoords();
        const std::vector<std::uint16_t>& indices = model->GetIndices();
        if (positions.empty() || indices.size() < 3) {
            return;
        }

        ::float3 parentPosition = ::float3::get_Zero();
        ::Entity* parent = drawable->get_Parent();
        if (parent != nullptr && parent->get_IsDisposed()) {
            return;
        }

        const bool useDiagnosticFlatColor = EnableFlatColorDiagnostics;
        const bool useLightingOnlyDiagnostics = EnableLightingOnlyDiagnostics;
        if (!useDiagnosticFlatColor) {
            ApplyMaterialAlphaState(*material);
        } else {
            gsKit_set_test(GsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(GsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            GsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        }

        const bool doubleSided = material->GetDoubleSided();
        ::float3 lightDirection = ::float3(0.0f, -0.70710678f, -0.70710678f);
        TryResolveDirectionalLightDirection(lightDirection);

        GSTEXTURE* texture = nullptr;
        if (!useDiagnosticFlatColor && !useLightingOnlyDiagnostics && material->HasTextureRelativePath()) {
            texture = ResolveTexture(GsGlobal, material->GetTextureRelativePath());
        }

        for (std::size_t index = 0; index + 2 < indices.size(); index += 3) {
            std::uint16_t indexA = indices[index + 0];
            std::uint16_t indexB = indices[index + 1];
            std::uint16_t indexC = indices[index + 2];
            if (indexA >= positions.size() || indexB >= positions.size() || indexC >= positions.size()) {
                continue;
            }

            ::float3 positionA = positions[indexA];
            ::float3 positionB = positions[indexB];
            ::float3 positionC = positions[indexC];
            ::float3 normalA = indexA < normals.size() ? normals[indexA] : ::float3::get_Zero();
            ::float3 normalB = indexB < normals.size() ? normals[indexB] : ::float3::get_Zero();
            ::float3 normalC = indexC < normals.size() ? normals[indexC] : ::float3::get_Zero();

            if (parent != nullptr) {
                parentPosition = parent->get_Position();
                ::float4 parentOrientation = parent->get_Orientation();
                ::float3 parentScale = parent->get_Scale();
                ::float3 localPositionA = ::float3(
                    positions[indexA].X * parentScale.X,
                    positions[indexA].Y * parentScale.Y,
                    positions[indexA].Z * parentScale.Z);
                ::float3 localPositionB = ::float3(
                    positions[indexB].X * parentScale.X,
                    positions[indexB].Y * parentScale.Y,
                    positions[indexB].Z * parentScale.Z);
                ::float3 localPositionC = ::float3(
                    positions[indexC].X * parentScale.X,
                    positions[indexC].Y * parentScale.Y,
                    positions[indexC].Z * parentScale.Z);
                positionA = ::float4::RotateVector(localPositionA, parentOrientation) + parentPosition;
                positionB = ::float4::RotateVector(localPositionB, parentOrientation) + parentPosition;
                positionC = ::float4::RotateVector(localPositionC, parentOrientation) + parentPosition;
                normalA = indexA < normals.size() ? ::float4::RotateVector(normals[indexA], parentOrientation) : ::float3::get_Zero();
                normalB = indexB < normals.size() ? ::float4::RotateVector(normals[indexB], parentOrientation) : ::float3::get_Zero();
                normalC = indexC < normals.size() ? ::float4::RotateVector(normals[indexC], parentOrientation) : ::float3::get_Zero();
            }

            ::float3 viewPositionA = TransformPosition(positionA, view);
            ::float3 viewPositionB = TransformPosition(positionB, view);
            ::float3 viewPositionC = TransformPosition(positionC, view);

            const std::uint64_t diagnosticColor = ResolveDiagnosticProxyColor(proxy);
            const std::uint64_t colorA = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalA, lightDirection);
            const std::uint64_t colorB = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalB, lightDirection);
            const std::uint64_t colorC = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalC, lightDirection);

            Ps2ClipVertex vertexA = CreateClipVertex(viewPositionA, texCoords.size() > indexA ? texCoords[indexA] : ::float2(0.0f, 0.0f), colorA);
            Ps2ClipVertex vertexB = CreateClipVertex(viewPositionB, texCoords.size() > indexB ? texCoords[indexB] : ::float2(0.0f, 0.0f), colorB);
            Ps2ClipVertex vertexC = CreateClipVertex(viewPositionC, texCoords.size() > indexC ? texCoords[indexC] : ::float2(0.0f, 0.0f), colorC);

            std::vector<Ps2ClipVertex> clippedVertices;
            ClipTriangleAgainstNearPlane(vertexA, vertexB, vertexC, nearPlaneDistance, clippedVertices);
            if (clippedVertices.size() < 3) {
                LastClipRejectCount++;
                continue;
            }

            const bool useTexture = !useDiagnosticFlatColor
                && !useLightingOnlyDiagnostics
                && texture != nullptr
                && indexA < texCoords.size()
                && indexB < texCoords.size()
                && indexC < texCoords.size();

            for (std::size_t clippedIndex = 1; clippedIndex + 1 < clippedVertices.size(); clippedIndex++) {
                const Ps2ClipVertex& clippedA = clippedVertices[0];
                const Ps2ClipVertex& clippedB = clippedVertices[clippedIndex];
                const Ps2ClipVertex& clippedC = clippedVertices[clippedIndex + 1];

                float screenAX;
                float screenAY;
                float screenAZ;
                float screenBX;
                float screenBY;
                float screenBZ;
                float screenCX;
                float screenCY;
                float screenCZ;
                if (!ProjectWorldPosition(clippedA.ViewPosition, projection, viewport, screenAX, screenAY, screenAZ)
                    || !ProjectWorldPosition(clippedB.ViewPosition, projection, viewport, screenBX, screenBY, screenBZ)
                    || !ProjectWorldPosition(clippedC.ViewPosition, projection, viewport, screenCX, screenCY, screenCZ)) {
                    LastProjectionRejectCount++;
                    continue;
                }

                if (!doubleSided
                    && !IsFrontFacingTriangle(screenAX, screenAY, screenBX, screenBY, screenCX, screenCY)) {
                    LastCullRejectCount++;
                    continue;
                }

                if (!useDiagnosticFlatColor && !ShouldDrawAlphaTestTriangle(
                        *material,
                        texture,
                        clippedA.TexCoord,
                        clippedB.TexCoord,
                        clippedC.TexCoord,
                        clippedA.Alpha,
                        clippedB.Alpha,
                        clippedC.Alpha)) {
                    continue;
                }

                const std::uint64_t clippedColorA = PackColor(clippedA.Red, clippedA.Green, clippedA.Blue, clippedA.Alpha);
                const std::uint64_t clippedColorB = PackColor(clippedB.Red, clippedB.Green, clippedB.Blue, clippedB.Alpha);
                const std::uint64_t clippedColorC = PackColor(clippedC.Red, clippedC.Green, clippedC.Blue, clippedC.Alpha);
                const float minX = std::min({ screenAX, screenBX, screenCX });
                const float minY = std::min({ screenAY, screenBY, screenCY });
                const float maxX = std::max({ screenAX, screenBX, screenCX });
                const float maxY = std::max({ screenAY, screenBY, screenCY });
                if (LastSubmittedTriangleCount == 0) {
                    LastSubmittedScreenBounds = ::float4(minX, minY, maxX, maxY);
                    LastSubmittedTriangleBoundsA = ::float4(minX, minY, maxX, maxY);
                    LastSubmittedTriangleVertexA0 = ::float4(screenAX, screenAY, screenAZ, 0.0f);
                    LastSubmittedTriangleVertexA1 = ::float4(screenBX, screenBY, screenBZ, 0.0f);
                    LastSubmittedTriangleVertexA2 = ::float4(screenCX, screenCY, screenCZ, 0.0f);
                } else if (LastSubmittedTriangleCount == 1) {
                    LastSubmittedTriangleBoundsB = ::float4(minX, minY, maxX, maxY);
                    LastSubmittedTriangleVertexB0 = ::float4(screenAX, screenAY, screenAZ, 0.0f);
                    LastSubmittedTriangleVertexB1 = ::float4(screenBX, screenBY, screenBZ, 0.0f);
                    LastSubmittedTriangleVertexB2 = ::float4(screenCX, screenCY, screenCZ, 0.0f);
                } else {
                    LastSubmittedScreenBounds.X = std::min(LastSubmittedScreenBounds.X, minX);
                    LastSubmittedScreenBounds.Y = std::min(LastSubmittedScreenBounds.Y, minY);
                    LastSubmittedScreenBounds.Z = std::max(LastSubmittedScreenBounds.Z, maxX);
                    LastSubmittedScreenBounds.W = std::max(LastSubmittedScreenBounds.W, maxY);
                }
                LastSubmittedTriangleCount++;

                if (useTexture) {
                    const ::float2 screenTexCoordA = ResolveGsTextureCoordinate(clippedA.TexCoord, texture);
                    const ::float2 screenTexCoordB = ResolveGsTextureCoordinate(clippedB.TexCoord, texture);
                    const ::float2 screenTexCoordC = ResolveGsTextureCoordinate(clippedC.TexCoord, texture);
                    gsKit_prim_triangle_goraud_texture_3d(
                        GsGlobal,
                        texture,
                        screenAX, screenAY, screenAZ, screenTexCoordA.X, screenTexCoordA.Y,
                        screenBX, screenBY, screenBZ, screenTexCoordB.X, screenTexCoordB.Y,
                        screenCX, screenCY, screenCZ, screenTexCoordC.X, screenTexCoordC.Y,
                        clippedColorA, clippedColorB, clippedColorC);
                } else {
                    gsKit_prim_triangle_gouraud_3d(
                        GsGlobal,
                        screenAX, screenAY, screenAZ,
                        screenBX, screenBY, screenBZ,
                        screenCX, screenCY, screenCZ,
                        clippedColorA, clippedColorB, clippedColorC);
                }

                if (!useDiagnosticFlatColor && !useLightingOnlyDiagnostics && HdrEnabled && ShouldEmitHdrGlow(*material, clippedColorA, clippedColorB, clippedColorC)) {
                    const float glowStrength = ComputeHdrGlowStrength(clippedColorA, clippedColorB, clippedColorC);
                    Ps2HdrGlowTriangle glowTriangle;
                    glowTriangle.ScreenAX = screenAX;
                    glowTriangle.ScreenAY = screenAY;
                    glowTriangle.ScreenAZ = screenAZ;
                    glowTriangle.ScreenBX = screenBX;
                    glowTriangle.ScreenBY = screenBY;
                    glowTriangle.ScreenBZ = screenBZ;
                    glowTriangle.ScreenCX = screenCX;
                    glowTriangle.ScreenCY = screenCY;
                    glowTriangle.ScreenCZ = screenCZ;
                    glowTriangle.TexCoordA = clippedA.TexCoord;
                    glowTriangle.TexCoordB = clippedB.TexCoord;
                    glowTriangle.TexCoordC = clippedC.TexCoord;
                    glowTriangle.ColorA = BoostHdrColor(clippedColorA, glowStrength);
                    glowTriangle.ColorB = BoostHdrColor(clippedColorB, glowStrength);
                    glowTriangle.ColorC = BoostHdrColor(clippedColorC, glowStrength);
                    glowTriangle.GlowStrength = glowStrength;
                    glowTriangle.Texture = texture;
                    glowTriangle.UseTexture = useTexture;
                    DeferredHdrGlowTriangles.push_back(glowTriangle);
                }
            }
        }
    }

    void Ps2RenderManager3D::SetHdrEnabled(bool enabled) {
        HdrEnabled = enabled;
    }

    ::RenderTarget* Ps2RenderManager3D::CreateRenderTarget(int32_t width, int32_t height) {
        std::uintptr_t thisAddress = reinterpret_cast<std::uintptr_t>(this);
        std::uintptr_t vtableAddress = this == nullptr ? 0u : reinterpret_cast<std::uintptr_t>(*reinterpret_cast<void**>(this));
        std::string createTargetLog =
            "CreateRenderTarget this=0x"
            + std::to_string(static_cast<unsigned long>(thisAddress))
            + " vtable=0x"
            + std::to_string(static_cast<unsigned long>(vtableAddress))
            + " width="
            + std::to_string(width)
            + " height="
            + std::to_string(height);
        std::printf("[helengine-ps2] %s\n", createTargetLog.c_str());
        std::fflush(stdout);
        scr_printf("[helengine-ps2] %s\n", createTargetLog.c_str());

        ::RenderTarget* renderTarget = new ::RenderTarget();
        renderTarget->RuntimeTexture::set_Width(width);
        renderTarget->RuntimeTexture::set_Height(height);
        renderTarget->set_CanSampleAsTexture(false);
        renderTarget->set_HasDepthBuffer(false);
        renderTarget->RuntimeData::set_Id(std::string("ps2-placeholder-render-target-") + std::to_string(width) + "x" + std::to_string(height));
        return renderTarget;
    }

    void Ps2RenderManager3D::DrawOpaqueProxyLegacy(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance) {
        DrawOpaqueProxy(proxy, view, projection, viewport, nearPlaneDistance);
    }

    void Ps2RenderManager3D::DrawAlphaProxy(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance) {
        DrawOpaqueProxy(proxy, view, projection, viewport, nearPlaneDistance);
    }

    void Ps2RenderManager3D::DrawSoftwareDepthPass(
        const Ps2FramePlan& plan,
        const ::float4x4& view,
        const ::float4x4& projection,
        const ::float4& viewport,
        float nearPlaneDistance,
        const ::float3& cameraPosition,
        const ::float3& cameraForward) {
        std::vector<const Ps2RenderProxy*> sortedProxies;
        sortedProxies.reserve(plan.OpaqueWorld.size() + plan.OpaqueDynamic.size() + plan.AlphaWorld.size() + plan.AlphaDynamic.size());
        sortedProxies.insert(sortedProxies.end(), plan.OpaqueWorld.begin(), plan.OpaqueWorld.end());
        sortedProxies.insert(sortedProxies.end(), plan.OpaqueDynamic.begin(), plan.OpaqueDynamic.end());
        sortedProxies.insert(sortedProxies.end(), plan.AlphaWorld.begin(), plan.AlphaWorld.end());
        sortedProxies.insert(sortedProxies.end(), plan.AlphaDynamic.begin(), plan.AlphaDynamic.end());

        if (EnableSingleProxyDiagnostics) {
            const Ps2RenderProxy* firstProxy = ResolveRenderableProxyByIndex(plan, SingleProxyDiagnosticIndex);
            if (firstProxy != nullptr) {
                DrawOpaqueProxyLegacy(*firstProxy, view, projection, viewport, nearPlaneDistance);
            }

            return;
        }

        SortAlphaProxies(sortedProxies, cameraPosition, cameraForward);

        for (const Ps2RenderProxy* proxy : sortedProxies) {
            if (proxy == nullptr) {
                continue;
            }

            DrawOpaqueProxyLegacy(*proxy, view, projection, viewport, nearPlaneDistance);
        }
    }

    void Ps2RenderManager3D::ApplyDepthState(bool enabled) {
        if (GsGlobal == nullptr) {
            return;
        }

        if (enabled) {
            gsKit_set_test(GsGlobal, GS_ZTEST_ON);
        } else {
            gsKit_set_test(GsGlobal, GS_ZTEST_OFF);
        }
    }

    void Ps2RenderManager3D::ApplyMaterialAlphaState(const Ps2RuntimeMaterial& material) {
        if (GsGlobal == nullptr) {
            return;
        }

        if (material.GetAlphaMode() == ::Ps2MaterialAlphaMode::Opaque) {
            gsKit_set_test(GsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(GsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            GsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        } else if (material.GetAlphaMode() == ::Ps2MaterialAlphaMode::AlphaTest) {
            gsKit_set_test(GsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(GsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            GsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        } else if (material.GetAlphaMode() == ::Ps2MaterialAlphaMode::AlphaBlend) {
            gsKit_set_test(GsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(GsGlobal, GS_BLEND_BACK2FRONT, 0);
            GsGlobal->PrimAlphaEnable = GS_SETTING_ON;
        } else if (material.GetAlphaMode() == ::Ps2MaterialAlphaMode::Additive) {
            gsKit_set_test(GsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(GsGlobal, GS_SETREG_ALPHA(0, 2, 2, 1, 0x80), 0);
            GsGlobal->PrimAlphaEnable = GS_SETTING_ON;
        }
    }

    bool Ps2RenderManager3D::ShouldDrawAlphaTestTriangle(
        const Ps2RuntimeMaterial& material,
        GSTEXTURE* texture,
        const ::float2& texCoordA,
        const ::float2& texCoordB,
        const ::float2& texCoordC,
        std::uint8_t alphaA,
        std::uint8_t alphaB,
        std::uint8_t alphaC) const {
        if (material.GetAlphaMode() != ::Ps2MaterialAlphaMode::AlphaTest) {
            return true;
        }

        if (texture != nullptr) {
            alphaA = SampleTextureAlpha(texture, texCoordA);
            alphaB = SampleTextureAlpha(texture, texCoordB);
            alphaC = SampleTextureAlpha(texture, texCoordC);
        }

        return alphaA >= AlphaTestCutoff
            || alphaB >= AlphaTestCutoff
            || alphaC >= AlphaTestCutoff;
    }

    bool Ps2RenderManager3D::ShouldEmitHdrGlow(const Ps2RuntimeMaterial& material, std::uint64_t colorA, std::uint64_t colorB, std::uint64_t colorC) const {
        if (!HdrEnabled) {
            return false;
        }

        if (material.GetAlphaMode() != ::Ps2MaterialAlphaMode::Opaque
            && material.GetAlphaMode() != ::Ps2MaterialAlphaMode::Additive) {
            return false;
        }

        return IsGlowColorBright(colorA)
            || IsGlowColorBright(colorB)
            || IsGlowColorBright(colorC);
    }

    bool Ps2RenderManager3D::IsGlowColorBright(std::uint64_t color) const {
        return ResolveColorComponent(color, 0) >= HdrGlowThreshold
            || ResolveColorComponent(color, 8) >= HdrGlowThreshold
            || ResolveColorComponent(color, 16) >= HdrGlowThreshold;
    }

    float Ps2RenderManager3D::ComputeHdrGlowStrength(std::uint64_t colorA, std::uint64_t colorB, std::uint64_t colorC) const {
        return std::max({ ResolveGlowStrengthFromColor(colorA), ResolveGlowStrengthFromColor(colorB), ResolveGlowStrengthFromColor(colorC) });
    }

    std::uint64_t Ps2RenderManager3D::BoostHdrColor(std::uint64_t color, float glowStrength) const {
        const float boostStrength = std::clamp(HdrGlowBoostMinimum + (glowStrength * HdrGlowBoostVariance), 0.0f, 1.0f);
        const auto boostChannel = [boostStrength](std::uint8_t value) {
            const double boosted = static_cast<double>(value) + ((255.0 - static_cast<double>(value)) * static_cast<double>(boostStrength));
            return static_cast<std::uint8_t>(std::clamp(std::lround(boosted), 0l, 255l));
        };

        return GS_SETREG_RGBAQ(
            boostChannel(ResolveColorComponent(color, 0)),
            boostChannel(ResolveColorComponent(color, 8)),
            boostChannel(ResolveColorComponent(color, 16)),
            ResolveColorComponent(color, 24),
            0x00);
    }

    void Ps2RenderManager3D::SortAlphaProxies(std::vector<const Ps2RenderProxy*>& proxies, const ::float3& cameraPosition, const ::float3& cameraForward) {
        std::sort(proxies.begin(), proxies.end(), [this, &cameraPosition, &cameraForward](const Ps2RenderProxy* left, const Ps2RenderProxy* right) {
            if (left == nullptr) {
                return false;
            }

            if (right == nullptr) {
                return true;
            }

            return this->ComputeProxyDepth(*left, cameraPosition, cameraForward) > this->ComputeProxyDepth(*right, cameraPosition, cameraForward);
        });
    }

    ::CameraComponent* Ps2RenderManager3D::GetActiveCamera() const {
        ::Core* core = ::Core::get_Instance();
        if (core == nullptr || core->get_ObjectManager() == nullptr || core->get_ObjectManager()->get_Cameras() == nullptr) {
            return nullptr;
        }

        List<::ICamera*>* cameras = core->get_ObjectManager()->get_Cameras();
        for (int32_t index = 0; index < cameras->Count(); index++) {
            ::CameraComponent* camera = he_cpp_try_cast<::CameraComponent>((*cameras)[index]);
            if (camera == nullptr || camera->get_IsDisposed()) {
                continue;
            }

            ::Entity* parent = camera->get_ParentUnsafe();
            if (!IsLiveEntity(parent)) {
                continue;
            }

            return camera;
        }

        return nullptr;
    }

    bool Ps2RenderManager3D::ProjectWorldPosition(
        const ::float3& viewPosition,
        const ::float4x4& projection,
        const ::float4& viewport,
        float& screenX,
        float& screenY,
        float& screenZ) const {
        const float clipX = (viewPosition.X * projection.M11)
            + (viewPosition.Y * projection.M21)
            + (viewPosition.Z * projection.M31)
            + projection.M41;
        const float clipY = (viewPosition.X * projection.M12)
            + (viewPosition.Y * projection.M22)
            + (viewPosition.Z * projection.M32)
            + projection.M42;
        const float clipZ = (viewPosition.X * projection.M13)
            + (viewPosition.Y * projection.M23)
            + (viewPosition.Z * projection.M33)
            + projection.M43;
        const float clipW = (viewPosition.X * projection.M14)
            + (viewPosition.Y * projection.M24)
            + (viewPosition.Z * projection.M34)
            + projection.M44;

        if (clipW <= MinimumClipW) {
            return false;
        }

        const float inverseClipW = 1.0f / clipW;
        const float normalizedX = clipX * inverseClipW;
        const float normalizedY = clipY * inverseClipW;
        const float normalizedZ = clipZ * inverseClipW;

        screenX = viewport.X + ((normalizedX + 1.0f) * 0.5f * viewport.Z);
        screenY = viewport.Y + ((1.0f - normalizedY) * 0.5f * viewport.W);
        screenZ = (normalizedZ + 1.0f) * 0.5f;
        return true;
    }

    bool Ps2RenderManager3D::IsFrontFacingTriangle(
        float screenAX,
        float screenAY,
        float screenBX,
        float screenBY,
        float screenCX,
        float screenCY) const {
        const float edgeABX = screenBX - screenAX;
        const float edgeABY = screenBY - screenAY;
        const float edgeACX = screenCX - screenAX;
        const float edgeACY = screenCY - screenAY;
        const float signedArea = (edgeABX * edgeACY) - (edgeABY * edgeACX);
        return signedArea < 0.0f;
    }

    std::uint8_t Ps2RenderManager3D::SampleTextureAlpha(GSTEXTURE* texture, const ::float2& texCoord) const {
        if (texture == nullptr || texture->Mem == nullptr || texture->Width == 0 || texture->Height == 0) {
            return 0xFF;
        }

        const float clampedU = std::clamp(texCoord.X, 0.0f, 1.0f);
        const float clampedV = std::clamp(texCoord.Y, 0.0f, 1.0f);
        const std::size_t pixelX = static_cast<std::size_t>(clampedU * static_cast<float>(texture->Width - 1));
        const std::size_t pixelY = static_cast<std::size_t>(clampedV * static_cast<float>(texture->Height - 1));
        const std::size_t pixelIndex = (pixelY * static_cast<std::size_t>(texture->Width)) + pixelX;
        const std::uint8_t* colorBytes = reinterpret_cast<const std::uint8_t*>(texture->Mem);
        return colorBytes[(pixelIndex * 4) + 3];
    }

    ::float3 Ps2RenderManager3D::TransformPosition(const ::float3& position, const ::float4x4& matrix) const {
        return ::float3(
            (position.X * matrix.M11) + (position.Y * matrix.M21) + (position.Z * matrix.M31) + matrix.M41,
            (position.X * matrix.M12) + (position.Y * matrix.M22) + (position.Z * matrix.M32) + matrix.M42,
            (position.X * matrix.M13) + (position.Y * matrix.M23) + (position.Z * matrix.M33) + matrix.M43);
    }

    double Ps2RenderManager3D::ComputeProxyDepth(const Ps2RenderProxy& proxy, const ::float3& cameraPosition, const ::float3& cameraForward) const {
        ::IDrawable3D* drawable = proxy.GetDrawable();
        if (drawable == nullptr) {
            return 0.0;
        }

        ::Entity* parent = drawable->get_Parent();
        if (!IsLiveEntity(parent)) {
            return 0.0;
        }

        const ::float3 proxyPosition = parent->get_Position();
        const ::float3 delta = proxyPosition - cameraPosition;
        return static_cast<double>(delta.X) * static_cast<double>(cameraForward.X)
            + static_cast<double>(delta.Y) * static_cast<double>(cameraForward.Y)
            + static_cast<double>(delta.Z) * static_cast<double>(cameraForward.Z);
    }

    void Ps2RenderManager3D::RebuildProxies() {
        Proxies.clear();

        ::Core* core = ::Core::get_Instance();
        if (core == nullptr || core->get_ObjectManager() == nullptr || core->get_ObjectManager()->get_Drawables3D() == nullptr) {
            return;
        }

        List<::IDrawable3D*>* drawables = core->get_ObjectManager()->get_Drawables3D();
        Proxies.reserve(static_cast<std::size_t>(drawables->Count()));
        for (int32_t index = 0; index < drawables->Count(); index++) {
            ::IDrawable3D* drawable = (*drawables)[index];
            Ps2RenderProxy proxy;
            try {
                proxy.Synchronize(drawable);
            } catch (const std::exception& exception) {
                std::printf("[helengine-ps2] draw3d stage=SynchronizeProxy index=%ld exception=%s\n", static_cast<long>(index), exception.what());
                std::fflush(stdout);
                throw;
            }
            if (proxy.GetModel() == nullptr || proxy.GetMaterial() == nullptr) {
                continue;
            }
            Proxies.push_back(proxy);
        }
    }

    bool Ps2RenderManager3D::TryResolveDirectionalLightDirection(::float3& lightDirection) const {
        ::Core* core = ::Core::get_Instance();
        if (core == nullptr || core->get_ObjectManager() == nullptr || core->get_ObjectManager()->get_DirectionalLights() == nullptr) {
            return false;
        }

        List<::DirectionalLightComponent*>* directionalLights = core->get_ObjectManager()->get_DirectionalLights();
        for (int32_t lightIndex = 0; lightIndex < directionalLights->Count(); lightIndex++) {
            ::DirectionalLightComponent* directionalLight = (*directionalLights)[lightIndex];
            if (directionalLight == nullptr || directionalLight->get_IsDisposed()) {
                continue;
            }

            ::Entity* parent = directionalLight->get_ParentUnsafe();
            if (!IsLiveEntity(parent)) {
                continue;
            }

            lightDirection = ::float4::RotateVector(::float3(0.0f, 0.0f, -1.0f), parent->get_Orientation());
            return true;
        }

        return false;
    }

    std::uint64_t Ps2RenderManager3D::ResolveVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal, const ::float3& lightDirection) {
        if (material.GetLightingMode() == ::Ps2MaterialLightingMode::Unlit) {
            return GS_SETREG_RGBAQ(
                material.GetBaseColorR(),
                material.GetBaseColorG(),
                material.GetBaseColorB(),
                material.GetBaseColorA(),
                0x00);
        }

        const double normalLengthSquared = static_cast<double>(normal.X) * static_cast<double>(normal.X)
            + static_cast<double>(normal.Y) * static_cast<double>(normal.Y)
            + static_cast<double>(normal.Z) * static_cast<double>(normal.Z);
        if (normalLengthSquared <= 0.000001) {
            return GS_SETREG_RGBAQ(0x40, 0x40, 0x40, 0x80, 0x00);
        }

        ::float3 normalizedNormal = ::float3::Normalize(normal);
        ::float3 normalizedLightDirection = ::float3::Normalize(lightDirection);
        const double lightX = static_cast<double>(normalizedLightDirection.X);
        const double lightY = static_cast<double>(normalizedLightDirection.Y);
        const double lightZ = static_cast<double>(normalizedLightDirection.Z);
        double ndotl = static_cast<double>(normalizedNormal.X) * lightX
            + static_cast<double>(normalizedNormal.Y) * lightY
            + static_cast<double>(normalizedNormal.Z) * lightZ;
        ndotl = std::max(0.0, ndotl);

        const double roughness = std::clamp(static_cast<double>(material.GetRoughness()), 0.0, 1.0);
        const double specularStrength = std::clamp(static_cast<double>(material.GetSpecularStrength()), 0.0, 1.0);
        const double emissiveStrength = std::clamp(static_cast<double>(material.GetEmissiveStrength()), 0.0, 1.0);
        const double diffuseScale = 1.0 - (roughness * 0.35);
        const double baseIntensity = static_cast<double>(LightingBias) + (ndotl * static_cast<double>(LightingScale) * diffuseScale);
        const double specularPower = 4.0 + ((1.0 - roughness) * 8.0);
        const double specularBoost = std::pow(ndotl, specularPower) * specularStrength * (material.GetLightingMode() == ::Ps2MaterialLightingMode::ShowcaseLit ? 96.0 : 48.0);
        const double emissiveBoost = emissiveStrength * (material.GetLightingMode() == ::Ps2MaterialLightingMode::ShowcaseLit ? 72.0 : 24.0);
        double intensityValue = baseIntensity + specularBoost + emissiveBoost;

        if (material.GetLightingMode() == ::Ps2MaterialLightingMode::ShowcaseLit && material.GetExpensiveModeAllowed()) {
            intensityValue += 24.0 + (ndotl * 28.0);
        }

        const std::uint8_t intensity = static_cast<std::uint8_t>(std::clamp(std::lround(intensityValue), 0l, 255l));
        const auto applyIntensity = [intensity](std::uint8_t channel) {
            const double litChannel = (static_cast<double>(channel) * static_cast<double>(intensity)) / 255.0;
            return static_cast<std::uint8_t>(std::clamp(std::lround(litChannel), 0l, 255l));
        };

        return GS_SETREG_RGBAQ(
            applyIntensity(material.GetBaseColorR()),
            applyIntensity(material.GetBaseColorG()),
            applyIntensity(material.GetBaseColorB()),
            material.GetBaseColorA(),
            0x00);
    }
}
