#include "platform/ps2/rendering/Ps2TexturedQuadGeometry.hpp"
#include <cstdio>
#include <limits>
#include <stdexcept>

using helengine::ps2::Ps2TexturedPolygon;
using helengine::ps2::Ps2TexturedQuadGeometry;

/// Fails a geometric invariant immediately rather than hiding a malformed test draw.
static void Require(bool condition, const char* message) {
    if (!condition) { throw std::runtime_error(message); }
}

/// Compares unclipped double-precision geometry before the GS fixed-point conversion.
static bool Near(double first, double second) { return std::abs(first - second) < 0.00001; }

/// Verifies world-origin rotation preserves the parent's pivot and independently scaled axes.
static void CheckPivot(double angle, double scaleX, double scaleY) {
    const double width = 1536.0 * scaleX;
    const double height = 1536.0 * scaleY;
    const double cosine = std::cos(angle);
    const double sine = std::sin(angle);
    const double originX = 320.0 - cosine * width / 2.0 + sine * height / 2.0;
    const double originY = 224.0 - sine * width / 2.0 - cosine * height / 2.0;
    const auto polygon = Ps2TexturedQuadGeometry::Create(originX, originY, width, height, angle, 0, 0, 1, 1);
    Require(polygon.Count == 4, "Sprite quad must retain four corners");
    Require(Near((polygon.Vertices[0].X + polygon.Vertices[2].X) / 2.0, 320.0)
        && Near((polygon.Vertices[0].Y + polygon.Vertices[2].Y) / 2.0, 224.0), "Rotating parent must preserve sprite centre");
    Require(Near(std::hypot(polygon.Vertices[1].X - polygon.Vertices[0].X, polygon.Vertices[1].Y - polygon.Vertices[0].Y), width), "World width was changed by rotation");
    Require(Near(std::hypot(polygon.Vertices[3].X - polygon.Vertices[0].X, polygon.Vertices[3].Y - polygon.Vertices[0].Y), height), "World height was changed by rotation");
    Require(Near(polygon.Vertices[0].X, originX) && Near(polygon.Vertices[0].Y, originY), "World origin must not be rotated twice");
    Require(polygon.Vertices[0].U == 0 && polygon.Vertices[0].V == 0
        && polygon.Vertices[2].U == 1 && polygon.Vertices[2].V == 1, "Corner UV identity changed");
}

/// Checks clipped UVs against the inverse sprite transform rather than another clipping implementation.
static void CheckClipping() {
    const double angle = std::acos(-1.0) / 4.0;
    const double cosine = std::cos(angle), sine = std::sin(angle);
    const double originX = 320.0, originY = 224.0 - std::sqrt(2.0) * 768.0;
    const auto quad = Ps2TexturedQuadGeometry::Create(originX, originY, 1536, 1536, angle, 0.1, 0.2, 0.7, 0.6);
    const auto clipped = Ps2TexturedQuadGeometry::Clip(quad, 0, 0, 640, 448);
    Require(clipped.Count == 4, "A covering rotated backdrop must cover all four viewport corners");
    for (std::size_t index = 0; index < clipped.Count; ++index) {
        const auto& vertex = clipped.Vertices[index];
        Require(vertex.X >= 0 && vertex.X <= 640 && vertex.Y >= 0 && vertex.Y <= 448, "Clipped destination escaped viewport");
        const double localX = cosine * (vertex.X - originX) + sine * (vertex.Y - originY);
        const double localY = -sine * (vertex.X - originX) + cosine * (vertex.Y - originY);
        Require(Near(vertex.U, 0.1 + localX / 1536.0 * 0.7) && Near(vertex.V, 0.2 + localY / 1536.0 * 0.6), "Clipping changed the rotated affine UV transform");
    }
    const auto small = Ps2TexturedQuadGeometry::Create(40, -10, 100, 50, angle, 0, 0, 1, 1);
    const auto partial = Ps2TexturedQuadGeometry::Clip(small, 0, 0, 80, 60);
    Require(partial.Count >= 3 && partial.Count <= 8, "Partial clipping must retain a bounded convex polygon");
    Require(Ps2TexturedQuadGeometry::Clip(quad, 10000, 10000, 10, 10).Count == 0, "Disjoint rotated polygon was not clipped away");
    Require(Ps2TexturedQuadGeometry::Clip(quad, 0, 0, 0, 20).Count == 0, "Empty clip must not produce a draw");
}

/// Runs production corner/clipping helpers at authored sizes and meaningful pathological boundaries.
int main() {
    const double pi = std::acos(-1.0);
    CheckPivot(0, 1, 1);
    CheckPivot(pi / 4.0, 1, 1);
    CheckPivot(pi / 2.0, 1, 1);
    CheckPivot(pi / 4.0, 0.5, 0.5);
    CheckPivot(pi / 2.0, 0.5, 0.25);
    CheckClipping();
    bool invalidRejected = false;
    try { Ps2TexturedQuadGeometry::Create(0, 0, 1, 1, std::numeric_limits<double>::quiet_NaN(), 0, 0, 1, 1); }
    catch (const std::invalid_argument&) { invalidRejected = true; }
    Require(invalidRejected, "Malformed rotation must fail explicitly");
    std::puts("PASS authored 1536 pivot at 0/45/90 degrees, uniform/anisotropic scale, viewport UV clipping and invalid geometry");
    return 0;
}
