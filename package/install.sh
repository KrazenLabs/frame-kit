#!/bin/sh
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
. "$HERE/constants.sh"
. "$HERE/$KIT_MANIFEST_ENV"

SYSTEMCTL=${FK_SYSTEMCTL:-systemctl}
VRPATHREG=${FK_VRPATHREG:-/opt/steamvr/bin/linuxarm64/vrpathreg}
OS_RELEASE=${FK_OS_RELEASE:-/etc/os-release}
UNAME_M=${FK_UNAME_M:-$(uname -m)}
FK_SERVICE=${FK_SERVICE:-0}
FK_TIMER=${FK_TIMER:-0}

FINISHED=0
STAGE=
OLD_SAME=
CHANGED=0
WAS_INSTALLED=0
TIMER_INSTALLED=0
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
    if [ -n "$STAGE" ] && [ -d "$STAGE" ]; then rm -rf "$STAGE"; fi
    if [ "$FINISHED" = 0 ] && [ "$status" -ne 0 ]; then
        if [ "$CHANGED" = 1 ]; then
            CHANGED=0
            set +e
            rollback "installation stopped unexpectedly (exit $status)"
        fi
        report_error "installation stopped unexpectedly (exit $status)"
    fi
}
trap on_exit EXIT

interrupted() {
    trap : HUP INT TERM
    set +e
    CAUSE=$KIT_CAUSE_INTERRUPTED
    if [ "$CHANGED" = 1 ]; then
        CHANGED=0
        rollback "installation was interrupted"
    fi
    finish_error "installation was interrupted"
}
trap interrupted HUP INT TERM
trap : PIPE

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

version_ge() {
    case "$(version_cmp "$1" "$2")" in 0|1) return 0 ;; esac
    return 1
}

in_list() {
    case " $2 " in *" $1 "*) return 0 ;; esac
    return 1
}

AUTO_UPDATE=
RESTART_STEAMVR=0
NO_ROLLBACK=0
while [ $# -gt 0 ]; do
    case "$1" in
        --auto-update)
            [ $# -ge 2 ] || finish_error "--auto-update needs 1 or 0"
            AUTO_UPDATE=$2
            shift 2
            ;;
        --restart-steamvr) RESTART_STEAMVR=1; shift ;;
        --no-rollback) NO_ROLLBACK=1; shift ;;
        *) finish_error "unknown option: $1" ;;
    esac
done

[ "$(id -u)" != 0 ] || finish_error "run this as the steamos user, not as root"
if [ -d "/run/user/$(id -u)" ]; then export XDG_RUNTIME_DIR="/run/user/$(id -u)"; fi

[ -r "$OS_RELEASE" ] || finish_error "unable to read $OS_RELEASE"
VARIANT=$(sed -n 's/^VARIANT_ID=//p' "$OS_RELEASE" | tr -d '"')
OS_VERSION=$(sed -n 's/^VERSION_ID=//p' "$OS_RELEASE" | tr -d '"')
[ "$VARIANT" = vr ] || refuse "$KIT_CAUSE_DEVICE" "not a Steam Frame (SteamOS variant '$VARIANT')"
[ "$UNAME_M" = aarch64 ] || refuse "$KIT_CAUSE_DEVICE" "not a Steam Frame (processor '$UNAME_M')"
version_ge "$OS_VERSION" "$FK_MIN_OS" || refuse "$KIT_CAUSE_DEVICE" "$FK_NAME needs SteamOS $FK_MIN_OS or newer; currently installed: $OS_VERSION"
FREE_MB=$(df -Pk "$HOME" | awk 'NR == 2 { print int($4 / 1024) }')
NEED_MB=$((FK_FREE_MB + KIT_SPACE_MARGIN_MB))
[ "$FREE_MB" -ge "$NEED_MB" ] || refuse "$KIT_CAUSE_DEVICE" "not enough free space: $FK_NAME needs at least ${NEED_MB} MB free space"

case "$AUTO_UPDATE" in ''|0|1) ;; *) finish_error "--auto-update must be 1 or 0" ;; esac

