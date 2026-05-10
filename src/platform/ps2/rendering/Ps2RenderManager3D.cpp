#include "platform/ps2/rendering/Ps2RenderManager3D.hpp"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <malloc.h>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>

#include <gsKit.h>

#include "ContentManager.hpp"
#include "AssetSerializer.hpp"
#include "CameraComponent.hpp"
#include "Core.hpp"
#include "DirectionalLightComponent.hpp"
#include "Entity.hpp"
#include "IDrawable3D.hpp"
#include "ModelAsset.hpp"
#include "ObjectManager.hpp"
#include "TextureAsset.hpp"
#include "Ps2MaterialAsset.hpp"
#include "Ps2MaterialLightingMode.hpp"
#include "MaterialBlendMode.hpp"
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
        constexpr double LightingScale = 191.0;
        constexpr double LightingBias = 64.0;
        constexpr bool EnableFlatColorDiagnostics = false;
        constexpr bool EnableLightingOnlyDiagnostics = true;
        constexpr bool EnableSingleProxyDiagnostics = false;
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
        const ::float3 DefaultForward(0.0f, 0.0f, -1.0f);
        const ::float3 DefaultUp(0.0f, 1.0f, 0.0f);

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

        GSTEXTURE* BuildTextureFromAsset(GSGLOBAL* gsGlobal, ::TextureAsset* data) {
            if (gsGlobal == nullptr || data == nullptr || data->Colors == nullptr || data->Colors->Length <= 0 || data->Width <= 0 || data->Height <= 0) {
                return nullptr;
            }

            GSTEXTURE* texture = new GSTEXTURE();
            texture->Width = data->Width;
            texture->Height = data->Height;
            texture->PSM = GS_PSM_CT32;
            texture->Clut = nullptr;
            texture->VramClut = 0;
            texture->Filter = GS_FILTER_NEAREST;
            texture->Mem = static_cast<u32*>(memalign(128, static_cast<std::size_t>(data->Colors->Length)));
            if (texture->Mem == nullptr) {
                delete texture;
                return nullptr;
            }

            std::memcpy(texture->Mem, data->Colors->Data, static_cast<std::size_t>(data->Colors->Length));
            texture->Vram = gsKit_vram_alloc(
                gsGlobal,
                gsKit_texture_size(texture->Width, texture->Height, texture->PSM),
                GSKIT_ALLOC_USERBUFFER);
            if (texture->Vram == GSKIT_ALLOC_ERROR) {
                free(texture->Mem);
                delete texture;
                return nullptr;
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

            const std::string fullPath = ::Path::GetFullPath(::Path::Combine(core->get_ContentManager()->get_RootDirectory(), textureRelativePath));
            ::FileStream* stream = ::File::OpenRead(fullPath);
            [[maybe_unused]] auto streamGuard = he_cpp_make_scope_exit([stream]() {
                if (stream != nullptr) {
                    stream->Dispose();
                }
            });
            ::Asset* asset = ::AssetSerializer::Deserialize(stream);
            ::TextureAsset* textureAsset = he_cpp_try_cast<::TextureAsset>(asset);
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
                    gsKit_prim_triangle_goraud_texture_3d(
                        gsGlobal,
                        triangle.Texture,
                        glowAX, glowAY, glowAZ, triangle.TexCoordA.X, triangle.TexCoordA.Y,
                        glowBX, glowBY, glowBZ, triangle.TexCoordB.X, triangle.TexCoordB.Y,
                        glowCX, glowCY, glowCZ, triangle.TexCoordC.X, triangle.TexCoordC.Y,
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
          HdrEnabled(false),
          GsGlobal(nullptr),
          Proxies(),
          LastProxyCount(0),
          LastOpaqueWorldCount(0),
          LastOpaqueDynamicCount(0),
          LastAlphaWorldCount(0),
          LastAlphaDynamicCount(0),
          LastClipRejectCount(0),
          LastProjectionRejectCount(0),
          LastCullRejectCount(0),
          LastSubmittedTriangleCount(0),
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

    ::RuntimeMaterial* Ps2RenderManager3D::BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset) {
        if (materialAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked material asset is required.");
        }

        Ps2RuntimeMaterial* runtimeMaterial = new Ps2RuntimeMaterial();
        runtimeMaterial->LoadFromCooked(materialAsset);
        return runtimeMaterial;
    }

    ::RuntimeModel* Ps2RenderManager3D::BuildModelFromRaw(::ModelAsset* data) {
        if (data == nullptr) {
            throw std::invalid_argument("PS2 raw model data is required.");
        }

        Ps2RuntimeModel* runtimeModel = new Ps2RuntimeModel();
        runtimeModel->LoadFromRaw(data);
        return runtimeModel;
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

        if (GsGlobal == nullptr) {
            return;
        }

        ::CameraComponent* camera = GetActiveCamera();
        if (camera == nullptr || camera->get_Parent() == nullptr) {
            return;
        }

        int2* windowSize = get_MainWindowSize();
        if (windowSize == nullptr || windowSize->X <= 0 || windowSize->Y <= 0) {
            return;
        }

        ::float4 viewport = ResolvePixelViewport(camera, windowSize);
        LastResolvedViewport = viewport;
        if (viewport.Z <= 0.0f || viewport.W <= 0.0f) {
            return;
        }

        ::float3 cameraPosition = camera->get_Parent()->get_Position();
        ::float4 cameraOrientation = camera->get_Parent()->get_Orientation();
        ::float3 cameraForward = ::float4::RotateVector(DefaultForward, cameraOrientation);
        ::float3 cameraUp = ::float4::RotateVector(DefaultUp, cameraOrientation);
        ::float3 cameraTarget = cameraPosition + cameraForward;
        ::float4x4 view;
        ::float4x4::CreateLookAt(cameraPosition, cameraTarget, cameraUp, view);

        ::float4x4 projection;
        ::float4x4::CreatePerspectiveFieldOfView(
            CameraFieldOfViewRadians,
            viewport.Z / viewport.W,
            camera->get_NearPlaneDistance(),
            camera->get_FarPlaneDistance(),
            projection);

        DeferredHdrGlowTriangles.clear();
        ApplyDepthState(GsGlobal->ZBuffering == GS_SETTING_ON);
        RebuildProxies();
        LastProxyCount = Proxies.size();
        Ps2FramePlan plan = FramePlanner.Build(Proxies);
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
                    DrawOpaqueProxy(*firstProxy, view, projection, viewport, camera->get_NearPlaneDistance());
                }

                DrawHdrGlowPass(GsGlobal);
                return;
            }

            for (const Ps2RenderProxy* proxy : plan.OpaqueWorld) {
                if (proxy != nullptr) {
                    DrawOpaqueProxy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
                }
            }

            for (const Ps2RenderProxy* proxy : plan.OpaqueDynamic) {
                if (proxy != nullptr) {
                    DrawOpaqueProxy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
                }
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
        if (!useDiagnosticFlatColor && !useLightingOnlyDiagnostics && !material->GetTextureRelativePath().empty()) {
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
                    gsKit_prim_triangle_goraud_texture_3d(
                        GsGlobal,
                        texture,
                        screenAX, screenAY, screenAZ, clippedA.TexCoord.X, clippedA.TexCoord.Y,
                        screenBX, screenBY, screenBZ, clippedB.TexCoord.X, clippedB.TexCoord.Y,
                        screenCX, screenCY, screenCZ, clippedC.TexCoord.X, clippedC.TexCoord.Y,
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
                DrawOpaqueProxy(*firstProxy, view, projection, viewport, nearPlaneDistance);
            }

            return;
        }

        SortAlphaProxies(sortedProxies, cameraPosition, cameraForward);

        for (const Ps2RenderProxy* proxy : sortedProxies) {
            if (proxy == nullptr) {
                continue;
            }

            DrawOpaqueProxy(*proxy, view, projection, viewport, nearPlaneDistance);
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
            if (camera != nullptr) {
                return camera;
            }
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
        if (drawable == nullptr || drawable->get_Parent() == nullptr) {
            return 0.0;
        }

        const ::float3 proxyPosition = drawable->get_Parent()->get_Position();
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
            proxy.Synchronize(drawable);
            if (proxy.GetModel() == nullptr || proxy.GetMaterial() == nullptr) {
                continue;
            }

            Proxies.push_back(proxy);
        }
    }

    bool Ps2RenderManager3D::TryResolveDirectionalLightDirection(::float3& lightDirection) const {
        ::Core* core = ::Core::get_Instance();
        if (core == nullptr || core->get_ObjectManager() == nullptr || core->get_ObjectManager()->get_Entities() == nullptr) {
            return false;
        }

        List<::Entity*>* entities = core->get_ObjectManager()->get_Entities();
        for (int32_t entityIndex = 0; entityIndex < entities->Count(); entityIndex++) {
            ::Entity* entity = (*entities)[entityIndex];
            if (entity == nullptr || entity->get_Components() == nullptr) {
                continue;
            }

            List<::Component*>* components = entity->get_Components();
            for (int32_t componentIndex = 0; componentIndex < components->Count(); componentIndex++) {
                ::Component* component = (*components)[componentIndex];
                ::DirectionalLightComponent* directionalLight = dynamic_cast<::DirectionalLightComponent*>(component);
                if (directionalLight == nullptr || directionalLight->get_Parent() == nullptr) {
                    continue;
                }

                lightDirection = ::float4::RotateVector(::float3(0.0f, 0.0f, -1.0f), directionalLight->get_Parent()->get_Orientation());
                return true;
            }
        }

        return false;
    }

    std::uint64_t Ps2RenderManager3D::ResolveVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal, const ::float3& lightDirection) {
        if (material.GetLightingMode() == ::Ps2MaterialLightingMode::Unlit) {
            return GS_SETREG_RGBAQ(0xC0, 0xC0, 0xC0, 0x80, 0x00);
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
