@echo off
cd /d "%~dp0"
python -m record_main
if errorlevel 1 pause

