#include "Ps2HostRenderManager3D.hpp"

#include <iostream>
#include <stdexcept>

#include "CameraComponent.hpp"
#include "Core.hpp"
#include "DirectionalLightComponent.hpp"
#include "Entity.hpp"
#include "IDrawable3D.hpp"
#include "MaterialAsset.hpp"
#include "ModelAsset.hpp"
#include "ObjectManager.hpp"
#include "PlatformMaterialAsset.hpp"
#include "float4.hpp"
#include "float4x4.hpp"
#include "int2.hpp"
#include "platform/ps2/rendering/Ps2FramePlan.hpp"
#include "Ps2MaterialAsset.hpp"
#include "RenderTarget.hpp"
#include "RuntimeMaterial.hpp"
#include "RuntimeModel.hpp"
#include "ShaderAsset.hpp"
#include "runtime/native_cast.hpp"

namespace helengine::ps2::host {
    namespace {
        constexpr float CameraFieldOfViewRadians = 0.785398185f;
        constexpr float MinimumClipW = 0.0001f;
        const ::float3 DefaultForward(0.0f, 0.0f, -1.0f);
        const ::float3 DefaultUp(0.0f, 1.0f, 0.0f);

        struct HostDebugProjectedVertex final {
            bool IsValid = false;
            float ClipX = 0.0f;
            float ClipY = 0.0f;
            float ClipZ = 0.0f;
            float ClipW = 0.0f;
            float ScreenX = 0.0f;
            float ScreenY = 0.0f;
            float ScreenZ = 0.0f;
            std::int32_t GsX = 0;
            std::int32_t GsY = 0;
            std::int32_t GsZ = 0;
        };

        ::CameraComponent* ResolveActiveCamera(ObjectManager* objectManager) {
            if (objectManager == nullptr || objectManager->get_Cameras() == nullptr) {
                return nullptr;
            }

            List<::ICamera*>* cameras = objectManager->get_Cameras();
            for (int32_t index = 0; index < cameras->Count(); index++) {
                ::CameraComponent* camera = he_cpp_try_cast<::CameraComponent>((*cameras)[index]);
                if (camera != nullptr) {
                    return camera;
                }
            }

            return nullptr;
        }

