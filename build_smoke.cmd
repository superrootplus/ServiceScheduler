@echo off
setlocal
pushd "%~dp0"
set "NETDIR=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%NETDIR%\csc.exe" set "NETDIR=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%NETDIR%\csc.exe" (
  echo C# compiler .NET Framework 4.x was not found.
  popd
  exit /b 1
)
if not exist dist mkdir dist
"%NETDIR%\csc.exe" /nologo /target:exe /platform:anycpu /optimize+ /main:ServiceScheduler.SmokeTests /out:"dist\ServiceScheduler.SmokeTests.exe" /r:System.dll /r:System.Core.dll /r:System.ServiceProcess.dll /r:System.Management.dll /r:System.Xml.dll /r:Microsoft.CSharp.dll "wpf\AssemblyInfo.cs" "wpf\Models.cs" "wpf\Logger.cs" "wpf\ServiceOperations.cs" "wpf\Scheduler.cs" "wpf\ExecutorDeployer.cs" "wpf\SmokeTests.cs"
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
