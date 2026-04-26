@echo off
echo ============================================
echo   File Converter Uninstaller
echo ============================================
echo.

:: Check admin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Please run this script as Administrator.
    echo Right-click ^> Run as administrator
    pause
    exit /b 1
)

set INSTALLDIR=%ProgramFiles%\File Converter
set APPDATA_DIR=%LOCALAPPDATA%\FileConverter

:: Step 1: Try MSI uninstall first
echo [1/5] Attempting MSI uninstall...
for /f "tokens=2 delims==" %%i in ('wmic product where "name='File Converter'" get IdentifyingNumber /value 2^>nul ^| find "="') do (
    echo Found MSI product: %%i
    msiexec /x %%i /quiet /norestart
    echo MSI uninstall completed.
    goto :cleanup
)
echo No MSI installation found, proceeding with manual cleanup.

:cleanup
:: Step 2: Unregister shell extension
echo [2/5] Unregistering shell extension...
if exist "%INSTALLDIR%\FileConverter.exe" (
    "%INSTALLDIR%\FileConverter.exe" --unregister-shell-extension "%INSTALLDIR%\FileConverterExtension.dll" 2>nul
)

:: Step 3: Kill processes
echo [3/5] Stopping processes...
taskkill /f /im FileConverter.exe 2>nul
taskkill /f /im explorer.exe 2>nul
timeout /t 3 /nobreak >nul

:: Step 4: Delete installation directory
echo [4/5] Removing installation files...
if exist "%INSTALLDIR%" (
    rmdir /s /q "%INSTALLDIR%"
    if exist "%INSTALLDIR%" (
        echo WARNING: Some files could not be deleted. They will be removed on reboot.
    ) else (
        echo Installation directory removed.
    )
) else (
    echo Installation directory not found (already clean).
)

:: Step 5: Clean user data (optional)
echo [5/5] Cleaning user data...
if exist "%APPDATA_DIR%" (
    rmdir /s /q "%APPDATA_DIR%"
    echo User data removed.
) else (
    echo No user data found.
)

:: Restart Explorer
echo.
echo Restarting Explorer...
start explorer.exe

echo.
echo ============================================
echo   Uninstall complete!
echo ============================================
echo.
pause
