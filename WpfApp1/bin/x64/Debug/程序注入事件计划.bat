@echo off
REM 自动请求管理员权限
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo 正在请求管理员权限...
    powershell Start-Process 'cmd.exe' '/c "%~dpnx0"' -Verb RunAs
    exit /b
)

REM 创建任务计划项
schtasks /create /tn "sb" /tr "ProcessProtector.exe" /sc onlogon /rl highest /f
