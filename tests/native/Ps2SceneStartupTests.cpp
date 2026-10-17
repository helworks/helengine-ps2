#include <cassert>
#include <limits>
#include "scene-startup-host/Ps2StartupSceneManagerSpy.hpp"
#include "platform/ps2/Ps2SceneStartup.hpp"

/// <summary>Exercises real helper ordering, same-ID boot, expected metadata failures and unmodified fractional budgets.</summary>
int main() {
    using helengine::ps2::Ps2SceneStartup;
    Ps2StartupSceneManagerSpy configured;
    Ps2SceneStartup::Start(configured, "menu", "loading");
    assert((configured.Calls == std::vector<std::string>{"additive:loading", "commit", "request:menu"}));
    assert(configured.Prepared && configured.Active && !configured.Pending);

    Ps2StartupSceneManagerSpy same;
    Ps2SceneStartup::Start(same, "menu", "menu");
    assert((same.Calls == std::vector<std::string>{"single:menu", "commit"}));
    assert(same.Prepared && !same.Active);

    Ps2StartupSceneManagerSpy none;
    Ps2SceneStartup::Start(none, "menu", "");
    assert((none.Calls == std::vector<std::string>{"request:menu"}));
    assert(none.Active && !none.Prepared);

    Ps2StartupSceneManagerSpy failure;
    failure.FailCommit = true;
    bool propagated = false;
    try { Ps2SceneStartup::Start(failure, "menu", "loading"); }
    catch (const std::runtime_error&) { propagated = true; }
    assert(propagated && !failure.Active && !failure.Prepared);
    assert((failure.Calls == std::vector<std::string>{"additive:loading", "commit"}));

    assert(Ps2SceneStartup::ValidateBudget(3.25) == 3.25);
    for (double value : {0.0, -1.0, std::numeric_limits<double>::infinity(),
        -std::numeric_limits<double>::infinity(), std::numeric_limits<double>::quiet_NaN()}) {
        bool rejected = false;
        try { Ps2SceneStartup::ValidateBudget(value); }
        catch (const std::invalid_argument&) { rejected = true; }
        assert(rejected);
    }
    for (const char* value : {static_cast<const char*>(nullptr), ""}) {
        Ps2StartupSceneManagerSpy invalid;
        bool rejected = false;
        try { Ps2SceneStartup::Start(invalid, value, "loading"); }
        catch (const std::invalid_argument&) { rejected = true; }
        assert(rejected && invalid.Calls.empty());
    }
    Ps2StartupSceneManagerSpy missing;
    bool rejected = false;
    try { Ps2SceneStartup::Start(missing, "menu", nullptr); }
    catch (const std::invalid_argument&) { rejected = true; }
    assert(rejected && missing.Calls.empty());
    return 0;
}
