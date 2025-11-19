@echo off
setlocal ENABLEDELAYEDEXPANSION

REM Root-relative asset target (run from solution root)
set TARGET=src\ScriptRunner.Service\wwwroot\lib
set FONTDIR=%TARGET%\fonts

REM Create directories
if not exist "%TARGET%" mkdir "%TARGET%"
if not exist "%FONTDIR%" mkdir "%FONTDIR%"

echo Using TARGET: %TARGET%

echo Detecting downloader...
where wget >nul 2>&1
if %errorlevel%==0 (
  set DL=wget -q -O
) else (
  where curl >nul 2>&1
  if %errorlevel%==0 (
    set DL=curl -L -s -o
  ) else (
    echo Neither wget nor curl found. Install one and rerun.
    exit /b 1
  )
)

echo Downloading Vue...
call :download "%TARGET%/vue.global.prod.js" https://unpkg.com/vue@3.4.31/dist/vue.global.prod.js || goto :err

echo Downloading PrimeVue CSS...
call :download "%TARGET%/primevue.min.css" https://unpkg.com/primevue@3.52.0/resources/primevue.min.css || goto :err
call :download "%TARGET%/theme.css" https://unpkg.com/primevue@3.52.0/resources/themes/aura-light-blue/theme.css || goto :err

echo Downloading PrimeIcons CSS and fonts...
call :download "%TARGET%/primeicons.css" https://unpkg.com/primeicons@6.0.1/primeicons.css || goto :err
call :download "%FONTDIR%/primeicons.eot" https://unpkg.com/primeicons@6.0.1/fonts/primeicons.eot || goto :err
call :download "%FONTDIR%/primeicons.ttf" https://unpkg.com/primeicons@6.0.1/fonts/primeicons.ttf || goto :err
call :download "%FONTDIR%/primeicons.woff" https://unpkg.com/primeicons@6.0.1/fonts/primeicons.woff || goto :err
call :download "%FONTDIR%/primeicons.woff2" https://unpkg.com/primeicons@6.0.1/fonts/primeicons.woff2 || goto :err

echo Downloading PrimeVue core (config) and components...
set PVM_BASE=https://unpkg.com/primevue@3.52.0
REM Core plugin UMD is config.min.js (NOT primevue.min.js)
call :download "%TARGET%/config.min.js" %PVM_BASE%/config/config.min.js || goto :err
call :download "%TARGET%/tree.min.js" %PVM_BASE%/tree/tree.min.js || goto :err
call :download "%TARGET%/overlaypanel.min.js" %PVM_BASE%/overlaypanel/overlaypanel.min.js || goto :err
call :download "%TARGET%/tabview.min.js" %PVM_BASE%/tabview/tabview.min.js || goto :err
call :download "%TARGET%/progressspinner.min.js" %PVM_BASE%/progressspinner/progressspinner.min.js || goto :err
call :download "%TARGET%/datatable.min.js" %PVM_BASE%/datatable/datatable.min.js || goto :err
call :download "%TARGET%/column.min.js" %PVM_BASE%/column/column.min.js || goto :err
call :download "%TARGET%/menubar.min.js" %PVM_BASE%/menubar/menubar.min.js || goto :err
call :download "%TARGET%/card.min.js" %PVM_BASE%/card/card.min.js || goto :err
call :download "%TARGET%/toast.min.js" %PVM_BASE%/toast/toast.min.js || goto :err
call :download "%TARGET%/toastservice.min.js" %PVM_BASE%/toastservice/toastservice.min.js || goto :err
call :download "%TARGET%/tooltip.min.js" %PVM_BASE%/tooltip/tooltip.min.js || goto :err

echo All UI assets downloaded successfully.
exit /b 0

:download
set DEST=%~1
set URL=%~2
echo   -> %DEST%
%DL% "%DEST%" %URL%
if %errorlevel% neq 0 (
  echo Failed: %URL%
  exit /b 1
)
exit /b 0

:err
echo ERROR downloading one or more files. Aborted.
exit /b 1

endlocal