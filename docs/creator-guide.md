# FrameKit Creator Guide

Do you want to ship your own Steam Frame applications with the FrameKit installer? You can!

This is how:

## Requirements for your dev machine

- Windows or Linux
- .NET10 SDK
- The latest FrameKit release (just download the main branch)

## Packaging your Steam Frame app with FrameKit
### Step 1: Creating a signature key

You must sign your packages for installation via FrameKit. This prevents thid-parties from distributing malicious versions of your application if your distribution platform ever gets compromised.

#### Create a key by running

```
.\framekit key --passphrase
```

This creates a `.framekit` directory in your home directory, containing `release-key.pem`.

> [!CAUTION]
Make sure to use a secure passphrase that is not used anywhere else! Your passphrase is needed whenever you want to release a new version of your application, so keep your key and the passphrase somewhere safe! Do not, under any circumstances, make the key publicly available, including on any git repositories.

While you can create a key without a passphrase, I really do not recommend that as it means anyone that has your key will be able to impersonate your application.

You only need to do this once, even if you publish multiple packages. They share your key.

### Step 2: Choose an application ID

The ID is the internal name of your application and must be unique across all FrameKit applications. So consider prefixing it with your name (something like creator-cool-app).

### Step 3: App-template

Make a copy of the app-template directory and rename it with your application ID.

### Step 4: Prepare your payload

Put your application files into the payload directory:

```
payload\
  creator-cool-app             your program (ARM64 Linux)
  creator-cool-app.service     service defintion (optional, only if your application should run in a system service)
  panel.json                   your section on the Community Apps tab (optional)
  icon.svg                     icon for your application in the Community Apps tab (optional)
  config.json                  application default settings (optional)
  hooks\post-install           post-install script (optional)
```

### Step 5: Edit `frameapp.json`

```json
{
  "id": "creator-cool-app",
  "name": "My Cool App",
  "version": "1.0.0",
  "description": "Does really cool things, if it works.",
  "publisher": "Cool Creator",
  "support_url": "https://git.example.com/creator/cool-app/issues",
  "min_os_version": "0.5.6",
  "free_mb": 20,
  "executables": ["creator-cool-app"],
  "service": true,
  "steamvr_drivers": [],
  "restart_steamvr": false,
  "data_dirs": [".local/state/creator-cool-app"],
  "panel": "panel.json",
  "updates": {
    "index": "https://git.example.com/creator/cool-app/releases/latest/download/creator-cool-app-latest.txt",
    "base": "https://git.example.com/creator/cool-app/releases/latest/download/"
  },
  "setup": {
    "found_it": "Open the Steam menu, then Quick Access → Community Apps → Cool App.",
    "icon": "setup-icon.png"
  }
}
```

### Step 6 (optional): Add your application to the Community Apps tab

Add `panel.json`:

```json
{
  "icon": "icon.svg",
  "status": { "command": ["%APP%/creator-cool-app", "panel-status"], "interval": 5 },
  "actions": [
    { "id": "do_it", "label": "Do the thing!", "command": ["%APP%/creator-cool-app", "do_it"] }
  ],
  "toggles": [
    { "id": "double", "label": "Do it double!",
      "get": ["%APP%/creator-cool-app", "double"],
      "on":  ["%APP%/creator-cool-app", "double", "on"],
      "off": ["%APP%/creator-cool-app", "double", "off"] }
  ]
}
```

- Status: Updates the panel status text with the output of the provided command every 'interval' seconds.
- Actions: Each action will add a button with the given label. Clicking it will run the command, which contains the command to run in your application hierarchy and a list of arguments.
- Toggles: same as actions, but creates toggle UI elements. Get should return the current setting as true or false while on or off run the respective command and arguments when the user clicks the toggle.

> [!NOTE]
> Regarding limits:
> - Buttons: max 4, up to 2 per row
> - Toggles: max 6
> - Icon: SVG, max 16kB
> - Status interval: min 2 secs, max 300 secs


### Step 7: Build the release

Run

```
.\framekit release --manifest <path to frameapp.json> --payload <path to payload dir> --out <output dir>
```

This will create the following files in the output directory:

- `creator-cool-app-1.0.0-aarch64.tar.gz`: application package
- `creator-cool-app-1.0.0-aarch64.tar.gz.sig`: signature
- `creator-cool-app-latest.txt`: version info for updates
- `CoolApp-Setup-1.0.0.exe`: Windows setup
- `CoolApp-Setup-1.0.0-linux-x64.tar.gz`: Linux setup

Publish these files whereever you like and run setup to install.

### Updating

1. Raise `version` in `frameapp.json` and in your application
2. Build and publish new release
3. Mark the new release as the latest one (for example Github release page)

Users can either update through the update button in the Community Apps tab or by downloading your new release and installing via the setup.

### Switching to a new key

1. Make a new key: `.\framekit key --path <path to new-key-name.pem> --passphrase`.
2. Release your next version signed with the **old** key, handing over to the new one:
   `.\framekit release … --key <old key> --next-key <new key>`.
3. Headsets need to update to this version before they can update to new releases using your new key, so make sure to givem them plenty of time to update first.

> [!IMPORTANT]
If users do not update in time, the next update check returns the error "Update file failed signature checks". In that case they need to run the newest setup from your releases to update.

