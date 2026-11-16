@echo off
echo Compilazione HyperNotch (.NET 8 Self-Contained Single-File)...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
if %errorlevel% neq 0 (
    echo Errore durante la compilazione.
    exit /b %errorlevel%
)
echo.
echo Build completata! File eseguibile generato in:
echo bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\HyperNotch.exe
