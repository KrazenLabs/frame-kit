#include "service.hpp"

#include <algorithm>
#include <cerrno>
#include <cmath>
#include <cstdio>
#include <exception>
#include <limits>
#include <map>
#include <optional>
#include <set>
#include <vector>

#include <fcntl.h>
#include <poll.h>
#include <sys/file.h>
#include <unistd.h>

#include <json/json.h>

#include "common.hpp"
#include "constants.hpp"
#include "modules.hpp"
#include "pairing.hpp"
#include "panel_js.hpp"
#include "panel_model.hpp"
#include "runner.hpp"
#include "steam_page.hpp"
#include "watcher.hpp"

namespace fkp {

namespace {

constexpr int kCefPort = 8080;
constexpr double kNever = std::numeric_limits<double>::infinity();
constexpr double kChangeSettleSeconds = 0.3;
constexpr double kWatchedReloadSeconds = 60.0;
constexpr double kPolledReloadSeconds = 2.0;
constexpr double kEmptyExitSeconds = 15.0;
constexpr double kFirstSweepSeconds = 120.0;
constexpr double kSweepSeconds = 3600.0;
constexpr double kStatusTimeout = constants::kStatusTimeoutSeconds;
constexpr double kActionTimeout = constants::kActionTimeoutSeconds;
constexpr double kCommandTimeout = 10.0;
constexpr double kCheckTimeout = 90.0;
constexpr double kToggleReadSeconds = 10.0;
constexpr double kNoteSeconds = 8.0;
constexpr double kUpdateGraceSeconds = 8.0;
constexpr double kUpdateTimeoutSeconds = 900.0;
constexpr double kRemoveTimeoutSeconds = 120.0;
constexpr double kStatusFileSeconds = 15.0;
constexpr double kStatusFileStaleSeconds = 3 * kStatusFileSeconds;
constexpr double kOpenExchangeSeconds = 0.5;
constexpr double kVisibleExchangeSeconds = 2.0;
constexpr double kHiddenExchangeSeconds = 5.0;
constexpr double kNotifiedOpenExchangeSeconds = 5.0;
constexpr double kNotifiedIdleExchangeSeconds = 15.0;
constexpr double kJobPollSeconds = 0.5;
constexpr double kMaxSleepSeconds = 60.0;
constexpr double kStaleStatusSeconds = 20.0;
constexpr double kErrorLogSeconds = 60.0;

std::string compactJson(const Json::Value& value) {
    Json::StreamWriterBuilder builder;
    builder["indentation"] = "";
    return Json::writeString(builder, value);
}

bool fileLocked(const std::filesystem::path& path) {
    const int fd = ::open(path.c_str(), O_RDONLY | O_CLOEXEC);
    if (fd < 0) {
        return false;
    }
    const bool locked = ::flock(fd, LOCK_SH | LOCK_NB) != 0 && errno == EWOULDBLOCK;
    ::close(fd);
    return locked;
}

bool failed(const JobResult& result) {
    return result.failedToStart || result.timedOut || result.exitCode != 0;
}

std::string failureText(const JobResult& result, const std::string& fallback) {
    if (result.timedOut) {
        return fallback + " (took too long)";
    }
    const std::string line = firstLine(result.output);
    return line.empty() ? fallback : line;
}

class Service {
public:
    Service(const Paths& paths, std::string version)
        : paths_(paths),
          version_(std::move(version)),
          page_(kCefPort, kPanelScript, log),
          runner_([this](const std::string& job, const std::string& error) {
              reportError("handling the result of " + job, error);
          }) {}

    int run();

private:
    template <typename Step>
    void guarded(const std::string& step, Step&& work) {
        try {
            work();
        } catch (const std::exception& error) {
            reportError(step, error.what());
        } catch (...) {
            reportError(step, "unknown error");
        }
    }

