namespace helengine.ps2.builder;

/// <summary>
/// Computes the axis-aligned bounds of PS2-owned model positions, which the PS2 cooked model format does not store
/// but the desktop player needs for culling.
/// </summary>
public static class Ps2PlayerModelBounds {
    /// <summary>
    /// Computes the per-axis minimum and maximum of the supplied vertex positions.
    /// </summary>
    /// <param name="positions">Model vertex positions; must contain at least one vertex.</param>
    /// <param name="boundsMin">Receives the per-axis minimum.</param>
    /// <param name="boundsMax">Receives the per-axis maximum.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="positions"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="positions"/> is empty.</exception>
    public static void Compute(float3[] positions, out float3 boundsMin, out float3 boundsMax) {
        if (positions == null) {
            throw new ArgumentNullException(nameof(positions));
        } else if (positions.Length == 0) {
            throw new ArgumentException("Model bounds require at least one vertex position.", nameof(positions));
        }

        boundsMin = positions[0];
        boundsMax = positions[0];
        for (int positionIndex = 1; positionIndex < positions.Length; positionIndex++) {
            float3 position = positions[positionIndex];
            boundsMin = new float3(
                RealMath.Min(boundsMin.X, position.X),
                RealMath.Min(boundsMin.Y, position.Y),
                RealMath.Min(boundsMin.Z, position.Z));
            boundsMax = new float3(
                RealMath.Max(boundsMax.X, position.X),
                RealMath.Max(boundsMax.Y, position.Y),
                RealMath.Max(boundsMax.Z, position.Z));
        }
    }
}