        ::float4 ResolvePixelViewport(::CameraComponent* camera, const int2& windowSize) {
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

        bool TryResolveDirectionalLightDirection(ObjectManager* objectManager, ::float3& lightDirection) {
            if (objectManager == nullptr || objectManager->get_Entities() == nullptr) {
                return false;
            }

            List<::Entity*>* entities = objectManager->get_Entities();
            for (int32_t entityIndex = 0; entityIndex < entities->Count(); entityIndex++) {
                ::Entity* entity = (*entities)[entityIndex];
                if (entity == nullptr || entity->get_Components() == nullptr) {
                    continue;
                }

                List<::Component*>* components = entity->get_Components();
                for (int32_t componentIndex = 0; componentIndex < components->Count(); componentIndex++) {
                    auto* directionalLight = dynamic_cast<::DirectionalLightComponent*>((*components)[componentIndex]);
                    if (directionalLight == nullptr || directionalLight->get_Parent() == nullptr) {
                        continue;
                    }

                    lightDirection = ::float4::RotateVector(DefaultForward, directionalLight->get_Parent()->get_Orientation());
                    return true;
                }
            }

            return false;
        }

        ::float4x4 BuildWorldMatrix(const helengine::ps2::Ps2RenderProxy& proxy) {
            ::IDrawable3D* drawable = proxy.GetDrawable();
            if (drawable == nullptr) {
                return ::float4x4::get_Identity();
            }

            ::Entity* parent = drawable->get_Parent();
            if (parent == nullptr) {
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
            ::float4x4::CreateScale(parentScale.X, parentScale.Y, parentScale.Z, scaleMatrix);
            ::float4x4::CreateFromQuaternion(parentOrientation, rotationMatrix);
            ::float4x4::CreateTranslation(parentPosition, translationMatrix);
            ::float4x4::Multiply(scaleMatrix, rotationMatrix, scaleRotationMatrix);
            ::float4x4::Multiply(scaleRotationMatrix, translationMatrix, worldMatrix);
            return worldMatrix;
        }

        ::float3 TransformWorldPosition(const float* sourcePosition, const ::float4x4& world) {
            return ::float3(
                (sourcePosition[0] * world.M11) + (sourcePosition[1] * world.M21) + (sourcePosition[2] * world.M31) + world.M41,
                (sourcePosition[0] * world.M12) + (sourcePosition[1] * world.M22) + (sourcePosition[2] * world.M32) + world.M42,
                (sourcePosition[0] * world.M13) + (sourcePosition[1] * world.M23) + (sourcePosition[2] * world.M33) + world.M43);
        }

        HostDebugProjectedVertex BuildCpuProjectedVertex(
            const ::float3& worldPosition,
            const ::float4x4& view,
            const ::float4x4& projection,
            const ::float4& viewport) {
            HostDebugProjectedVertex vertex;
            const float viewX = (worldPosition.X * view.M11)
                + (worldPosition.Y * view.M21)
                + (worldPosition.Z * view.M31)
                + view.M41;
            const float viewY = (worldPosition.X * view.M12)
                + (worldPosition.Y * view.M22)
                + (worldPosition.Z * view.M32)
                + view.M42;
            const float viewZ = (worldPosition.X * view.M13)
                + (worldPosition.Y * view.M23)
                + (worldPosition.Z * view.M33)
                + view.M43;
            vertex.ClipX = (viewX * projection.M11)
                + (viewY * projection.M21)
                + (viewZ * projection.M31)
                + projection.M41;
            vertex.ClipY = (viewX * projection.M12)
                + (viewY * projection.M22)
                + (viewZ * projection.M32)
                + projection.M42;
            vertex.ClipZ = (viewX * projection.M13)
                + (viewY * projection.M23)
                + (viewZ * projection.M33)
                + projection.M43;
            vertex.ClipW = (viewX * projection.M14)
                + (viewY * projection.M24)
                + (viewZ * projection.M34)
                + projection.M44;
            if (vertex.ClipW <= MinimumClipW) {
                return vertex;
            }

            const float inverseClipW = 1.0f / vertex.ClipW;
            const float normalizedX = vertex.ClipX * inverseClipW;
            const float normalizedY = vertex.ClipY * inverseClipW;
            const float normalizedZ = vertex.ClipZ * inverseClipW;
            vertex.ScreenX = viewport.X + ((normalizedX + 1.0f) * 0.5f * viewport.Z);
            vertex.ScreenY = viewport.Y + ((1.0f - normalizedY) * 0.5f * viewport.W);
            vertex.ScreenZ = (normalizedZ + 1.0f) * 0.5f;
            vertex.GsX = static_cast<std::int32_t>((2048.0f + vertex.ScreenX) * 16.0f);
            vertex.GsY = static_cast<std::int32_t>((2048.0f + vertex.ScreenY) * 16.0f);
            const float gsDepth = 1.0f - vertex.ScreenZ;
            vertex.GsZ = static_cast<std::int32_t>(gsDepth * static_cast<float>(1u << 23));
            vertex.IsValid = true;
            return vertex;
        }

        HostDebugProjectedVertex BuildVuProjectedVertex(
            const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup,
            const float* sourcePosition) {
            HostDebugProjectedVertex vertex;
            vertex.ClipX = (sourcePosition[0] * triangleSetup.WorldViewProjectionMatrix[0])
                + (sourcePosition[1] * triangleSetup.WorldViewProjectionMatrix[4])
                + (sourcePosition[2] * triangleSetup.WorldViewProjectionMatrix[8])
                + (sourcePosition[3] * triangleSetup.WorldViewProjectionMatrix[12]);
            vertex.ClipY = (sourcePosition[0] * triangleSetup.WorldViewProjectionMatrix[1])
                + (sourcePosition[1] * triangleSetup.WorldViewProjectionMatrix[5])
                + (sourcePosition[2] * triangleSetup.WorldViewProjectionMatrix[9])
                + (sourcePosition[3] * triangleSetup.WorldViewProjectionMatrix[13]);
            vertex.ClipZ = (sourcePosition[0] * triangleSetup.WorldViewProjectionMatrix[2])
                + (sourcePosition[1] * triangleSetup.WorldViewProjectionMatrix[6])
                + (sourcePosition[2] * triangleSetup.WorldViewProjectionMatrix[10])
                + (sourcePosition[3] * triangleSetup.WorldViewProjectionMatrix[14]);
            vertex.ClipW = (sourcePosition[0] * triangleSetup.WorldViewProjectionMatrix[3])
                + (sourcePosition[1] * triangleSetup.WorldViewProjectionMatrix[7])
                + (sourcePosition[2] * triangleSetup.WorldViewProjectionMatrix[11])
                + (sourcePosition[3] * triangleSetup.WorldViewProjectionMatrix[15]);
            if (vertex.ClipW <= MinimumClipW) {
                return vertex;
            }

            const float inverseClipW = 1.0f / vertex.ClipW;
            const float normalizedX = vertex.ClipX * inverseClipW;
            const float normalizedY = vertex.ClipY * inverseClipW;
            const float normalizedZ = vertex.ClipZ * inverseClipW;
            const float gsFloatX = (normalizedX * triangleSetup.GsScale[0]) + triangleSetup.GsOffset[0];
            const float gsFloatY = (normalizedY * triangleSetup.GsScale[1]) + triangleSetup.GsOffset[1];
            const float gsFloatZ = (normalizedZ * triangleSetup.GsScale[2]) + triangleSetup.GsOffset[2];
            vertex.ScreenX = gsFloatX - 2048.0f;
            vertex.ScreenY = gsFloatY - 2048.0f;
            vertex.ScreenZ = 1.0f - (gsFloatZ / static_cast<float>(1u << 23));
            vertex.GsX = static_cast<std::int32_t>(gsFloatX * 16.0f);
            vertex.GsY = static_cast<std::int32_t>(gsFloatY * 16.0f);
            vertex.GsZ = static_cast<std::int32_t>(gsFloatZ);
            vertex.IsValid = true;
            return vertex;
        }

        HostDebugProjectedVertex BuildCombinedMatrixClip(
            const ::float4x4& combined,
            const float* sourcePosition) {
            HostDebugProjectedVertex vertex;
            vertex.ClipX = (sourcePosition[0] * combined.M11)
                + (sourcePosition[1] * combined.M21)
                + (sourcePosition[2] * combined.M31)
                + (sourcePosition[3] * combined.M41);
            vertex.ClipY = (sourcePosition[0] * combined.M12)
                + (sourcePosition[1] * combined.M22)
                + (sourcePosition[2] * combined.M32)
                + (sourcePosition[3] * combined.M42);
            vertex.ClipZ = (sourcePosition[0] * combined.M13)
                + (sourcePosition[1] * combined.M23)
                + (sourcePosition[2] * combined.M33)
                + (sourcePosition[3] * combined.M43);
            vertex.ClipW = (sourcePosition[0] * combined.M14)
                + (sourcePosition[1] * combined.M24)
                + (sourcePosition[2] * combined.M34)
                + (sourcePosition[3] * combined.M44);
            vertex.IsValid = true;
            return vertex;
        }

        void LogProjectedVertexComparison(
            const char* label,
            const ::float3& worldPosition,
            const ::float4x4& world,
            const ::float4x4& view,
            const ::float4x4& projection,
            const ::float4& viewport,
            const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup,
            const float* sourcePosition) {
            const HostDebugProjectedVertex cpuVertex = BuildCpuProjectedVertex(worldPosition, view, projection, viewport);
            const HostDebugProjectedVertex vuVertex = BuildVuProjectedVertex(triangleSetup, sourcePosition);
            ::float4x4 projectionCopy = projection;
            ::float4x4 viewCopy = view;
            ::float4x4 worldCopy = world;
            ::float4x4 projectionView;
            ::float4x4 projectionViewWorld;
            ::float4x4 viewWorld;
            ::float4x4 viewWorldProjection;
            ::float4x4::Multiply(projectionCopy, viewCopy, projectionView);
            ::float4x4::Multiply(projectionView, worldCopy, projectionViewWorld);
            ::float4x4::Multiply(viewCopy, worldCopy, viewWorld);
            ::float4x4::Multiply(viewWorld, projectionCopy, viewWorldProjection);
            const HostDebugProjectedVertex altProjectionViewWorld = BuildCombinedMatrixClip(projectionViewWorld, sourcePosition);
            const HostDebugProjectedVertex altViewWorldProjection = BuildCombinedMatrixClip(viewWorldProjection, sourcePosition);
            std::cout
                << "[ps2-host-debug] compare " << label
                << " cpuValid=" << cpuVertex.IsValid
                << " vuValid=" << vuVertex.IsValid
                << " cpuClip=(" << cpuVertex.ClipX << "," << cpuVertex.ClipY << "," << cpuVertex.ClipZ << "," << cpuVertex.ClipW << ")"
                << " vuClip=(" << vuVertex.ClipX << "," << vuVertex.ClipY << "," << vuVertex.ClipZ << "," << vuVertex.ClipW << ")"
                << " altPVWClip=(" << altProjectionViewWorld.ClipX << "," << altProjectionViewWorld.ClipY << "," << altProjectionViewWorld.ClipZ << "," << altProjectionViewWorld.ClipW << ")"
                << " altVWPClip=(" << altViewWorldProjection.ClipX << "," << altViewWorldProjection.ClipY << "," << altViewWorldProjection.ClipZ << "," << altViewWorldProjection.ClipW << ")"
                << " cpuScreen=(" << cpuVertex.ScreenX << "," << cpuVertex.ScreenY << "," << cpuVertex.ScreenZ << ")"
                << " vuScreen=(" << vuVertex.ScreenX << "," << vuVertex.ScreenY << "," << vuVertex.ScreenZ << ")"
                << " cpuGs=(" << cpuVertex.GsX << "," << cpuVertex.GsY << "," << cpuVertex.GsZ << ")"
                << " vuGs=(" << vuVertex.GsX << "," << vuVertex.GsY << "," << vuVertex.GsZ << ")"
                << std::endl;
        }
    }

    Ps2HostRenderManager3D::Ps2HostRenderManager3D()
        : ::RenderManager3D(),
          FramePlanner(),
          VuOpaqueBatchBuilder(),
          VuUntexturedSetupBuilder(),
          Proxies(),
          LastCameraCount(0),
          LastDrawable3DCount(0),
          LastDrawable2DCount(0),
          LastProxyCount(0),
          LastOpaqueWorldCount(0),
          LastOpaqueDynamicCount(0),
          LastAlphaWorldCount(0),
          LastAlphaDynamicCount(0),
          LastVuBatchCount(0),
          LastVuRejectedMissingMaterialCount(0),
          LastVuRejectedMissingModelCount(0),
          LastVuRejectedMissingPackedModelCount(0),
          LastVuTriangleSetupMilliseconds(0.0),
          LastVuTrianglePrepMilliseconds(0.0),
          LastVuTriangleEmitMilliseconds(0.0),
          LastVuSubmittedTriangleCount(0) {
    }

    RuntimeMaterial* Ps2HostRenderManager3D::BuildMaterialFromCooked(PlatformMaterialAsset* materialAsset) {
        if (materialAsset == nullptr) {
            throw std::invalid_argument("One PS2 cooked platform material asset is required.");
        }

        auto* ps2MaterialAsset = dynamic_cast<Ps2MaterialAsset*>(materialAsset);
        if (ps2MaterialAsset == nullptr) {
            throw std::invalid_argument("One PS2 cooked platform material asset must deserialize to Ps2MaterialAsset.");
        }

        auto* runtimeMaterial = new ::helengine::ps2::Ps2RuntimeMaterial();
        runtimeMaterial->LoadFromCooked(ps2MaterialAsset);
        return runtimeMaterial;
    }

    RuntimeMaterial* Ps2HostRenderManager3D::BuildMaterialFromRaw(MaterialAsset* materialAsset, ShaderAsset* shaderAsset) {
        throw std::runtime_error("PS2 host debug does not support one raw material path.");
    }

    RuntimeModel* Ps2HostRenderManager3D::BuildModelFromRaw(ModelAsset* data) {
        if (data == nullptr) {
            throw std::invalid_argument("One PS2 raw model asset is required.");
        }

        auto* runtimeModel = new ::helengine::ps2::Ps2RuntimeModel();
        runtimeModel->LoadFromRaw(data);
        return runtimeModel;
    }

    RenderTarget* Ps2HostRenderManager3D::CreateRenderTarget(int32_t width, int32_t height) {
        auto* renderTarget = new ::RenderTarget();
        renderTarget->set_Width(width);
        renderTarget->set_Height(height);
        renderTarget->set_CanSampleAsTexture(true);
        renderTarget->set_HasDepthBuffer(true);
        return renderTarget;
    }

    void Ps2HostRenderManager3D::Draw() {
        Core* core = Core::get_Instance();
        if (core == nullptr || core->get_ObjectManager() == nullptr) {
            LastCameraCount = 0;
            LastDrawable3DCount = 0;
            LastDrawable2DCount = 0;
            std::cout << "[ps2-host-debug] draw skipped because core object manager is unavailable" << std::endl;
            return;
        }

        ObjectManager* objectManager = core->get_ObjectManager();
        LastCameraCount = objectManager->get_Cameras() != nullptr ? objectManager->get_Cameras()->get_Count() : 0;
        LastDrawable3DCount = objectManager->get_Drawables3D() != nullptr ? objectManager->get_Drawables3D()->get_Count() : 0;
        LastDrawable2DCount = objectManager->get_Drawables2D() != nullptr ? objectManager->get_Drawables2D()->get_Count() : 0;

        Proxies.clear();
        if (objectManager->get_Drawables3D() != nullptr) {
            Proxies.reserve(static_cast<std::size_t>(objectManager->get_Drawables3D()->get_Count()));
            for (int32_t drawableIndex = 0; drawableIndex < objectManager->get_Drawables3D()->get_Count(); drawableIndex++) {
                IDrawable3D* drawable = (*objectManager->get_Drawables3D())[drawableIndex];
                if (drawable == nullptr) {
                    continue;
                }

                helengine::ps2::Ps2RenderProxy proxy;
                proxy.Synchronize(drawable);
                Proxies.push_back(proxy);
            }
        }

        const helengine::ps2::Ps2FramePlan framePlan = FramePlanner.Build(Proxies);
        LastProxyCount = static_cast<int32_t>(Proxies.size());
        LastOpaqueWorldCount = static_cast<int32_t>(framePlan.OpaqueWorld.size());
        LastOpaqueDynamicCount = static_cast<int32_t>(framePlan.OpaqueDynamic.size());
        LastAlphaWorldCount = static_cast<int32_t>(framePlan.AlphaWorld.size());
        LastAlphaDynamicCount = static_cast<int32_t>(framePlan.AlphaDynamic.size());
        const std::vector<helengine::ps2::Ps2VuOpaqueBatch> opaqueBatches = VuOpaqueBatchBuilder.Build(framePlan);
        LastVuBatchCount = static_cast<int32_t>(opaqueBatches.size());
        LastVuRejectedMissingMaterialCount = static_cast<int32_t>(VuOpaqueBatchBuilder.GetLastRejectedMissingMaterialCount());
        LastVuRejectedMissingModelCount = static_cast<int32_t>(VuOpaqueBatchBuilder.GetLastRejectedMissingModelCount());
        LastVuRejectedMissingPackedModelCount = static_cast<int32_t>(VuOpaqueBatchBuilder.GetLastRejectedMissingPackedModelCount());
        LastVuTriangleSetupMilliseconds = 0.0;
        LastVuTrianglePrepMilliseconds = 0.0;
        LastVuTriangleEmitMilliseconds = 0.0;
        LastVuSubmittedTriangleCount = 0;

        ::CameraComponent* camera = ResolveActiveCamera(objectManager);
        if (camera != nullptr && camera->get_Parent() != nullptr) {
            const int2 windowSize = get_MainWindowSize();
            if (windowSize.X > 0 && windowSize.Y > 0) {
                const ::float4 viewport = ResolvePixelViewport(camera, windowSize);
                if (viewport.Z > 0.0f && viewport.W > 0.0f) {
                    ::float3 cameraPosition = camera->get_Parent()->get_Position();
                    const ::float4 cameraOrientation = camera->get_Parent()->get_Orientation();
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
                    ::float3 lightDirection = DefaultForward;
                    TryResolveDirectionalLightDirection(objectManager, lightDirection);
                    for (const helengine::ps2::Ps2VuOpaqueBatch& batch : opaqueBatches) {
                        if (batch.Textured || batch.Proxy == nullptr) {
                            continue;
                        }

                        const ::float4x4 world = BuildWorldMatrix(*batch.Proxy);
                        VuUntexturedSetupBuilder.Build(
                            batch,
                            world,
                            view,
                            projection,
                            viewport,
                            lightDirection,
                            camera->get_NearPlaneDistance(),
                            nullptr);
                        LastVuTriangleSetupMilliseconds += VuUntexturedSetupBuilder.GetLastTriangleSetupMilliseconds();
                        LastVuTrianglePrepMilliseconds += VuUntexturedSetupBuilder.GetLastTrianglePrepMilliseconds();
                        LastVuTriangleEmitMilliseconds += VuUntexturedSetupBuilder.GetLastTriangleEmitMilliseconds();
                        LastVuSubmittedTriangleCount += static_cast<int32_t>(VuUntexturedSetupBuilder.GetSubmittedTriangleCount());
                        const std::vector<Ps2VuOpaqueUntexturedTriangleSetup>& triangleSetups = VuUntexturedSetupBuilder.GetTriangleSetups();
                        std::cout
                            << "[ps2-host-debug] triangle setup count="
                            << triangleSetups.size()
                            << " submitted="
                            << VuUntexturedSetupBuilder.GetSubmittedTriangleCount()
                            << std::endl;
                        if (!triangleSetups.empty()) {
                            const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup = triangleSetups[0];
                            const ::float3 worldPositionA = TransformWorldPosition(triangleSetup.SourceTriangle.PositionA, world);
                            const ::float3 worldPositionB = TransformWorldPosition(triangleSetup.SourceTriangle.PositionB, world);
                            const ::float3 worldPositionC = TransformWorldPosition(triangleSetup.SourceTriangle.PositionC, world);
                            LogProjectedVertexComparison("A", worldPositionA, world, view, projection, viewport, triangleSetup, triangleSetup.SourceTriangle.PositionA);
                            LogProjectedVertexComparison("B", worldPositionB, world, view, projection, viewport, triangleSetup, triangleSetup.SourceTriangle.PositionB);
                            LogProjectedVertexComparison("C", worldPositionC, world, view, projection, viewport, triangleSetup, triangleSetup.SourceTriangle.PositionC);
                        }
                        break;
                    }
                }
            }
        }

        std::cout
            << "[ps2-host-debug] draw stats cameras=" << LastCameraCount
            << " drawables3d=" << LastDrawable3DCount
            << " drawables2d=" << LastDrawable2DCount
            << " proxies=" << LastProxyCount
            << " opaqueWorld=" << LastOpaqueWorldCount
            << " opaqueDynamic=" << LastOpaqueDynamicCount
            << " alphaWorld=" << LastAlphaWorldCount
            << " alphaDynamic=" << LastAlphaDynamicCount
            << " vuBatches=" << LastVuBatchCount
            << " rejMissingMaterial=" << LastVuRejectedMissingMaterialCount
            << " rejMissingModel=" << LastVuRejectedMissingModelCount
            << " rejMissingPackedModel=" << LastVuRejectedMissingPackedModelCount
            << " triSetupMs=" << LastVuTriangleSetupMilliseconds
            << " triPrepMs=" << LastVuTrianglePrepMilliseconds
            << " triEmitMs=" << LastVuTriangleEmitMilliseconds
            << " submittedTriangles=" << LastVuSubmittedTriangleCount
            << std::endl;
    }

    int32_t Ps2HostRenderManager3D::get_LastCameraCount() const {
        return LastCameraCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastDrawable3DCount() const {
        return LastDrawable3DCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastDrawable2DCount() const {
        return LastDrawable2DCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastProxyCount() const {
        return LastProxyCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastOpaqueWorldCount() const {
        return LastOpaqueWorldCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastOpaqueDynamicCount() const {
        return LastOpaqueDynamicCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastAlphaWorldCount() const {
        return LastAlphaWorldCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastAlphaDynamicCount() const {
        return LastAlphaDynamicCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastVuBatchCount() const {
        return LastVuBatchCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastVuRejectedMissingMaterialCount() const {
        return LastVuRejectedMissingMaterialCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastVuRejectedMissingModelCount() const {
        return LastVuRejectedMissingModelCount;
    }

    int32_t Ps2HostRenderManager3D::get_LastVuRejectedMissingPackedModelCount() const {
        return LastVuRejectedMissingPackedModelCount;
    }

    double Ps2HostRenderManager3D::get_LastVuTriangleSetupMilliseconds() const {
        return LastVuTriangleSetupMilliseconds;
    }

    double Ps2HostRenderManager3D::get_LastVuTrianglePrepMilliseconds() const {
        return LastVuTrianglePrepMilliseconds;
    }

    double Ps2HostRenderManager3D::get_LastVuTriangleEmitMilliseconds() const {
        return LastVuTriangleEmitMilliseconds;
    }

    int32_t Ps2HostRenderManager3D::get_LastVuSubmittedTriangleCount() const {
        return LastVuSubmittedTriangleCount;
    }
}