    void reportError(const std::string& step, const std::string& error);
    void reload(double now);
    void watchDirectories(const std::vector<std::filesystem::path>& directories);
    void schedule(double now);
    std::string view(double now) const;
    double exchangeInterval() const;
    double nextNoteExpiry(double now) const;
    double nextWake(double now) const;
    void exchange(double now);
    void exchangeIfChanged(double now);
    void pageGone();
    void handle(const PageAction& action, double now);
    const Module* find(const std::string& id) const;
    void startStatus(const std::string& id, const std::vector<std::string>& command, double interval);
    void readToggle(const std::string& id, size_t index);
    void continueSweep();
    void setNote(const std::string& id, const std::string& text, const std::string& tone);
    void forgetPairedPcs();
    void checkSteamvr();
    void applySteamvr();
    void writeStatusFile();
    std::vector<std::string> detached(const std::string& op, const Module& module,
                                      const std::vector<std::string>& command) const;
    std::string script(const Module& module, const char* name) const;

    Paths paths_;
    std::string version_;
    SteamPage page_;
    Runner runner_;
    DirectoryWatcher watcher_;
    std::string watchProblem_;
    std::vector<Module> modules_;
    std::string iconsJson_ = "{}";
    std::map<std::string, ModuleRuntime> runtime_;
    std::map<std::string, std::string> loggedProblems_;
    std::map<std::string, double> errorLogged_;
    int pairedPcs_ = 0;
    std::string pairingNote_;
    std::string pairingNoteTone_;
    double pairingNoteUntil_ = 0.0;
    Handled handled_;
    bool popupOpen_ = false;
    bool tabActive_ = false;
    bool visible_ = false;
    std::string view_;
    double nextReload_ = 0.0;
    double nextExchange_ = 0.0;
    double nextStatusFile_ = 0.0;
    double nextSweep_ = 0.0;
    double nextSchedule_ = kNever;
    double noteExpiry_ = kNever;
    double emptySince_ = -1.0;
    std::vector<std::string> sweepQueue_;
    bool sweepRunning_ = false;
    bool exchangeSoon_ = false;
    bool viewChanged_ = false;
    std::string steamvrRun_;
    bool steamvrCheckRunning_ = false;
};

const Module* Service::find(const std::string& id) const {
    for (const Module& module : modules_) {
        if (module.id == id) {
            return &module;
        }
    }
    return nullptr;
}

void Service::reportError(const std::string& step, const std::string& error) {
    const double now = monotonicSeconds();
    const auto last = errorLogged_.find(step);
    if (last != errorLogged_.end() && now - last->second < kErrorLogSeconds) {
        return;
    }
    errorLogged_[step] = now;
    log(step + " failed: " + error);
}

std::string Service::script(const Module& module, const char* name) const {
    return (module.appDir / name).string();
}

std::vector<std::string> Service::detached(const std::string& op, const Module& module,
                                           const std::vector<std::string>& command) const {
    std::vector<std::string> argv = {
        "systemd-run",
        "--user",
        "--collect",
        "--quiet",
        "--unit=framekit-" + op + "-" + module.id + "-" + std::to_string(static_cast<long long>(wallSeconds())),
        "--description=FrameKit " + op + ": " + module.name,
    };
    argv.insert(argv.end(), command.begin(), command.end());
    return argv;
}

void Service::setNote(const std::string& id, const std::string& text, const std::string& tone) {
    ModuleRuntime& state = runtime_[id];
    state.note = capitalizeFirst(text);
    state.noteTone = tone;
    state.noteUntil = monotonicSeconds() + kNoteSeconds;
    viewChanged_ = true;
}

void Service::watchDirectories(const std::vector<std::filesystem::path>& directories) {
    watcher_.watch(directories);
    if (watcher_.problem() == watchProblem_) {
        return;
    }
    watchProblem_ = watcher_.problem();
    if (watchProblem_.empty()) {
        log("watching the apps' folders for changes again");
    } else {
        log("can't watch the apps' folders for changes (" + watchProblem_ + "); reading them every " +
            std::to_string(static_cast<int>(kPolledReloadSeconds)) + " s instead");
    }
}

void Service::reload(double now) {
    nextReload_ = now + kPolledReloadSeconds;
    const std::vector<std::string> ids = moduleIds(paths_.panelDir);
    const std::vector<std::filesystem::path> watched = watchedDirectories(paths_, ids, modules_);
    watchDirectories(watched);
    std::vector<Module> fresh;
    for (const std::string& id : ids) {
        fresh.push_back(loadModule(paths_, id));
    }
    const std::vector<std::filesystem::path> watchedNow = watchedDirectories(paths_, ids, fresh);
    if (watchedNow != watched) {
        watchDirectories(watchedNow);
    }
    for (Module& module : fresh) {
        if (module.update.state == "updating") {
            markInterruptedUpdate(module.update,
                                  fileLocked(module.appHome / constants::kAppStateDir / constants::kUpdateLockFile));
        }
    }
    const std::set<std::string> present(ids.begin(), ids.end());
    for (const Module& old : modules_) {
        if (!present.count(old.id)) {
            log("removed from the panel: " + old.name);
            runtime_.erase(old.id);
            loggedProblems_.erase(old.id);
        }
    }
    for (const Module& module : fresh) {
        const Module* old = find(module.id);
        if (!old) {
            log("in the panel: " + module.name + " " + module.version);
        } else if (old->version != module.version) {
            log(module.name + " is now version " + module.version);
        }
        if (loggedProblems_[module.id] != module.problem) {
            loggedProblems_[module.id] = module.problem;
            if (!module.problem.empty()) {
                log(module.name + ": " + module.problem);
            }
        }
        ModuleRuntime& state = runtime_[module.id];
        if (!state.autoBusy) {
            state.autoOverride.reset();
        }
        if (state.updating) {
            const bool settled =
                updateRunOver(module.update, now - state.updateStarted, kUpdateGraceSeconds,
                              fileLocked(module.appHome / constants::kAppStateDir / constants::kUpdateLockFile));
            if (settled || now - state.updateStarted > kUpdateTimeoutSeconds) {
                state.updating = false;
                if (module.update.state == "failed" || module.update.state == "error") {
                    setNote(module.id, module.update.message.empty() ? "Update failed" : module.update.message,
                            "error");
                } else if (!module.update.message.empty()) {
                    setNote(module.id, module.update.message, "ok");
                }
            }
        }
        if (state.removing && now - state.removeStarted > kRemoveTimeoutSeconds) {
            state.removing = false;
            setNote(module.id, "Failed to remove. Try again or use the PC setup to remove.",
                    "error");
        }
    }
    bool busy = false;
    for (const Module& module : fresh) {
        const ModuleRuntime& state = runtime_[module.id];
        busy = busy || state.updating || state.removing || module.update.state == "updating";
    }
    pairedPcs_ = countPairedPcs(paths_.home);
    modules_ = std::move(fresh);
    iconsJson_ = iconsJson(modules_);
    viewChanged_ = true;
    nextReload_ = now + (busy || !watcher_.watching() ? kPolledReloadSeconds : kWatchedReloadSeconds);
    applySteamvr();
    checkSteamvr();
}

void Service::applySteamvr() {
    for (const Module& module : modules_) {
        ModuleRuntime& state = runtime_[module.id];
        const bool pending = !module.steamvrRestartFor.empty() && module.steamvrRestartFor == steamvrRun_;
        if (state.steamvrRestart != pending) {
            state.steamvrRestart = pending;
            viewChanged_ = true;
        }
    }
}

void Service::checkSteamvr() {
    const bool wanted = std::any_of(modules_.begin(), modules_.end(),
                                    [](const Module& module) { return !module.steamvrRestartFor.empty(); });
    if (!wanted || steamvrCheckRunning_) {
        return;
    }
    steamvrCheckRunning_ = true;
    runner_.start({"systemctl", "--user", "show", "-p", "InvocationID", "--value", constants::kSteamvrUnit}, kStatusTimeout,
                  [this](const JobResult& result) {
                      steamvrCheckRunning_ = false;
                      steamvrRun_ = failed(result) ? std::string() : parseSteamvrRestart(result.output);
                      applySteamvr();
                  });
}

void Service::startStatus(const std::string& id, const std::vector<std::string>& command, double interval) {
    ModuleRuntime& state = runtime_[id];
    state.statusRunning = true;
    state.nextStatus = monotonicSeconds() + interval;
    runner_.start(command, kStatusTimeout, [this, id, interval](const JobResult& result) {
        const auto found = runtime_.find(id);
        if (found == runtime_.end()) {
            return;
        }
        ModuleRuntime& state = found->second;
        state.statusRunning = false;
        state.statusKnown = true;
        state.nextStatus = monotonicSeconds() + interval;
        if (result.failedToStart || result.timedOut) {
            state.lines = {{failureText(result, "Status unavailable"), "error"}};
            state.controls = {};
        } else {
            state.lines = parseStatusOutput(result.output);
            state.controls = parseStatusControls(result.output);
            if (state.lines.empty() && result.exitCode != 0) {
                state.lines = {{"Status unavailable (exit " + std::to_string(result.exitCode) + ")", "error"}};
            }
        }
        viewChanged_ = true;
    });
}

void Service::readToggle(const std::string& id, size_t index) {
    const Module* module = find(id);
    const auto found = runtime_.find(id);
    if (!module || found == runtime_.end()) {
        return;
    }
    if (index >= module->toggles.size()) {
        found->second.toggleReadRunning = false;
        viewChanged_ = true;
        return;
    }
    const ToggleSpec spec = module->toggles[index];
    runner_.start(spec.get, kStatusTimeout, [this, id, index, toggleId = spec.id](const JobResult& result) {
        const auto state = runtime_.find(id);
        if (state == runtime_.end()) {
            return;
        }
        if (!state->second.toggleBusy.count(toggleId) && !failed(result)) {
            const std::optional<bool> value = parseToggleState(result.output);
            if (value) {
                state->second.toggleState[toggleId] = *value;
            }
        }
        readToggle(id, index + 1);
    });
}

void Service::continueSweep() {
    while (!sweepQueue_.empty()) {
        const std::string id = sweepQueue_.back();
        sweepQueue_.pop_back();
        const Module* module = find(id);
        if (!module || !module->updatesConfigured || runtime_[id].checking || runtime_[id].updating) {
            continue;
        }
        sweepRunning_ = true;
        runner_.start({"/bin/sh", script(*module, constants::kUpdateScript), "check", "--if-due"}, kCheckTimeout,
                      [this](const JobResult&) {
                          sweepRunning_ = false;
                          nextReload_ = 0.0;
                          continueSweep();
                      });
        return;
    }
}

void Service::schedule(double now) {
    nextSchedule_ = kNever;
    const bool active = popupOpen_ && tabActive_;
    for (const Module& module : modules_) {
        ModuleRuntime& state = runtime_[module.id];
        if (active && !module.statusCommand.empty() && !state.statusRunning) {
            if (now >= state.nextStatus) {
                startStatus(module.id, module.statusCommand, module.statusInterval);
            } else {
                nextSchedule_ = std::min(nextSchedule_, state.nextStatus);
            }
        }
        if (active && view_ == module.id && !module.toggles.empty() && !state.toggleReadRunning &&
            state.toggleBusy.empty()) {
            if (now >= state.nextToggleRead) {
                state.toggleReadRunning = true;
                state.nextToggleRead = now + kToggleReadSeconds;
                readToggle(module.id, 0);
            } else {
                nextSchedule_ = std::min(nextSchedule_, state.nextToggleRead);
            }
        }
        if (state.statusKnown && !active && now - state.nextStatus > kStaleStatusSeconds) {
            state.statusKnown = false;
        }
    }
    if (now >= nextSweep_ && !sweepRunning_) {
        nextSweep_ = now + kSweepSeconds;
        sweepQueue_.clear();
        for (const Module& module : modules_) {
            if (module.updatesConfigured) {
                sweepQueue_.push_back(module.id);
            }
        }
        continueSweep();
    }
    if (!sweepRunning_) {
        nextSchedule_ = std::min(nextSchedule_, nextSweep_);
    }
}

void Service::handle(const PageAction& action, double now) {
    if (!action.session.empty()) {
        if (action.session != handled_.session) {
            handled_.session = action.session;
            handled_.seq = 0;
        }
        handled_.seq = std::max(handled_.seq, action.seq);
    }
    if (action.op == "forget-pcs") {
        forgetPairedPcs();
        return;
    }
    const Module* found = find(action.module);
    if (!found) {
        return;
    }
    const Module module = *found;
    const std::string id = module.id;
    ModuleRuntime& state = runtime_[id];
    if (state.removing) {
        return;
    }

    if (action.op == "action") {
        const auto spec = std::find_if(module.actions.begin(), module.actions.end(),
                                       [&](const ActionSpec& a) { return a.id == action.id; });
        if (spec == module.actions.end() || state.actionBusy.count(action.id) || !actionShown(state, action.id) ||
            controlDisabled(state, action.id)) {
            return;
        }
        state.actionBusy.insert(action.id);
        log(module.name + ": " + spec->label);
        runner_.start(spec->command, kActionTimeout, [this, id, actionId = spec->id, label = spec->label](const JobResult& result) {
            const auto it = runtime_.find(id);
            if (it == runtime_.end()) {
                return;
            }
            it->second.actionBusy.erase(actionId);
            if (failed(result)) {
                setNote(id, label + ": " + failureText(result, "failed"), "error");
            } else {
                const std::string line = firstLine(result.output);
                setNote(id, line.empty() ? label + ": done" : line, "");
            }
            it->second.nextStatus = 0.0;
            it->second.nextToggleRead = 0.0;
        });
        return;
    }

    if (action.op == "toggle") {
        const auto spec = std::find_if(module.toggles.begin(), module.toggles.end(),
                                       [&](const ToggleSpec& t) { return t.id == action.id; });
        if (spec == module.toggles.end() || state.toggleBusy.count(action.id) || !toggleShown(state, action.id) ||
            controlDisabled(state, action.id)) {
            return;
        }
        state.toggleBusy.insert(action.id);
        state.toggleState[action.id] = action.on;
        runner_.start(action.on ? spec->on : spec->off, kActionTimeout,
                      [this, id, toggleId = spec->id, label = spec->label](const JobResult& result) {
                          const auto it = runtime_.find(id);
                          if (it == runtime_.end()) {
                              return;
                          }
                          it->second.toggleBusy.erase(toggleId);
                          if (failed(result)) {
                              it->second.toggleState.erase(toggleId);
                              setNote(id, label + ": " + failureText(result, "failed to toggle"), "error");
                          }
                          it->second.nextToggleRead = 0.0;
                          it->second.nextStatus = 0.0;
                          viewChanged_ = true;
                      });
        return;
    }

    if (action.op == "check") {
        if (!module.updatesConfigured || state.checking || state.updating) {
            return;
        }
        state.checking = true;
        runner_.start({"/bin/sh", script(module, constants::kUpdateScript), "check"}, kCheckTimeout,
                      [this, id](const JobResult& result) {
                          const auto it = runtime_.find(id);
                          if (it == runtime_.end()) {
                              return;
                          }
                          it->second.checking = false;
                          if (failed(result)) {
                              setNote(id, failureText(result, "The update check failed"), "error");
                          }
                          nextReload_ = 0.0;
                          viewChanged_ = true;
                      });
        return;
    }

    if (action.op == "auto") {
        if (state.autoBusy) {
            return;
        }
        state.autoBusy = true;
        state.autoOverride = action.on;
        runner_.start({"/bin/sh", script(module, constants::kUpdateScript), "auto", action.on ? "on" : "off"}, kCommandTimeout,
                      [this, id](const JobResult& result) {
                          const auto it = runtime_.find(id);
                          if (it == runtime_.end()) {
                              return;
                          }
                          it->second.autoBusy = false;
                          if (failed(result)) {
                              it->second.autoOverride.reset();
                              setNote(id, failureText(result, "The setting could not be saved"), "error");
                          }
                          nextReload_ = 0.0;
                          viewChanged_ = true;
                      });
        return;
    }

    if (action.op == "update") {
        if (!module.updatesConfigured || state.updating) {
            return;
        }
        state.updating = true;
        state.updateStarted = now;
        nextReload_ = std::min(nextReload_, now + kPolledReloadSeconds);
        log(module.name + ": updating to " + module.update.latest);
        runner_.start(detached("update", module, {"/bin/sh", script(module, constants::kUpdateScript), "apply"}), kCommandTimeout,
                      [this, id](const JobResult& result) {
                          const auto it = runtime_.find(id);
                          if (it == runtime_.end()) {
                              return;
                          }
                          if (failed(result)) {
                              it->second.updating = false;
                              setNote(id, "Failed to start update: " + failureText(result, "systemd-run failed"),
                                      "error");
                          }
                      });
        return;
    }

    if (action.op == "restart-steamvr") {
        if (!state.steamvrRestart) {
            return;
        }
        log(module.name + ": restarting SteamVR");
        for (auto& entry : runtime_) {
            entry.second.steamvrRestart = false;
        }
        steamvrRun_.clear();
        runner_.start({"systemctl", "--user", "--no-block", "restart", constants::kSteamvrUnit}, kCommandTimeout,
                      [this, id](const JobResult& result) {
                          if (failed(result)) {
                              setNote(id, "Failed to restart SteamVR: " + failureText(result, "systemctl failed"), "error");
                              checkSteamvr();
                          }
                          viewChanged_ = true;
                      });
        return;
    }

    if (action.op == "uninstall") {
        if (state.removing || state.updating || module.update.state == "updating") {
            return;
        }
        state.removing = true;
        state.removeStarted = now;
        nextReload_ = std::min(nextReload_, now + kPolledReloadSeconds);
        log(module.name + ": removing" + std::string(action.purge ? " with its data" : ""));
        std::vector<std::string> command = {"/bin/sh", script(module, constants::kUninstallScript)};
        if (action.purge) {
            command.push_back("--purge");
        }
        runner_.start(detached("uninstall", module, command), kCommandTimeout, [this, id](const JobResult& result) {
            const auto it = runtime_.find(id);
            if (it == runtime_.end()) {
                return;
            }
            if (failed(result)) {
                it->second.removing = false;
                setNote(id, "Failed to initiate removal: " + failureText(result, "systemd-run failed"), "error");
            }
        });
    }
}

void Service::forgetPairedPcs() {
    int removed = 0;
    std::string error;
    if (fkp::forgetPairedPcs(paths_.home, &removed, &error)) {
        log("forgot " + std::to_string(removed) + " paired PC" + (removed == 1 ? "" : "s"));
        pairingNote_ = removed == 1 ? "Forgot 1 paired PC." : "Forgot " + std::to_string(removed) + " paired PCs.";
        pairingNoteTone_ = "ok";
    } else {
        log("forgetting the paired PCs failed: " + error);
        pairingNote_ = "Failed to forget paired PCs: " + error;
        pairingNoteTone_ = "error";
    }
    pairingNoteUntil_ = monotonicSeconds() + kNoteSeconds;
    pairedPcs_ = countPairedPcs(paths_.home);
    viewChanged_ = true;
}

std::string Service::view(double now) const {
    PairingView pairing;
    pairing.count = pairedPcs_;
    if (now < pairingNoteUntil_) {
        pairing.note = pairingNote_;
        pairing.noteTone = pairingNoteTone_;
    }
    return viewJson(modules_, runtime_, wallSeconds(), now, handled_, pairing);
}

double Service::exchangeInterval() const {
    if (page_.notifies()) {
        return popupOpen_ ? kNotifiedOpenExchangeSeconds : kNotifiedIdleExchangeSeconds;
    }
    return popupOpen_ ? kOpenExchangeSeconds : visible_ ? kVisibleExchangeSeconds : kHiddenExchangeSeconds;
}

void Service::pageGone() {
    popupOpen_ = false;
    tabActive_ = false;
    visible_ = false;
}

void Service::exchange(double now) {
    exchangeSoon_ = false;
    viewChanged_ = false;
    nextExchange_ = now + kVisibleExchangeSeconds;
    if (!page_.connect(now)) {
        pageGone();
        nextExchange_ = std::max(page_.nextAttempt(), now + kVisibleExchangeSeconds);
        return;
    }
    const std::optional<PageReport> report = page_.exchange(view(now), iconsJson_, now);
    if (!report) {
        pageGone();
        nextExchange_ = std::max(page_.nextAttempt(), now + kVisibleExchangeSeconds);
        return;
    }
    const bool wasActive = popupOpen_ && tabActive_;
    const bool wasOpen = popupOpen_;
    const std::string oldView = view_;
    popupOpen_ = report->popupOpen;
    if (popupOpen_ && !wasOpen) {
        checkSteamvr();
    }
    tabActive_ = report->tabActive;
    visible_ = report->visible;
    view_ = report->view;
    if (popupOpen_ && tabActive_ && (!wasActive || view_ != oldView)) {
        for (auto& [id, state] : runtime_) {
            state.nextStatus = std::min(state.nextStatus, now);
            state.nextToggleRead = std::min(state.nextToggleRead, now);
        }
        nextSchedule_ = now;
    }
    for (const PageAction& action : report->actions) {
        handle(action, now);
    }
    if (!report->actions.empty()) {
        viewChanged_ = true;
    }
    nextExchange_ = now + exchangeInterval();
}

void Service::exchangeIfChanged(double now) {
    viewChanged_ = false;
    if (page_.connected() && !page_.holds(view(now), iconsJson_)) {
        exchange(now);
    }
}

double Service::nextNoteExpiry(double now) const {
    double next = kNever;
    for (const auto& [id, state] : runtime_) {
        if (state.noteUntil > now) {
            next = std::min(next, state.noteUntil);
        }
    }
    if (pairingNoteUntil_ > now) {
        next = std::min(next, pairingNoteUntil_);
    }
    return next;
}

double Service::nextWake(double now) const {
    double next = std::min({nextExchange_, nextReload_, nextStatusFile_, nextSchedule_, noteExpiry_,
                            now + kMaxSleepSeconds});
    if (runner_.running()) {
        next = std::min(next, now + kJobPollSeconds);
    }
    if (emptySince_ >= 0.0) {
        next = std::min(next, emptySince_ + kEmptyExitSeconds + 0.1);
    }
    return next;
}

void Service::writeStatusFile() {
    Json::Value root(Json::objectValue);
    root["updated"] = wallSeconds();
    root["version"] = version_;
    root["pid"] = static_cast<int>(::getpid());
    root["page"] = page_.state();
    root["popupOpen"] = popupOpen_;
    root["tabActive"] = tabActive_;
    root["pairedPcs"] = pairedPcs_;
    Json::Value apps(Json::arrayValue);
    for (const Module& module : modules_) {
        Json::Value app(Json::objectValue);
        app["id"] = module.id;
        app["name"] = module.name;
        app["version"] = module.version;
        app["problem"] = module.problem;
        app["updates"] = module.updatesConfigured;
        app["auto"] = module.autoUpdate;
        app["available"] = module.update.available;
        app["latest"] = module.update.latest;
        app["checked"] = module.update.checked;
        app["updateState"] = module.update.state;
        app["updateMessage"] = module.update.message;
        apps.append(app);
    }
    root["apps"] = apps;
    writeFileAtomically(paths_.status, compactJson(root) + "\n");
}

int Service::run() {
    std::error_code ignored;
    std::filesystem::create_directories(paths_.runtimeDir, ignored);
    const int lockFd = ::open(paths_.lock.c_str(), O_RDWR | O_CREAT | O_CLOEXEC, 0600);
    if (lockFd < 0 || ::flock(lockFd, LOCK_EX | LOCK_NB) != 0) {
        log("another FrameKit panel is already running; exiting");
        if (lockFd >= 0) {
            ::close(lockFd);
        }
        return 0;
    }
    log("FrameKit panel " + version_ + " started");
    nextSweep_ = monotonicSeconds() + kFirstSweepSeconds;

    while (!stopRequested()) {
        double now = monotonicSeconds();
        if (watcher_.takeChanges()) {
            nextReload_ = std::min(nextReload_, now + kChangeSettleSeconds);
        }
        if (now >= nextReload_) {
            guarded("reading the apps", [&] { reload(now); });
        }
        if (modules_.empty()) {
            if (emptySince_ < 0.0) {
                emptySince_ = now;
            } else if (now - emptySince_ > kEmptyExitSeconds) {
                log("no app uses the panel any more; exiting");
                break;
            }
        } else {
            emptySince_ = -1.0;
        }
        guarded("starting commands", [&] { schedule(now); });
        if (now >= noteExpiry_) {
            viewChanged_ = true;
        }
        if (exchangeSoon_ || now >= nextExchange_) {
            guarded("updating the tab", [&] { exchange(now); });
        } else if (viewChanged_) {
            guarded("updating the tab", [&] { exchangeIfChanged(now); });
        }
        if (now >= nextStatusFile_) {
            nextStatusFile_ = now + kStatusFileSeconds;
            guarded("writing the status file", [&] { writeStatusFile(); });
        }
        noteExpiry_ = nextNoteExpiry(now);

        std::vector<pollfd> fds;
        for (const int fd : {page_.fd(), watcher_.fd(), stopSignalFd()}) {
            if (fd >= 0) {
                fds.push_back({fd, POLLIN, 0});
            }
        }
        runner_.addPollFds(fds);
        const double sleep = std::clamp(nextWake(now) - now, 0.01, kMaxSleepSeconds);
        const int timeoutMs = exchangeSoon_ || viewChanged_ ? 0 : static_cast<int>(std::ceil(sleep * 1000.0));
        ::poll(fds.data(), fds.size(), timeoutMs);
        now = monotonicSeconds();
        guarded("collecting finished commands", [&] { runner_.service(now); });
        if (page_.takePing()) {
            exchangeSoon_ = true;
        }
    }

    runner_.stopAll();
    page_.remove();
    std::filesystem::remove(paths_.status, ignored);
    log("FrameKit panel stopped");
    ::close(lockFd);
    return 0;
}

}

int runService(const Paths& paths, const std::string& version) {
    Service service(paths, version);
    return service.run();
}

namespace {

void printStatusOf(const Json::Value& root) {
    const double age = wallSeconds() - root["updated"].asDouble();
    std::printf("FrameKit panel %s (pid %d)%s\n", root["version"].asString().c_str(), root["pid"].asInt(),
                age > kStatusFileStaleSeconds ? "  -- not responding (status is old)" : "");
    std::printf("  Steam page: %s, popup %s, tab %s\n", root["page"].asString().c_str(),
                root["popupOpen"].asBool() ? "open" : "closed", root["tabActive"].asBool() ? "shown" : "not shown");
    std::printf("  paired PCs (FrameKit Setups): %d\n", root["pairedPcs"].asInt());
    for (const Json::Value& app : root["apps"]) {
        std::string updates = "no updates";
        if (app["updates"].asBool()) {
            updates = std::string("auto-check ") + (app["auto"].asBool() ? "on" : "off");
            if (app["available"].asBool()) {
                updates += ", " + app["latest"].asString() + " available";
            }
            if (app["checked"].asDouble() > 0.0) {
                updates += ", checked " + relativeAge(wallSeconds() - app["checked"].asDouble());
            }
            if (app["updateState"].asString() == "updating") {
                updates += ", updating now";
            } else if (app["updateState"].asString() == "failed") {
                updates += ", last update failed: " + app["updateMessage"].asString();
            }
        }
        std::printf("  %-24s %-10s %s\n", app["name"].asString().c_str(), app["version"].asString().c_str(),
                    updates.c_str());
        if (!app["problem"].asString().empty()) {
            std::printf("    problem: %s\n", app["problem"].asString().c_str());
        }
    }
}

}

int printStatus(const Paths& paths) {
    const std::optional<std::string> text = readFile(paths.status);
    const std::optional<Json::Value> root = text ? parseJson(*text) : std::nullopt;
    if (!root || !root->isObject()) {
        std::printf("The FrameKit panel isn't running (no %s).\n", paths.status.c_str());
        std::printf("Check: systemctl --user status %s\n", constants::kPanelUnit);
        return 1;
    }
    try {
        printStatusOf(*root);
    } catch (const std::exception& error) {
        std::printf("The status file %s is damaged (%s).\n", paths.status.c_str(), error.what());
        return 1;
    }
    return 0;
}

int printModules(const Paths& paths) {
    const std::vector<Module> modules = loadModules(paths);
    if (modules.empty()) {
        std::printf("No panel modules in %s\n", paths.panelDir.c_str());
        return 0;
    }
    for (const Module& module : modules) {
        std::printf("%s  (%s %s)\n", module.id.c_str(), module.name.c_str(), module.version.c_str());
        std::printf("  app folder: %s\n", module.appDir.c_str());
        if (!module.problem.empty()) {
            std::printf("  problem: %s\n", module.problem.c_str());
        }
        std::printf("  icon: %s\n", module.iconSvg.empty() ? "(none)" : module.iconPath.c_str());
        if (!module.statusCommand.empty()) {
            std::printf("  status every %.0f s: %s\n", module.statusInterval,
                        joinWords(module.statusCommand, " ").c_str());
        }
        for (const ActionSpec& action : module.actions) {
            std::printf("  button %s \"%s\": %s\n", action.id.c_str(), action.label.c_str(),
                        joinWords(action.command, " ").c_str());
        }
        for (const ToggleSpec& toggle : module.toggles) {
            std::printf("  toggle %s \"%s\": get %s\n", toggle.id.c_str(), toggle.label.c_str(), joinWords(toggle.get, " ").c_str());
        }
        std::printf("  updates: %s, automatic checks %s\n", module.updatesConfigured ? "yes" : "no",
                    module.autoUpdate ? "on" : "off");
    }
    return 0;
}

}