APP_HOME=$HOME/$KIT_APPS_ROOT/$FK_ID
META=$APP_HOME/$KIT_APP_STATE_DIR
VERSIONS=$APP_HOME/$KIT_VERSIONS_DIR
CURRENT=$APP_HOME/$KIT_CURRENT_LINK
UNIT_DIR=$HOME/$KIT_UNIT_DIR
SHARED_HOME=$HOME/$KIT_APPS_ROOT/$KIT_SHARED_DIR
PANEL_D=$SHARED_HOME/$KIT_PANEL_MODULES_DIR
PANEL_HOME=$SHARED_HOME/$KIT_PANEL_HOME
case "$FK_ID" in "$KIT_RESERVED_ID"|"$KIT_RESERVED_ID_PREFIX"*) finish_error "the app id '$FK_ID' is reserved by FrameKit" ;; esac
if [ -L "$APP_HOME" ] || { [ -e "$APP_HOME" ] && [ ! -d "$META" ]; }; then
    finish_error "~/$KIT_APPS_ROOT/$FK_ID already exists"
fi
UNIT=$FK_ID$KIT_SERVICE_SUFFIX
TIMER=$FK_ID$KIT_TIMER_SUFFIX
unit_is_ours() {
    [ -f "$UNIT_DIR/$UNIT" ] && grep -qF "$KIT_APPS_ROOT/$FK_ID/$KIT_CURRENT_LINK/" "$UNIT_DIR/$UNIT"
}
timer_is_ours() {
    [ -f "$UNIT_DIR/$TIMER" ] && grep -qx "Unit=$UNIT" "$UNIT_DIR/$TIMER"
}
if [ "$FK_SERVICE" = 1 ]; then
    if [ -e "$UNIT_DIR/$UNIT" ] || [ -L "$UNIT_DIR/$UNIT" ]; then
        unit_is_ours || finish_error "a service named $UNIT already exists"
    else
        TAKEN=$("$SYSTEMCTL" --user show -p FragmentPath --value "$UNIT" 2>/dev/null || true)
        [ -z "$TAKEN" ] || finish_error "a service named $UNIT already exists ($TAKEN)"
    fi
fi
if [ "$FK_TIMER" = 1 ]; then
    if [ -e "$UNIT_DIR/$TIMER" ] || [ -L "$UNIT_DIR/$TIMER" ]; then
        timer_is_ours || finish_error "a timer named $TIMER already exists"
    else
        TAKEN=$("$SYSTEMCTL" --user show -p FragmentPath --value "$TIMER" 2>/dev/null || true)
        [ -z "$TAKEN" ] || finish_error "a timer named $TIMER already exists ($TAKEN)"
    fi
