$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = 'fileconverter'
  softwareName   = 'File Converter*'
  fileType       = 'msi'
  silentArgs     = '/qn /norestart'
  validExitCodes = @(0, 3010, 1641)
}

$uninstalled = $false
[array]$key = Get-UninstallRegistryKey -SoftwareName $packageArgs['softwareName']

if ($key.Count -eq 1) {
  $key | ForEach-Object {
    $packageArgs['file'] = ''
    $packageArgs['silentArgs'] = "$($_.PSChildName) /qn /norestart"
    Uninstall-ChocolateyPackage @packageArgs
  }
} elseif ($key.Count -eq 0) {
  Write-Warning "$($packageArgs['packageName']) has already been uninstalled by other means."
} elseif ($key.Count -gt 1) {
  Write-Warning "$($key.Count) matches found! Using first."
  $key[0] | ForEach-Object {
    $packageArgs['file'] = ''
    $packageArgs['silentArgs'] = "$($_.PSChildName) /qn /norestart"
    Uninstall-ChocolateyPackage @packageArgs
  }
}
