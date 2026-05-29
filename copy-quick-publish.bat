@echo off
setlocal

set "PUBLISH_DIR=E:\dot_net\MyWireGuard\src\MyWireGuard.App\bin\Release\net10.0-windows\publish\win-x64"
set "QUICK_DIR=%PUBLISH_DIR%\quick"
set "FAILED=0"

if not exist "%PUBLISH_DIR%\" (
    echo [ERROR] Publish directory was not found:
    echo         %PUBLISH_DIR%
    pause
    exit /b 1
)

if not exist "%QUICK_DIR%\" (
    mkdir "%QUICK_DIR%"
    if errorlevel 1 (
        echo [ERROR] Failed to create quick directory:
        echo         %QUICK_DIR%
        pause
        exit /b 1
    )
)

call :CopyFile "MyWireGuard.exe"
call :CopyFile "MyWireGuard.dll"
call :CopyFile "MyWireGuard.Core.dll"
call :CopyFile "MyWireGuard.Infrastructure.dll"
call :CopyFile "MyWireGuard.deps.json"
call :CopyFile "MyWireGuard.runtimeconfig.json"
call :CopyFile "MyWireGuard.ServiceHost.exe"
call :CopyFile "MyWireGuard.ServiceHost.dll"
call :CopyFile "MyWireGuard.ServiceHost.deps.json"
call :CopyFile "MyWireGuard.ServiceHost.runtimeconfig.json"

if "%FAILED%"=="1" (
    echo.
    echo [ERROR] Some files were not copied.
    pause
    exit /b 1
)

echo.
echo [OK] Files copied to:
echo      %QUICK_DIR%
pause
exit /b 0

:CopyFile
set "FILE_NAME=%~1"
if not exist "%PUBLISH_DIR%\%FILE_NAME%" (
    echo [ERROR] Missing %FILE_NAME%
    set "FAILED=1"
    exit /b 0
)

copy /Y "%PUBLISH_DIR%\%FILE_NAME%" "%QUICK_DIR%\" >nul
if errorlevel 1 (
    echo [ERROR] Failed to copy %FILE_NAME%
    set "FAILED=1"
) else (
    echo [OK] %FILE_NAME%
)
exit /b 0
