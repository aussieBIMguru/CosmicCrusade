@echo off
rem ---------------------------------------------------------------------------
rem  publish.bat - builds a Cosmic Crusade release: tests, the self-contained app and
rem  artifacts\CosmicCrusade-Setup-<version>.exe. Needs the .NET 8 SDK and Inno Setup 6.
rem  The version comes from <Version> in Directory.Build.props.
rem
rem  Passes any arguments on to build\publish.ps1, for example:
rem    publish.bat -SkipTests          (no tests)
rem    publish.bat -AllowMissingArt    (build even with an empty data\art, to try the installer out)
rem    publish.bat -InnoCompiler "D:\Tools\Inno Setup 6\ISCC.exe"
rem    publish.bat -Sign -CertThumbprint <thumbprint>
rem ---------------------------------------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\publish.ps1" %*
if errorlevel 1 (
    echo.
    echo Publish FAILED - see the messages above.
)
pause
