param([string]$OutputPath = (Join-Path $PSScriptRoot '..\LARP Client.exe'))
$ErrorActionPreference='Stop'
$compilerPath=Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
$absoluteOutput=[IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($absoluteOutput)) -Force|Out-Null
$compileArgs=@('/nologo','/warnaserror','/codepage:65001','/target:winexe','/platform:x64','/optimize+','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/out:'+$absoluteOutput),(Join-Path $PSScriptRoot 'LarpClient.cs'))
function Invoke-Compiler([string[]]$compilerArguments){
 $argumentLine=($compilerArguments|ForEach-Object{'"'+$_+'"'}) -join ' '
 $outputLog=$absoluteOutput+'.build.log';$errorLog=$absoluteOutput+'.build-errors.log'
 $job=Start-Process -FilePath $compilerPath -ArgumentList $argumentLine -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $outputLog -RedirectStandardError $errorLog
 if($job.ExitCode -ne 0){throw ('C# compilation failed: '+[IO.File]::ReadAllText($outputLog)+[IO.File]::ReadAllText($errorLog))}
 Remove-Item -LiteralPath $outputLog,$errorLog -Force
}
$iconPath=Join-Path $PSScriptRoot '..\assets\larp.ico'
if(-not(Test-Path -LiteralPath $iconPath)){
 Invoke-Compiler $compileArgs
 $assetDirectory=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\assets'))
 $assetJob=Start-Process -FilePath $absoluteOutput -ArgumentList ('--export-assets "'+$assetDirectory+'"') -WindowStyle Hidden -PassThru -Wait
 if($assetJob.ExitCode -ne 0){throw 'Asset export failed.'}
}
$compileArgs+='/win32icon:'+ [IO.Path]::GetFullPath($iconPath)
Invoke-Compiler $compileArgs
Write-Output $absoluteOutput
