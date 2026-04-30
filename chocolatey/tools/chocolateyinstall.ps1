$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = 'fileconverter'
  fileType       = 'msi'
  url64bit       = 'https://github.com/UCHIHAHA103/FileConverter/releases/latest/download/FileConverter-setup.msi'
  softwareName   = 'File Converter*'
  silentArgs     = '/qn /norestart REBOOT=ReallySuppress'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
