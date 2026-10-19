#include "page_bridge.hpp"

#include <json/json.h>

#include "common.hpp"

namespace fkp {

namespace {

constexpr const char* kInstallNeeded = "install-needed";
constexpr size_t kMaxActionsPerReport = 32;

std::string jsonString(const std::string& text) {
    Json::StreamWriterBuilder builder;
    builder["indentation"] = "";
    return Json::writeString(builder, Json::Value(text));
}

std::string jsonOrNull(const std::string& json) {
    return json.empty() ? "null" : json;
}

std::string stringField(const Json::Value& item, const char* name, size_t maxBytes) {
    if (!item[name].isString()) {
        return "";
    }
    const std::string value = item[name].asString();
    return value.size() > maxBytes ? "" : value;
}

}

bool isKnownOp(const std::string& op) {
    return op == "action" || op == "toggle" || op == "check" || op == "update" || op == "auto" || op == "uninstall" ||
           op == "restart-steamvr" || isGlobalOp(op);
}

bool isGlobalOp(const std::string& op) {
    return op == "forget-pcs";
}

std::string scriptVersion(const std::string& script) {
    return fnv1aHex(script);
}

std::string installExpression(const std::string& script, const std::string& version, const std::string& dataJson,
                              const std::string& iconsJson) {
    return "(" + trim(script) + ")(" + jsonString(version) + ").update(" + jsonOrNull(dataJson) + ", " +
           jsonOrNull(iconsJson) + ")";
}

std::string updateExpression(const std::string& version, const std::string& dataJson, const std::string& iconsJson) {
    return "(window.__fkp && window.__fkp.version === " + jsonString(version) + ") ? window.__fkp.update(" +
           jsonOrNull(dataJson) + ", " + jsonOrNull(iconsJson) + ") : \"" + kInstallNeeded + "\"";
}

std::string removeExpression() {
    return "(window.__fkp && typeof window.__fkp.dispose === 'function') ? window.__fkp.dispose() : 'absent'";
}

PageReport parsePageReport(const std::string& value) {
    PageReport report;
    if (value == kInstallNeeded) {
        report.installNeeded = true;
        return report;
    }
    const std::optional<Json::Value> parsed = parseJson(value);
    if (!parsed || !parsed->isObject()) {
        report.state = value;
        return report;
    }
    const Json::Value& root = *parsed;
    report.valid = true;
    report.state = root["state"].isString() ? root["state"].asString() : "";
    report.visible = root["visible"].isBool() && root["visible"].asBool();
    report.popupOpen = root["popupOpen"].isBool() && root["popupOpen"].asBool();
    report.tabActive = root["tabActive"].isBool() && root["tabActive"].asBool();
    report.view = stringField(root, "view", 60);
    report.popup = root["popup"].isString() ? root["popup"].asString() : "";
    for (const Json::Value& item : root["actions"]) {
        if (report.actions.size() >= kMaxActionsPerReport) {
            break;
        }
        if (!item.isObject() || !item["op"].isString() || !isKnownOp(item["op"].asString())) {
            continue;
        }
        PageAction action;
        action.op = item["op"].asString();
        action.module = stringField(item, "module", 60);
        action.id = stringField(item, "id", 60);
        action.on = item["on"].isBool() && item["on"].asBool();
        action.purge = item["purge"].isBool() && item["purge"].asBool();
        action.session = stringField(item, "session", 40);
        action.seq = item["seq"].isInt() ? item["seq"].asInt() : 0;
        if (action.module.empty() && !isGlobalOp(action.op)) {
            continue;
        }
        report.actions.push_back(action);
    }
    return report;
}

}
