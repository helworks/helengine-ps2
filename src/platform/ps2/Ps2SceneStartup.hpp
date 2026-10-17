#pragma once

#include <cmath>
#include <cstring>
#include <stdexcept>
#include "SceneLoadMode.hpp"

namespace helengine::ps2 {
    /// <summary>Bootstraps the configured persistent loading scene before requesting budgeted main-scene preparation.</summary>
    class Ps2SceneStartup {
    public:
        /// <summary>Rejects invalid manifest budgets before initializing Core; synchronous I/O and individual callbacks remain cooperative steps.</summary>
        /// <param name="milliseconds">Positive finite per-frame loading budget emitted by the common runtime manifest.</param>
        /// <returns>The validated budget without rounding, defaulting or clamping.</returns>
        static double ValidateBudget(double milliseconds) {
            if (!std::isfinite(milliseconds) || milliseconds <= 0.0) {
                throw std::invalid_argument("PS2 scene loading budget must be finite and positive.");
            }
            return milliseconds;
        }

        /// <summary>Publishes the small authored overlay synchronously, then arms the target transition before the first normal Update. A shared startup/loading ID preserves direct Single boot.</summary>
        /// <typeparam name="TSceneManager">Production SceneManager or the focused ordering spy exposing the same three operations.</typeparam>
        /// <param name="manager">Initialized scene manager; the configured IDs must already be validated against its packaged catalog.</param>
        /// <param name="startupSceneId">Nonempty packaged startup scene identity.</param>
        /// <param name="loadingSceneId">Packaged loading identity, or the explicit empty manifest value when no overlay is configured.</param>
        template <typename TSceneManager>
        static void Start(TSceneManager& manager, const char* startupSceneId, const char* loadingSceneId) {
            if (startupSceneId == nullptr || startupSceneId[0] == '\0' || loadingSceneId == nullptr) {
                throw std::invalid_argument("PS2 startup/loading scene metadata is invalid.");
            }
            if (std::strcmp(startupSceneId, loadingSceneId) == 0) {
                manager.LoadScene(startupSceneId, ::SceneLoadMode::Single);
                manager.CommitPendingOperationsAtFrameBoundary();
                return;
            }
            if (loadingSceneId[0] != '\0') {
                manager.LoadScene(loadingSceneId, ::SceneLoadMode::Additive);
                manager.CommitPendingOperationsAtFrameBoundary();
            }
            manager.RequestSceneTransition(startupSceneId);
        }
    };
}
