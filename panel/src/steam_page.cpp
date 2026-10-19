#include "steam_page.hpp"

namespace fkp {

namespace {

constexpr const char* kBarOverlayKey = "valve.steam.gamepadui.bar";
constexpr double kRetrySeconds = 5.0;

}

SteamPage::SteamPage(int port, std::string script, Logger log)
    : port_(port), script_(std::move(script)), version_(scriptVersion(script_)), log_(std::move(log)) {}

void SteamPage::report(const std::string& state, const std::string& message) {
    if (state == state_) {
        return;
    }
    state_ = state;
    if (!message.empty()) {
        log_(message);
    }
}

void SteamPage::forgetSent() {
    sentData_.clear();
    sentIcons_.clear();
}

void SteamPage::drop(double monotonicNow, const std::string& state, const std::string& message) {
    session_.reset();
    notifies_ = false;
    forgetSent();
    nextAttempt_ = monotonicNow + 1.0;
    report(state, message);
}

bool SteamPage::connect(double monotonicNow) {
    if (session_) {
        return true;
    }
    if (monotonicNow < nextAttempt_) {
        return false;
    }
    nextAttempt_ = monotonicNow + kRetrySeconds;
    std::string error;
    const std::optional<std::string> targets = httpGetLocal(port_, "/json", &error);
    if (!targets) {
        report("unreachable", "Steam's UI debugger isn't reachable on 127.0.0.1:" + std::to_string(port_) + " (" +
                                  error + "); waiting for Steam");
        return false;
    }
    const std::optional<std::string> path = findOverlayPagePath(*targets, kBarOverlayKey, false);
    if (!path) {
        report("no-page", "Steam's VR bar page isn't open yet; waiting for it");
        return false;
    }
    session_ = DevToolsSession::open(port_, *path, &error);
    if (!session_) {
        report("unreachable", "could not attach to Steam's VR bar page: " + error);
        return false;
    }
    forgetSent();
    notifies_ = session_->addBinding(kNotifyBinding);
    if (!notifies_) {
        log_("Steam's bar page refused the notification binding; clicks will be noticed a little later");
    }
    return true;
}

bool SteamPage::holds(const std::string& dataJson, const std::string& iconsJson) const {
    return session_ && sentData_ == dataJson && sentIcons_ == iconsJson;
}

bool SteamPage::takePing() {
    if (!session_) {
        return false;
    }
    if (!session_->drainEvents()) {
        session_.reset();
        notifies_ = false;
        forgetSent();
        nextAttempt_ = 0.0;
        report("lost", "lost the connection to Steam's VR bar page; reconnecting");
        return true;
    }
    return session_->takeBindingCalls() > 0;
}

std::optional<PageReport> SteamPage::exchange(const std::string& dataJson, const std::string& iconsJson,
                                              double monotonicNow) {
    if (!connect(monotonicNow)) {
        return std::nullopt;
    }
    const std::string newData = dataJson == sentData_ ? std::string() : dataJson;
    const std::string newIcons = iconsJson == sentIcons_ ? std::string() : iconsJson;
    forgetSent();
    std::optional<EvaluationResult> result = session_->evaluate(updateExpression(version_, newData, newIcons));
    if (!result) {
        drop(monotonicNow, "lost", "lost the connection to Steam's VR bar page; reconnecting");
        return std::nullopt;
    }
    if (result->ok && result->value == "install-needed") {
        result = session_->evaluate(installExpression(script_, version_, dataJson, iconsJson));
        if (!result) {
            drop(monotonicNow, "lost", "lost the connection to Steam's VR bar page while installing; reconnecting");
            return std::nullopt;
        }
        if (result->ok) {
            log_("installed the Community Apps tab script in Steam's VR bar page (version " + version_.substr(0, 8) + ")");
        }
    }
    if (!result->ok) {
        report("script-error", "the page script failed in Steam's bar page: " + result->value);
        return std::nullopt;
    }
    PageReport parsed = parsePageReport(result->value);
    if (!parsed.valid) {
        report("bad-report", "unexpected answer from the page script: " + result->value.substr(0, 200));
        return std::nullopt;
    }
    sentData_ = dataJson;
    sentIcons_ = iconsJson;
    if (parsed.popup != "attached") {
        report("no-popup", "attached to Steam's bar page, but its Quick Access popup isn't reachable yet");
    } else {
        report("shown", "the Community Apps tab is in Steam's Quick Access popup");
    }
    return parsed;
}

void SteamPage::remove() {
    if (!session_ && !connect(nextAttempt_)) {
        return;
    }
    session_->evaluate(removeExpression());
    session_.reset();
    notifies_ = false;
    forgetSent();
}

}
