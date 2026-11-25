# exit 1 when a Unity editor has the MAIN Assetto Corsa Dreamport project open (not the lab copy), else 0
$n = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -match 'Assetta Corsa' -and $_.CommandLine -notmatch 'labproject' -and $_.CommandLine -notmatch 'AssetImportWorker' }).Count
if ($n -gt 0) { exit 1 } else { exit 0 }
