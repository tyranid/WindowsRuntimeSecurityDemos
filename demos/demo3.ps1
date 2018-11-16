#  This file is part of WindowsRuntimeDemos.
#  Copyright (C) James Forshaw 2018
#
#  WindowsRuntimeDemos is free software: you can redistribute it and/or modify
#  it under the terms of the GNU General Public License as published by
#  the Free Software Foundation, either version 3 of the License, or
#  (at your option) any later version.
#
#  WindowsRuntimeDemos is distributed in the hope that it will be useful,
#  but WITHOUT ANY WARRANTY; without even the implied warranty of
#  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
#  GNU General Public License for more details.
#
#  You should have received a copy of the GNU General Public License
#  along with WindowsRuntimeDemos.  If not, see <http://www.gnu.org/licenses/>.

Import-Module OleViewDotNet

$mod = Get-Module OleViewDotNet
if ($mod.Version -lt "1.6") {
    Write-Host "OleViewDotNet module must be version 1.6 or above"
    exit
}

$db = Get-CurrentComDatabase 
if ($null -eq $db) {
    Get-ComDatabase -SetCurrent
}

$cls = Get-ComRuntimeClass | ? Name -Match "HumanInterface"
$ifs = Get-ComClassInterface -Factory $cls
$ifs | Select Name
$prx = $ifs | Get-ComProxy
$prx | Format-ComProxy 