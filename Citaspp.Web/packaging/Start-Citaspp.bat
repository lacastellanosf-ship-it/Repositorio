@echo off
setlocal
cd /d "%~dp0"

start "Citaspp - servidor local" "%~dp0Citaspp.Web.exe" --urls "http://localhost:5020"

set /a attempts=0
:wait_for_server
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $response = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5020/' -TimeoutSec 2; if ($response.StatusCode -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>nul
if not errorlevel 1 goto open_browser
set /a attempts+=1
if %attempts% GEQ 30 goto server_timeout
timeout /t 1 /nobreak >nul
goto wait_for_server

:open_browser
start "" "http://localhost:5020/"
exit /b 0

:server_timeout
echo No se pudo iniciar Citaspp en http://localhost:5020/.
echo Comprueba que el puerto 5020 este libre y revisa la ventana del servidor.
pause
exit /b 1
