@echo off
chcp 65001 >nul
color 0A
cls

echo.
echo ╔═══════════════════════════════════════════════════════════════╗
echo ║                                                               ║
echo ║          🚀 ServerApi - Clean Build and Start 🚀              ║
echo ║                                                               ║
echo ╚═══════════════════════════════════════════════════════════════╝
echo.

REM ═══════════════════════════════════════════════════════════════
REM   Step 1: Clean up old processes
REM ═══════════════════════════════════════════════════════════════
echo.
echo ═══════════════════════════════════════════════════════════════
echo   🧹 Step 1/3: Cleaning up old processes
echo ═══════════════════════════════════════════════════════════════
echo.

REM Kill processes on port 5000 (WebSocket)
echo Checking port 5000 (WebSocket)...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5000 ^| findstr LISTENING') do (
    echo   ✓ Killing process %%a on port 5000...
    taskkill /F /PID %%a >nul 2>&1
)

REM Kill processes on port 5001 (HTTP)
echo Checking port 5001 (HTTP)...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5001 ^| findstr LISTENING') do (
    echo   ✓ Killing process %%a on port 5001...
    taskkill /F /PID %%a >nul 2>&1
)

REM Kill processes on port 5003 (TCP Stream)
echo Checking port 5003 (TCP Stream)...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5003 ^| findstr LISTENING') do (
    echo   ✓ Killing process %%a on port 5003...
    taskkill /F /PID %%a >nul 2>&1
)

echo.
echo ✅ All old processes cleaned up
timeout /t 1 /nobreak >nul

REM ═══════════════════════════════════════════════════════════════
REM   Step 2: Build all projects
REM ═══════════════════════════════════════════════════════════════
echo.
echo ═══════════════════════════════════════════════════════════════
echo   🔨 Step 2/4: Building projects
echo ═══════════════════════════════════════════════════════════════
echo.

REM Build Core project first
echo [1/3] Building Core library...
cd /d "%~dp0Core"
dotnet build --nologo --verbosity quiet
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ❌ Core build failed!
    echo.
    pause
    exit /b 1
)
echo ✅ Core build successful

echo.
echo [2/3] Building Asp.net server...
cd /d "%~dp0Asp.net"
dotnet build --nologo --verbosity quiet
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ❌ Asp.net server build failed!
    echo.
    pause
    exit /b 1
)
echo ✅ Asp.net server build successful

echo.
echo [3/3] Building Client.Asp.net...
cd /d "%~dp0Client.Asp.net"
dotnet build --nologo --verbosity quiet
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ❌ Client.Asp.net build failed!
    echo.
    pause
    exit /b 1
)
echo ✅ Client.Asp.net build successful

REM ═══════════════════════════════════════════════════════════════
REM   Step 3: Start Asp.net Server in new window
REM ═══════════════════════════════════════════════════════════════
echo.
echo ═══════════════════════════════════════════════════════════════
echo   🚀 Step 3/4: Starting Asp.net Server
echo ═══════════════════════════════════════════════════════════════
echo.

echo Starting server in new window...
start "🖥️ Asp.net Server" cmd /k "color 0B && title 🖥️ Asp.net Server - WS:5000 HTTP:5001 TCP:5003 && cd /d %~dp0Asp.net && echo. && echo ╔═══════════════════════════════════════════════════════════════╗ && echo ║          🖥️  ASP.NET SERVER 🖥️                              ║ && echo ║          WebSocket: ws://localhost:5000/ws                  ║ && echo ║          HTTP API:  http://localhost:5001                   ║ && echo ║          TCP Stream: localhost:5003                          ║ && echo ╚═══════════════════════════════════════════════════════════════╝ && echo. && dotnet run --no-build"

echo ✅ Server starting on ports:
echo   • WebSocket:   ws://localhost:5000/ws
echo   • HTTP API:    http://localhost:5001
echo   • TCP Stream:  localhost:5003
echo.
echo ⏳ Waiting 5 seconds for server to initialize...
timeout /t 5 /nobreak >nul

REM ═══════════════════════════════════════════════════════════════
REM   Step 4: Start Client.Asp.net in new window
REM ═══════════════════════════════════════════════════════════════
echo.
echo ═══════════════════════════════════════════════════════════════
echo   🚀 Step 4/4: Starting Client.Asp.net
echo ═══════════════════════════════════════════════════════════════
echo.

echo Starting client in new window...
start "💻 Client.Asp.net" cmd /k "color 0E && title 💻 Client.Asp.net - API:5002 && cd /d %~dp0Client.Asp.net && echo. && echo ╔═══════════════════════════════════════════════════════════════╗ && echo ║          💻  CLIENT.ASP.NET 💻                                ║ && echo ║          API:     http://localhost:5002                      ║ && echo ║          Web UI: http://localhost:5002/client-ui.html        ║ && echo ╚═══════════════════════════════════════════════════════════════╝ && echo. && dotnet run --no-build"

echo ✅ Client starting on port 5002
echo.
echo ⏳ Waiting 3 seconds for client to initialize...
timeout /t 3 /nobreak >nul

echo.
echo ═══════════════════════════════════════════════════════════════
echo   ✅ All Systems Started!
echo ═══════════════════════════════════════════════════════════════
echo.
echo 📊 Server Console (Blue Window):
echo    - WebSocket: ws://localhost:5000/ws
echo    - HTTP API:  http://localhost:5001
echo    - TCP Stream: localhost:5003
echo    - Web UI: http://localhost:5001/server-ui.html
echo.
echo 📊 Client Console (Yellow Window):
echo    - API: http://localhost:5002
echo    - Web UI: http://localhost:5002/client-ui.html
echo.
echo 🌐 Opening Web UI in browser...
timeout /t 2 /nobreak >nul
start http://localhost:5002/client-ui.html

echo.
echo ═══════════════════════════════════════════════════════════════
echo   ✅ Setup Complete!
echo ═══════════════════════════════════════════════════════════════
echo.
echo 💡 Tips:
echo    • Check the Blue window for server logs
echo    • Check the Yellow window for client logs
echo    • Use the browser to test chat functionality
echo.
echo Press any key to stop all servers and exit...
pause >nul

echo.
echo 🛑 Stopping all servers...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5000 ^| findstr LISTENING') do (
    taskkill /F /PID %%a >nul 2>&1
)
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5001 ^| findstr LISTENING') do (
    taskkill /F /PID %%a >nul 2>&1
)
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5002 ^| findstr LISTENING') do (
    taskkill /F /PID %%a >nul 2>&1
)
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5003 ^| findstr LISTENING') do (
    taskkill /F /PID %%a >nul 2>&1
)

echo ✅ All servers stopped
echo.
echo Goodbye! 👋
timeout /t 2 /nobreak >nul
