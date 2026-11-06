@echo off
setlocal

cd /d "%~dp0"
if errorlevel 1 exit /b 1

rem Build shared dependencies once before starting any runtime.
echo Building the middleware solution...
dotnet build .\WISE_Integrated_Middleware.sln --configuration Debug -m:1
if errorlevel 1 (
    echo Build failed. No new middleware processes were started.
    echo If files are locked, stop existing middleware consoles before retrying.
    exit /b 1
)

start "Middleware.Api" cmd /k dotnet run --no-build --configuration Debug --project .\src\Middleware.Api\Middleware.Api.csproj
start "Middleware.Worker" cmd /k dotnet run --no-build --configuration Debug --project .\src\Middleware.Worker\Middleware.Worker.csproj
start "ProcessCoordinator" cmd /k dotnet run --no-build --configuration Debug --project .\src\ProcessCoordinator\ProcessCoordinator.csproj

echo Opened Middleware.Api, Middleware.Worker, and ProcessCoordinator consoles. Check each console for startup status.
