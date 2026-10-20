#!/bin/sh
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
. "$HERE/constants.sh"
. "$HERE/$KIT_MANIFEST_ENV"

APP_HOME=$HOME/$KIT_APPS_ROOT/$FK_ID
META=$APP_HOME/$KIT_APP_STATE_DIR
STATE=$META/$KIT_UPDATE_STATE_FILE
DAY_SECONDS=86400

FINISHED=0
UPDATING=0
WORK=

say() { printf '%s\n' "$*"; }

finish_ok() {
    FINISHED=1
    say "$KIT_RESULT_PREFIX $KIT_RESULT_OK $*"
    exit 0
}

finish_error() {
    FINISHED=1
    say "$KIT_RESULT_PREFIX $KIT_RESULT_ERROR $*"
    exit 1
}

on_exit() {
    status=$?
    if [ -n "$WORK" ] && [ -d "$WORK" ]; then rm -rf "$WORK"; fi
    if [ "$FINISHED" = 0 ] && [ "$status" -ne 0 ]; then
        say "$KIT_RESULT_PREFIX $KIT_RESULT_ERROR update stopped unexpectedly (exit $status)"
    fi
}
trap on_exit EXIT

interrupted() {
    trap - HUP INT TERM
    if [ "$UPDATING" = 1 ]; then
        UPDATING=0
        write_state "$now" "$LATEST" true failed "update was interrupted"
    fi
    finish_error "update was interrupted"
}
trap interrupted HUP INT TERM

version_cmp() {
    VA=$1 VB=$2 LC_ALL=C awk '
        function number(x, y) {
            sub(/^0+/, "", x)
            sub(/^0+/, "", y)
            if (length(x) != length(y)) return (length(x) < length(y)) ? -1 : 1
            return ((x "") == (y "")) ? 0 : (((x "") < (y "")) ? -1 : 1)
        }
        function part(x, y,   xn, yn) {
            xn = (x ~ /^[0-9]+$/)
            yn = (y ~ /^[0-9]+$/)
            if (xn && yn) return number(x, y)
            if (xn != yn) return xn ? -1 : 1
            return ((x "") == (y "")) ? 0 : (((x "") < (y "")) ? -1 : 1)
        }
        function parts(x, y,   nx, ny, xs, ys, i, c) {
            nx = split(x, xs, ".")
            ny = split(y, ys, ".")
            for (i = 1; i <= nx && i <= ny; i++) {
                c = part(xs[i], ys[i])
                if (c != 0) return c
            }
            return (nx == ny) ? 0 : ((nx < ny) ? -1 : 1)
        }
        function versions(a, b,   i, j, c) {
            i = index(a, "-")
            j = index(b, "-")
            c = parts(i ? substr(a, 1, i - 1) : a, j ? substr(b, 1, j - 1) : b)
            if (c != 0 || (!i && !j)) return c
            if (!i) return 1
            if (!j) return -1
            return parts(substr(a, i + 1), substr(b, j + 1))
        }
        BEGIN { print versions(ENVIRON["VA"], ENVIRON["VB"]) }'
}

version_gt() {
    [ "$(version_cmp "$1" "$2")" = 1 ]
}

auto_setting() {
    value=$(sed -n 's/^AUTO_UPDATE=\([01]\)$/\1/p' "$META/$KIT_SETTINGS_FILE" 2>/dev/null || true)
    printf '%s\n' "${value:-1}"
}

installed() {
    [ -f "$META/$KIT_RECEIPT_FILE" ]
}

state_field() {
    sed -n "s/.*\"$1\": *\"\{0,1\}\([^,\"}]*\)\"\{0,1\}.*/\1/p" "$STATE" 2>/dev/null | head -n 1
}

write_state() {
    [ -d "$META" ] || return 0
    printf '{"checked": %s, "current": "%s", "latest": "%s", "available": %s, "auto": %s, "state": "%s", "message": "%s"}\n' \
        "$1" "$FK_VERSION" "$2" "$3" "$(auto_setting | sed 's/1/true/;s/0/false/')" "$4" "$5" >"$STATE.new"
    mv -f "$STATE.new" "$STATE"
}

LATEST=
LATEST_FILE=
LATEST_SHA=
FETCH_PROBLEM=

url_protocol() {
    case "$1" in
        https://*) echo https ;;
        file://*) echo file ;;
    esac
}

work_dir() {
    [ -n "$WORK" ] || WORK=$(mktemp -d "${XDG_RUNTIME_DIR:-/tmp}/fk-update-$FK_ID.XXXXXX")
}

url_host() {
    printf '%s\n' "$1" | sed -n 's#^[a-z]*://\([^/?#]*\).*#\1#p' | sed 's/.*@//; s/:[0-9]*$//' | tr -cd 'A-Za-z0-9.-'
}

url_name() {
    printf '%s\n' "$1" | sed 's/[?#].*//; s#.*/##' | tr -cd 'A-Za-z0-9._+-'
}

