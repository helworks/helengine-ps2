#include "platform/ps2/rendering/Ps2TexturedQuadPrimitive.hpp"

#include <cmath>
#include <cstdio>
#include <stdexcept>

using helengine::ps2::Ps2TexturedQuadPrimitive;

/// Fails the run with the broken expectation instead of continuing with a malformed draw plan.
static void Require(bool condition, const char* message) {
    if (!condition) { throw std::runtime_error(message); }
}

/// Compares double-precision plan coordinates before the GS fixed-point conversion.
static bool Near(double first, double second) { return std::abs(first - second) < 0.0001; }

/// Demodisc's main-menu backdrop: a 1536x1536 sprite child at local (-768,-768) under a parent at the
/// 640x448 canvas centre that rotates every frame. The command carries the parent-rotated top-left origin
/// and the parent's angle; the PS2 must turn the quad around that origin, not only move it.
static void CheckMenuBackdropRotates(double angle) {
    const double centreX = 320.0;
    const double centreY = 224.0;
    const double cosine = std::cos(angle);
    const double sine = std::sin(angle);
    const double originX = centreX + cosine * -768.0 - sine * -768.0;
    const double originY = centreY + sine * -768.0 + cosine * -768.0;
    const auto primitive = Ps2TexturedQuadPrimitive::Create(originX, originY, 1536.0, 1536.0, angle,
        0.0, 0.0, 1.0, 1.0, 256.0, 256.0);
    Require(primitive.IsRotated, "A rotated sprite command was planned as an axis-aligned sprite (rotation dropped)");
    Require(primitive.Polygon.Count == 4 && primitive.TriangleCount() == 2, "An unclipped rotated quad must be two triangles");
    const auto& topLeft = primitive.Polygon.Vertices[0];
    const auto& topRight = primitive.Polygon.Vertices[1];
    const auto& bottomRight = primitive.Polygon.Vertices[2];
    const auto& bottomLeft = primitive.Polygon.Vertices[3];
    Require(Near(topLeft.X, originX) && Near(topLeft.Y, originY), "The top-left origin must stay where the parent put it");
    Require(Near(topRight.X, originX + cosine * 1536.0) && Near(topRight.Y, originY + sine * 1536.0),
        "The top edge must follow the rotated unit X axis");
    Require(Near(bottomLeft.X, originX - sine * 1536.0) && Near(bottomLeft.Y, originY + cosine * 1536.0),
        "The left edge must follow the rotated unit Y axis");
    Require(Near((topLeft.X + bottomRight.X) / 2.0, centreX) && Near((topLeft.Y + bottomRight.Y) / 2.0, centreY),
        "The backdrop must turn in place around the canvas centre");
    Require(topLeft.U == 0.0 && topLeft.V == 0.0 && topRight.U == 256.0 && topRight.V == 0.0
        && bottomRight.U == 256.0 && bottomRight.V == 256.0 && bottomLeft.U == 0.0 && bottomLeft.V == 256.0,
        "Corner texture coordinates must be in texels of the uploaded texture");
}

/// Unrotated quads keep the exact sprite path so text and UI are not turned into triangles.
static void CheckUnrotatedStaysSprite() {
    Require(!Ps2TexturedQuadPrimitive::Create(10, 20, 30, 40, 0.0, 0, 0, 1, 1, 64, 64).IsRotated,
        "An unrotated quad must keep the sprite primitive");
    Require(!Ps2TexturedQuadPrimitive::CreateClipped(10, 20, 30, 40, 0.0, 0, 0, 1, 1, 64, 64, 0, 0, 640, 448).IsRotated,
        "An unrotated clipped quad must keep the sprite clip path");
}

/// Rotated quads under a clip rectangle are clipped as polygons with UVs that follow the inverse sprite transform.
static void CheckRotatedClipping() {
    const double angle = std::acos(-1.0) / 6.0;
    const double cosine = std::cos(angle);
    const double sine = std::sin(angle);
    const double originX = 100.0;
    const double originY = 50.0;
    const double width = 400.0;
    const double height = 300.0;
    const auto primitive = Ps2TexturedQuadPrimitive::CreateClipped(originX, originY, width, height, angle,
        0.25, 0.5, 0.5, 0.25, 128.0, 64.0, 120.0, 80.0, 200.0, 150.0);
    Require(primitive.IsRotated, "A rotated clipped quad was planned as an axis-aligned sprite");
    Require(primitive.TriangleCount() >= 1, "A rotated quad overlapping the clip must stay visible");
    for (std::size_t index = 0; index < primitive.Polygon.Count; ++index) {
        const auto& vertex = primitive.Polygon.Vertices[index];
        Require(vertex.X >= 120.0 - 0.0001 && vertex.X <= 320.0 + 0.0001 && vertex.Y >= 80.0 - 0.0001 && vertex.Y <= 230.0 + 0.0001,
            "A clipped vertex escaped the clip rectangle");
        const double localX = cosine * (vertex.X - originX) + sine * (vertex.Y - originY);
        const double localY = -sine * (vertex.X - originX) + cosine * (vertex.Y - originY);
        Require(Near(vertex.U, (0.25 + localX / width * 0.5) * 128.0) && Near(vertex.V, (0.5 + localY / height * 0.25) * 64.0),
            "Clipping changed the rotated affine texel mapping");
    }

    const auto hidden = Ps2TexturedQuadPrimitive::CreateClipped(originX, originY, width, height, angle,
        0, 0, 1, 1, 128, 64, 2000.0, 2000.0, 10.0, 10.0);
    Require(hidden.IsRotated && hidden.TriangleCount() == 0, "A rotated quad outside the clip must submit no triangles");
}

/// Runs the PS2 textured-quad primitive plan at the demodisc backdrop size and under clipping.
int main() {
    try {
        const double pi = std::acos(-1.0);
        CheckMenuBackdropRotates(0.5);
        CheckMenuBackdropRotates(pi / 4.0);
        CheckMenuBackdropRotates(-2.0);
        CheckUnrotatedStaysSprite();
        CheckRotatedClipping();
    } catch (const std::exception& exception) {
        std::printf("FAIL %s\n", exception.what());
        return 1;
    }

    std::puts("PASS rotated menu backdrop turns around its top-left origin, unrotated quads stay sprites, rotated clipping keeps texel UVs");
    return 0;
}
