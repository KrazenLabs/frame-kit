#include "paths.hpp"

#include "constants.hpp"

#include <cstdlib>

#include <pwd.h>
#include <unistd.h>

namespace fkp {

namespace {

std::filesystem::path homeDirectory() {
    const char* home = std::getenv("HOME");
    if (home && home[0] != '\0') {
        return home;
    }
    if (const passwd* entry = getpwuid(getuid())) {
        if (entry->pw_dir && entry->pw_dir[0] != '\0') {
            return entry->pw_dir;
        }
    }
    return "/home/steamos";
}

}

Paths pathsForHome(const std::filesystem::path& home, const std::filesystem::path& runtimeDir) {
    Paths paths;
    paths.home = home;
    paths.appsRoot = home / constants::kAppsRoot;
    paths.kitHome = paths.appsRoot / constants::kSharedDir;
    paths.panelDir = paths.kitHome / constants::kPanelModulesDir;
    paths.runtimeDir = runtimeDir;
    paths.status = runtimeDir / "status.json";
    paths.lock = runtimeDir / "lock";
    return paths;
}

Paths defaultPaths() {
    return pathsForHome(homeDirectory(),
                        std::filesystem::path("/run/user") / std::to_string(getuid()) / constants::kPanelBinary);
}

}
