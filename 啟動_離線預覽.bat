@echo off
chcp 65001 >nul
rem 超禁忌學園 EDEN — 離線預覽：啟動模擬伺服器，再開遊戲（不連正式伺服器，不會消耗 EP）
cd /d "%~dp0"
start "EDEN 模擬伺服器" /min python "Tools\eden_mock_server.py"
timeout /t 2 /nobreak >nul
start "" "Build\ForbiddenEden.exe" -server http://127.0.0.1:3905
