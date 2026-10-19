#include "runner.hpp"

#include <cerrno>
#include <csignal>
#include <cstring>
#include <exception>
#include <tuple>

#include <fcntl.h>
#include <spawn.h>
#include <sys/wait.h>
#include <unistd.h>

#include "common.hpp"

extern char** environ;

namespace fkp {

namespace {

constexpr size_t kMaxOutputBytes = 64 * 1024;

std::string jobLabel(const std::vector<std::string>& argv) {
    if (argv.empty()) {
        return "(empty command)";
    }
    return argv.size() > 1 ? argv[0] + " " + argv[1] : argv[0];
}

}

Runner::~Runner() {
    stopAll();
}

bool Runner::start(const std::vector<std::string>& argv, double timeoutSeconds, Callback done) {
    const std::string label = jobLabel(argv);
    JobResult failure;
    failure.failedToStart = true;
    if (argv.empty()) {
        failure.output = "empty command";
        finish(done, failure, label);
        return false;
    }
    int pipeFds[2];
    if (::pipe2(pipeFds, O_CLOEXEC) != 0) {
        failure.output = std::strerror(errno);
        finish(done, failure, label);
        return false;
    }

    posix_spawn_file_actions_t actions;
    posix_spawn_file_actions_init(&actions);
    posix_spawn_file_actions_addopen(&actions, 0, "/dev/null", O_RDONLY, 0);
    posix_spawn_file_actions_adddup2(&actions, pipeFds[1], 1);
    posix_spawn_file_actions_adddup2(&actions, pipeFds[1], 2);

    posix_spawnattr_t attributes;
    posix_spawnattr_init(&attributes);
    sigset_t noSignals;
    sigemptyset(&noSignals);
    sigset_t defaults;
    sigemptyset(&defaults);
    sigaddset(&defaults, SIGINT);
    sigaddset(&defaults, SIGTERM);
    sigaddset(&defaults, SIGPIPE);
    sigaddset(&defaults, SIGCHLD);
    posix_spawnattr_setsigmask(&attributes, &noSignals);
    posix_spawnattr_setsigdefault(&attributes, &defaults);
    posix_spawnattr_setpgroup(&attributes, 0);
    posix_spawnattr_setflags(&attributes, POSIX_SPAWN_SETPGROUP | POSIX_SPAWN_SETSIGMASK | POSIX_SPAWN_SETSIGDEF);

    std::vector<char*> args;
    args.reserve(argv.size() + 1);
    for (const std::string& argument : argv) {
        args.push_back(const_cast<char*>(argument.c_str()));
    }
    args.push_back(nullptr);

    pid_t pid = -1;
    const int result = ::posix_spawnp(&pid, args[0], &actions, &attributes, args.data(), environ);
    posix_spawn_file_actions_destroy(&actions);
    posix_spawnattr_destroy(&attributes);
    ::close(pipeFds[1]);
    if (result != 0) {
        ::close(pipeFds[0]);
        failure.output = argv.front() + ": " + std::strerror(result);
        finish(done, failure, label);
        return false;
    }
    ::fcntl(pipeFds[0], F_SETFL, ::fcntl(pipeFds[0], F_GETFL) | O_NONBLOCK);
    Job job;
    job.pid = pid;
    job.fd = pipeFds[0];
    job.deadline = monotonicSeconds() + timeoutSeconds;
    job.label = label;
    job.done = std::move(done);
    jobs_.push_back(std::move(job));
    return true;
}

void Runner::addPollFds(std::vector<pollfd>& fds) const {
    for (const Job& job : jobs_) {
        if (job.fd >= 0) {
            fds.push_back({job.fd, POLLIN, 0});
        }
    }
}

void Runner::drain(Job& job) {
    if (job.fd < 0) {
        return;
    }
    char chunk[4096];
    while (true) {
        const ssize_t count = ::read(job.fd, chunk, sizeof(chunk));
        if (count > 0) {
            if (job.output.size() < kMaxOutputBytes) {
                job.output.append(chunk, static_cast<size_t>(count));
            }
            continue;
        }
        if (count < 0 && errno == EINTR) {
            continue;
        }
        if (count == 0) {
            ::close(job.fd);
            job.fd = -1;
        }
        return;
    }
}

void Runner::service(double monotonicNow) {
    std::vector<std::tuple<Callback, JobResult, std::string>> finished;
    for (size_t i = 0; i < jobs_.size();) {
        Job& job = jobs_[i];
        drain(job);
        if (!job.killed && monotonicNow > job.deadline) {
            ::kill(-job.pid, SIGKILL);
            job.killed = true;
        }
        int status = 0;
        const pid_t reaped = ::waitpid(job.pid, &status, WNOHANG);
        if (reaped != job.pid) {
            ++i;
            continue;
        }
        drain(job);
        if (job.fd >= 0) {
            ::close(job.fd);
        }
        JobResult result;
        result.output = job.output;
        result.timedOut = job.killed;
        if (WIFEXITED(status)) {
            result.exitCode = WEXITSTATUS(status);
        } else if (WIFSIGNALED(status)) {
            result.exitCode = 128 + WTERMSIG(status);
        }
        finished.emplace_back(std::move(job.done), std::move(result), std::move(job.label));
        jobs_.erase(jobs_.begin() + static_cast<long>(i));
    }
    for (const auto& [callback, result, label] : finished) {
        finish(callback, result, label);
    }
}

void Runner::finish(const Callback& done, const JobResult& result, const std::string& label) const {
    if (!done) {
        return;
    }
    try {
        done(result);
    } catch (const std::exception& error) {
        if (onError_) {
            onError_(label, error.what());
        }
    } catch (...) {
        if (onError_) {
            onError_(label, "unknown error");
        }
    }
}

void Runner::stopAll() {
    for (Job& job : jobs_) {
        ::kill(-job.pid, SIGTERM);
    }
    for (Job& job : jobs_) {
        int status = 0;
        for (int attempt = 0; attempt < 20; ++attempt) {
            if (::waitpid(job.pid, &status, WNOHANG) == job.pid) {
                break;
            }
            ::usleep(25000);
            if (attempt == 19) {
                ::kill(-job.pid, SIGKILL);
                ::waitpid(job.pid, &status, 0);
            }
        }
        if (job.fd >= 0) {
            ::close(job.fd);
        }
    }
    jobs_.clear();
}

}