fi
PACKAGE_FILES=$HERE/$KIT_PACKAGE_FILES
[ -f "$PACKAGE_FILES" ] || finish_error "package is incomplete: $KIT_PACKAGE_FILES is missing"
while IFS= read -r file; do
    case "$file" in
        '') continue ;;
        /*|..|../*|*/..|*/../*) finish_error "package file list names a file outside package: $file" ;;
    esac
    [ -f "$HERE/$file" ] || finish_error "package is incomplete: $file is missing"
done <"$PACKAGE_FILES"
mkdir -p "$META" "$VERSIONS" "$UNIT_DIR" "$PANEL_D"

if [ "${FK_UPDATE_LOCK_HELD:-0}" != 1 ]; then
    exec 8>"$META/$KIT_UPDATE_LOCK_FILE"
    flock -w 120 8 || refuse "$KIT_CAUSE_BUSY" "another installation or update is still running"
fi
exec 9>"$META/$KIT_LOCK_FILE"
flock -w 120 9 || refuse "$KIT_CAUSE_BUSY" "another installation or update is still running"

for leftover in "$VERSIONS"/.staging-* "$VERSIONS"/.old-*; do
    if [ -e "$leftover" ]; then rm -rf "$leftover"; fi
done

PREVIOUS=
if [ -L "$CURRENT" ]; then
    PREVIOUS=$(readlink "$CURRENT" | sed "s#^$KIT_VERSIONS_DIR/##")
fi
if [ -n "$PREVIOUS" ] || [ -f "$META/$KIT_RECEIPT_FILE" ]; then WAS_INSTALLED=1; fi
KEEP=$(cat "$META/$KIT_PREVIOUS_FILE" 2>/dev/null || true)
if [ -n "$PREVIOUS" ] && [ "$PREVIOUS" != "$FK_VERSION" ] && [ -d "$VERSIONS/$PREVIOUS" ]; then KEEP=$PREVIOUS; fi

if [ -z "$AUTO_UPDATE" ]; then
    AUTO_UPDATE=$(sed -n 's/^AUTO_UPDATE=\([01]\)$/\1/p' "$META/$KIT_SETTINGS_FILE" 2>/dev/null || true)
    [ -n "$AUTO_UPDATE" ] || AUTO_UPDATE=1
fi

OLD_DRIVERS=$(sed -n 's/^driver //p' "$META/$KIT_RECEIPT_FILE" 2>/dev/null || true)

switch_current() {
    ln -sfn "$KIT_VERSIONS_DIR/$1" "$CURRENT.new"
    mv -Tf "$CURRENT.new" "$CURRENT"
}

remove_units() {
    for unit in "$@"; do
        "$SYSTEMCTL" --user disable --now "$unit" >/dev/null 2>&1 || true
        rm -f "$UNIT_DIR/$unit"
    done
}

panel_modules_left() {
    for module in "$PANEL_D"/*.json; do
        if [ -e "$module" ]; then return 0; fi
    done
    return 1
}

remove_shared_panel_if_unused() {
    if ! panel_modules_left; then
        remove_units "$KIT_PANEL_UNIT"
        rm -rf "$PANEL_HOME"
    fi
}

rollback() {
    trap : HUP INT TERM
    reason=$1
    if timer_is_ours; then "$SYSTEMCTL" --user stop "$TIMER" >/dev/null 2>&1 || true; fi
    if unit_is_ours; then "$SYSTEMCTL" --user stop "$UNIT" >/dev/null 2>&1 || true; fi
    if [ "$NO_ROLLBACK" = 1 ]; then
        OUTCOME=$KIT_OUTCOME_PARTIAL
        finish_error "$reason"
    fi
    if [ "$TIMER_INSTALLED" = 1 ]; then
        remove_units "$TIMER"
        "$SYSTEMCTL" --user daemon-reload >/dev/null 2>&1 || true
    fi
    for driver in $FK_DRIVERS; do "$VRPATHREG" removedriver "$CURRENT/$driver" >/dev/null 2>&1 || true; done
    back=
    if [ -n "$OLD_SAME" ] && [ -d "$OLD_SAME" ]; then
        rm -rf "$VERSIONS/$FK_VERSION"
        mv "$OLD_SAME" "$VERSIONS/$FK_VERSION"
        OLD_SAME=
        back=$FK_VERSION
    elif [ -n "$PREVIOUS" ] && [ "$PREVIOUS" != "$FK_VERSION" ] && [ -f "$VERSIONS/$PREVIOUS/$KIT_INSTALL_SCRIPT" ]; then
        rm -rf "$VERSIONS/$FK_VERSION"
        back=$PREVIOUS
    fi
    if [ -z "$back" ]; then
        if unit_is_ours; then remove_units "$UNIT"; fi
        rm -f "$PANEL_D/$FK_ID.json"
        remove_shared_panel_if_unused
        "$SYSTEMCTL" --user daemon-reload >/dev/null 2>&1 || true
        rm -rf "$APP_HOME"
        if [ "$WAS_INSTALLED" = 1 ]; then
            OUTCOME=$KIT_OUTCOME_REMOVED
            finish_error "$reason; could not revert, $FK_NAME was removed"
        fi
        finish_error "$reason"
    fi
    flock -u 9
    if FK_UPDATE_LOCK_HELD=1 sh "$VERSIONS/$back/$KIT_INSTALL_SCRIPT" --auto-update "$AUTO_UPDATE" --no-rollback >"$META/rollback.log" 2>&1 8>&-; then
        OUTCOME=$KIT_OUTCOME_REVERTED
        finish_error "$reason; reverted to version $back"
    fi
    OUTCOME=$KIT_OUTCOME_PARTIAL
    finish_error "$reason; failed to revert to $back (see $META/rollback.log)"
}

say "Installing $FK_NAME $FK_VERSION${PREVIOUS:+ (replacing $PREVIOUS)}"

STAGE=$VERSIONS/.staging-$$
mkdir -p "$STAGE"
while IFS= read -r file; do
    case "$file" in ''|"$KIT_PANEL_BINARY"/*) continue ;; esac
    case "$file" in */*) [ -d "$STAGE/${file%/*}" ] || mkdir -p "$STAGE/${file%/*}" ;; esac
    cp -p "$HERE/$file" "$STAGE/$file"
    printf '%s\n' "$file"
