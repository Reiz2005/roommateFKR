@echo off
REM �Զ��������ԱȨ��
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo �����������ԱȨ��...
    powershell Start-Process 'cmd.exe' '/c "%~dpnx0"' -Verb RunAs
    exit /b
)

:InputPath
set /p exePath=��������������·������ C:\MyApp\program.exe��: 
if not exist "%exePath%" (
    echo ����·�������ڣ����������룡
    goto InputPath
)

:InputTaskName
set /p taskName=����������ƻ����ƣ��� MyStartupTask��: 
if "%taskName%"=="" (
    echo �����������Ʋ���Ϊ�գ�
    goto InputTaskName
)

REM ��������ƻ���
schtasks /create /tn "%taskName%" /tr "%exePath%" /sc onlogon /rl highest /f

echo ����ɹ����������ƣ�%taskName%������·����%exePath%
pause