#pragma once

#include <filesystem>
#include <set>
#include <string>
#include <vector>

namespace fkp {

class DirectoryWatcher {
public:
    DirectoryWatcher();
    ~DirectoryWatcher();
    DirectoryWatcher(const DirectoryWatcher&) = delete;
    DirectoryWatcher& operator=(const DirectoryWatcher&) = delete;

    int fd() const { return fd_; }
    bool watching() const { return fd_ >= 0 && problem_.empty(); }
    const std::string& problem() const { return problem_; }
    void watch(const std::vector<std::filesystem::path>& directories);
    bool takeChanges();

private:
    int fd_ = -1;
    std::set<int> watches_;
    std::string problem_;
};

}
