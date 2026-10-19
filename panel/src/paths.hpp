#pragma once

#include <filesystem>
#include <string>

namespace fkp {

struct Paths {
    std::filesystem::path home;
    std::filesystem::path appsRoot;
    std::filesystem::path kitHome;
    std::filesystem::path panelDir;
    std::filesystem::path runtimeDir;
    std::filesystem::path status;
    std::filesystem::path lock;
};

Paths defaultPaths();
Paths pathsForHome(const std::filesystem::path& home, const std::filesystem::path& runtimeDir);

}
