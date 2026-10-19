#include "modules.hpp"

#include <algorithm>
#include <regex>

#include <json/json.h>

#include "common.hpp"
#include "constants.hpp"

namespace fkp {

namespace {

constexpr size_t kMaxModuleBytes = constants::kMaxModuleBytes;
constexpr size_t kMaxIconBytes = constants::kMaxIconBytes;
constexpr size_t kMaxActions = constants::kMaxActions;
constexpr size_t kMaxToggles = constants::kMaxToggles;
constexpr size_t kMaxArguments = constants::kMaxArguments;
constexpr size_t kMaxLabelBytes = constants::kMaxLabelBytes;
constexpr size_t kMaxConfirmBytes = constants::kMaxConfirmBytes;
constexpr double kMinInterval = constants::kMinStatusInterval;
constexpr double kMaxInterval = constants::kMaxStatusInterval;

std::optional<std::vector<std::string>> parseCommand(const Json::Value& value, const std::string& appDir) {
    if (!value.isArray() || value.empty() || value.size() > kMaxArguments) {
        return std::nullopt;
    }
    std::vector<std::string> argv;
    for (const Json::Value& item : value) {
        if (!item.isString() || item.asString().empty()) {
            return std::nullopt;
        }
        argv.push_back(substituteApp(item.asString(), appDir));
    }
    return argv;
}

void noteProblem(std::string* problem, const std::string& text) {
    if (problem && problem->empty()) {
        *problem = text;
    }
}

}

bool isValidId(const std::string& id) {
    static const std::regex pattern(constants::kIdPattern);
    return std::regex_match(id, pattern);
}

bool isValidElementId(const std::string& id) {
    static const std::regex pattern(constants::kElementIdPattern);
    return std::regex_match(id, pattern);
}

bool isButtonStyle(const std::string& style) {
    return std::any_of(std::begin(constants::kButtonStyles), std::end(constants::kButtonStyles),
                       [&style](const char* known) { return style == known; });
}

bool isSafeRelativePath(const std::string& path) {
    static const std::regex pattern(constants::kRelativePathPattern);
    if (path.size() > static_cast<size_t>(constants::kMaxRelativePathLength) || !std::regex_match(path, pattern) ||
        path.front() == '/') {
        return false;
    }
    return !contains("/" + path + "/", "/../");
}

std::string substituteApp(const std::string& argument, const std::string& appDir) {
    static const std::string token = constants::kAppToken;
    std::string out;
    size_t position = 0;
    while (true) {
        const size_t found = argument.find(token, position);
        if (found == std::string::npos) {
            out += argument.substr(position);
            return out;
        }
        out += argument.substr(position, found - position);
        out += appDir;
        position = found + token.size();
    }
}

std::string clipText(const std::string& text, size_t maxBytes) {
    if (text.size() <= maxBytes) {
        return text;
    }
    size_t cut = maxBytes;
    while (cut > 0 && (static_cast<unsigned char>(text[cut]) & 0xC0) == 0x80) {
        --cut;
    }
    return text.substr(0, cut) + "…";
}

std::vector<std::string> moduleIds(const std::filesystem::path& panelDir) {
    std::vector<std::string> ids;
    for (const std::string& name : listDirectory(panelDir)) {
        if (!endsWith(name, ".json")) {
            continue;
        }
        const std::string id = name.substr(0, name.size() - 5);
        if (isValidId(id)) {
            ids.push_back(id);
        }
    }
    std::sort(ids.begin(), ids.end());
    return ids;
}

bool parseModuleJson(const std::string& text, const std::string& appDir, Module& module, std::string* problem) {
    if (text.size() > kMaxModuleBytes) {
        noteProblem(problem, "panel description is too large");
        return false;
    }
    const std::optional<Json::Value> parsed = parseJson(text);
    if (!parsed || !parsed->isObject()) {
        noteProblem(problem, "panel description isn't valid JSON");
        return false;
    }
    const Json::Value& root = *parsed;

    if (root.isMember("icon")) {
        if (root["icon"].isString() && isSafeRelativePath(root["icon"].asString())) {
            module.iconPath = std::filesystem::path(appDir) / root["icon"].asString();
        } else {
            noteProblem(problem, "invalid icon path");
        }
    }

    if (root.isMember("status")) {
        const Json::Value& status = root["status"];
        const std::optional<std::vector<std::string>> command =
            status.isObject() ? parseCommand(status["command"], appDir) : std::nullopt;
        if (command) {
            module.statusCommand = *command;
        } else {
            noteProblem(problem, "invalid status command");
        }
        if (status.isObject() && status.isMember("interval")) {
            const Json::Value& interval = status["interval"];
            if (!interval.isNumeric() || interval.asDouble() < kMinInterval || interval.asDouble() > kMaxInterval) {
                noteProblem(problem, "invalid status interval");
            }
            if (command && interval.isNumeric()) {
                module.statusInterval = std::clamp(interval.asDouble(), kMinInterval, kMaxInterval);
            }
        }
    }

    for (const char* key : {"actions", "toggles"}) {
        if (root.isMember(key) && !root[key].isArray()) {
            noteProblem(problem, std::string(key) + " must be a list");
        }
    }

    for (const Json::Value& item : root["actions"]) {
        if (module.actions.size() >= kMaxActions) {
            noteProblem(problem, "only up to " + std::to_string(kMaxActions) + " buttons are supported");
            break;
        }
        const std::optional<std::vector<std::string>> command =
            item.isObject() ? parseCommand(item["command"], appDir) : std::nullopt;
        if (!command || !item["id"].isString() || !isValidElementId(item["id"].asString()) ||
            !item["label"].isString() || item["label"].asString().empty()) {
            noteProblem(problem, "a button failed validation");
            continue;
        }
        if (item.isMember("confirm") && (!item["confirm"].isString() || item["confirm"].asString().empty() ||
                                         item["confirm"].asString().size() > kMaxConfirmBytes)) {
            noteProblem(problem, "a button's question failed validation");
            continue;
        }
        if (item["label"].asString().size() > kMaxLabelBytes) {
            noteProblem(problem, "button label is too long");
        }
        std::string style;
        if (item.isMember("style")) {
            if (item["style"].isString() && isButtonStyle(item["style"].asString())) {
                style = item["style"].asString();
            } else {
                noteProblem(problem, "button style not found");
            }
        }
        if (item.isMember("wide") && !item["wide"].isBool()) {
            noteProblem(problem, "button wide parameter is undefined");
        }
        module.actions.push_back({item["id"].asString(), clipText(item["label"].asString(), kMaxLabelBytes), *command,
                                  item["confirm"].isString() ? item["confirm"].asString() : "", style,
                                  item["wide"].isBool() && item["wide"].asBool()});
    }

    for (const Json::Value& item : root["toggles"]) {
        if (module.toggles.size() >= kMaxToggles) {
            noteProblem(problem, "only up to " + std::to_string(kMaxToggles) + " toggles are supported");
            break;
        }
        if (!item.isObject()) {
            noteProblem(problem, "a toggle failed validation");
            continue;
        }
        const auto get = parseCommand(item["get"], appDir);
        const auto on = parseCommand(item["on"], appDir);
        const auto off = parseCommand(item["off"], appDir);
        if (!get || !on || !off || !item["id"].isString() || !isValidElementId(item["id"].asString()) ||
            !item["label"].isString() || item["label"].asString().empty()) {
            noteProblem(problem, "a toggle failed validation");
            continue;
        }
        if (item["label"].asString().size() > kMaxLabelBytes) {
            noteProblem(problem, "toggle label is too long");
        }
        module.toggles.push_back(
            {item["id"].asString(), clipText(item["label"].asString(), kMaxLabelBytes), *get, *on, *off});
    }
    return true;
}

void applyManifest(const std::string& manifestJson, Module& module) {
    const std::optional<Json::Value> parsed = parseJson(manifestJson);
    if (!parsed || !parsed->isObject()) {
        return;
    }
    const Json::Value& root = *parsed;
    if (root["name"].isString() && !root["name"].asString().empty()) {
        module.name = clipText(root["name"].asString(), 60);
    }
    if (root["version"].isString()) {
        module.version = clipText(root["version"].asString(), constants::kMaxVersionLength);
    }
    if (root["description"].isString()) {
        module.description = clipText(root["description"].asString(), 200);
    }
    const Json::Value& updates = root["updates"];
    module.updatesConfigured =
        updates.isObject() && updates["index"].isString() && !updates["index"].asString().empty();
    const Json::Value& dataDirs = root["data_dirs"];
    if (dataDirs.isArray()) {
        module.hasData = !dataDirs.empty();
    }
}

bool parseAutoUpdate(const std::string& settingsEnv) {
    for (const std::string& line : splitLines(settingsEnv)) {
        const std::string trimmed = trim(line);
        if (trimmed == "AUTO_UPDATE=0") {
            return false;
        }
        if (trimmed == "AUTO_UPDATE=1") {
            return true;
        }
    }
    return true;
}

UpdateInfo parseUpdateJson(const std::string& text) {
    UpdateInfo info;
    const std::optional<Json::Value> parsed = parseJson(text);
    if (!parsed || !parsed->isObject()) {
        return info;
    }
    const Json::Value& root = *parsed;
    info.known = true;
    info.checked = root["checked"].isNumeric() ? root["checked"].asDouble() : 0.0;
    info.latest = root["latest"].isString() ? clipText(root["latest"].asString(), constants::kMaxVersionLength) : "";
    info.available = root["available"].isBool() && root["available"].asBool();
    info.state = root["state"].isString() ? clipText(root["state"].asString(), 20) : "";
    info.message = root["message"].isString() ? capitalizeFirst(clipText(root["message"].asString(), 160)) : "";
    return info;
}

namespace {

std::vector<std::string> versionParts(const std::string& text) {
    std::vector<std::string> parts;
    if (text.empty()) {
        return parts;
    }
    size_t start = 0;
    while (true) {
        const size_t dot = text.find('.', start);
        parts.push_back(text.substr(start, dot == std::string::npos ? std::string::npos : dot - start));
        if (dot == std::string::npos) {
            return parts;
        }
        start = dot + 1;
    }
}

bool allDigits(const std::string& text) {
    return !text.empty() && std::all_of(text.begin(), text.end(), [](char c) { return c >= '0' && c <= '9'; });
}

int signOf(int value) {
    return (value > 0) - (value < 0);
}

int compareVersionPart(const std::string& a, const std::string& b) {
    const bool numberA = allDigits(a);
    const bool numberB = allDigits(b);
    if (numberA && numberB) {
        const std::string digitsA = a.substr(std::min(a.find_first_not_of('0'), a.size()));
        const std::string digitsB = b.substr(std::min(b.find_first_not_of('0'), b.size()));
        if (digitsA.size() != digitsB.size()) {
            return digitsA.size() < digitsB.size() ? -1 : 1;
        }
        return signOf(digitsA.compare(digitsB));
    }
    if (numberA != numberB) {
        return numberA ? -1 : 1;
    }
    return signOf(a.compare(b));
}

int compareVersionParts(const std::string& a, const std::string& b) {
    const std::vector<std::string> partsA = versionParts(a);
    const std::vector<std::string> partsB = versionParts(b);
    for (size_t i = 0; i < partsA.size() && i < partsB.size(); ++i) {
        const int byPart = compareVersionPart(partsA[i], partsB[i]);
        if (byPart != 0) {
            return byPart;
        }
    }
    if (partsA.size() == partsB.size()) {
        return 0;
    }
    return partsA.size() < partsB.size() ? -1 : 1;
}

}

int compareVersions(const std::string& a, const std::string& b) {
    const size_t dashA = a.find('-');
    const size_t dashB = b.find('-');
    const int byCore = compareVersionParts(a.substr(0, dashA), b.substr(0, dashB));
    if (byCore != 0 || (dashA == std::string::npos && dashB == std::string::npos)) {
        return byCore;
    }
    if (dashA == std::string::npos) {
        return 1;
    }
    if (dashB == std::string::npos) {
        return -1;
    }
    return compareVersionParts(a.substr(dashA + 1), b.substr(dashB + 1));
}

void judgeAvailable(UpdateInfo& info, const std::string& installedVersion) {
    if (!installedVersion.empty()) {
        info.available = !info.latest.empty() && compareVersions(info.latest, installedVersion) > 0;
    }
}

std::string parseSteamvrRestart(const std::string& text) {
    static const std::regex invocation("[0-9a-f]{32}");
    const std::string value = trim(text);
    return std::regex_match(value, invocation) ? value : std::string();
}

void markInterruptedUpdate(UpdateInfo& info, bool updateRunning) {
    if (info.state == "updating" && !updateRunning) {
        info.state = "failed";
        info.message = "Update was interrupted";
    }
}

bool updateRunOver(const UpdateInfo& info, double secondsSinceStart, double graceSeconds, bool updateRunning) {
    return info.state != "updating" && secondsSinceStart > graceSeconds && !updateRunning;
}

Module loadModule(const Paths& paths, const std::string& id) {
    Module module;
    module.id = id;
    module.name = id;
    module.appHome = paths.appsRoot / id;
    module.appDir = module.appHome / constants::kCurrentLink;

    const std::optional<std::string> manifest = readFile(module.appDir / constants::kManifestJson);
    if (!manifest) {
        module.problem = std::string("not installed properly (no ") + constants::kManifestJson + " in " + module.appDir.string() + ")";
        return module;
    }
    applyManifest(*manifest, module);

    const std::optional<std::string> text = readFile(paths.panelDir / (id + ".json"));
    if (!text) {
        module.problem = "cannot read panel description";
        return module;
    }
    std::string problem;
    parseModuleJson(*text, module.appDir.string(), module, &problem);

    if (!module.iconPath.empty()) {
        const std::optional<std::string> svg = readFile(module.iconPath);
        if (svg && svg->size() <= kMaxIconBytes && contains(*svg, "<svg")) {
            module.iconSvg = *svg;
            module.iconKey = fnv1aHex(*svg);
        } else if (problem.empty()) {
            problem = "icon is either missing, too large or not an SVG";
        }
    }

    const std::filesystem::path state = module.appHome / constants::kAppStateDir;
    if (const std::optional<std::string> settings = readFile(state / constants::kSettingsFile)) {
        module.autoUpdate = parseAutoUpdate(*settings);
    }
    if (const std::optional<std::string> update = readFile(state / constants::kUpdateStateFile)) {
        module.update = parseUpdateJson(*update);
        judgeAvailable(module.update, module.version);
    }
    if (const std::optional<std::string> restart = readFile(state / constants::kSteamvrRestartFile)) {
        module.steamvrRestartFor = parseSteamvrRestart(*restart);
    }
    module.problem = problem;
    return module;
}

std::vector<Module> loadModules(const Paths& paths) {
    std::vector<Module> modules;
    for (const std::string& id : moduleIds(paths.panelDir)) {
        modules.push_back(loadModule(paths, id));
    }
    return modules;
}

std::vector<std::filesystem::path> watchedDirectories(const Paths& paths, const std::vector<std::string>& ids,
                                                      const std::vector<Module>& modules) {
    std::vector<std::filesystem::path> directories;
    const auto add = [&directories](const std::filesystem::path& directory) {
        if (!directory.empty() && std::find(directories.begin(), directories.end(), directory) == directories.end()) {
            directories.push_back(directory);
        }
    };
    add(paths.panelDir);
    add((paths.home / constants::kAuthorizedKeys).parent_path());
    for (const std::string& id : ids) {
        const std::filesystem::path appHome = paths.appsRoot / id;
        add(appHome);
        add(appHome / constants::kCurrentLink);
        add(appHome / constants::kAppStateDir);
    }
    for (const Module& module : modules) {
        if (!module.iconPath.empty() && std::find(ids.begin(), ids.end(), module.id) != ids.end()) {
            add(module.iconPath.parent_path());
        }
    }
    return directories;
}

}
