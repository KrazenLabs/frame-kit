#include "panel_model.hpp"

#include <cmath>

#include <json/json.h>

#include "common.hpp"
#include "constants.hpp"

namespace fkp {

namespace {

constexpr size_t kMaxStatusLines = constants::kMaxStatusLines;
constexpr size_t kMaxStatusBytes = constants::kMaxStatusLineBytes;
constexpr size_t kMaxControlIds = constants::kMaxActions + constants::kMaxToggles;

std::string normalTone(const std::string& tone) {
    if (tone == "ok" || tone == "warn" || tone == "error") {
        return tone;
    }
    return "";
}

void repairStrings(Json::Value& value) {
    if (value.isString()) {
        value = repairUtf8(value.asString());
    } else if (value.isArray() || value.isObject()) {
        for (Json::Value& child : value) {
            repairStrings(child);
        }
    }
}

std::string compactJson(const Json::Value& value) {
    Json::StreamWriterBuilder builder;
    builder["indentation"] = "";
    return Json::writeString(builder, value);
}

bool isStatusJson(const Json::Value& root) {
    return root.isObject() &&
           (root.isMember("lines") || root.isMember("actions") || root.isMember("toggles") || root.isMember("disabled"));
}

}

std::vector<StatusLine> parseStatusOutput(const std::string& rawOutput) {
    std::vector<StatusLine> lines;
    const std::string output = repairUtf8(rawOutput);
    const std::string trimmed = trim(output);
    if (!trimmed.empty() && trimmed.front() == '{') {
        const std::optional<Json::Value> root = parseJson(trimmed);
        if (root && isStatusJson(*root)) {
            const Json::Value& items = member(*root, "lines");
            if (!items.isArray()) {
                return lines;
            }
            for (const Json::Value& item : items) {
                if (lines.size() >= kMaxStatusLines) {
                    break;
                }
                if (item.isString() && !trim(item.asString()).empty()) {
                    lines.push_back({clipText(trim(item.asString()), kMaxStatusBytes), ""});
                } else if (item.isObject() && item["text"].isString() && !trim(item["text"].asString()).empty()) {
                    lines.push_back({clipText(trim(item["text"].asString()), kMaxStatusBytes),
                                     item["tone"].isString() ? normalTone(item["tone"].asString()) : ""});
                }
            }
            return lines;
        }
    }
    for (const std::string& line : splitLines(output)) {
        const std::string text = trim(line);
        if (text.empty()) {
            continue;
        }
        lines.push_back({clipText(text, kMaxStatusBytes), ""});
        if (lines.size() >= kMaxStatusLines) {
            break;
        }
    }
    return lines;
}

namespace {

std::optional<std::set<std::string>> idList(const Json::Value& value) {
    if (!value.isArray()) {
        return std::nullopt;
    }
    std::set<std::string> ids;
    for (const Json::Value& item : value) {
        if (ids.size() >= kMaxControlIds) {
            break;
        }
        if (item.isString() && isValidElementId(item.asString())) {
            ids.insert(item.asString());
        }
    }
    return ids;
}

}

StatusControls parseStatusControls(const std::string& rawOutput) {
    StatusControls controls;
    const std::string trimmed = trim(repairUtf8(rawOutput));
    if (trimmed.empty() || trimmed.front() != '{') {
        return controls;
    }
    const std::optional<Json::Value> root = parseJson(trimmed);
    if (!root || !root->isObject()) {
        return controls;
    }
    controls.actions = idList(member(*root, "actions"));
    controls.toggles = idList(member(*root, "toggles"));
    controls.disabled = idList(member(*root, "disabled")).value_or(std::set<std::string>());
    return controls;
}

bool actionShown(const ModuleRuntime& state, const std::string& id) {
    return !state.controls.actions || state.controls.actions->count(id) > 0;
}

bool toggleShown(const ModuleRuntime& state, const std::string& id) {
    return !state.controls.toggles || state.controls.toggles->count(id) > 0;
}

bool controlDisabled(const ModuleRuntime& state, const std::string& id) {
    return state.controls.disabled.count(id) > 0;
}

std::optional<bool> parseToggleState(const std::string& output) {
    const std::vector<std::string> words = splitWhitespace(output);
    if (words.empty()) {
        return std::nullopt;
    }
    const std::string word = toLower(words.front());
    if (word == "on" || word == "1" || word == "true" || word == "yes" || word == "enabled") {
        return true;
    }
    if (word == "off" || word == "0" || word == "false" || word == "no" || word == "disabled") {
        return false;
    }
    return std::nullopt;
}

std::string firstLine(const std::string& output, size_t maxBytes) {
    static const std::string okLine = std::string(constants::kResultPrefix) + " " + constants::kResultOk;
    static const std::string errorLine = std::string(constants::kResultPrefix) + " " + constants::kResultError + " ";
    for (const std::string& line : splitLines(repairUtf8(output))) {
        const std::string text = trim(line);
        if (!text.empty() && !startsWith(text, okLine)) {
            if (startsWith(text, errorLine)) {
                return clipText(text.substr(errorLine.size()), maxBytes);
            }
            return clipText(text, maxBytes);
        }
    }
    return "";
}

std::string relativeAge(double secondsAgo) {
    if (secondsAgo < 90.0) {
        return "just now";
    }
    const long minutes = std::lround(secondsAgo / 60.0);
    if (minutes < 90) {
        return std::to_string(minutes) + " mins ago";
    }
    const long hours = std::lround(secondsAgo / 3600.0);
    if (hours < 36) {
        return std::to_string(hours) + " h ago";
    }
    const long days = std::lround(secondsAgo / 86400.0);
    return std::to_string(days) + (days == 1 ? " day ago" : " days ago");
}

std::string iconDataUrl(const std::string& svg) {
    if (svg.empty()) {
        return "";
    }
    return "data:image/svg+xml;base64," + base64Encode(svg);
}

std::string iconsJson(const std::vector<Module>& modules) {
    Json::Value icons(Json::objectValue);
    for (const Module& module : modules) {
        if (!module.iconKey.empty()) {
            icons[module.iconKey] = iconDataUrl(module.iconSvg);
        }
    }
    return compactJson(icons);
}

std::string viewJson(const std::vector<Module>& modules, const std::map<std::string, ModuleRuntime>& runtime,
                     double wallNow, double monotonicNow, const Handled& handled, const PairingView& pairing) {
    Json::Value root(Json::objectValue);
    Json::Value apps(Json::arrayValue);
    static const ModuleRuntime empty;
    for (const Module& module : modules) {
        const auto found = runtime.find(module.id);
        const ModuleRuntime& state = found == runtime.end() ? empty : found->second;
        Json::Value app(Json::objectValue);
        app["id"] = module.id;
        app["name"] = module.name;
        app["version"] = module.version;
        app["description"] = module.description;
        app["icon"] = module.iconKey;
        app["problem"] = module.problem;
        app["statusKnown"] = state.statusKnown;
        app["hasStatus"] = !module.statusCommand.empty();
        Json::Value lines(Json::arrayValue);
        for (const StatusLine& line : state.lines) {
            Json::Value item(Json::objectValue);
            item["text"] = line.text;
            item["tone"] = line.tone;
            lines.append(item);
        }
        app["lines"] = lines;
        Json::Value actions(Json::arrayValue);
        for (const ActionSpec& action : module.actions) {
            if (!actionShown(state, action.id)) {
                continue;
            }
            Json::Value item(Json::objectValue);
            item["id"] = action.id;
            item["label"] = action.label;
            item["confirm"] = action.confirm;
            item["busy"] = state.actionBusy.count(action.id) > 0;
            item["disabled"] = controlDisabled(state, action.id);
            item["style"] = action.style;
            item["wide"] = action.wide;
            actions.append(item);
        }
        app["actions"] = actions;
        Json::Value toggles(Json::arrayValue);
        for (const ToggleSpec& toggle : module.toggles) {
            if (!toggleShown(state, toggle.id)) {
                continue;
            }
            Json::Value item(Json::objectValue);
            item["id"] = toggle.id;
            item["label"] = toggle.label;
            item["disabled"] = controlDisabled(state, toggle.id);
            const auto value = state.toggleState.find(toggle.id);
            item["known"] = value != state.toggleState.end();
            item["on"] = value != state.toggleState.end() && value->second;
            item["busy"] = state.toggleBusy.count(toggle.id) > 0;
            toggles.append(item);
        }
        app["toggles"] = toggles;
        Json::Value note(Json::objectValue);
        const bool noteShown = !state.note.empty() && monotonicNow < state.noteUntil;
        note["text"] = noteShown ? state.note : "";
        note["tone"] = noteShown ? state.noteTone : "";
        app["note"] = note;
        Json::Value update(Json::objectValue);
        update["enabled"] = module.updatesConfigured;
        update["auto"] = state.autoOverride.value_or(module.autoUpdate);
        update["available"] = module.update.available && !module.update.latest.empty();
        update["latest"] = module.update.latest;
        update["state"] = module.update.state;
        update["message"] = module.update.message;
        update["checkedText"] =
            module.update.known && module.update.checked > 0.0 ? relativeAge(wallNow - module.update.checked) : "";
        update["checking"] = state.checking;
        update["autoBusy"] = state.autoBusy;
        update["updating"] = state.updating || module.update.state == "updating";
        app["update"] = update;
        app["removing"] = state.removing;
        app["hasData"] = module.hasData;
        app["steamvrRestart"] = state.steamvrRestart && !state.removing;
        apps.append(app);
    }
    root["apps"] = apps;
    Json::Value paired(Json::objectValue);
    paired["count"] = pairing.count;
    paired["note"] = pairing.note;
    paired["noteTone"] = pairing.noteTone;
    root["pairing"] = paired;
    Json::Value ack(Json::objectValue);
    ack["session"] = handled.session;
    ack["seq"] = handled.seq;
    root["handled"] = ack;
    repairStrings(root);
    return compactJson(root);
}

}
