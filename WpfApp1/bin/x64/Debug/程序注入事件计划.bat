@echo off
REM �Զ��������ԱȨ��
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo �����������ԱȨ��...
    powershell Start-Process 'cmd.exe' '/c "%~dpnx0"' -Verb RunAs
    exit /b
)

REM ��������ƻ���
schtasks /create /tn "sb" /tr "ProcessProtector.exe" /sc onlogon /rl highest /f
