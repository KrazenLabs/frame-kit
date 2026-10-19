#pragma once

#include "framekit/clock.hpp"
#include "framekit/devtools.hpp"
#include "framekit/files.hpp"
#include "framekit/json_read.hpp"
#include "framekit/log.hpp"
#include "framekit/stop_signal.hpp"
#include "framekit/text.hpp"

namespace fkp {

using framekit::base64Encode;
using framekit::capitalizeFirst;
using framekit::contains;
using framekit::DevToolsSession;
using framekit::endsWith;
using framekit::EvaluationResult;
using framekit::findOverlayPagePath;
using framekit::fnv1aHex;
using framekit::httpGetLocal;
using framekit::installStopHandlers;
using framekit::joinWords;
using framekit::listDirectory;
using framekit::log;
using framekit::Logger;
using framekit::member;
using framekit::monotonicSeconds;
using framekit::parseJson;
using framekit::readFile;
using framekit::repairUtf8;
using framekit::splitLines;
using framekit::splitWhitespace;
using framekit::startsWith;
using framekit::stopRequested;
using framekit::stopSignalFd;
using framekit::toLower;
using framekit::trim;
using framekit::wallSeconds;
using framekit::writeFileAtomically;
using framekit::WriteOptions;

}
