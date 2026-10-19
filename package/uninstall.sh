#!/bin/sh
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
. "$HERE/constants.sh"
. "$HERE/$KIT_MANIFEST_ENV"

SYSTEMCTL=${FK_SYSTEMCTL:-systemctl}
VRPATHREG=${FK_VRPATHREG:-/opt/steamvr/bin/linuxarm64/vrpathreg}

FINISHED=0
OUTCOME=$KIT_OUTCOME_UNCHANGED
CAUSE=$KIT_CAUSE_FAILED

say() { printf '%s\n' "$*" || true; }

finish_ok() {
    FINISHED=1
    say "$KIT_RESULT_PREFIX $KIT_RESULT_OK $*"
    exit 0
}

report_error() {
    say "$KIT_OUTCOME_PREFIX $OUTCOME $CAUSE"
    say "$KIT_RESULT_PREFIX $KIT_RESULT_ERROR $*"
}

finish_error() {
    FINISHED=1
    report_error "$@"
    exit 1
}

refuse() {
    CAUSE=$1
    shift
    finish_error "$@"
}

on_exit() {
    status=$?
    if [ "$FINISHED" = 0 ] && [ "$status" -ne 0 ]; then
        report_error "removal stopped unexpectedly (exit $status)"
    fi
}
trap on_exit EXIT
trap : HUP INT TERM PIPE

PURGE=0
while [ $# -gt 0 ]; do
    case "$1" in
        --purge) PURGE=1; shift ;;
        *) finish_error "unknown option: $1" ;;
    esac
done

[ "$(id -u)" != 0 ] || finish_error "run this as the steamos user, not as root"
if [ -d "/run/user/$(id -u)" ]; then export XDG_RUNTIME_DIR="/run/user/$(id -u)"; fi

APP_HOME=$HOME/$KIT_APPS_ROOT/$FK_ID
META=$APP_HOME/$KIT_APP_STATE_DIR
CURRENT=$APP_HOME/$KIT_CURRENT_LINK
UNIT_DIR=$HOME/$KIT_UNIT_DIR
SHARED_HOME=$HOME/$KIT_APPS_ROOT/$KIT_SHARED_DIR
PANEL_D=$SHARED_HOME/$KIT_PANEL_MODULES_DIR
case "$FK_ID" in "$KIT_RESERVED_ID"|"$KIT_RESERVED_ID_PREFIX"*) finish_error "the app id '$FK_ID' is reserved by FrameKit" ;; esac
if [ -L "$APP_HOME" ] || { [ -e "$APP_HOME" ] && [ ! -d "$META" ]; }; then
    finish_error "~/$KIT_APPS_ROOT/$FK_ID is not a FrameKit install, aborting removal."
fi
UNIT=$FK_ID$KIT_SERVICE_SUFFIX
TIMER=$FK_ID$KIT_TIMER_SUFFIX
unit_is_ours() {
    [ -f "$UNIT_DIR/$UNIT" ] && grep -qF "$KIT_APPS_ROOT/$FK_ID/$KIT_CURRENT_LINK/" "$UNIT_DIR/$UNIT"
}
timer_is_ours() {
    [ -f "$UNIT_DIR/$TIMER" ] && grep -qx "Unit=$UNIT" "$UNIT_DIR/$TIMER"
}

if [ -d "$META" ]; then
    exec 8>"$META/$KIT_UPDATE_LOCK_FILE"
    flock -w 120 8 || refuse "$KIT_CAUSE_BUSY" "an update is still running; try again in a few minutes"
    exec 9>"$META/$KIT_LOCK_FILE"
    flock -w 120 9 || refuse "$KIT_CAUSE_BUSY" "an installation or update is still running"
fi

say "Removing $FK_NAME"
OUTCOME=$KIT_OUTCOME_PARTIAL

if [ -f "$CURRENT/hooks/pre-uninstall" ]; then
    hook_status=0
    APP_HOME=$APP_HOME APP_DIR=$CURRENT FK_VERSION=$FK_VERSION \
        timeout -k 10 "$KIT_HOOK_TIMEOUT_SECONDS" sh "$CURRENT/hooks/pre-uninstall" </dev/null 8>&- 9>&- || hook_status=$?
    case $hook_status in
        0) ;;
        124|137) say "warning: the pre-uninstall step of $FK_NAME did not finish within $((KIT_HOOK_TIMEOUT_SECONDS / 60)) minutes, removing anyway" ;;
        *) say "warning: the pre-uninstall step of $FK_NAME failed; removing anyway" ;;
    esac
fi

DRIVERS=$(sed -n 's/^driver //p' "$META/$KIT_RECEIPT_FILE" 2>/dev/null || true)
if timer_is_ours; then
    "$SYSTEMCTL" --user disable --now "$TIMER" >/dev/null 2>&1 || true
    rm -f "$UNIT_DIR/$TIMER"
fi
if unit_is_ours; then
    "$SYSTEMCTL" --user disable --now "$UNIT" >/dev/null 2>&1 || true
    rm -f "$UNIT_DIR/$UNIT"
fi
for driver in $DRIVERS; do
    "$VRPATHREG" removedriver "$driver" >/dev/null 2>&1 || true
done

rm -f "$PANEL_D/$FK_ID.json"
MODULES_LEFT=0
for module in "$PANEL_D"/*.json; do
    if [ -e "$module" ]; then MODULES_LEFT=1; fi
done
if [ "$MODULES_LEFT" = 0 ]; then
    "$SYSTEMCTL" --user disable --now "$KIT_PANEL_UNIT" >/dev/null 2>&1 || true
    rm -f "$UNIT_DIR/$KIT_PANEL_UNIT"
    rm -rf "$SHARED_HOME"
fi
"$SYSTEMCTL" --user daemon-reload >/dev/null 2>&1 || true

own_data_dir() {
    for root in $KIT_DATA_ROOTS; do
        case "$1" in
            "$root/$FK_ID"|"$root/$FK_ID"/*)
                case "/$1/" in
                    */../*|*/./*|*//*) return 1 ;;
                esac
                return 0
                ;;
        esac
    done
    return 1
}

KEPT=
set -f
for data_dir in $FK_DATA_DIRS; do
    if ! own_data_dir "$data_dir"; then
        say "warning: ~/$data_dir does not belong to $FK_NAME"
        continue
    fi
    [ -e "$HOME/$data_dir" ] || continue
    if [ "$PURGE" = 1 ]; then
        rm -rf "$HOME/$data_dir"
    else
        KEPT="$KEPT ~/$data_dir"
    fi
done
set +f

rm -rf "$APP_HOME"

if [ -n "$DRIVERS" ]; then
    say "SteamVR unloads the driver on next start"
fi
if [ -n "$KEPT" ]; then
    say "$KIT_KEPT_DATA_LINE$KEPT"
fi
finish_ok "$FK_ID removed"