done <"$PACKAGE_FILES" >"$STAGE/$KIT_PACKAGE_FILES"
chmod 644 "$STAGE/$KIT_PACKAGE_FILES"
chmod 755 "$STAGE" "$STAGE/$KIT_INSTALL_SCRIPT" "$STAGE/$KIT_UNINSTALL_SCRIPT" "$STAGE/$KIT_UPDATE_SCRIPT"
for executable in $FK_EXECUTABLES; do chmod 755 "$STAGE/$executable"; done

CHANGED=1
if timer_is_ours; then
    "$SYSTEMCTL" --user stop "$TIMER" >/dev/null 2>&1 || true
fi
if unit_is_ours; then
    "$SYSTEMCTL" --user stop "$UNIT" >/dev/null 2>&1 || true
fi

if [ -d "$VERSIONS/$FK_VERSION" ]; then
    OLD_SAME=$VERSIONS/.old-$$
    mv "$VERSIONS/$FK_VERSION" "$OLD_SAME"
fi
mv "$STAGE" "$VERSIONS/$FK_VERSION"
STAGE=

switch_current "$FK_VERSION"
APP_DIR=$CURRENT

if [ "$FK_SERVICE" = 1 ]; then
    cp "$APP_DIR/$UNIT" "$UNIT_DIR/$UNIT.new"
    mv -f "$UNIT_DIR/$UNIT.new" "$UNIT_DIR/$UNIT"
elif unit_is_ours; then
    remove_units "$UNIT"
fi
if [ "$FK_TIMER" = 1 ]; then
    cp "$APP_DIR/$TIMER" "$UNIT_DIR/$TIMER.new"
    mv -f "$UNIT_DIR/$TIMER.new" "$UNIT_DIR/$TIMER"
    TIMER_INSTALLED=1
elif timer_is_ours; then
    remove_units "$TIMER"
fi

DRIVERS_CHANGED=0
for driver in $OLD_DRIVERS; do
    if ! in_list "$driver" "$(for d in $FK_DRIVERS; do printf '%s ' "$APP_DIR/$d"; done)"; then
        "$VRPATHREG" removedriver "$driver" >/dev/null 2>&1 || true
        DRIVERS_CHANGED=1
    fi
done
for driver in $FK_DRIVERS; do
    "$VRPATHREG" adddriver "$APP_DIR/$driver" >/dev/null 2>&1 || rollback "failed to install SteamVR driver $driver"
    DRIVERS_CHANGED=1
done

if [ -n "$FK_PANEL" ] && [ -f "$APP_DIR/$FK_PANEL" ]; then
    cp "$APP_DIR/$FK_PANEL" "$PANEL_D/$FK_ID.json.new"
else
    printf '{}\n' >"$PANEL_D/$FK_ID.json.new"
fi
mv -f "$PANEL_D/$FK_ID.json.new" "$PANEL_D/$FK_ID.json"

panel_lists() {
    sed 's/^[0-9a-f]\{64\}  //' "$1/$KIT_PANEL_SUMS" | grep -qxF "$2"
}

panel_signed() {
    [ -f "$1/$KIT_PANEL_SUMS" ] && [ -f "$1/$KIT_PANEL_SIGNATURE" ] && [ -f "$HERE/$KIT_PANEL_KEY" ] &&
        openssl dgst -sha256 -verify "$HERE/$KIT_PANEL_KEY" -signature "$1/$KIT_PANEL_SIGNATURE" "$1/$KIT_PANEL_SUMS" >/dev/null 2>&1 &&
        ! grep -qv '^[0-9a-f]\{64\}  [A-Za-z0-9][A-Za-z0-9._-]*$' "$1/$KIT_PANEL_SUMS" &&
        panel_lists "$1" "$KIT_PANEL_BINARY" && panel_lists "$1" "$KIT_PANEL_UNIT" && panel_lists "$1" "$KIT_PANEL_VERSION_FILE" &&
        (cd "$1" && sha256sum -c --quiet "$KIT_PANEL_SUMS" >/dev/null 2>&1)
}

PANEL_UPGRADED=0
BUNDLED_PANEL=$HERE/$KIT_PANEL_BINARY
INSTALLED_PANEL=$PANEL_HOME/$KIT_CURRENT_LINK
CURRENT_PANEL=$(cat "$INSTALLED_PANEL/$KIT_PANEL_VERSION_FILE" 2>/dev/null || true)
if [ -f "$BUNDLED_PANEL/$KIT_PANEL_VERSION_FILE" ] && ! panel_signed "$BUNDLED_PANEL"; then
    if [ -n "$CURRENT_PANEL" ]; then
        say "warning: included community app panel failed signature checks. Keeping the existing version. Please report this."
    else
        say "warning: the community app panel in this package is not a signed FrameKit release, so the Community Apps tab won't be installed. Please report this."
    fi
