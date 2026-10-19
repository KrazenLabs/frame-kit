#include "pairing.hpp"

#include <sstream>
#include <vector>

#include "common.hpp"
#include "constants.hpp"

namespace fkp {

namespace {

std::filesystem::path authorizedKeysPath(const std::filesystem::path& home) {
    return home / constants::kAuthorizedKeys;
}

}

bool isPairingLine(const std::string& line) {
    const std::vector<std::string> fields = splitWhitespace(line);
    return fields.size() >= 3 && fields[0] == "ssh-rsa" && fields[2] == constants::kPairingName;
}

int countPairings(const std::string& authorizedKeys) {
    int count = 0;
    std::istringstream lines(authorizedKeys);
    std::string line;
    while (std::getline(lines, line)) {
        if (isPairingLine(line)) {
            ++count;
        }
    }
    return count;
}

std::string withoutPairings(const std::string& authorizedKeys, int* removed) {
    std::string kept;
    int count = 0;
    std::size_t start = 0;
    while (start < authorizedKeys.size()) {
        std::size_t end = authorizedKeys.find('\n', start);
        const bool lastLine = end == std::string::npos;
        if (lastLine) {
            end = authorizedKeys.size();
        }
        const std::string line = authorizedKeys.substr(start, end - start);
        if (isPairingLine(line)) {
            ++count;
        } else {
            kept += line;
            if (!lastLine) {
                kept += '\n';
            }
        }
        start = end + 1;
    }
    if (removed) {
        *removed = count;
    }
    return kept;
}

int countPairedPcs(const std::filesystem::path& home) {
    const std::optional<std::string> text = readFile(authorizedKeysPath(home));
    return text ? countPairings(*text) : 0;
}

bool forgetPairedPcs(const std::filesystem::path& home, int* removed, std::string* error) {
    *removed = 0;
    const std::filesystem::path path = authorizedKeysPath(home);
    std::error_code ec;
    const std::filesystem::file_status status = std::filesystem::symlink_status(path, ec);
    if (status.type() == std::filesystem::file_type::not_found) {
        return true;
    }
    if (ec) {
        *error = "cannot check " + path.string() + ": " + ec.message();
        return false;
    }
    if (status.type() != std::filesystem::file_type::regular) {
        *error = path.string() + " isn't a regular file";
        return false;
    }
    const std::optional<std::string> text = readFile(path);
    if (!text) {
        *error = "cannot read " + path.string();
        return false;
    }
    int count = 0;
    const std::string kept = withoutPairings(*text, &count);
    if (count == 0) {
        return true;
    }
    WriteOptions options;
    options.durable = true;
    options.ownerOnly = true;
    if (!writeFileAtomically(path, kept, error, options)) {
        return false;
    }
    *removed = count;
    return true;
}

}
