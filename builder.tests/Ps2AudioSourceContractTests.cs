namespace helengine.ps2.builder.tests;

/// <summary>
/// Guards the native PS2 audio wiring contract across the boot host, backend source, and Makefile.
/// </summary>
public sealed class Ps2AudioSourceContractTests {
    /// <summary>
    /// Ensures the PS2 boot host constructs the AUDSRV backend, attaches it to generated core, and compiles the embedded IRX bridge inputs.
    /// </summary>
    [Fact]
    public void Source_contract_wires_ps2_audio_backend_into_boot_host_and_makefile() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string bootHostHeaderPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.hpp");
        string bootHostSourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp");
        string backendHeaderPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "audio", "Ps2AudioBackend.hpp");
        string backendSourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "audio", "Ps2AudioBackend.cpp");
        string audsrvEmbedPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "audio", "irx", "audsrv.irx-em");
        string libsdEmbedPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "audio", "irx", "libsd.irx-em");
        string makefilePath = Path.Combine(repositoryRootPath, "Makefile");

        Assert.True(File.Exists(bootHostHeaderPath), "Expected Ps2BootHost.hpp to exist.");
        Assert.True(File.Exists(bootHostSourcePath), "Expected Ps2BootHost.cpp to exist.");
        Assert.True(File.Exists(backendHeaderPath), "Expected Ps2AudioBackend.hpp to exist.");
        Assert.True(File.Exists(backendSourcePath), "Expected Ps2AudioBackend.cpp to exist.");
        Assert.True(File.Exists(audsrvEmbedPath), "Expected audsrv.irx-em to exist.");
        Assert.True(File.Exists(libsdEmbedPath), "Expected libsd.irx-em to exist.");
        Assert.True(File.Exists(makefilePath), "Expected Makefile to exist.");

        string bootHostHeaderSource = File.ReadAllText(bootHostHeaderPath);
        string bootHostSource = File.ReadAllText(bootHostSourcePath);
        string backendHeaderSource = File.ReadAllText(backendHeaderPath);
        string backendSource = File.ReadAllText(backendSourcePath);
        string makefileSource = File.ReadAllText(makefilePath);

        Assert.Contains("class IAudioBackend;", bootHostHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::IAudioBackend* EngineAudioBackend;", bootHostHeaderSource, StringComparison.Ordinal);
        Assert.Contains("#include \"IAudioBackend.hpp\"", bootHostSource, StringComparison.Ordinal);
        Assert.Contains("#include \"platform/ps2/audio/Ps2AudioBackend.hpp\"", bootHostSource, StringComparison.Ordinal);
        Assert.Contains("EngineAudioBackend = new Ps2AudioBackend();", bootHostSource, StringComparison.Ordinal);
        Assert.Contains("EngineCore->SetAudioBackend(EngineAudioBackend);", bootHostSource, StringComparison.Ordinal);
        Assert.Contains("class Ps2AudioBackend final : public ::IAudioBackend", backendHeaderSource, StringComparison.Ordinal);
        Assert.Contains("#include <audsrv.h>", backendSource, StringComparison.Ordinal);
        Assert.Contains("LoadEmbeddedModule(libsd_irx, size_libsd_irx, \"libsd_irx\");", backendSource, StringComparison.Ordinal);
        Assert.Contains("LoadEmbeddedModule(audsrv_irx, size_audsrv_irx, \"audsrv_irx\");", backendSource, StringComparison.Ordinal);
        Assert.Contains("audsrv_init();", backendSource, StringComparison.Ordinal);
        Assert.Contains("audsrv_set_format(&format)", backendSource, StringComparison.Ordinal);
        Assert.Contains("audsrv_play_audio(", backendSource, StringComparison.Ordinal);
        Assert.Contains("audsrv_set_volume(", backendSource, StringComparison.Ordinal);
        Assert.Contains("audsrv_stop_audio();", backendSource, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/audio/Ps2AudioBackend.cpp", makefileSource, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/audio/irx/audsrv.irx-em", makefileSource, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/audio/irx/libsd.irx-em", makefileSource, StringComparison.Ordinal);
        Assert.Contains(".incbin", makefileSource, StringComparison.Ordinal);
        Assert.Contains("-laudsrv", makefileSource, StringComparison.Ordinal);
        Assert.Contains("-lpatches", makefileSource, StringComparison.Ordinal);
    }
}
