@echo off
REM 自动请求管理员权限
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo 正在请求管理员权限...
    powershell Start-Process 'cmd.exe' '/c "%~dpnx0"' -Verb RunAs
    exit /b
)

:InputPath
set /p exePath=请输入程序的完整路径（如 C:\MyApp\program.exe）: 
if not exist "%exePath%" (
    echo 错误：路径不存在，请重新输入！
    goto InputPath
)

:InputTaskName
set /p taskName=请输入任务计划名称（如 MyStartupTask）: 
if "%taskName%"=="" (
    echo 错误：任务名称不能为空！
    goto InputTaskName
)

REM 创建任务计划项
schtasks /create /tn "%taskName%" /tr "%exePath%" /sc onlogon /rl highest /f

echo 部署成功！任务名称：%taskName%，程序路径：%exePath%
pause