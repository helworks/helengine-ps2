#pragma once

#include <cstddef>

#include "platform/ps2/rendering/Ps2TexturedQuadGeometry.hpp"

namespace helengine::ps2 {
    /// Decides how one 2D textured-quad command reaches the GS.
    /// Unrotated quads keep the axis-aligned sprite primitive; rotated quads become a clipped
    /// texel-space triangle fan built from the top-left-origin corners of Ps2TexturedQuadGeometry.
    class Ps2TexturedQuadPrimitive {
    public:
        /// Retains exact affine corners; only positively aligned rectangles use the GS sprite fast path.
        static Ps2TexturedQuadPrimitive FromCorners(Ps2TexturedPolygon polygon, double textureWidth, double textureHeight,
            bool clipped, double clipX = 0, double clipY = 0, double clipWidth = 0, double clipHeight = 0) {
            Ps2TexturedQuadPrimitive primitive;
            primitive.IsRotated = true;
            if (polygon.Count == 4) {
                const auto& tl = polygon.Vertices[0];
                const auto& tr = polygon.Vertices[1];
                const auto& br = polygon.Vertices[2];
                const auto& bl = polygon.Vertices[3];
                primitive.IsRotated = !(tl.Y == tr.Y && bl.Y == br.Y && tl.X == bl.X && tr.X == br.X
                    && tr.X > tl.X && bl.Y > tl.Y);
            }
            if (!primitive.IsRotated) { return primitive; }
            primitive.Polygon = clipped ? Ps2TexturedQuadGeometry::Clip(polygon, clipX, clipY, clipWidth, clipHeight) : polygon;
            primitive.ScaleToTexels(textureWidth, textureHeight);
            return primitive;
        }

        /// True when the quad must be drawn as the triangle fan in Polygon instead of an axis-aligned sprite.
        bool IsRotated = false;

        /// Clipped destination corners with texture coordinates in texels; only populated when IsRotated is true.
        Ps2TexturedPolygon Polygon;

        /// Plans an unclipped quad whose world origin (top-left) is already parent-transformed.
        static Ps2TexturedQuadPrimitive Create(double x, double y, double width, double height, double rotation,
            double u, double v, double sourceWidth, double sourceHeight, double textureWidth, double textureHeight) {
            Ps2TexturedQuadPrimitive primitive;
            if (rotation == 0.0) {
                return primitive;
            }

            primitive.IsRotated = true;
            primitive.Polygon = Ps2TexturedQuadGeometry::Create(x, y, width, height, rotation, u, v, sourceWidth, sourceHeight);
            primitive.ScaleToTexels(textureWidth, textureHeight);
            return primitive;
        }

        /// Plans a quad clipped to the active rectangle; rotated quads are clipped as polygons, not bounding boxes.
        static Ps2TexturedQuadPrimitive CreateClipped(double x, double y, double width, double height, double rotation,
            double u, double v, double sourceWidth, double sourceHeight, double textureWidth, double textureHeight,
            double clipX, double clipY, double clipWidth, double clipHeight) {
            Ps2TexturedQuadPrimitive primitive;
            if (rotation == 0.0) {
                return primitive;
            }

            primitive.IsRotated = true;
            primitive.Polygon = Ps2TexturedQuadGeometry::Clip(
                Ps2TexturedQuadGeometry::Create(x, y, width, height, rotation, u, v, sourceWidth, sourceHeight),
                clipX, clipY, clipWidth, clipHeight);
            primitive.ScaleToTexels(textureWidth, textureHeight);
            return primitive;
        }

        /// Number of GS triangles the fan submits; zero when clipping left no visible area.
        std::size_t TriangleCount() const {
            return Polygon.Count >= 3 ? Polygon.Count - 2 : 0;
        }

    private:
        /// Converts the normalized UVs carried through clipping into the texel coordinates gsKit's UV registers take.
        void ScaleToTexels(double textureWidth, double textureHeight) {
            for (std::size_t index = 0; index < Polygon.Count; ++index) {
                Polygon.Vertices[index].U *= textureWidth;
                Polygon.Vertices[index].V *= textureHeight;
            }
        }
    };
}
