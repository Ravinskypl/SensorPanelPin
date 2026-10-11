SensorPanelPin
==============

Keeps the AIDA64 SensorPanel on the monitor you choose and moves other windows off that monitor.
Full documentation: https://github.com/Ravinskypl/SensorPanelPin

Install
-------
1. Extract this ZIP anywhere.
2. Start SensorPanelPin.exe. Its icon appears in the notification area.
3. Right-click the icon, choose "Install SensorPanelPin…" and approve the administrator prompt.
   (The settings window offers the same button.)
   The program copies itself to C:\Program Files\SensorPanelPin, sets itself to start at logon
   with administrator rights (AIDA64 runs as administrator, and Windows lets only an elevated
   program move its windows), and starts the installed copy.
4. Click the tray icon. On the first page, pick the screen the SensorPanel belongs on.

The extracted folder is not needed afterwards.

Windows SmartScreen may say "Windows protected your PC" at step 2, because the program is not
code-signed. Choose "More info", then "Run anyway". The source code and the build that produced
this ZIP are public; SHA256SUMS.txt on the release page lets you check the download.

Run without installing
----------------------
SensorPanelPin.exe also runs straight from this folder, without administrator rights. It then
cannot move the SensorPanel if AIDA64 runs as administrator, which it usually does.

Uninstall
---------
In the settings, open Startup and choose "Uninstall…". You can keep or remove your settings.

The interface is in English, or in Polish when Windows is set to Polish.

License: MIT, see LICENSE.txt. Not affiliated with FinalWire Ltd., the maker of AIDA64.
