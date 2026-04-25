$ErrorActionPreference = 'Stop'

$packageName = 'sitepix'
$softwareName = 'SitePix*'

# Find and run the Inno Setup uninstaller registered by the original installer.
[array]$key = Get-UninstallRegistryKey -SoftwareName $softwareName

if ($key.Count -eq 1) {
  $uninstall = $key[0].UninstallString
  # Inno Setup uninstall strings may be wrapped in quotes — parse out the exe path.
  if ($uninstall -match '^"([^"]+)"') {
    $uninstallExe = $Matches[1]
  } else {
    $uninstallExe = ($uninstall -split ' ')[0]
  }

  $packageArgs = @{
    packageName    = $packageName
    fileType       = 'exe'
    silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    validExitCodes = @(0, 3010, 1605, 1614, 1641)
    file           = $uninstallExe
  }

  Uninstall-ChocolateyPackage @packageArgs
} elseif ($key.Count -eq 0) {
  Write-Warning "$packageName has already been uninstalled by other means."
} else {
  Write-Warning "$($key.Count) matching registry keys found for '$softwareName' — uninstall manually to avoid removing the wrong app."
  $key | ForEach-Object { Write-Warning "  - $($_.DisplayName) :: $($_.UninstallString)" }
}
