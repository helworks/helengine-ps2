using helengine.baseplatform.Builders;
using helengine.baseplatform.Reporting;

namespace helengine.ps2.builder.tests;

public sealed class RecordingDiagnosticReporter : IPlatformBuildDiagnosticReporter {
    public List<PlatformBuildDiagnostic> Diagnostics { get; } = [];

    public void Report(PlatformBuildDiagnostic diagnostic) {
        Diagnostics.Add(diagnostic);
    }
}
