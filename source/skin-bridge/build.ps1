param([string]$MinecraftJar='')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if(-not $MinecraftJar){
 $candidate=Get-ChildItem -LiteralPath (Join-Path $root 'game\.fabric\remappedJars') -Filter 'client-intermediary.jar' -Recurse -ErrorAction SilentlyContinue|Select-Object -First 1
 if(-not $candidate){throw 'Run Minecraft once to create the Fabric intermediary cache, or pass -MinecraftJar.'}
 $MinecraftJar=$candidate.FullName
}
$MinecraftJar=[IO.Path]::GetFullPath($MinecraftJar)
if(-not(Test-Path -LiteralPath $MinecraftJar)){throw 'Minecraft intermediary JAR not found.'}
$work=Join-Path ([IO.Path]::GetTempPath()) ('larp-skin-build-'+[Guid]::NewGuid().ToString('N'))
$classes=Join-Path $work 'classes'
New-Item -ItemType Directory -Path $classes -Force|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$api=[IO.Compression.ZipFile]::OpenRead((Join-Path $root 'game\mods\fabric-api-0.141.6+1.21.11.jar'))
$nested=@()
try{
 foreach($pattern in @('fabric-networking-api-v1','fabric-lifecycle-events-v1','fabric-api-base')){
  $entry=$api.Entries|Where-Object{$_.FullName -match ('/'+$pattern+'-[^/]+\.jar$')}|Select-Object -First 1
  if(-not $entry){throw ('Missing Fabric API dependency: '+$pattern)}
  $destination=Join-Path $work ($pattern+'.jar')
  [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$true)
  $nested+=$destination
 }
}finally{$api.Dispose()}
$config=Get-Content -LiteralPath (Join-Path $root 'launch-config.json') -Raw -Encoding UTF8|ConvertFrom-Json
$dependencies=@($MinecraftJar,(Join-Path $root 'game\mods\LARP-CRACKED-BY-MITTKEE.jar'))+$nested+@($config.classpath|ForEach-Object{Join-Path $root $_})
$classpath=$dependencies -join ';'
function Invoke-Tool([string]$tool,[string[]]$toolArguments){
 $stdout=Join-Path $work 'stdout.log';$stderr=Join-Path $work 'stderr.log'
 $arguments=($toolArguments|ForEach-Object{'"'+$_+'"'}) -join ' '
 $job=Start-Process -FilePath $tool -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
 if($job.ExitCode -ne 0){throw ([IO.File]::ReadAllText($stdout)+[IO.File]::ReadAllText($stderr))}
}
Invoke-Tool (Join-Path $root 'runtime\bin\javac.exe') @('-encoding','UTF-8','--release','21','-proc:none','-cp',$classpath,'-d',$classes,(Join-Path $PSScriptRoot 'SkinBridge.java'))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fabric.mod.json') -Destination (Join-Path $classes 'fabric.mod.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $classes 'LICENSE')
$testClasses=Join-Path $work 'tests'
New-Item -ItemType Directory -Path $testClasses -Force|Out-Null
Invoke-Tool (Join-Path $root 'runtime\bin\javac.exe') @('-encoding','UTF-8','--release','21','-proc:none','-cp',($classes+';'+$classpath),'-d',$testClasses,(Join-Path $PSScriptRoot 'SkinBridgeTests.java'))
Invoke-Tool (Join-Path $root 'runtime\bin\java.exe') @('-cp',($testClasses+';'+$classes+';'+$classpath),'com.sunakle.larpbridge.SkinBridgeTests')
Write-Output ([IO.File]::ReadAllText((Join-Path $work 'stdout.log')).Trim())
$output=Join-Path $root 'game\mods\LarpSkinBridge-1.0.0.jar'
Invoke-Tool (Join-Path $root 'runtime\bin\jar.exe') @('--create','--file',$output,'-C',$classes,'.')
Write-Output $output
