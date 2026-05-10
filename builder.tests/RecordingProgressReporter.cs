using helengine.baseplatform.Builders;
using helengine.baseplatform.Reporting;

namespace helengine.ps2.builder.tests;

public sealed class RecordingProgressReporter : IPlatformBuildProgressReporter {
    public List<PlatformBuildProgressUpdate> Updates { get; } = [];

    public void Report(PlatformBuildProgressUpdate update) {
        Updates.Add(update);
    }
}
