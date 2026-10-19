#pragma once

#include <filesystem>
#include <string>

namespace fkp {

bool isPairingLine(const std::string& line);
int countPairings(const std::string& authorizedKeys);
std::string withoutPairings(const std::string& authorizedKeys, int* removed);
int countPairedPcs(const std::filesystem::path& home);
bool forgetPairedPcs(const std::filesystem::path& home, int* removed, std::string* error);

}
