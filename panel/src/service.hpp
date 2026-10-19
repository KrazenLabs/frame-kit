#pragma once

#include <string>

#include "paths.hpp"

namespace fkp {

int runService(const Paths& paths, const std::string& version);
int printStatus(const Paths& paths);
int printModules(const Paths& paths);

}