elif [ -f "$BUNDLED_PANEL/$KIT_PANEL_VERSION_FILE" ]; then
    NEW_PANEL=$(cat "$BUNDLED_PANEL/$KIT_PANEL_VERSION_FILE")
    if [ -z "$CURRENT_PANEL" ] || ! panel_signed "$INSTALLED_PANEL" || version_gt "$NEW_PANEL" "$CURRENT_PANEL"; then
        PANEL_DEST=$PANEL_HOME/$KIT_VERSIONS_DIR/$NEW_PANEL
        rm -rf "$PANEL_DEST.new"
        mkdir -p "$PANEL_DEST.new"
        cp -a "$BUNDLED_PANEL/." "$PANEL_DEST.new/"
        chmod 755 "$PANEL_DEST.new/$KIT_PANEL_BINARY"
        rm -rf "$PANEL_DEST"
        mv "$PANEL_DEST.new" "$PANEL_DEST"
        ln -sfn "$KIT_VERSIONS_DIR/$NEW_PANEL" "$PANEL_HOME/$KIT_CURRENT_LINK.new"
        mv -Tf "$PANEL_HOME/$KIT_CURRENT_LINK.new" "$PANEL_HOME/$KIT_CURRENT_LINK"
        cp "$PANEL_DEST/$KIT_PANEL_UNIT" "$UNIT_DIR/$KIT_PANEL_UNIT.new"
        mv -f "$UNIT_DIR/$KIT_PANEL_UNIT.new" "$UNIT_DIR/$KIT_PANEL_UNIT"
        for panel_version in "$PANEL_HOME/$KIT_VERSIONS_DIR"/*; do
            [ "$panel_version" = "$PANEL_DEST" ] || rm -rf "$panel_version"
        done
        PANEL_UPGRADED=1
    fi
fi
if [ ! -f "$UNIT_DIR/$KIT_PANEL_UNIT" ] && [ -f "$INSTALLED_PANEL/$KIT_PANEL_UNIT" ]; then
    cp "$INSTALLED_PANEL/$KIT_PANEL_UNIT" "$UNIT_DIR/$KIT_PANEL_UNIT.new"
    mv -f "$UNIT_DIR/$KIT_PANEL_UNIT.new" "$UNIT_DIR/$KIT_PANEL_UNIT"
fi

printf 'AUTO_UPDATE=%s\n' "$AUTO_UPDATE" >"$META/$KIT_SETTINGS_FILE.new"
mv -f "$META/$KIT_SETTINGS_FILE.new" "$META/$KIT_SETTINGS_FILE"

if [ -f "$APP_DIR/hooks/post-install" ]; then
    hook_status=0
    APP_HOME=$APP_HOME APP_DIR=$APP_DIR FK_VERSION=$FK_VERSION FK_PREVIOUS=$PREVIOUS \
        timeout -k 10 "$KIT_HOOK_TIMEOUT_SECONDS" sh "$APP_DIR/hooks/post-install" </dev/null 8>&- 9>&- || hook_status=$?
    case $hook_status in
        0) ;;
        124|137) rollback "post-install step of $FK_NAME failed to finish within $((KIT_HOOK_TIMEOUT_SECONDS / 60)) minutes" ;;
        *) rollback "post-install step of $FK_NAME failed" ;;
    esac
fi

"$SYSTEMCTL" --user daemon-reload
UNIT_TYPE=
if [ "$FK_TIMER" = 1 ]; then
    "$SYSTEMCTL" --user disable "$UNIT" >/dev/null 2>&1 || true
    "$SYSTEMCTL" --user enable "$TIMER" >/dev/null 2>&1 || rollback "failed to enable $TIMER"
    "$SYSTEMCTL" --user restart "$TIMER" || rollback "could not start $TIMER"
elif [ "$FK_SERVICE" = 1 ]; then
    UNIT_TYPE=$("$SYSTEMCTL" --user show -p Type --value "$UNIT" 2>/dev/null || true)
    "$SYSTEMCTL" --user enable "$UNIT" >/dev/null 2>&1 || rollback "failed to enable $UNIT"
    start_status=0
    timeout -k 10 "$KIT_HOOK_TIMEOUT_SECONDS" "$SYSTEMCTL" --user restart "$UNIT" || start_status=$?
    case $start_status in
        0) ;;
        124|137) rollback "$UNIT failed to install within $((KIT_HOOK_TIMEOUT_SECONDS / 60)) minutes" ;;
        *) rollback "could not start $UNIT" ;;
    esac
fi
if [ -f "$UNIT_DIR/$KIT_PANEL_UNIT" ]; then
    "$SYSTEMCTL" --user enable "$KIT_PANEL_UNIT" >/dev/null 2>&1 || true
    if [ "$PANEL_UPGRADED" = 1 ]; then
        "$SYSTEMCTL" --user restart "$KIT_PANEL_UNIT" >/dev/null 2>&1 || say "warning: community app panel did not start"
    else
        "$SYSTEMCTL" --user start "$KIT_PANEL_UNIT" >/dev/null 2>&1 || say "warning: community app panel did not start"
    fi
fi

if [ "$FK_TIMER" = 1 ]; then
    "$SYSTEMCTL" --user is-active --quiet "$TIMER" || rollback "$TIMER is not active"
elif [ "$FK_SERVICE" = 1 ] && [ "$UNIT_TYPE" != oneshot ]; then
    sleep 2
    if ! "$SYSTEMCTL" --user is-active --quiet "$UNIT"; then
        sleep 3
        "$SYSTEMCTL" --user is-active --quiet "$UNIT" || rollback "$UNIT stopped unexpectedly"
    fi
fi

{
    printf 'format %s\n' "$KIT_FORMAT"
    printf 'version %s\n' "$FK_VERSION"
    if [ "$FK_SERVICE" = 1 ]; then printf 'unit %s\n' "$UNIT"; fi
    if [ "$FK_TIMER" = 1 ]; then printf 'timer %s\n' "$TIMER"; fi
    for driver in $FK_DRIVERS; do printf 'driver %s\n' "$APP_DIR/$driver"; done
    printf 'panel %s\n' "$PANEL_D/$FK_ID.json"
} >"$META/$KIT_RECEIPT_FILE.new"
mv -f "$META/$KIT_RECEIPT_FILE.new" "$META/$KIT_RECEIPT_FILE"

if [ -n "$KEEP" ] && [ "$KEEP" != "$FK_VERSION" ] && [ -d "$VERSIONS/$KEEP" ]; then
    printf '%s\n' "$KEEP" >"$META/$KIT_PREVIOUS_FILE"
else
    KEEP=
    rm -f "$META/$KIT_PREVIOUS_FILE"
fi
[ -z "$OLD_SAME" ] || rm -rf "$OLD_SAME"
for version_dir in "$VERSIONS"/*; do
    name=${version_dir##*/}
    [ "$name" = "$FK_VERSION" ] || [ "$name" = "$KEEP" ] || rm -rf "$version_dir"
