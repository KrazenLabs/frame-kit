#pragma once

#include <string>
#include <vector>

namespace fkp {

constexpr const char* kNotifyBinding = "__fkpNotify";

struct PageAction {
    std::string op;
    std::string module;
    std::string id;
    bool on = false;
    bool purge = false;
    std::string session;
    int seq = 0;
};

struct PageReport {
    bool valid = false;
    bool installNeeded = false;
    std::string state;
    bool visible = false;
    bool popupOpen = false;
    bool tabActive = false;
    std::string view;
    std::string popup;
    std::vector<PageAction> actions;
};

std::string scriptVersion(const std::string& script);
std::string installExpression(const std::string& script, const std::string& version, const std::string& dataJson,
                              const std::string& iconsJson);
std::string updateExpression(const std::string& version, const std::string& dataJson, const std::string& iconsJson);
std::string removeExpression();
PageReport parsePageReport(const std::string& value);
bool isKnownOp(const std::string& op);
bool isGlobalOp(const std::string& op);

}
