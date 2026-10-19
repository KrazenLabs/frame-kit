#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <vector>

#include "constants.hpp"
#include "paths.hpp"

namespace fkp {

struct ActionSpec {
    std::string id;
    std::string label;
    std::vector<std::string> command;
    std::string confirm;
    std::string style;
    bool wide = false;
};

struct ToggleSpec {
    std::string id;
    std::string label;
    std::vector<std::string> get;
    std::vector<std::string> on;
    std::vector<std::string> off;
};

struct UpdateInfo {
    bool known = false;
    double checked = 0.0;
    std::string latest;
    bool available = false;
    std::string state;
    std::string message;
};

struct Module {
    std::string id;
    std::string name;
    std::string version;
    std::string description;
    std::filesystem::path appHome;
    std::filesystem::path appDir;
    std::filesystem::path iconPath;
    std::string iconSvg;
    std::string iconKey;
    std::vector<std::string> statusCommand;
    double statusInterval = constants::kDefaultStatusInterval;
    std::vector<ActionSpec> actions;
    std::vector<ToggleSpec> toggles;
    bool updatesConfigured = false;
    bool autoUpdate = true;
    bool hasData = true;
    UpdateInfo update;
    std::string problem;
    std::string steamvrRestartFor;
};

bool isValidId(const std::string& id);
bool isValidElementId(const std::string& id);
bool isButtonStyle(const std::string& style);
bool isSafeRelativePath(const std::string& path);
std::string substituteApp(const std::string& argument, const std::string& appDir);
std::string clipText(const std::string& text, size_t maxBytes);
std::vector<std::string> moduleIds(const std::filesystem::path& panelDir);
bool parseModuleJson(const std::string& text, const std::string& appDir, Module& module, std::string* problem);
void applyManifest(const std::string& manifestJson, Module& module);
bool parseAutoUpdate(const std::string& settingsEnv);
UpdateInfo parseUpdateJson(const std::string& text);
int compareVersions(const std::string& a, const std::string& b);
void judgeAvailable(UpdateInfo& info, const std::string& installedVersion);
std::string parseSteamvrRestart(const std::string& text);
void markInterruptedUpdate(UpdateInfo& info, bool updateRunning);
bool updateRunOver(const UpdateInfo& info, double secondsSinceStart, double graceSeconds, bool updateRunning);
Module loadModule(const Paths& paths, const std::string& id);
std::vector<Module> loadModules(const Paths& paths);
std::vector<std::filesystem::path> watchedDirectories(const Paths& paths, const std::vector<std::string>& ids,
                                                      const std::vector<Module>& modules);

}
