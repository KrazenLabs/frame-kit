#include "watcher.hpp"

#include <cerrno>
#include <cstdint>
#include <cstring>

#include <sys/inotify.h>
#include <unistd.h>

namespace fkp {

namespace {

constexpr uint32_t kChangeEvents =
    IN_CREATE | IN_DELETE | IN_MOVED_FROM | IN_MOVED_TO | IN_CLOSE_WRITE | IN_DELETE_SELF | IN_MOVE_SELF;

}

DirectoryWatcher::DirectoryWatcher() {
    fd_ = ::inotify_init1(IN_NONBLOCK | IN_CLOEXEC);
    if (fd_ < 0) {
        problem_ = std::string("inotify isn't available: ") + std::strerror(errno);
    }
}

DirectoryWatcher::~DirectoryWatcher() {
    if (fd_ >= 0) {
        ::close(fd_);
    }
}

void DirectoryWatcher::watch(const std::vector<std::filesystem::path>& directories) {
    if (fd_ < 0) {
        return;
    }
    std::set<int> wanted;
    std::string problem;
    for (const std::filesystem::path& directory : directories) {
        const int wd = ::inotify_add_watch(fd_, directory.c_str(), kChangeEvents);
        if (wd >= 0) {
            wanted.insert(wd);
        } else if (errno != ENOENT && errno != ENOTDIR && problem.empty()) {
            problem = directory.string() + ": " + std::strerror(errno);
        }
    }
    for (const int wd : watches_) {
        if (!wanted.count(wd)) {
            ::inotify_rm_watch(fd_, wd);
        }
    }
    watches_ = std::move(wanted);
    problem_ = problem;
}

bool DirectoryWatcher::takeChanges() {
    if (fd_ < 0) {
        return false;
    }
    bool changed = false;
    alignas(inotify_event) char buffer[8192];
    while (true) {
        const ssize_t count = ::read(fd_, buffer, sizeof(buffer));
        if (count < 0 && errno == EINTR) {
            continue;
        }
        if (count <= 0) {
            return changed;
        }
        size_t offset = 0;
        while (offset + sizeof(inotify_event) <= static_cast<size_t>(count)) {
            inotify_event event;
            std::memcpy(&event, buffer + offset, sizeof(event));
            if (event.mask != IN_IGNORED) {
                changed = true;
            }
            offset += sizeof(inotify_event) + event.len;
        }
    }
}

}