done

steamvr_restart_pending() {
    invocation=$("$SYSTEMCTL" --user show -p InvocationID --value "$KIT_STEAMVR_UNIT" 2>/dev/null | tr -cd '0-9a-f')
    if [ -n "$invocation" ]; then
        printf '%s\n' "$invocation" >"$META/$KIT_STEAMVR_RESTART_FILE.new" && mv -f "$META/$KIT_STEAMVR_RESTART_FILE.new" "$META/$KIT_STEAMVR_RESTART_FILE"
    fi
    say "$KIT_STEAMVR_RESTART_NEEDED"
}

if [ "$DRIVERS_CHANGED" = 1 ] && [ "${FK_RESTART_STEAMVR:-0}" = 1 ] && "$SYSTEMCTL" --user is-active --quiet "$KIT_STEAMVR_UNIT"; then
    if [ "$RESTART_STEAMVR" = 1 ]; then
        say "Restarting SteamVR to load new driver"
        if ! "$SYSTEMCTL" --user restart "$KIT_STEAMVR_UNIT"; then
            say "warning: SteamVR did not restart"
            steamvr_restart_pending
        fi
    else
        steamvr_restart_pending
    fi
fi

if [ "${FK_UPDATE_LOCK_HELD:-0}" != 1 ]; then
    sh "$APP_DIR/$KIT_UPDATE_SCRIPT" refresh >/dev/null 2>&1 8>&- 9>&- || true
fi

finish_ok "$FK_ID $FK_VERSION${PREVIOUS:+ previous $PREVIOUS}"
