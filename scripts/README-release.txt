SensorPanelPin
==============

Keeps the AIDA64 SensorPanel on the monitor you choose and moves other windows off that monitor.
Full documentation: https://github.com/Ravinskypl/SensorPanelPin

Install
-------
1. Extract this ZIP anywhere.
2. Double-click Install.cmd and approve the administrator prompt.
   It copies the program to C:\Program Files\SensorPanelPin, sets it to start at logon with
   administrator rights (AIDA64 runs as administrator, and Windows lets only an elevated program
   move its windows), and starts it.
3. Click the tray icon. On the first page, pick the screen the SensorPanel belongs on.

The extracted folder is not needed afterwards.

Windows SmartScreen may say "Windows protected your PC", because the program is not code-signed.
Choose "More info", then "Run anyway". The source code and the build that produced this ZIP are
public; SHA256SUMS.txt on the release page lets you check the download.

Run without installing
----------------------
SensorPanelPin.exe also runs straight from this folder, without administrator rights. It then
cannot move the SensorPanel if AIDA64 runs as administrator, which it usually does.

Uninstall
---------
Run Uninstall.cmd (also copied to C:\Program Files\SensorPanelPin) and approve the prompt.
It removes the program, its autostart entry, and its settings and logs.

The interface is in Polish for now.

License: MIT, see LICENSE.txt. Not affiliated with FinalWire Ltd., the maker of AIDA64.
