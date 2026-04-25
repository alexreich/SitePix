$ErrorActionPreference = 'Stop'

$packageName  = 'sitepix'
$version      = '__VERSION__'
$url64        = "https://github.com/alexreich/SitePix/releases/download/v$version/SitePix-Setup-$version.exe"
$checksum64   = '__SHA256__'
$checksumType = 'sha256'

$packageArgs = @{
  packageName    = $packageName
  fileType       = 'exe'
  url64bit       = $url64
  softwareName   = 'SitePix*'
  checksum64     = $checksum64
  checksumType64 = $checksumType
  # Inno Setup silent install flags
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
