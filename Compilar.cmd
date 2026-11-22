@echo off
dotnet publish "%~dp0src\Pulso.csproj" -c Release -o "%~dp0Aplicativo"
exit /b %ERRORLEVEL%
