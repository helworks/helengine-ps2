using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies PS2 native source inputs required by the runtime build pipeline.
/// </summary>
public sealed class Ps2NativeBuildInputsTests {
    /// <summary>
    /// Ensures the PS2 DualShock analog axes are normalized into the shared gamepad state used by menu navigation.
    /// </summary>
    [Fact]
    public void Ps2_input_backend_maps_dualshock_left_stick_axes() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string mapperHeader = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2PadInputMapper.hpp"));
        string inputSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2InputBackend.cpp"));

        Assert.Contains("int16_t LeftStickX = 0;", mapperHeader, StringComparison.Ordinal);
        Assert.Contains("int16_t LeftStickY = 0;", mapperHeader, StringComparison.Ordinal);
        Assert.Contains("padSetMainMode(Port, Slot, PAD_MMODE_DUALSHOCK, PAD_MMODE_LOCK);", inputSource, StringComparison.Ordinal);
        Assert.Contains("gamepad.set_LeftStickX(CurrentButtons.LeftStickX);", inputSource, StringComparison.Ordinal);
        Assert.Contains("gamepad.set_LeftStickY(CurrentButtons.LeftStickY);", inputSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 native runtime exposes one custom file-system bridge that maps rooted cooked logical paths onto the generated disc-layout manifest before delegating to file-stream reads.
    /// </summary>
    [Fact]
    public void Ps2_runtime_custom_file_system_resolves_rooted_cooked_paths_through_the_generated_manifest() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string makefile = File.ReadAllText(Path.Combine(repositoryRootPath, "Makefile"));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2DiscFileSystem.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2DiscFileSystem.cpp"));

        Assert.Contains("$(SOURCE_DIR)/platform/ps2/Ps2DiscFileSystem.cpp", makefile, StringComparison.Ordinal);
        Assert.Contains("class Ps2DiscFileSystem final", header, StringComparison.Ordinal);
        Assert.Contains("static bool CanHandlePath(const char* path);", header, StringComparison.Ordinal);
        Assert.Contains("static bool Exists(const char* path);", header, StringComparison.Ordinal);
        Assert.Contains("static FileStream* OpenRead(const char* path);", header, StringComparison.Ordinal);
        Assert.Contains("#include \"runtime/runtime_ps2_asset_path_manifest.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("return path.rfind(\"/cooked/\", 0) == 0;", source, StringComparison.Ordinal);
        Assert.Contains("const char* physicalPath = he_get_runtime_ps2_asset_physical_path(logicalPath);", source, StringComparison.Ordinal);
        Assert.Contains("return new FileStream(resolvedPhysicalPath, FileMode::Open, FileAccess::Read, FileShare::Read);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the native boot host receives its mandatory content stream source from the PS2 disc runtime instead of a host-only generated class.
    /// </summary>
    [Fact]
    public void Ps2_boot_host_uses_the_native_disc_content_stream_source() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string makefile = File.ReadAllText(Path.Combine(repositoryRootPath, "Makefile"));
        string bootHostSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp"));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2DiscContentStreamSource.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2DiscContentStreamSource.cpp"));

        Assert.Contains("$(SOURCE_DIR)/platform/ps2/Ps2DiscContentStreamSource.cpp", makefile, StringComparison.Ordinal);
        Assert.Contains("#include \"platform/ps2/Ps2DiscContentStreamSource.hpp\"", bootHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HostFileSystemContentStreamSource", bootHostSource, StringComparison.Ordinal);
        Assert.Contains("class Ps2DiscContentStreamSource final : public ::IContentStreamSource", header, StringComparison.Ordinal);
        Assert.Contains("::Stream* OpenRead(std::string assetPath) override;", header, StringComparison.Ordinal);
        Assert.Contains("#include \"system/io/file-stream.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("return Ps2DiscFileSystem::OpenRead(assetPath.c_str());", source, StringComparison.Ordinal);
        Assert.Contains("new helengine::ps2::Ps2DiscContentStreamSource()", bootHostSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 runtime model exposes embedded VU packed geometry loaded directly from the single-file PS2 cooked model asset payload.
    /// </summary>
    [Fact]
    public void Ps2_runtime_model_exposes_vu_packed_geometry_for_fast_path_loading() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RuntimeModel.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RuntimeModel.cpp"));

        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuPackedModel.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("void LoadFromCooked(::Ps2ModelAsset* modelAsset);", header, StringComparison.Ordinal);
        Assert.Contains("const Ps2VuPackedModel* GetVuPackedModel() const;", header, StringComparison.Ordinal);
        Assert.Contains("Ps2VuPackedModel* VuPackedModel;", header, StringComparison.Ordinal);
        Assert.Contains("VuPackedModel = new Ps2VuPackedModel();", source, StringComparison.Ordinal);
        Assert.Contains("modelAsset->PackedMeshBytes", source, StringComparison.Ordinal);
        Assert.Contains("VuPackedModel->LoadFromPackedBytes(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("            return;\r\n        }\r\n\r\n        if (modelAsset->Indices32 != nullptr && modelAsset->Indices32->Length > 0) {", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the packed VU runtime model parses the embedded triangle-stream header and exposes section counts and qword-aligned section pointers for packet assembly.
    /// </summary>
    [Fact]
    public void Ps2_vu_packed_model_parses_triangle_stream_header_and_exposes_section_accessors() {
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuPackedModel.hpp");
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuPackedModel.cpp");

        Assert.Contains("std::uint32_t GetTriangleVertexCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("const std::uint8_t* GetPositionBlockBytes() const;", header, StringComparison.Ordinal);
        Assert.Contains("const std::uint8_t* GetTexCoordBlockBytes() const;", header, StringComparison.Ordinal);
        Assert.Contains("TriangleVertexCount = ReadUInt32(4);", source, StringComparison.Ordinal);
        Assert.Contains("PositionBlockOffsetQwords = ReadUInt32(8);", source, StringComparison.Ordinal);
        Assert.Contains("TexCoordBlockOffsetQwords = ReadUInt32(16);", source, StringComparison.Ordinal);
        Assert.Contains("return PackedBytes.data() + (PositionBlockOffsetQwords * 16u);", source, StringComparison.Ordinal);
        Assert.Contains("return PackedBytes.data() + (TexCoordBlockOffsetQwords * 16u);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer header declares the VU opaque batch and packet infrastructure required by the fast path.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_declares_vu_opaque_batch_and_packet_infrastructure() {
        string rendererHeader = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");

        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.hpp\"", rendererHeader, StringComparison.Ordinal);
        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuProgramRegistry.hpp\"", rendererHeader, StringComparison.Ordinal);
        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp\"", rendererHeader, StringComparison.Ordinal);
        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuGifStateEncoder.hpp\"", rendererHeader, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer routes opaque draws through a VU path while retaining the current CPU path behind an explicit internal fallback gate.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_routes_opaque_draws_through_vu_path_while_retaining_cpu_fallback() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("RenderOpaqueWithVuPath(", source, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxyLegacy(", source, StringComparison.Ordinal);
        Assert.Contains("if (UseLegacyCpuOpaquePath)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the VU opaque batch builder emits per-proxy batches from the opaque frame-plan lists only when a packed VU model and runtime material are available.
    /// </summary>
    [Fact]
    public void Ps2_vu_opaque_batch_builder_emits_batches_for_opaque_proxies_with_packed_models() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueBatchBuilder.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueBatchBuilder.cpp"));

        Assert.Contains("std::size_t GetLastRejectedMissingMaterialCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastRejectedMissingModelCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastRejectedMissingPackedModelCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("mutable std::size_t LastRejectedMissingMaterialCount = 0;", header, StringComparison.Ordinal);
        Assert.Contains("mutable std::size_t LastRejectedMissingModelCount = 0;", header, StringComparison.Ordinal);
        Assert.Contains("mutable std::size_t LastRejectedMissingPackedModelCount = 0;", header, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingMaterialCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingModelCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingPackedModelCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("AppendProxyBatches(plan.OpaqueWorld, batches);", source, StringComparison.Ordinal);
        Assert.Contains("AppendProxyBatches(plan.OpaqueDynamic, batches);", source, StringComparison.Ordinal);
        Assert.Contains("proxy->GetMaterial()", source, StringComparison.Ordinal);
        Assert.Contains("proxy->GetModel()", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingMaterialCount += 1;", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingModelCount += 1;", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingPackedModelCount += 1;", source, StringComparison.Ordinal);
        Assert.Contains("runtimeModel->GetVuPackedModel()", source, StringComparison.Ordinal);
        Assert.Contains("batch.Proxy = proxy;", source, StringComparison.Ordinal);
        Assert.Contains("batch.Model = packedModel;", source, StringComparison.Ordinal);
        Assert.Contains("batch.Material = runtimeMaterial;", source, StringComparison.Ordinal);
        Assert.Contains("batch.Textured = runtimeMaterial->HasTextureRelativePath();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("batch.Textured = !runtimeMaterial->GetTextureRelativePath().empty();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 VU packet builder assembles a real VIF chain packet from the packed triangle stream and a
    /// per-batch local-screen transform instead of remaining as a placeholder.
    /// </summary>
    [Fact]
    public void Ps2_vu_vif_packet_builder_assembles_local_screen_and_triangle_stream_packet_data() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("std::uint32_t GetLastCompletedPhase() const;", header, StringComparison.Ordinal);
        Assert.Contains("~Ps2VuVifPacketBuilder();", header, StringComparison.Ordinal);
        Assert.Contains("packet2_t* GetPacket() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetPacketByteCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("packet2_t* Packet = nullptr;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t LastCompletedPhase = 0;", header, StringComparison.Ordinal);
        Assert.Contains("const ::float4& viewport, float nearPlaneDistance, const ::float3& lightDirection, GSGLOBAL* gsGlobal, int textureWidth, int textureHeight", header, StringComparison.Ordinal);
        Assert.Contains("#include <packet2.h>", source, StringComparison.Ordinal);
        Assert.Contains("#include <packet2_utils.h>", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint32_t EnableVuPacketPhaseDiagnostics = 0;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint32_t VuPacketDiagnosticCutoffPhase = 11;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint32_t XtopGifPacketAddress = 0;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableVuFixedTriangleDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("constexpr bool EnableVuTwoTriangleBatchDiagnostic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("constexpr std::uint32_t VuDiagnosticBatchTriangleCount = 2u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t TriangleGifPacketTemplateQwordCount = 11u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t LitTrianglePayloadQwordCount = sizeof(Ps2VuLitTrianglePayload) / 16u;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 1;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 2;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 3;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 4;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 5;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 6;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 9;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 10;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 11;", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {", source, StringComparison.Ordinal);
        Assert.Contains("packet2_create(", source, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_open_unpack(", source, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_close_unpack(", source, StringComparison.Ordinal);
        Assert.Contains("batch.Model->GetTriangleVertexCount()", source, StringComparison.Ordinal);
        Assert.Contains("batch.Model->GetPositionBlockBytes()", source, StringComparison.Ordinal);
        Assert.Contains("struct alignas(16) Ps2VuLitTrianglePayload", source, StringComparison.Ordinal);
        Assert.Contains("std::memcpy(payload.FaceNormal, triangleSetup.FaceNormal, sizeof(triangleSetup.FaceNormal));", source, StringComparison.Ordinal);
        Assert.Contains("TryBuildVertexPositionRegister(", source, StringComparison.Ordinal);
        Assert.Contains("GifPacketBytes.resize(TriangleGifPacketTemplateByteCount);", source, StringComparison.Ordinal);
        Assert.Contains("std::memcpy(GifPacketBytes.data(), trianglePayloads.front().GifPacketTemplate, TriangleGifPacketTemplateByteCount);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildUntexturedTriangleGifPacketBytes(", source, StringComparison.Ordinal);
        Assert.Contains("GetTexCoordBlockBytes()", source, StringComparison.Ordinal);
        Assert.Contains("packet2_get_qw_count(", source, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_mscal(packet.get(), UntexturedMicroProgramAddress, 0);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\n            PacketBytes.resize(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\n            std::memcpy(PacketBytes.data()", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer computes a combined view-projection matrix once per frame and a per-proxy world matrix
    /// before feeding opaque batches into the VU packet builder.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_computes_view_projection_and_world_matrices_for_vu_batches() {
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("::float4x4 BuildWorldMatrix(const Ps2RenderProxy& proxy) const;", header, StringComparison.Ordinal);
        Assert.Contains("const int2 windowSize = get_MainWindowSize();", source, StringComparison.Ordinal);
        Assert.Contains("::float4 viewport = ResolvePixelViewport(camera, windowSize);", source, StringComparison.Ordinal);
        Assert.Contains("::float4x4 world = BuildWorldMatrix(*batch.Proxy);", source, StringComparison.Ordinal);
        Assert.Contains("VuVifPacketBuilder.AddOpaqueBatch(", source, StringComparison.Ordinal);
        Assert.Contains(
            "VuVifPacketBuilder.AddOpaqueBatch(\n                batch,\n                world,\n                view,\n                projection,\n                viewport,\n                nearPlaneDistance,\n                lightDirection,\n                GsGlobal,\n                0,\n                0);",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer stages the assembled VU packet into packet2 memory and dispatches it over VIF1 when
    /// the fast opaque path is active.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_dispatches_assembled_vu_packets_over_vif1() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");

        Assert.Contains("#include <dma.h>", source, StringComparison.Ordinal);
        Assert.Contains("VuVifPacketBuilder.GetPacket()", source, StringComparison.Ordinal);
        Assert.Contains("VuVifPacketBuilder.GetPacketByteCount()", source, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuBatchDispatchCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuTriangleVertexCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuPacketByteCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuRejectedMissingMaterialCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuRejectedMissingModelCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuRejectedMissingPackedModelCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t GetLastVuPacketPhase() const;", header, StringComparison.Ordinal);
        Assert.Contains("LastVuTriangleVertexCount += static_cast<std::size_t>(batch.Model->GetTriangleVertexCount());", source, StringComparison.Ordinal);
        Assert.Contains("LastVuPacketByteCount += VuVifPacketBuilder.GetPacketByteCount();", source, StringComparison.Ordinal);
        Assert.Contains("LastVuRejectedMissingMaterialCount = VuOpaqueBatchBuilder.GetLastRejectedMissingMaterialCount();", source, StringComparison.Ordinal);
        Assert.Contains("LastVuRejectedMissingModelCount = VuOpaqueBatchBuilder.GetLastRejectedMissingModelCount();", source, StringComparison.Ordinal);
        Assert.Contains("LastVuRejectedMissingPackedModelCount = VuOpaqueBatchBuilder.GetLastRejectedMissingPackedModelCount();", source, StringComparison.Ordinal);
        Assert.Contains("LastVuPacketPhase = VuVifPacketBuilder.GetLastCompletedPhase();", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_wait(DMA_CHANNEL_VIF1, 0);", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("std::memcpy(vifPacket->base", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the checked-in PS2 renderer defaults opaque draws to the VU path.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_defaults_opaque_runtime_path_to_vu() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("UseLegacyCpuOpaquePath(false)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 native build includes the new VU opaque renderer source files.
    /// </summary>
    [Fact]
    public void Ps2_makefile_compiles_vu_opaque_renderer_units() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\Makefile");

        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuProgramRegistry.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 native build includes one assembled VU1 opaque microprogram source for the new renderer path.
    /// </summary>
    [Fact]
    public void Ps2_makefile_builds_vu_opaque_microprogram_object() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\Makefile");
        string microProgramPath = @"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\programs\Ps2OpaqueDraw3D.vsm";

        Assert.True(File.Exists(microProgramPath));
        Assert.Contains("EE_DVP := dvp-as", source, StringComparison.Ordinal);
        Assert.Contains("$(BUILD_DIR)/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.o", source, StringComparison.Ordinal);
        Assert.Contains("$(BUILD_DIR)/platform/ps2/rendering/vu/programs/%.o: $(SOURCE_DIR)/platform/ps2/rendering/vu/programs/%.vsm", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host uploads the VU opaque microprogram and configures VU1 double buffering before rendering begins.
    /// </summary>
    [Fact]
    public void Boot_host_uploads_vu_opaque_microprogram_and_initializes_vif1_double_buffering() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("#include <dma.h>", source, StringComparison.Ordinal);
        Assert.Contains("#include <packet2.h>", source, StringComparison.Ordinal);
        Assert.Contains("#include <packet2_utils.h>", source, StringComparison.Ordinal);
        Assert.Contains("extern u32 Ps2OpaqueDraw3D_CodeStart", source, StringComparison.Ordinal);
        Assert.Contains("extern u32 Ps2OpaqueDraw3D_CodeEnd", source, StringComparison.Ordinal);
        Assert.Contains("extern u32 Ps2OpaqueTexturedDraw3D_CodeStart", source, StringComparison.Ordinal);
        Assert.Contains("extern u32 Ps2OpaqueTexturedDraw3D_CodeEnd", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_initialize(DMA_CHANNEL_VIF1, NULL, 0);", source, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_add_micro_program(", source, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_add_micro_program(packet2, 0, &Ps2OpaqueDraw3D_CodeStart, &Ps2OpaqueDraw3D_CodeEnd);", source, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_add_micro_program(packet2, 64, &Ps2OpaqueTexturedDraw3D_CodeStart, &Ps2OpaqueTexturedDraw3D_CodeEnd);", source, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_add_double_buffer(", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_send_packet2(", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_wait(DMA_CHANNEL_VIF1, 0);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host initializes and waits for the CD/DVD subsystem before runtime asset loading begins.
    /// </summary>
    [Fact]
    public void Boot_host_initializes_cdvd_before_runtime_asset_loading() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("#include <libcdvd.h>", source, StringComparison.Ordinal);
        Assert.Contains("constexpr const char* CubeModelDiagnosticPath = \"cdrom0:\\\\COOKED\\\\ENGINE\\\\MODELS\\\\CUBE.HAS;1\";", source, StringComparison.Ordinal);
        Assert.Contains("constexpr const char* CubeMaterialEarlyDiagnosticPath = \"cdrom0:\\\\COOKED\\\\ENGINE\\\\MAT\\\\CUBE00\\\\CUBE00.HAS;1\";", source, StringComparison.Ordinal);
        Assert.Contains("constexpr const char* CubeMaterialLateDiagnosticPath = \"cdrom0:\\\\COOKED\\\\ENGINE\\\\MAT\\\\CUBE14\\\\CUBE14.HAS;1\";", source, StringComparison.Ordinal);
        Assert.Contains("void BootLogDiscProbe(const char* label, const char* path)", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(\"cdvd init begin\");", source, StringComparison.Ordinal);
        Assert.Contains("sceCdInit(SCECdINoD);", source, StringComparison.Ordinal);
        Assert.Contains("sceCdDiskReady(0);", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(\"cdvd ready\");", source, StringComparison.Ordinal);
        Assert.Contains("std::FILE* directFile = std::fopen(path, \"rb\");", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(std::string(label) + \": fopen=\" + (directFile != nullptr ? \"true\" : \"false\"));", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube model\", CubeModelDiagnosticPath);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube material early\", CubeMaterialEarlyDiagnosticPath);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube material late\", CubeMaterialLateDiagnosticPath);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host applies the engine's PS2 framebuffer defaults before gsKit initializes the screen.
    /// </summary>
    [Fact]
    public void Boot_host_when_graphics_initialize_applies_ps2_framebuffer_defaults_before_screen_init() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("constexpr int Ps2DefaultFramebufferWidth = 640;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr int Ps2DefaultFramebufferHeight = 448;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Interlace = GS_INTERLACED;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Field = GS_FIELD;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->DoubleBuffering = GS_SETTING_ON;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Aspect = GS_ASPECT_4_3;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Width = Ps2DefaultFramebufferWidth;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Height = Ps2DefaultFramebufferHeight;", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_init_screen(GsGlobal);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host publishes the configured GS framebuffer size to the shared render manager.
    /// </summary>
    [Fact]
    public void Boot_host_when_graphics_initialize_publishes_gs_backbuffer_size_to_render_manager() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("EngineRenderManager3D->AddWindow(", source, StringComparison.Ordinal);
        Assert.Contains("static_cast<int32_t>(GsGlobal->Width)", source, StringComparison.Ordinal);
        Assert.Contains("static_cast<int32_t>(GsGlobal->Height)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the current cube display-path diagnostic can bypass 3D submission and draw a plain 2D sprite test rectangle.
    /// </summary>
    [Fact]
    public void Boot_host_supports_cube_sprite_display_diagnostic_frame() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("constexpr bool EnableCubeSpriteDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeSpriteDiagnosticLeft = 211.843231f;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeSpriteDiagnosticTop = 115.843239f;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeSpriteDiagnosticRight = 428.156738f;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeSpriteDiagnosticBottom = 332.156738f;", source, StringComparison.Ordinal);
        Assert.Contains("void DrawCubeSpriteDiagnosticsFrame(GSGLOBAL* gsGlobal)", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_sprite(", source, StringComparison.Ordinal);
        Assert.Contains("cube sprite diagnostic halt", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the current cube display-path diagnostic can draw the measured cube face as two plain 2D triangles.
    /// </summary>
    [Fact]
    public void Boot_host_supports_cube_two_triangle_2d_diagnostic_frame() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("constexpr bool EnableCubeTriangle2dDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("void DrawCubeTriangle2dDiagnosticsFrame(GSGLOBAL* gsGlobal)", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_triangle_gouraud(", source, StringComparison.Ordinal);
        Assert.Contains("CubeTriangle2dVertexA0X", source, StringComparison.Ordinal);
        Assert.Contains("CubeTriangle2dVertexB2Y", source, StringComparison.Ordinal);
        Assert.Contains("cube triangle 2d diagnostic halt", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the current cube diagnostic can submit the measured cube face through the 3D triangle API with fixed screen-space coordinates and depth.
    /// </summary>
    [Fact]
    public void Boot_host_supports_cube_two_triangle_3d_diagnostic_frame() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");
        int callPosition = source.IndexOf("gsKit_prim_triangle_gouraud_3d(", StringComparison.Ordinal);
        int firstVertexPosition = source.IndexOf("CubeTriangle2dVertexA0X, CubeTriangle2dVertexA0Y, CubeTriangle3dDiagnosticDepth,", callPosition, StringComparison.Ordinal);
        int lastVertexPosition = source.IndexOf("CubeTriangle2dVertexA2X, CubeTriangle2dVertexA2Y, CubeTriangle3dDiagnosticDepth,", callPosition, StringComparison.Ordinal);
        int firstColorPosition = source.IndexOf("darkerRed);", lastVertexPosition, StringComparison.Ordinal);

        Assert.Contains("constexpr bool EnableCubeTriangle3dDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeTriangle3dDiagnosticDepth = 1.0f;", source, StringComparison.Ordinal);
        Assert.Contains("void DrawCubeTriangle3dDiagnosticsFrame(GSGLOBAL* gsGlobal)", source, StringComparison.Ordinal);
        Assert.True(callPosition >= 0);
        Assert.True(firstVertexPosition >= 0);
        Assert.True(lastVertexPosition >= 0);
        Assert.True(firstColorPosition > lastVertexPosition);
        Assert.Contains("cube triangle 3d diagnostic halt", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 3D renderer resolves authored camera viewports into pixel bounds before projection and rasterization.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_resolves_camera_viewport_to_pixels_before_rendering() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("ResolvePixelViewport(camera, windowSize)", source, StringComparison.Ordinal);
        Assert.Contains("const int2 windowSize = get_MainWindowSize();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer submits untextured 3D triangles using gsKit's required vertex-first, color-last argument order.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_submits_untextured_triangles_with_vertex_first_color_last_argument_order() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");
        int screenVertexPosition = source.IndexOf("screenAX, screenAY, screenAZ,", StringComparison.Ordinal);
        int screenColorPosition = source.IndexOf("clippedColorA, clippedColorB, clippedColorC);", StringComparison.Ordinal);
        int glowVertexPosition = source.IndexOf("glowAX, glowAY, glowAZ,", StringComparison.Ordinal);
        int glowColorPosition = source.IndexOf("glowColorA, glowColorB, glowColorC);", StringComparison.Ordinal);

        Assert.True(screenVertexPosition >= 0);
        Assert.True(screenColorPosition > screenVertexPosition);
        Assert.True(glowVertexPosition >= 0);
        Assert.True(glowColorPosition > glowVertexPosition);
        Assert.DoesNotContain("screenAX, screenAY, screenAZ, clippedColorA,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("glowAX, glowAY, glowAZ, glowColorA,", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures temporary cube runtime diagnostics can be disabled so the cube scene runs normally on the fixed renderer path.
    /// </summary>
    [Fact]
    public void Boot_host_allows_cube_runtime_diagnostics_to_be_disabled_for_normal_scene_execution() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("constexpr bool EnableCubeRuntimeDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableCubeRuntimeDiagnostics && !CubeDiagnosticsShown)", source, StringComparison.Ordinal);
        Assert.Contains("\"cube runtime counts: proxies=\"", source, StringComparison.Ordinal);
        Assert.Contains("\"cube runtime rejects: missingMaterial=\"", source, StringComparison.Ordinal);
        Assert.Contains("\"cube runtime checkpoint: after draw phase=\"", source, StringComparison.Ordinal);
        Assert.Contains("\"cube draw returned: viewport=\"", source, StringComparison.Ordinal);
        Assert.Contains("\"cube draw returned: triB0=\"", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host can emit averaged frame-phase timing diagnostics from the real runtime loop when performance investigation is needed.
    /// </summary>
    [Fact]
    public void Boot_host_supports_frame_timing_diagnostics_for_update_draw_and_present() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("#include <ctime>", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableFrameTimingDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableFrameTimingDiagnosticHalt = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr int FrameTimingSampleFrameCount = 60;", source, StringComparison.Ordinal);
        Assert.Contains("double ResolveSecondsFromClockTicks(std::clock_t startTicks, std::clock_t endTicks)", source, StringComparison.Ordinal);
        Assert.Contains("void RecordFrameTimingSample(", source, StringComparison.Ordinal);
        Assert.Contains("double updateSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double draw3dSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double gifWaitSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double draw2dSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double drawSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double presentSeconds)", source, StringComparison.Ordinal);
        Assert.Contains("\"frame timing avg updateMs=\"", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingSampleCompleted = true;", source, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t frameUpdateStartTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("frameUpdateEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("frameDraw3dEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("frameGifWaitEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("frameDrawEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("framePresentEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("RecordFrameTimingSample(", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableFrameTimingDiagnostics &&", source, StringComparison.Ordinal);
        Assert.Contains("EnableFrameTimingDiagnosticHalt &&", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingSampleCompleted &&", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 screen-space front-face test preserves the engine's counter-clockwise mesh winding after viewport projection flips Y downward.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_treats_negative_screen_space_signed_area_as_front_facing() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("return signedArea < 0.0f;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return signedArea > 0.0f;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 near-plane clipper treats negative view-space Z as in front of the camera, matching the shared look-at matrix convention.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_clips_against_negative_view_space_near_plane() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("const float nearPlaneZ = -nearPlaneDistance;", source, StringComparison.Ordinal);
        Assert.Contains("bool previousInside = previous.ViewPosition.Z <= nearPlaneZ;", source, StringComparison.Ordinal);
        Assert.Contains("bool currentInside = current.ViewPosition.Z <= nearPlaneZ;", source, StringComparison.Ordinal);
        Assert.Contains("const float amount = (nearPlaneZ - previous.ViewPosition.Z) / denominator;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("previous.ViewPosition.Z >= nearPlaneDistance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("current.ViewPosition.Z >= nearPlaneDistance", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer applies the drawable parent's authored scale and orientation before camera-space projection.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_applies_parent_scale_and_orientation_to_model_vertices() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("::float4 parentOrientation = parent->get_Orientation();", source, StringComparison.Ordinal);
        Assert.Contains("::float3 parentScale = parent->get_Scale();", source, StringComparison.Ordinal);
        Assert.Contains("::float3 localPositionA = ::float3(", source, StringComparison.Ordinal);
        Assert.Contains("positionA = ::float4::RotateVector(localPositionA, parentOrientation) + parentPosition;", source, StringComparison.Ordinal);
        Assert.Contains("normalA = indexA < normals.size() ? ::float4::RotateVector(normals[indexA], parentOrientation) : ::float3::get_Zero();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::float3 positionA = positions[indexA] + parentPosition;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 runtime source retains the renderer-side triangle diagnostics used during malformed 3D scene debugging.
    /// </summary>
    [Fact]
    public void Ps2_runtime_renderer_exposes_triangle_stage_diagnostics_for_3d_submission() {
        string rendererHeaderSource = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");

        Assert.Contains("std::size_t GetLastClipRejectCount() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastProjectionRejectCount() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastCullRejectCount() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastSubmittedTriangleCount() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedScreenBounds() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleBoundsA() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleBoundsB() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexA0() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexA1() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexA2() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexB0() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexB1() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexB2() const;", rendererHeaderSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer can force one flat-color diagnostic mode that bypasses textures, material alpha state, lighting, and HDR glow.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_supports_flat_color_diagnostic_submission_mode() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("constexpr bool EnableFlatColorDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableLightingOnlyDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("ResolveDiagnosticProxyColor(proxy)", source, StringComparison.Ordinal);
        Assert.Contains("const bool useDiagnosticFlatColor = EnableFlatColorDiagnostics;", source, StringComparison.Ordinal);
        Assert.Contains("const bool useLightingOnlyDiagnostics = EnableLightingOnlyDiagnostics;", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor) {", source, StringComparison.Ordinal);
        Assert.Contains("ApplyMaterialAlphaState(*material);", source, StringComparison.Ordinal);
        Assert.Contains("GSTEXTURE* texture = nullptr;", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !useLightingOnlyDiagnostics && !material->GetTextureRelativePath().empty()) {", source, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t diagnosticColor = ResolveDiagnosticProxyColor(proxy);", source, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t colorA = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalA, lightDirection);", source, StringComparison.Ordinal);
        Assert.Contains("const bool useTexture = !useDiagnosticFlatColor", source, StringComparison.Ordinal);
        Assert.Contains("&& !useLightingOnlyDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !ShouldDrawAlphaTestTriangle(", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !useLightingOnlyDiagnostics && HdrEnabled && ShouldEmitHdrGlow(*material, clippedColorA, clippedColorB, clippedColorC)) {", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_triangle_goraud_texture_3d(", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_triangle_gouraud_3d(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer converts normalized mesh UVs into texel-space coordinates before submitting textured triangles to gsKit.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_scales_normalized_uvs_into_gskit_texel_space() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("::float2 ResolveGsTextureCoordinate(const ::float2& normalizedTexCoord, const GSTEXTURE* texture)", source, StringComparison.Ordinal);
        Assert.Contains("normalizedTexCoord.X * static_cast<float>(texture->Width)", source, StringComparison.Ordinal);
        Assert.Contains("normalizedTexCoord.Y * static_cast<float>(texture->Height)", source, StringComparison.Ordinal);
        Assert.Contains("const ::float2 screenTexCoordA = ResolveGsTextureCoordinate(clippedA.TexCoord, texture);", source, StringComparison.Ordinal);
        Assert.Contains("const ::float2 glowTexCoordA = ResolveGsTextureCoordinate(triangle.TexCoordA, triangle.Texture);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("screenAX, screenAY, screenAZ, clippedA.TexCoord.X, clippedA.TexCoord.Y,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("glowAX, glowAY, glowAZ, triangle.TexCoordA.X, triangle.TexCoordA.Y,", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer resolves lit vertex colors from the authored directional light before falling back to the diagnostic light vector.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_uses_scene_directional_light_for_vertex_lighting() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");

        Assert.Contains("#include \"DirectionalLightComponent.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("TryResolveDirectionalLightDirection(lightDirection);", source, StringComparison.Ordinal);
        Assert.Contains("dynamic_cast<::DirectionalLightComponent*>(component)", source, StringComparison.Ordinal);
        Assert.Contains("std::uint64_t Ps2RenderManager3D::ResolveVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal, const ::float3& lightDirection)", source, StringComparison.Ordinal);
        Assert.Contains("bool TryResolveDirectionalLightDirection(::float3& lightDirection) const;", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 lit shading path modulates directional-light intensity by the cooked authored base-color channels.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_modulates_lighting_by_cooked_base_color() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RuntimeMaterial.hpp");

        Assert.Contains("material.GetBaseColorR()", source, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorG()", source, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorB()", source, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorA()", source, StringComparison.Ordinal);
        Assert.Contains("std::uint8_t GetBaseColorR() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint8_t GetBaseColorG() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint8_t GetBaseColorB() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint8_t GetBaseColorA() const;", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer does not leave the single-proxy diagnostic clamp enabled for normal exports.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_disables_single_proxy_diagnostic_submission_mode_for_normal_exports() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("constexpr bool EnableSingleProxyDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t SingleProxyDiagnosticIndex = 1;", source, StringComparison.Ordinal);
        Assert.Contains("ResolveRenderableProxyByIndex(const helengine::ps2::Ps2FramePlan& plan, std::size_t proxyIndex)", source, StringComparison.Ordinal);
        Assert.Contains("const Ps2RenderProxy* firstProxy = ResolveRenderableProxyByIndex(plan, SingleProxyDiagnosticIndex);", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableSingleProxyDiagnostics) {", source, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxyLegacy(*firstProxy, view, projection, viewport, camera->get_NearPlaneDistance());", source, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxyLegacy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());", source, StringComparison.Ordinal);
    }
}
