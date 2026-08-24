namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that authored directional-light color and intensity reach every PS2 lit-material submission path.
/// </summary>
public sealed class Ps2DirectionalLightColorSourceTests {
    /// <summary>
    /// Ensures the renderer resolves directional-light radiance from the live component and supplies it beside direction to every packet path.
    /// </summary>
    [Fact]
    public void RendererCarriesDirectionalLightRadianceIntoEveryLightingPath() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string rendererHeader = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.hpp"));
        string rendererSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));
        string packetBuilderHeader = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp"));

        Assert.Contains("TryResolveDirectionalLightState(lightDirection, lightColor);", rendererSource, StringComparison.Ordinal);
        Assert.Contains("const ::float4 authoredLightColor = directionalLight->get_Color();", rendererSource, StringComparison.Ordinal);
        Assert.Contains("const float authoredLightIntensity = directionalLight->get_Intensity();", rendererSource, StringComparison.Ordinal);
        Assert.Contains("std::clamp(authoredLightColor.X * authoredLightIntensity, 0.0f, 1.0f)", rendererSource, StringComparison.Ordinal);
        Assert.Contains("bool TryResolveDirectionalLightState(::float3& lightDirection, ::float3& lightColor) const;", rendererHeader, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(packetBuilderHeader, "const ::float3& lightColor"));
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(rendererSource, @"lightDirection,\r?\n\s+lightColor,").Count);
        Assert.Contains("applyIntensity(material.GetBaseColorR(), lightColor.X)", rendererSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures CPU/direct-GIF lighting and textured VU shared state tint material RGB with the resolved directional-light radiance.
    /// </summary>
    [Fact]
    public void PacketBuilderAppliesDirectionalLightRadianceToLitMaterialColor() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("ResolveTexturedVertexColor(lightingConstants, worldFaceNormal, normalizedLightDirection, lightColor)", source, StringComparison.Ordinal);
        Assert.Contains("cachedSharedState.MaterialLighting[0] = (static_cast<float>(lightingConstants.BaseColorR) / 255.0f) * resolvedLightColor.X;", source, StringComparison.Ordinal);
        Assert.Contains("cachedSharedState.MaterialLighting[1] = (static_cast<float>(lightingConstants.BaseColorG) / 255.0f) * resolvedLightColor.Y;", source, StringComparison.Ordinal);
        Assert.Contains("cachedSharedState.MaterialLighting[2] = (static_cast<float>(lightingConstants.BaseColorB) / 255.0f) * resolvedLightColor.Z;", source, StringComparison.Ordinal);
        Assert.Contains("const ::float3 resolvedLightColor = lightingConstants.Unlit ? WhiteLightColor : lightColor;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the repository root from the builder-test output directory.
    /// </summary>
    /// <returns>Absolute repository root used to locate native PS2 renderer sources.</returns>
    static string ResolveRepositoryRoot() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    /// <summary>
    /// Counts exact non-overlapping occurrences of one source fragment.
    /// </summary>
    /// <param name="source">Source text to search.</param>
    /// <param name="fragment">Exact fragment whose occurrences should be counted.</param>
    /// <returns>Number of exact non-overlapping fragment occurrences.</returns>
    static int CountOccurrences(string source, string fragment) {
        int count = 0;
        int searchIndex = 0;
        while ((searchIndex = source.IndexOf(fragment, searchIndex, StringComparison.Ordinal)) >= 0) {
            count++;
            searchIndex += fragment.Length;
        }

        return count;
    }
}
