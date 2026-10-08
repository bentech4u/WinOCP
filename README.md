# WinOCP

Portable Windows desktop file transfer for OpenShift. Native WPF interface, bundled .NET 10 runtime and official `oc.exe`.

## Run

Extract **all** files from `WinOCP-win-x64.zip` into a writable folder and launch `WinOCP.exe`. No installer, Node, Electron, or separately installed .NET runtime is required. Targets Windows x64 with a desktop environment supported by .NET 10; Windows Server Core is not supported.

1. The Connection Manager opens at startup. Select an existing site or choose **New site**. Enter a site name and server on General, then choose Token, Username / password, or Kubeconfig on General.
2. For token/password, enter the HTTPS API server and secret; username is required only for password login. For kubeconfig, choose the file instead.
3. Use **Save site** to keep the connection, then **Login** to open it in the file browser. Select a project, a running pod, and a container.
4. Browse local folders on the left and container folders on the right. Use the local Locations dropdown to open a drive, Home, Desktop, Documents, or Downloads without typing a path. The drive list refreshes whenever you open the dropdown. Double-click folders, type a path and press Enter, or use Go / Up.
5. Select one or more files/folders and drag them into the opposite pane. Confirm the destination; existing files may be overwritten. Cancel stops the CLI but does not roll back partial transfers.

The project picker uses the OpenShift projects endpoint for the signed-in account, then hides system projects named `openshift`, `openshift-*`, or `kube-*`. This applies at login and on refresh, including for administrators. Other accessible projects, including `default`, remain visible. A saved default project cannot bypass this filter. If no user projects remain, the list stays empty and the status explains why. The account needs permission to list projects/pods and execute commands in the selected container. Browsing requires `sh`, `find` supporting `-mindepth`/`-maxdepth`, and `printf`; file streaming requires `cat` and `wc`; folder transfer requires `tar` inside the container. Distroless containers without these tools cannot be browsed. Local Windows filenames and permissions apply to downloads. Transfers opens by default above Activity log; opening either section folds the other. Each queued item has a Cancel button. Individual files display measured bytes, percentage, remaining size, and average speed. Folder transfers use `oc cp` with indeterminate progress because it does not expose byte counts. File progress measures bytes streamed through the CLI; Completed is shown only after successful CLI exit. Transfers run sequentially. No resume is implemented. Folder symlink behavior follows `oc cp`; individual file streaming follows the source symlink.

## Saved connections

The manager opens in front of the file browser at startup. Use the **Connections** toolbar button to reopen it. Create, edit, search, group, and delete named sites. The compact General tab contains the site name, API URL, login method, and credentials together. Advanced holds group, description, default project, and TLS settings. Notes holds free text. Login also works without saving a site. Failed login stays in the manager; Cancel stops the login attempt. One cluster connection is active at a time, and logging into another site replaces it.

Sites and the startup preference are stored in `connections.json` beside the executable. Secrets are not saved unless **Save secret for this Windows account** is checked when you save the site. Saved tokens/passwords use Windows DPAPI, tied to the current Windows account. When moving the portable folder to another PC or Windows account, you may need to enter and save the secret again. Site metadata is portable; no plaintext password or token is stored in the site file. Do not put secrets into Notes. Kubeconfig files are referenced, not copied; relative paths are resolved from the application folder. Re-select the file if its path changes.

The startup checkbox also controls whether the manager reopens after disconnecting. Closing the manager leaves the disconnected file browser open. Logging in does not automatically save unsaved site edits.

## Appearance and drag-and-drop

Use the toolbar Theme menu to choose **System**, **Light**, or **Dark**. System is the default and follows the Windows app theme, including changes while WinOCP is open. The selection is stored in `settings.json` beside the executable (no credentials are stored there).

Drag selected files or folders from the local file list to the remote file list to upload, or from remote to local to download. Drops always target the directory currently shown in the destination pane, including when you drop over a folder row. A confirmation shows the destination before copying; source files are kept. Multiple selections are supported. Explorer files can also be dropped into the remote pane. Remote files cannot be dragged out to Explorer in this version. Connect and select a project, pod, and container first. Drops are disabled during operations.

## Authentication

TLS verification is enabled by default. Select **Skip TLS certificate verification** before connecting to bypass server certificate validation for login, browsing, and transfers (including kubeconfig). Reconnect after changing the checkbox. When unchecked, verification is explicitly enabled even if the kubeconfig disables it. For private certificate authorities, supply a kubeconfig containing the CA. A kubeconfig is flattened from its original location to preserve relative certificate references; its original file is not edited. External credential plugins (`exec` / `auth-provider`) are rejected because their dependencies are not bundled. Password login depends on the cluster's identity provider supporting it.

Saved site secrets, when explicitly enabled, are encrypted in the application folder as described above. The CLI uses an isolated temporary kubeconfig, removed on disconnect or normal exit. A crash can leave that temporary folder behind. Login secrets are passed to `oc` through process arguments and can be visible to privileged local process inspection; use this application on trusted Windows machines. No supplied example token is included in the project.

## Build

Install a .NET 10 SDK on the build machine and obtain a trusted official Windows x64 OpenShift CLI compatible with your cluster. Then run:

```powershell
./Build-Portable.ps1 -OcExe C:/path/to/oc.exe
```

This creates `portable-v11/` and `WinOCP-win-x64.zip`, bundling the runtime and CLI. Building requires access to NuGet when runtime packs are not cached. The source uses only framework libraries. The initial bundle includes OpenShift CLI 4.22.17; its downloaded archive was verified against the official SHA-256 checksum. Rebuild with a different official CLI if your cluster requires another version.

## Validation

The project has been compiled locally. Connection persistence, encryption roundtrip, search, startup settings, and login success/failure/cancellation using a mock CLI have been checked. Light and dark manager layouts have been rendered for inspection. Live authentication, browsing, and transfers require a real OpenShift cluster and remain unverified. Before operational use, test all required login methods and round-trip a small folder on a nonproduction pod, including spaces and Unicode filenames. Check cancellation, denied permissions, and a container without tar.

## References



- .NET self-contained deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/
- OpenShift CLI: https://mirror.openshift.com/pub/openshift-v4/clients/ocp/
- OpenShift file-copy prerequisites: https://docs.redhat.com/en/documentation/openshift_container_platform/4.13/html-single/nodes/index#nodes-containers-copying-files
