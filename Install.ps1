param([string]$Target='')
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($Target)){
 Add-Type -AssemblyName System.Windows.Forms
 $dialog=New-Object System.Windows.Forms.FolderBrowserDialog
 $dialog.Description='Выберите существующую папку LARP Client с launch-config.json'
 $dialog.ShowNewFolderButton=$false
 try{
  if($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK){throw 'Установка отменена.'}
  $Target=$dialog.SelectedPath
 }finally{$dialog.Dispose()}
}
$root=[IO.Path]::GetFullPath($Target).TrimEnd('\')
if(-not(Test-Path -LiteralPath (Join-Path $root 'launch-config.json'))){throw 'Не найдена папка готового клиента (launch-config.json).'}
$jarRelative='game\mods\LARP-CRACKED-BY-MITTKEE.jar'
$jarPath=Join-Path $root $jarRelative
if(-not(Test-Path -LiteralPath $jarPath)){throw 'Не найден мод LARP.'}
foreach($p in Get-Process -Name 'LARP Client','java','javaw' -ErrorAction SilentlyContinue){
 try{$processPath=$p.Path}catch{continue}
 if($processPath -and $processPath.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Закройте LARP Client и Minecraft перед установкой.'}
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$changes=@{'assets/larp-client/icon.png'='assets\logo-64.png';'assets/larp-client/textures/gui/logo.png'='assets\logo-64.png';'assets/larp-client/textures/gui/logo-halloween.png'='assets\logo-512.png'}
$zip=[IO.Compression.ZipFile]::OpenRead($jarPath)
try{foreach($name in $changes.Keys){if(-not $zip.GetEntry($name)){throw ('Неизвестная структура мода: '+$name)}}}finally{$zip.Dispose()}
$payload=@('LARP Client.exe','source\LarpClient.cs','source\build-launcher.ps1','source\app.manifest','source\README.md')
$payload+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets') -File|ForEach-Object{'assets\'+$_.Name})
foreach($relative in $payload){if(-not(Test-Path -LiteralPath (Join-Path $PSScriptRoot $relative))){throw ('Нет файла обновления: '+$relative)}}
$tracked=@($payload)+@($jarRelative,'build-info.json','verification.json','README.txt','redesign-install.json')
$backup=Join-Path $root ('backups\redesign-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $backup -Force|Out-Null
$existed=@{}
foreach($relative in $tracked){$file=Join-Path $root $relative;$existed[$relative]=[bool](Test-Path -LiteralPath $file);if($existed[$relative]){$copy=Join-Path $backup $relative;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($copy)) -Force|Out-Null;Copy-Item -LiteralPath $file -Destination $copy}}
$utf8=New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $backup 'files.json'),($existed|ConvertTo-Json),$utf8)
$restoreScript=@'
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$files=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'files.json') -Raw|ConvertFrom-Json
foreach($property in $files.PSObject.Properties){$destination=Join-Path $root $property.Name;if($property.Value){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $property.Name) -Destination $destination -Force}elseif(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination -Force}}
Write-Output 'Предыдущая версия восстановлена.'
'@
[IO.File]::WriteAllText((Join-Path $backup 'Restore.ps1'),([char]0xFEFF+$restoreScript),$utf8)
try{
 foreach($relative in $payload){$destination=Join-Path $root $relative;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $PSScriptRoot $relative) -Destination $destination -Force}
 $originalHash=(Get-FileHash -LiteralPath $jarPath -Algorithm SHA256).Hash.ToLowerInvariant()
 $zip=[IO.Compression.ZipFile]::Open($jarPath,[IO.Compression.ZipArchiveMode]::Update)
 try{foreach($name in $changes.Keys){$zip.GetEntry($name).Delete();[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $PSScriptRoot $changes[$name]),$name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$zip.Dispose()}
 $newHash=(Get-FileHash -LiteralPath $jarPath -Algorithm SHA256).Hash.ToLowerInvariant()
 $buildFile=Join-Path $root 'build-info.json'
 if(Test-Path -LiteralPath $buildFile){$info=Get-Content -LiteralPath $buildFile -Raw -Encoding UTF8|ConvertFrom-Json;foreach($mod in $info.mods){if($mod.file -eq 'LARP-CRACKED-BY-MITTKEE.jar'){$mod.sha256=$newHash}};[IO.File]::WriteAllText($buildFile,($info|ConvertTo-Json -Depth 30),$utf8)}
 $verificationFile=Join-Path $root 'verification.json'
 if(Test-Path -LiteralPath $verificationFile){$v=Get-Content -LiteralPath $verificationFile -Raw -Encoding UTF8|ConvertFrom-Json;$v|Add-Member -Force NoteProperty originalModSha256Match $false;$v|Add-Member -Force NoteProperty interactiveStartup 'not rerun after branding update';$v|Add-Member -Force NoteProperty larpMenuReached $null;$v|Add-Member -Force NoteProperty scope 'Branding update; previous launch checks do not validate this update.';[IO.File]::WriteAllText($verificationFile,($v|ConvertTo-Json -Depth 30),$utf8)}
 $readme=Join-Path $root 'README.txt'
 if(Test-Path -LiteralPath $readme){$text=[IO.File]::ReadAllText($readme);if(-not $text.StartsWith('ОБНОВЛЕНИЕ ОФОРМЛЕНИЯ')){[IO.File]::WriteAllText($readme,('ОБНОВЛЕНИЕ ОФОРМЛЕНИЯ: новое меню и иконка EXE; в моде заменены только три PNG логотипа. Старые утверждения ниже о неизменности всего JAR относятся к первоначальной сборке. Подробности: redesign-install.json.'+[Environment]::NewLine+[Environment]::NewLine+$text),$utf8)}}
 $receipt=[ordered]@{installedAt=[DateTime]::UtcNow.ToString('o');originalModSha256=$originalHash;updatedModSha256=$newHash;changedModEntries=@($changes.Keys);backup=$backup;gameStartupTest='not run';launcher='new interface and embedded multi-resolution icon'}
 [IO.File]::WriteAllText((Join-Path $root 'redesign-install.json'),($receipt|ConvertTo-Json -Depth 10),$utf8)
 Write-Output ('Установлено: '+$root);Write-Output ('Резервная копия: '+$backup)
}catch{
 $failure=$_
 foreach($relative in $tracked){$destination=Join-Path $root $relative;if($existed[$relative]){Copy-Item -LiteralPath (Join-Path $backup $relative) -Destination $destination -Force}elseif(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination -Force}}
 throw $failure
}
