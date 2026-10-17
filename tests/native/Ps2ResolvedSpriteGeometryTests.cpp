#include "platform/ps2/rendering/Ps2TexturedQuadGeometry.hpp"
#include "platform/ps2/rendering/Ps2TexturedQuadPrimitive.hpp"
#include <cmath>
#include <stdexcept>
#include <cstdio>

/// Verifies exact signed/sheared corners and inverse affine UVs after production clipping.
int main() {
    using helengine::ps2::Ps2TexturedQuadGeometry;
    const auto polygon = Ps2TexturedQuadGeometry::CreateCorners(80, 10, 0, 30, 20, 90, 100, 70, 0.1, 0.2, 0.6, 0.5);
    if (polygon.Count != 4 || polygon.Vertices[1].X != 0 || polygon.Vertices[3].X != 100
        || polygon.Vertices[2].U != 0.7 || polygon.Vertices[2].V != 0.7) {
        throw std::runtime_error("Signed/sheared corner or UV identity changed");
    }
    const auto clipped = Ps2TexturedQuadGeometry::Clip(polygon, 10, 20, 70, 50);
    if (clipped.Count < 3) { throw std::runtime_error("Visible sheared polygon vanished"); }
    for (std::size_t i = 0; i < clipped.Count; ++i) {
        const auto& p = clipped.Vertices[i];
        const double x = p.X - 80.0, y = p.Y - 10.0;
        const double u = (60.0 * x - 20.0 * y) / -5200.0;
        const double v = (-20.0 * x - 80.0 * y) / -5200.0;
        if (std::abs(p.U - (0.1 + u * 0.6)) > 1e-8 || std::abs(p.V - (0.2 + v * 0.5)) > 1e-8) {
            throw std::runtime_error("Clipping changed signed/sheared affine UVs");
        }
    }
    const auto plan = helengine::ps2::Ps2TexturedQuadPrimitive::FromCorners(polygon, 128, 64, true, 10, 20, 70, 50);
    if (!plan.IsRotated || plan.TriangleCount() == 0 || plan.Polygon.Count != clipped.Count) {
        throw std::runtime_error("Signed/sheared sprite did not reach the clipped triangle path");
    }
    for (std::size_t i = 0; i < clipped.Count; ++i) {
        if (plan.Polygon.Vertices[i].U != clipped.Vertices[i].U * 128
            || plan.Polygon.Vertices[i].V != clipped.Vertices[i].V * 64) {
            throw std::runtime_error("GS triangle plan lost texel-space UV association");
        }
    }
    const auto aligned = Ps2TexturedQuadGeometry::CreateCorners(10, 20, 40, 20, 40, 60, 10, 60, 0, 0, 1, 1);
    if (helengine::ps2::Ps2TexturedQuadPrimitive::FromCorners(aligned, 64, 64, false).IsRotated) {
        throw std::runtime_error("Aligned sprite lost its native fast path");
    }
    std::puts("PASS exact signed/sheared corners and clipped affine UVs");
}
