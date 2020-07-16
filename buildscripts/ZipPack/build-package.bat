rem Generate the zip Release package.
rem For information on how to setup your environment.
rem see https://github.com/neuoy/KSPTrajectories/blob/master/CONTRIBUTING.md

@echo off

rem get parameters that are passed by visual studio post build event
SET TargetName=%1
SET Dllversion=%~n2
SET KSPversion=%3

rem make sure the initial working directory is the one containing the current script
SET scriptPath=%~dp0
SET rootPath=%scriptPath%..\..\
SET initialWD=%CD%

echo Generating %TargetName% Bootstrap files...
cd "%rootPath%"
rem copy Bootstrap dll from build directory to GameData
IF EXIST "%initialWD%\%TargetName%Bootstrap.dll" xcopy /y "%initialWD%\%TargetName%Bootstrap.dll" "GameData\%TargetName%\Plugin\*" > nul
rem rename dll to KSP version specific bin
echo %TargetName%.dll -^> %TargetName%%KSPversion%.bin
move /y "%initialWD%\%TargetName%.dll" "%initialWD%\%TargetName%%KSPversion%.bin" > nul

rem only one bin file for KSP 1.3.1
IF %KSPversion% EQU 13 GOTO ksp13
IF %KSPversion% GTR 17 GOTO ksp18

rem if built version is KSP 1.7.2 then we copy built bin for the other compatible lower versions 
xcopy /y "%initialWD%\%TargetName%%KSPversion%.bin" "%initialWD%\%TargetName%14.bin*" > nul
xcopy /y "%initialWD%\%TargetName%%KSPversion%.bin" "%initialWD%\%TargetName%15.bin*" > nul
xcopy /y "%initialWD%\%TargetName%%KSPversion%.bin" "%initialWD%\%TargetName%16.bin*" > nul
xcopy /y "%initialWD%\%TargetName%%KSPversion%.bin" "%initialWD%\%TargetName%17.bin*" > nul

:ksp18
rem if built version is greater than KSP 1.7.2 then we assume latest version and copy built bin for the other compatible versions
xcopy /y "%initialWD%\%TargetName%%KSPversion%.bin" "%initialWD%\%TargetName%18.bin*" > nul
xcopy /y "%initialWD%\%TargetName%%KSPversion%.bin" "%initialWD%\%TargetName%19.bin*" > nul

:ksp13
rem delete Trajectories.bin if it exists
IF EXIST "%initialWD%\%TargetName%.bin" del "%initialWD%\%TargetName%.bin"
rem copy Bootstrap bins from build directory to GameData
xcopy /y "%initialWD%\%TargetName%*.bin" "GameData\%TargetName%\Plugin\*" > nul

echo Generating %TargetName% Release Package...
IF EXIST package\ rd /s /q package
mkdir package
cd package

mkdir GameData
cd GameData

mkdir "%TargetName%"
cd "%TargetName%"
xcopy /y /e "..\..\..\GameData\%TargetName%\*" .
xcopy /y ..\..\..\CHANGELOG.md .
xcopy /y ..\..\..\LICENSE.md .
xcopy /y ..\..\..\COPYRIGHTS.md .
xcopy /y ..\..\..\CONTRIBUTING.md .
xcopy /y ..\..\..\README.md .

echo.
echo Compressing %TargetName% Release Package...
IF EXIST "%rootPath%%TargetName%*.zip" del "%rootPath%%TargetName%*.zip"
"%scriptPath%7za.exe" a "..\..\..\%TargetName%%Dllversion%.zip" ..\..\..\package\GameData

rem check all bootstrap files exist
cd "%rootPath%"
IF NOT EXIST "package\GameData\%TargetName%\Plugin\%TargetName%Bootstrap.dll" echo **WARNING** %TargetName%Bootstrap.dll is missing
IF NOT EXIST "package\GameData\%TargetName%\Plugin\%TargetName%*.bin" echo **WARNING** %TargetName% bin is missing

rem remove temp files
rd /s /q package

cd "%initialWD%"
