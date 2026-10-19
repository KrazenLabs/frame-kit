#pragma once

#include <functional>
#include <string>
#include <vector>

#include <poll.h>
#include <sys/types.h>

namespace fkp {

struct JobResult {
    int exitCode = -1;
    bool timedOut = false;
    bool failedToStart = false;
    std::string output;
};

class Runner {
public:
    using Callback = std::function<void(const JobResult&)>;
    using ErrorHandler = std::function<void(const std::string& job, const std::string& error)>;

    explicit Runner(ErrorHandler onError = {}) : onError_(std::move(onError)) {}
    ~Runner();
    Runner(const Runner&) = delete;
    Runner& operator=(const Runner&) = delete;

    bool start(const std::vector<std::string>& argv, double timeoutSeconds, Callback done);
    bool running() const { return !jobs_.empty(); }
    void addPollFds(std::vector<pollfd>& fds) const;
    void service(double monotonicNow);
    void stopAll();

private:
    struct Job {
        pid_t pid = -1;
        int fd = -1;
        double deadline = 0.0;
        std::string output;
        bool killed = false;
        std::string label;
        Callback done;
    };

    static void drain(Job& job);
    void finish(const Callback& done, const JobResult& result, const std::string& label) const;
    ErrorHandler onError_;
    std::vector<Job> jobs_;
};

}
