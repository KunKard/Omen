@echo off
rem Replicate Unity's script compilation without launching the editor.
rem
rem Why: the Unity editor is normally open on this project, so -batchmode would fight over the
rem project lock. This compiles every .cs under Assets\ against Unity's own reference assemblies
rem (Mono 4.7.1-api + the UnityEngine modules) and prints the same diagnostics the Console would.
rem
rem Usage:  Tools\verify-compile.cmd
rem Exit code 0 = compile OK. Non-zero = compile failed; read the reported errors.

setlocal
set PS1=%~dp0verify-compile.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
exit /b %ERRORLEVEL%
