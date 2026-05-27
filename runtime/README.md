MyWireGuard runtime assets

Place the official embeddable runtime files in this directory:

- tunnel.dll
- wireguard.dll

How they are used:

- During build, any .dll files in this folder are copied to the WPF app output directory.
- At runtime, MyWireGuard also searches this folder and copies the DLLs next to the executable if they are missing.

Expected sources:

- tunnel.dll: build it from WireGuard/wireguard-windows/embeddable-dll-service
- wireguard.dll: obtain the matching WireGuardNT runtime from the official WireGuard distribution channel

Helper script:

- from the repository root, run: powershell -ExecutionPolicy Bypass -File .\scripts\Get-WireGuardDll.ps1
- to request a specific SDK archive version: powershell -ExecutionPolicy Bypass -File .\scripts\Get-WireGuardDll.ps1 -Version 1.1
- to fetch the complete runtime set: powershell -ExecutionPolicy Bypass -File .\scripts\Get-WireGuardRuntime.ps1
- when no official prebuilt tunnel.dll URL is available, the runtime script falls back to downloading upstream source and building tunnel.dll automatically

Alternative source locations supported by the app:

- %LOCALAPPDATA%/MyWireGuard/Runtime
- the directory pointed to by MYWIREGUARD_RUNTIME_DIR

Notes:

- wireguard.exe is not a substitute for tunnel.dll
- both tunnel.dll and wireguard.dll are required