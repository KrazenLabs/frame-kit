#pragma once

#include <map>
#include <optional>
#include <set>
#include <string>
#include <vector>

#include "modules.hpp"

namespace fkp {

struct StatusLine {
    std::string text;
    std::string tone;
};

struct StatusControls {
    std::optional<std::set<std::string>> actions;
    std::optional<std::set<std::string>> toggles;
    std::set<std::string> disabled;
};

struct ModuleRuntime {
    std::vector<StatusLine> lines;
    StatusControls controls;
    bool statusKnown = false;
    bool statusRunning = false;
    double nextStatus = 0.0;
    std::map<std::string, bool> toggleState;
    std::set<std::string> toggleBusy;
    bool toggleReadRunning = false;
    double nextToggleRead = 0.0;
    std::set<std::string> actionBusy;
    std::string note;
    std::string noteTone;
    double noteUntil = 0.0;
    bool checking = false;
    bool autoBusy = false;
    std::optional<bool> autoOverride;
    bool updating = false;
    double updateStarted = 0.0;
    bool removing = false;
    bool steamvrRestart = false;
    double removeStarted = 0.0;
};

struct Handled {
    std::string session;
    int seq = 0;
};

struct PairingView {
    int count = 0;
    std::string note;
    std::string noteTone;
};

std::vector<StatusLine> parseStatusOutput(const std::string& output);
StatusControls parseStatusControls(const std::string& output);
bool actionShown(const ModuleRuntime& state, const std::string& id);
bool toggleShown(const ModuleRuntime& state, const std::string& id);
bool controlDisabled(const ModuleRuntime& state, const std::string& id);
std::optional<bool> parseToggleState(const std::string& output);
std::string firstLine(const std::string& output, size_t maxBytes = 100);
std::string relativeAge(double secondsAgo);
std::string iconDataUrl(const std::string& svg);
std::string iconsJson(const std::vector<Module>& modules);
std::string viewJson(const std::vector<Module>& modules, const std::map<std::string, ModuleRuntime>& runtime,
                     double wallNow, double monotonicNow, const Handled& handled, const PairingView& pairing = {});

}
