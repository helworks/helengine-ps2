using helengine.baseplatform.Manifest;
using helengine.editor;

namespace helengine.ps2.builder;

/// <summary>Retains a preflighted texture cook and its prepared bytes until the complete batch can be published.</summary>
internal sealed class Ps2TextureCookWorkPlan {
    /// <summary>Identifies the source, settings and logical identity supplied by the editor.</summary>
    public PlatformCookWorkItem WorkItem { get; init; }
    /// <summary>Retains settings that passed format, alpha and quantizer validation.</summary>
    public TextureAssetProcessorSettings Settings { get; init; }
    /// <summary>Stores the contained destination in the PS2 staging tree.</summary>
    public string DestinationPath { get; init; }
    /// <summary>Retains the exact staging root used to recheck containment immediately before publication.</summary>
    public string StagingRootPath { get; init; }
    /// <summary>Holds serialized bytes only after the source decode and native cook succeeded.</summary>
    public byte[] Payload { get; set; }
}
