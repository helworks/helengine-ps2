#pragma once

#include "platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.hpp"

namespace helengine::ps2 {
    /// <summary>
    /// Clips one textured triangle against the view-space near plane and homogeneous side planes without allocating memory.
    /// </summary>
    class Ps2VuTexturedTriangleClipper final {
    public:
        /// <summary>
        /// Clips a triangle into the supplied fixed polygon using the required near, left, right, bottom, and top plane order.
        /// </summary>
        /// <param name="vertexA">The first triangle vertex in winding order.</param>
        /// <param name="vertexB">The second triangle vertex in winding order.</param>
        /// <param name="vertexC">The third triangle vertex in winding order.</param>
        /// <param name="nearPlaneDistance">The positive distance from the camera to the view-space near plane.</param>
        /// <param name="outputPolygon">The fixed polygon that receives the clipped result.</param>
        static void ClipTriangle(
            const Ps2VuTexturedClipVertex& vertexA,
            const Ps2VuTexturedClipVertex& vertexB,
            const Ps2VuTexturedClipVertex& vertexC,
            float nearPlaneDistance,
            Ps2VuTexturedClipPolygon& outputPolygon);

    private:
        /// <summary>
        /// Enumerates the clipping planes in the required Sutherland-Hodgman pass order.
        /// </summary>
        enum class Plane {
            Near,
            Left,
            Right,
            Bottom,
            Top
        };

        /// <summary>
        /// Copies one input polygon through a single clipping plane into the destination polygon.
        /// </summary>
        /// <param name="inputPolygon">The polygon from the preceding clipping pass.</param>
        /// <param name="outputPolygon">The polygon receiving this pass's vertices.</param>
        /// <param name="plane">The plane to clip against.</param>
        /// <param name="nearPlaneDistance">The positive view-space near plane distance.</param>
        static void ClipAgainstPlane(
            const Ps2VuTexturedClipPolygon& inputPolygon,
            Ps2VuTexturedClipPolygon& outputPolygon,
            Plane plane,
            float nearPlaneDistance);

        /// <summary>
        /// Returns the signed inside distance for one vertex and clipping plane.
        /// </summary>
        /// <param name="vertex">The vertex whose distance is required.</param>
        /// <param name="plane">The plane that defines the signed distance.</param>
        /// <param name="nearPlaneDistance">The positive view-space near plane distance.</param>
        /// <returns>A non-negative value when the vertex is inside the plane.</returns>
        static float GetPlaneDistance(
            const Ps2VuTexturedClipVertex& vertex,
            Plane plane,
            float nearPlaneDistance);

        /// <summary>
        /// Interpolates every vertex component consistently at one edge crossing amount.
        /// </summary>
        /// <param name="previousVertex">The edge's previous vertex.</param>
        /// <param name="currentVertex">The edge's current vertex.</param>
        /// <param name="amount">The clamped interpolation amount from the previous vertex to the current vertex.</param>
        /// <returns>The finite interpolated crossing vertex.</returns>
        static Ps2VuTexturedClipVertex Interpolate(
            const Ps2VuTexturedClipVertex& previousVertex,
            const Ps2VuTexturedClipVertex& currentVertex,
            float amount);

        /// <summary>
        /// Verifies that a vertex contains finite components and reports whether it is valid.
        /// </summary>
        /// <param name="vertex">The vertex to inspect.</param>
        /// <returns><c>true</c> when every coordinate and texture component is finite.</returns>
        static bool IsFinite(const Ps2VuTexturedClipVertex& vertex);
    };
}
