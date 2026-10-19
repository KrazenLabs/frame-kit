#include <cstdio>
#include <string>
#include <vector>

#include "common.hpp"
#include "constants.hpp"
#include "panel_version.hpp"
#include "paths.hpp"
#include "service.hpp"

namespace {

int usage() {
    std::fprintf(stderr,
                 "usage: framekit-panel <command>\n"
                 "  run       the background service (systemd starts it)\n"
                 "  status    what the running service shows\n"
                 "  modules   the panel modules it would load, with any problems\n"
                 "  version\n");
    return 2;
}

}

int main(int argc, char** argv) {
    fkp::installStopHandlers();
    const std::vector<std::string> args(argv + 1, argv + argc);
    if (args.size() != 1) {
        return usage();
    }
    const fkp::Paths paths = fkp::defaultPaths();
    const std::string& command = args.front();
    if (command == "run") {
        return fkp::runService(paths, fkp::kPanelVersion);
    }
    if (command == "status") {
        return fkp::printStatus(paths);
    }
    if (command == "modules") {
        return fkp::printModules(paths);
    }
    if (command == "version" || command == "--version") {
        std::printf("%s %s\n", fkp::constants::kPanelBinary, fkp::kPanelVersion);
        return 0;
    }
    return usage();
}
