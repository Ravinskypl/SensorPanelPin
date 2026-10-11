# Security policy

SensorPanelPin normally runs with administrator rights, started at logon without a UAC prompt. A
flaw that lets an ordinary program on the same account influence what it runs, loads, reads or
writes is a privilege escalation, and is treated as the most serious kind of bug here.

## Reporting a vulnerability

Report it privately: on the repository page open **Security → Report a vulnerability**. Please do
not open a public issue for it.

Include the version, what an attacker needs (for example "a program running as the same standard
user"), and the steps to reproduce. Expect a first answer within two weeks.

## Supported versions

Only the latest release gets fixes.

## What is already defended

The README's *Security* section lists the boundaries the program checks today: the protected
install location required before a logon task is created, no native libraries loaded from `%TEMP%`,
the Task Scheduler API instead of definition files, link-resistant writes to `%APPDATA%`, and the
handling of window titles controlled by other programs. A way around any of these is in scope.
