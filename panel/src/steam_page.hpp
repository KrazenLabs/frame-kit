#pragma once

#include <memory>
#include <optional>
#include <string>

#include "common.hpp"
#include "page_bridge.hpp"

namespace fkp {

class SteamPage {
public:
    SteamPage(int port, std::string script, Logger log);

    int fd() const { return session_ ? session_->fd() : -1; }
    const std::string& state() const { return state_; }
    bool connected() const { return session_ != nullptr; }
    bool notifies() const { return session_ && notifies_; }
    double nextAttempt() const { return nextAttempt_; }
    bool connect(double monotonicNow);
    bool holds(const std::string& dataJson, const std::string& iconsJson) const;
    bool takePing();
    std::optional<PageReport> exchange(const std::string& dataJson, const std::string& iconsJson, double monotonicNow);
    void remove();

private:
    void drop(double monotonicNow, const std::string& state, const std::string& message);
    void report(const std::string& state, const std::string& message);
    void forgetSent();

    int port_;
    std::string script_;
    std::string version_;
    Logger log_;
    std::unique_ptr<DevToolsSession> session_;
    bool notifies_ = false;
    double nextAttempt_ = 0.0;
    std::string state_;
    std::string sentData_;
    std::string sentIcons_;
};

}
