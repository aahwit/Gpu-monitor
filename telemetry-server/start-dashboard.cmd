@echo off
setlocal
cd /d "%~dp0"
echo Starting GPU Fleet Health dashboard...
echo Open http://127.0.0.1:8765 in your browser.
python server.py
