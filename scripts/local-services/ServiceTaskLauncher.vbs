Option Explicit

If WScript.Arguments.Count <> 2 Then WScript.Quit 2

Dim serviceName, taskAction
serviceName = LCase(WScript.Arguments(0))
taskAction = LCase(WScript.Arguments(1))

If taskAction = "status" Then
    If serviceName <> "all" Then WScript.Quit 2
ElseIf taskAction = "start" Or taskAction = "stop" Then
    Select Case serviceName
        Case "mongodb", "elasticsearch", "hdfs", "monstache"
        Case Else
            WScript.Quit 2
    End Select
Else
    WScript.Quit 2
End If

Dim shell, scriptFolder, powershellPath, commandLine, quote
Set shell = CreateObject("WScript.Shell")
scriptFolder = Left(WScript.ScriptFullName, InStrRev(WScript.ScriptFullName, "\"))
powershellPath = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
quote = Chr(34)
commandLine = quote & powershellPath & quote & " -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " & _
    quote & scriptFolder & "ServiceTask.ps1" & quote & " -Service " & serviceName & " -Action " & taskAction

' WScript is a GUI host. Window style 0 is applied before PowerShell starts.
WScript.Quit shell.Run(commandLine, 0, True)

