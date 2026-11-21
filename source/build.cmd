@echo off
setlocal
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /main:LauncherProgram /optimize+ /out:Trapka.exe /win32icon:Trapka.ico /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /resource:assets\onnxruntime.dll,Trapka.onnxruntime.dll /resource:assets\onnxruntime_providers_shared.dll,Trapka.onnxruntime_providers_shared.dll /resource:assets\yolox_tiny.onnx,Trapka.yolox_tiny.onnx /resource:assets\yolox_nano.onnx,Trapka.yolox_nano.onnx /resource:assets\ORT-LICENSE.txt,Trapka.ORT-LICENSE.txt /resource:assets\ORT-NOTICES.txt,Trapka.ORT-NOTICES.txt /resource:assets\YOLOX-LICENSE.txt,Trapka.YOLOX-LICENSE.txt /resource:update-public-key.xml,Trapka.update-public-key.xml /resource:update-source.txt,Trapka.update-source.txt Launcher.cs RRStudio.cs Overlay.cs Menu.cs Vision.cs Tracking.cs VisionWorker.cs Updates.cs
exit /b %errorlevel%
