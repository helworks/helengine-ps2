#pragma once
#include <stdexcept>
#include <string>
#include <vector>
#include "SceneLoadMode.hpp"

/// <summary>Records production startup helper calls and models the publication boundary without executing game/input callbacks.</summary>
class Ps2StartupSceneManagerSpy {
public:
    /// <summary>Ordered calls made through the exact helper interface.</summary>
    std::vector<std::string> Calls;
    /// <summary>Whether a synchronous load awaits its explicit publication boundary.</summary>
    bool Pending = false;
    /// <summary>Whether the configured overlay has been fully prepared by the boundary.</summary>
    bool Prepared = false;
    /// <summary>Whether the main-scene budgeted transition was armed.</summary>
    bool Active = false;
    /// <summary>Whether the boundary should simulate an initialization failure.</summary>
    bool FailCommit = false;

    /// <summary>Queues a synchronous bootstrap load without publishing its pending hierarchy.</summary>
    void LoadScene(const char* id, SceneLoadMode mode) {
        Calls.push_back(std::string(mode == SceneLoadMode::Single ? "single:" : "additive:") + id);
        Pending = true;
    }

    /// <summary>Publishes the bootstrap hierarchy only after a successful explicit boundary.</summary>
    void CommitPendingOperationsAtFrameBoundary() {
        Calls.push_back("commit");
        if (FailCommit) throw std::runtime_error("bootstrap initialization failed");
        Prepared = Pending;
        Pending = false;
    }

    /// <summary>Arms a budgeted target transition without deserializing or publishing it synchronously.</summary>
    void RequestSceneTransition(const char* id) {
        if (Pending) throw std::logic_error("target requested before overlay publication");
        Calls.push_back(std::string("request:") + id);
        Active = true;
    }
};
