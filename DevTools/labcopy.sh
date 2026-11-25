#!/bin/bash
# The private lab copy of the project (slot B of lab.sh): DevTools/labproject, outside Assets so nothing imports it twice.
# First run copies the whole project including Library (so it does not import from scratch); later runs mirror only the code
# tree (Assets/#User) and the scene. Uses PowerShell robocopy (Git Bash mangles /MIR).
# (The Halo-era copy was moved to D:\Dreamport\Assetta Corsa\_HaloTemplate\DevTools\labproject; this script makes a fresh one.)
D='D:\Dreamport\Assetta Corsa'
L="$D\DevTools\labproject"
U="/d/Dreamport/Assetta Corsa/DevTools/labproject"
if [ ! -f "$U/.copied" ] || [ ! -d "$U/Assets/Photon" ] || [ ! -d "$U/Assets/Plugins" ] || [ ! -d "$U/Assets/Urp" ]; then
  # \$sub reaches PowerShell as its own variable (bash must not expand it; PowerShell names ignore case, so never reuse D / L there)
  powershell -NoProfile -Command "foreach (\$sub in 'Assets','Packages','ProjectSettings','Library') { robocopy \"$D\\\$sub\" \"$L\\\$sub\" /E /NFL /NDL /NJH /NJS /NP /XD Temp | Out-Null }"
  touch "$U/.copied"
fi
powershell -NoProfile -Command "robocopy \"$D\Packages\" \"$L\Packages\" manifest.json packages-lock.json /NFL /NDL /NJH /NJS /NP | Out-Null; robocopy \"$D\ProjectSettings\" \"$L\ProjectSettings\" /E /NFL /NDL /NJH /NJS /NP | Out-Null"
powershell -NoProfile -Command "robocopy \"$D\Assets\#User\" \"$L\Assets\#User\" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null; Copy-Item -Force \"$D\Assets\Assetto Scene.unity\",\"$D\Assets\Assetto Scene.unity.meta\" \"$L\Assets\\\""
echo "lab copy ready"
