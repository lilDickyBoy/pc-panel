@echo off
title PC Panel - Publish to GitHub
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish-github.ps1"