fetch_problem() {
    problem_name=$(url_name "$1")
    problem_host=$(url_host "$1")
    at=${problem_host:+ ($problem_host)}
    problem_status=$2
    case "$3" in
        [45][0-9][0-9]) problem_status=22 ;;
    esac
    case "$problem_status:$3" in
        22:404|22:410) echo "failed to find release file ${problem_name:-update info} ($3)" ;;
        37:*) echo "failed to find release file ${problem_name:-update info}" ;;
        22:401|22:403) echo "update server refused to send ${problem_name:-the update info} ($3)" ;;
        22:5??) echo "update server reported an internal error ($3); try again later" ;;
        22:*) echo "failed to download ${problem_name:-the update info}: error $3" ;;
        1:3??) echo "update server does not support secure connection" ;;
        47:*) echo "redirect error with update server" ;;
        51:*|58:*|59:*|60:*|77:*|83:*|90:*|91:*) echo "failed to verify update server's certificate$at" ;;
        23:*) echo "failed to save ${problem_name:-the update info}" ;;
        28:*) echo "update server timed out$at" ;;
        5:*|6:*|7:*|35:*|52:*|55:*|56:*) echo "could not reach update server$at" ;;
        *) echo "could not reach update server$at (curl error $2)" ;;
    esac
}

fetch() {
    fetch_url=$1
    fetch_out=$2
    shift 2
    fetch_proto="=$(url_protocol "$fetch_url")"
    http_code=$(curl -fsSL --proto "$fetch_proto" --proto-redir "$fetch_proto" -w '%{http_code}' -o "$fetch_out" "$@" "$fetch_url" 2>/dev/null) && return 0
    curl_status=$?
    FETCH_PROBLEM=$(fetch_problem "$fetch_url" "$curl_status" "$http_code")
    return 1
}

package_value() {
    sed -n "s/^$1='\(.*\)'$/\1/p" "$WORK/package/$KIT_MANIFEST_ENV"
}

read_index() {
    [ -n "$FK_UPDATE_INDEX" ] || return 2
    [ -n "$(url_protocol "$FK_UPDATE_INDEX")" ] && [ -n "$(url_protocol "$FK_UPDATE_BASE")" ] || return 4
    work_dir
    fetch "$FK_UPDATE_INDEX" "$WORK/index" --max-time 20 --retry 1 || return 1
    index=$(cat "$WORK/index")
    LATEST=$(printf '%s\n' "$index" | sed -n "s/^version=\([0-9A-Za-z.+~-]\{1,$KIT_MAX_VERSION_LENGTH\}\)\$/\1/p" | head -n 1)
    LATEST_FILE=$(printf '%s\n' "$index" | sed -n 's/^file=\([A-Za-z0-9._+-]\{1,120\}\)$/\1/p' | head -n 1)
    LATEST_SHA=$(printf '%s\n' "$index" | sed -n 's/^sha256=\([0-9a-f]\{64\}\)$/\1/p' | head -n 1)
    [ -n "$LATEST" ] && [ -n "$LATEST_FILE" ] && [ -n "$LATEST_SHA" ] || return 3
    return 0
}

known_update() {
    last=$(state_field checked)
    case "$last" in ''|*[!0-9]*) last=0 ;; esac
    known=$(state_field latest | sed -n "s/^\([0-9A-Za-z.+~-]\{1,$KIT_MAX_VERSION_LENGTH\}\)\$/\1/p")
    known_available=false
    if [ -n "$known" ] && version_gt "$known" "$FK_VERSION"; then known_available=true; fi
}

check_failed() {
    known_update
    write_state "$last" "$known" "$known_available" error "$1"
    finish_error "$1"
}

check() {
    now=$(date +%s)
    status=0
    read_index || status=$?
    case "$status" in
        0) ;;
        2) write_state "$now" "" false none "no update server defined"; finish_ok "no-updates" ;;
        1) check_failed "$FETCH_PROBLEM" ;;
        4) check_failed "update server address is not secure" ;;
        *)
            check_failed "no update index found on update server"
            ;;
    esac
    if version_gt "$LATEST" "$FK_VERSION"; then
        write_state "$now" "$LATEST" true ok ""
        return 0
    fi
    write_state "$now" "$LATEST" false ok ""
    return 1
}

