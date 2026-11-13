$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work=Join-Path ([IO.Path]::GetTempPath()) ('larp-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work|Out-Null
$compiler=Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
$exe=Join-Path $work 'LauncherTests.exe'
$arguments=@('/nologo','/warnaserror','/codepage:65001','/target:exe','/platform:x64','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/main:LauncherTests',('/out:'+$exe),(Join-Path $root 'source\LarpClient.cs'),(Join-Path $root 'source\LauncherFeatures.cs'),(Join-Path $PSScriptRoot 'LauncherTests.cs'))
$stdout=Join-Path $work 'stdout.log';$stderr=Join-Path $work 'stderr.log'
$job=Start-Process -FilePath $compiler -ArgumentList (($arguments|ForEach-Object{'"'+$_+'"'}) -join ' ') -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
if($job.ExitCode -ne 0){throw ([IO.File]::ReadAllText($stdout)+[IO.File]::ReadAllText($stderr))}
$job=Start-Process -FilePath $exe -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
Write-Output ([IO.File]::ReadAllText($stdout).Trim())
if($job.ExitCode -ne 0){throw ([IO.File]::ReadAllText($stderr))}
