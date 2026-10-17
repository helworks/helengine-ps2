#pragma once

#include <cmath>
#include <cstddef>
#include <stdexcept>

namespace helengine::ps2 {
    /// Stores one destination corner and its normalized texture coordinate through clipping.
    struct Ps2TexturedVertex {
        double X;
        double Y;
        double U;
        double V;
    };

    /// Retains the convex clipped quad without per-draw heap allocations.
    struct Ps2TexturedPolygon {
        Ps2TexturedVertex Vertices[12] {};
        std::size_t Count = 0;
    };

    /// Builds transformed top-left-origin sprite corners and clips their interpolated UVs.
    class Ps2TexturedQuadGeometry {
    public:
        /// Attaches UV identity to exact shared corners, including reflection and shear.
        static Ps2TexturedPolygon CreateCorners(double tlX, double tlY, double trX, double trY,
            double brX, double brY, double blX, double blY, double u, double v, double width, double height) {
            const double values[] = {tlX, tlY, trX, trY, brX, brY, blX, blY, u, v, width, height};
            for (double value : values) {
                if (!std::isfinite(value)) { throw std::invalid_argument("PS2 sprite corners and UVs must be finite."); }
            }
            Ps2TexturedPolygon polygon;
            if ((trX - tlX) * (blY - tlY) - (trY - tlY) * (blX - tlX) == 0.0) { return polygon; }
            polygon.Count = 4;
            polygon.Vertices[0] = {tlX, tlY, u, v};
            polygon.Vertices[1] = {trX, trY, u + width, v};
            polygon.Vertices[2] = {brX, brY, u + width, v + height};
            polygon.Vertices[3] = {blX, blY, u, v + height};
            return polygon;
        }

        /// Uses world-origin and world-axis rotation; the origin is already transformed by its parent.
        static Ps2TexturedPolygon Create(double x, double y, double width, double height,
            double rotation, double u, double v, double sourceWidth, double sourceHeight) {
            if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(width) || !std::isfinite(height)
                || !std::isfinite(rotation) || !std::isfinite(u) || !std::isfinite(v)
                || !std::isfinite(sourceWidth) || !std::isfinite(sourceHeight)) {
                throw std::invalid_argument("PS2 textured corners require finite geometry and UVs.");
            }
            Ps2TexturedPolygon polygon;
            if (width <= 0.0 || height <= 0.0) { return polygon; }
            const double rightX = std::cos(rotation) * width;
            const double rightY = std::sin(rotation) * width;
            const double downX = -std::sin(rotation) * height;
            const double downY = std::cos(rotation) * height;
            polygon.Count = 4;
            polygon.Vertices[0] = {x, y, u, v};
            polygon.Vertices[1] = {x + rightX, y + rightY, u + sourceWidth, v};
            polygon.Vertices[2] = {x + rightX + downX, y + rightY + downY, u + sourceWidth, v + sourceHeight};
            polygon.Vertices[3] = {x + downX, y + downY, u, v + sourceHeight};
            return polygon;
        }

        /// Clips the rotated polygon itself, preserving affine UVs instead of cropping an unrotated bounding box.
        static Ps2TexturedPolygon Clip(Ps2TexturedPolygon polygon, double x, double y, double width, double height) {
            if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(width) || !std::isfinite(height)) {
                throw std::invalid_argument("PS2 textured clip requires a finite rectangle.");
            }
            if (width <= 0.0 || height <= 0.0) { return {}; }
            polygon = ClipEdge(polygon, 0, x);
            polygon = ClipEdge(polygon, 1, x + width);
            polygon = ClipEdge(polygon, 2, y);
            return ClipEdge(polygon, 3, y + height);
        }

    private:
        /// Tests the four inclusive rectangle planes without discarding their boundary vertices.
        static bool IsInside(const Ps2TexturedVertex& vertex, int edge, double boundary) {
            if (edge == 0) { return vertex.X >= boundary; }
            if (edge == 1) { return vertex.X <= boundary; }
            if (edge == 2) { return vertex.Y >= boundary; }
            return vertex.Y <= boundary;
        }

        /// Interpolates geometry and texture coordinates at one crossed clipping plane.
        static Ps2TexturedVertex Intersect(const Ps2TexturedVertex& first, const Ps2TexturedVertex& second,
            int edge, double boundary) {
            const bool horizontalCoordinate = edge < 2;
            const double start = horizontalCoordinate ? first.X : first.Y;
            const double end = horizontalCoordinate ? second.X : second.Y;
            const double ratio = (boundary - start) / (end - start);
            Ps2TexturedVertex result {first.X + (second.X - first.X) * ratio,
                first.Y + (second.Y - first.Y) * ratio, first.U + (second.U - first.U) * ratio,
                first.V + (second.V - first.V) * ratio};
            if (horizontalCoordinate) { result.X = boundary; } else { result.Y = boundary; }
            return result;
        }

        /// Runs one bounded Sutherland-Hodgman pass on the convex textured polygon.
        static Ps2TexturedPolygon ClipEdge(const Ps2TexturedPolygon& polygon, int edge, double boundary) {
            Ps2TexturedPolygon result;
            if (polygon.Count == 0) { return result; }
            Ps2TexturedVertex previous = polygon.Vertices[polygon.Count - 1];
            bool previousInside = IsInside(previous, edge, boundary);
            for (std::size_t index = 0; index < polygon.Count; ++index) {
                const Ps2TexturedVertex& current = polygon.Vertices[index];
                const bool currentInside = IsInside(current, edge, boundary);
                if (previousInside != currentInside) { result.Vertices[result.Count++] = Intersect(previous, current, edge, boundary); }
                if (currentInside) { result.Vertices[result.Count++] = current; }
                previous = current;
                previousInside = currentInside;
            }
            return result;
        }
    };
}