command=${1:-}
[ $# -eq 0 ] || shift

case "$command" in
    check)
        installed || finish_error "$FK_NAME is not installed"
        exec 8>"$META/$KIT_UPDATE_LOCK_FILE"
        flock -n 8 || finish_ok "skipped an update is running"
        if [ "${1:-}" = "--if-due" ]; then
            [ "$(auto_setting)" = 1 ] || finish_ok "skipped automatic checks are off"
            last=$(state_field checked)
            case "$last" in ''|*[!0-9]*) last=0 ;; esac
            [ $(( $(date +%s) - last )) -ge "$DAY_SECONDS" ] || finish_ok "skipped checked recently"
        fi
        if check; then finish_ok "available $LATEST"; fi
        finish_ok "up-to-date $FK_VERSION"
        ;;
    apply)
        installed || finish_error "$FK_NAME is not installed"
        exec 8>"$META/$KIT_UPDATE_LOCK_FILE"
        flock -w 60 8 || finish_error "an update is already running"
        installed || finish_error "$FK_NAME is not installed"
        if ! check; then finish_ok "up-to-date $FK_VERSION"; fi
        now=$(date +%s)
        write_state "$now" "$LATEST" true updating "Updating to $LATEST"
        UPDATING=1
        work_dir
        fail_update() {
            UPDATING=0
            write_state "$now" "$LATEST" true failed "$1"
            finish_error "$1"
        }
        still_current() {
            installed || fail_update "$FK_NAME was removed during update"
            meanwhile=$(readlink "$APP_HOME/$KIT_CURRENT_LINK" 2>/dev/null | sed -n "s#^$KIT_VERSIONS_DIR/\([0-9A-Za-z.+~-]\{1,$KIT_MAX_VERSION_LENGTH\}\)\$#\1#p")
            [ "$meanwhile" != "$FK_VERSION" ] || return 0
            [ -n "$meanwhile" ] || fail_update "$FK_NAME was removed during update"
            UPDATING=0
            FK_VERSION=$meanwhile
            if version_gt "$LATEST" "$FK_VERSION"; then newer=true; else newer=false; fi
            write_state "$now" "$LATEST" "$newer" ok "Version $FK_VERSION is already installed, skipping update"
            finish_ok "skipped version $FK_VERSION was installed meanwhile"
        }
        fetch "$FK_UPDATE_BASE$LATEST_FILE" "$WORK/$LATEST_FILE" --max-time 600 ||
            fail_update "download failed: $FETCH_PROBLEM"
        fetch "$FK_UPDATE_BASE$LATEST_FILE.sig" "$WORK/$LATEST_FILE.sig" --max-time 60 ||
            fail_update "signature download failed: $FETCH_PROBLEM"
        still_current
        [ "$(sha256sum "$WORK/$LATEST_FILE" | cut -d ' ' -f 1)" = "$LATEST_SHA" ] ||
            fail_update "downloaded file is damaged (checksum mismatch)"
        openssl dgst -sha256 -verify "$HERE/$KIT_SIGNING_KEY" -signature "$WORK/$LATEST_FILE.sig" "$WORK/$LATEST_FILE" >/dev/null 2>&1 ||
            fail_update "update file failed signature checks - please report this"
        mkdir "$WORK/package"
        tar -xzf "$WORK/$LATEST_FILE" -C "$WORK/package" || fail_update "failed to unpack update file"
        [ "$(package_value FK_ID)" = "$FK_ID" ] || fail_update "incorrect update file"
        PACKAGE_VERSION=$(package_value FK_VERSION)
        [ "$PACKAGE_VERSION" = "$LATEST" ] ||
            fail_update "update file is version $PACKAGE_VERSION, not $LATEST - please report this"
        still_current
        output=$(FK_UPDATE_LOCK_HELD=1 sh "$WORK/package/$KIT_INSTALL_SCRIPT" 8>&- 2>&1) || true
        printf '%s\n' "$output" >"$META/$KIT_UPDATE_LOG_FILE"
        result=$(printf '%s\n' "$output" | grep "^$KIT_RESULT_PREFIX" | tail -n 1)
        case "$result" in
            "$KIT_RESULT_PREFIX $KIT_RESULT_OK"*)
                FK_VERSION=$LATEST
                UPDATING=0
                message="Updated to $LATEST"
                if printf '%s\n' "$output" | grep -qx "$KIT_STEAMVR_RESTART_NEEDED"; then
                    message="$message; restart SteamVR to finish update"
                fi
                write_state "$now" "$LATEST" false ok "$message"
                finish_ok "updated $LATEST"
                ;;
            *)
                fail_update "update failed"
                ;;
        esac
        ;;
    auto)
        case "${1:-}" in
            on) value=1 ;;
            off) value=0 ;;
            *) finish_error "usage: update.sh auto on|off" ;;
        esac
        installed || finish_error "$FK_NAME is not installed"
        printf 'AUTO_UPDATE=%s\n' "$value" >"$META/$KIT_SETTINGS_FILE.new"
        mv -f "$META/$KIT_SETTINGS_FILE.new" "$META/$KIT_SETTINGS_FILE"
        if [ -f "$STATE" ]; then
            sed "s/\"auto\": [a-z]*/\"auto\": $( [ "$value" = 1 ] && echo true || echo false )/" "$STATE" >"$STATE.new"
            mv -f "$STATE.new" "$STATE"
        fi
        finish_ok "auto $1"
        ;;
    refresh)
        installed || finish_error "$FK_NAME is not installed"
        [ -f "$STATE" ] || finish_ok "refreshed $FK_VERSION"
        known_update
        if [ -z "$FK_UPDATE_INDEX" ]; then
            write_state "$last" "" false none "no update server defined"
        else
            write_state "$last" "$known" "$known_available" ok ""
        fi
        finish_ok "refreshed $FK_VERSION"
        ;;
    *)
        say "usage: update.sh check [--if-due] | apply | auto on|off | refresh"
        finish_error "unknown command '$command'"
        ;;
esac
