# SensorPanelPin

Aplikacja dla Windows 11, która pilnuje, aby okno **SensorPanel** z AIDA64 zawsze znajdowało się
na wybranym monitorze, a okna innych aplikacji, które pojawią się na tym monitorze, przenosi na
monitor główny.

---

## Czego ta aplikacja NIE robi

**Nie blokuje uruchamiania aplikacji na wybranym ekranie.** Windows nie udostępnia żadnego API,
którym można by zabronić procesowi otwarcia okna na konkretnym monitorze. Nie ma takiej polityki
ani w Win32, ani w rejestrze, ani w ustawieniach systemu.

Jedyne, co da się zrobić bez ingerencji w cudze procesy, to **wykrywać okna, które się tam
pojawiają, i natychmiast je przenosić**. To właśnie robi ta aplikacja. W praktyce efekt jest taki,
jakiego oczekujesz — okno mignie na ekranie panelu i po ~0,2 s znajdzie się na monitorze głównym —
ale technicznie jest to przenoszenie, nie blokowanie.

Aplikacja nie używa wstrzykiwania DLL, globalnych hooków CBT w cudzych procesach, sterowników ani
modyfikacji jądra. Tylko udokumentowane, user-mode Win32 API.

---

## Wybór stacku

**C# / .NET 10 (LTS) — silnik na P/Invoke do Win32, interfejs w WPF z motywem Fluent.**

Silnik sprowadza się do intensywnego korzystania z Win32: `SetWinEventHook`, `SetWindowPos`,
`SetWindowPlacement`, `EnumWindows`, `EnumDisplayMonitors` i CCD (`QueryDisplayConfig`). C# daje do
tego bezpośredni dostęp przez P/Invoke bez żadnej warstwy pośredniej, a jednocześnie pozwala napisać
interfejs i obsługę JSON-a bez tysięcy linii, których wymagałoby C++. Python odpadł przez konieczność
dowożenia runtime'u albo pakowania PyInstallerem.

Interfejs jest w **WPF z motywem Fluent** — tym samym językiem wizualnym co Ustawienia Windows 11:
jasny lub ciemny według ustawienia systemu, kolor akcentu z systemu, tło Mica, zaokrąglone karty.
Wersja 1 miała interfejs w WinForms, który na .NET 8 nie ma trybu ciemnego — każdą kontrolkę
trzeba by malować ręcznie, a natywne listy rozwijane i pola liczbowe i tak zostałyby jasne. WPF na
.NET 10 ma motyw Fluent wbudowany. Drugi powód zmiany: wsparcie .NET 8 kończy się w listopadzie
2026, a .NET 10 to wydanie LTS.

WinForms zostaje w dwóch miejscach, których WPF nie obsługuje: ikona w zasobniku (`NotifyIcon`) i
ukryte okno odbierające rozgłoszenia `WM_DISPLAYCHANGE`, `WM_POWERBROADCAST` i zmian sesji.

Manifest deklaruje **Per-Monitor DPI Awareness V2**. Świadomie nie używamy
`Application.SetHighDpiMode`: system stosuje ustawienie z manifestu jeszcze zanim uruchomi się kod
zarządzany, a program, który mierzy i przestawia cudze okna, musi być świadomy DPI od pierwszej
instrukcji. Analizator WinForms (`WFAC010`, w .NET 10 `WFO0003`) zakłada drogę przez API, dlatego
jest jawnie wyciszony w `.csproj`.

---

## Instalacja

Wymagania do budowania: .NET 10 SDK. Do uruchamiania wersji opublikowanej: nic — runtime jest w
środku.

```bash
dotnet publish src/SensorPanelPin/SensorPanelPin.csproj -c Release -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

Efekt: `publish\SensorPanelPin.exe` (62,6 MB, z runtime'em .NET w środku) oraz pięć bibliotek
natywnych WPF obok niego — razem 70,5 MB.

**Celowo bez `IncludeNativeLibrariesForSelfExtract`.** Z tą flagą wszystko byłoby w jednym pliku,
ale biblioteki natywne są wtedy przy każdym starcie wypakowywane do `%TEMP%\.net\…` — katalogu, nad
którym użytkownik ma pełną kontrolę. Program uruchamiany z uprawnieniami administratora ładowałby
biblioteki z miejsca, w które dowolny program działający na zwykłym koncie może podłożyć własne.

Potem instalacja, w PowerShellu uruchomionym jako administrator:

```bash
powershell -ExecutionPolicy Bypass -File scripts\Install.ps1
```

Skrypt kopiuje build do `C:\Program Files\SensorPanelPin`, ustawia administratorów jako właściciela,
przywraca chronione uprawnienia dziedziczone z `Program Files`, a jeśli zadanie autostartu istnieje —
przestawia je na zainstalowaną kopię i uruchamia. To jedyne miejsce, z którego program może
startować z uprawnieniami administratora; patrz sekcja *Bezpieczeństwo*.

### Przejście z SensorPanelGlue

Do wersji 2.0 program nazywał się SensorPanelGlue. Przejście na nową nazwę nie wymaga ręcznej pracy:

- `Install.ps1` zatrzymuje starą wersję, przenosi jej zadanie autostartu pod nazwę `SensorPanelPin`
  (z tym samym wyzwalaczem i kontem) i usuwa `C:\Program Files\SensorPanelGlue`.
- Przy pierwszym starcie SensorPanelPin przejmuje `%APPDATA%\SensorPanelGlue\config.json`. Stary
  plik jest tylko czytany; folder zostaje razem z dawnymi logami i można go usunąć ręcznie.
- Stary wpis autostartu w rejestrze (`HKCU\...\Run\SensorPanelGlue`) jest rozpoznawany jako bieżący
  autostart i znika przy pierwszej zmianie trybu w ustawieniach.
- Jeśli stara wersja jeszcze działa, nowa nie wystartuje obok niej, tylko poprosi o jej zamknięcie —
  dwie kopie przestawiałyby te same okna.

### Pierwsze uruchomienie

1. Uruchom AIDA64 i włącz SensorPanel.
2. Uruchom `SensorPanelPin.exe` — w zasobniku pojawi się ikona.
3. Kliknij ikonę — otworzą się ustawienia na stronie **Ekrany**, z mapą Twoich monitorów.
4. W karcie **Ekran SensorPanela** wybierz ekran, na którym ma być panel. **Identyfikuj** pokazuje na
   każdym monitorze jego numer z mapy.
5. Jeśli panel nie stoi jeszcze dokładnie tam, gdzie ma, ustaw go ręcznie i na stronie
   **SensorPanel** naciśnij **Zapamiętaj obecne**. Bez tego miejsce zapamięta się samo, gdy panel
   pierwszy raz pojawi się na swoim ekranie.
6. Na stronie **Uruchamianie** wybierz, czy program ma startować razem z Windows.

Zmiany działają od razu, jak w Ustawieniach Windows — nie ma przycisku „Zapisz".

---

## Interfejs

Cztery strony w panelu nawigacji:

* **Ekrany** — mapa monitorów w skali, z SensorPanelem narysowanym tam, gdzie naprawdę jest: w
  kolorze akcentu, gdy stoi na swoim miejscu, a w kolorze ostrzeżenia, gdy jest na innym ekranie,
  z przerywanym obrysem miejsca, w którym powinien być. Mapa odświeża się co sekundę, póki okno jest
  otwarte. Pod nią jedno zdanie o stanie i wybór ról ekranów.
* **SensorPanel** — pilnowanie miejsca i rozmiaru, zapamiętanie bieżącego miejsca, zachowanie okna.
  Reguła rozpoznawania okna jest w zwiniętej karcie „Zaawansowane" — domyślna pasuje do AIDA64.
* **Inne okna** — przenoszenie okien z ekranu panelu, wyjątki, okna, które wracały.
* **Uruchamianie** — autostart, log, a w zwiniętej karcie czasy reakcji.

Menu ikony w zasobniku ma tylko to, co robi się z zasobnika: stan, otwarcie ustawień, natychmiastową
poprawkę położenia, wstrzymanie i zamknięcie.

---

## ⚠ Najważniejsze ograniczenie: AIDA64 działa jako administrator

AIDA64 prawie zawsze uruchamia się z podniesionymi uprawnieniami (potrzebuje ich do odczytu
czujników). Windows UIPI nie pozwala procesowi o niższym poziomie integralności wywołać
`SetWindowPos` na oknie procesu o wyższym — wywołanie kończy się błędem `ERROR_ACCESS_DENIED (5)`.

**W praktyce oznacza to, że SensorPanelPin uruchomiony normalnie nie przesunie panelu AIDA64.**

Aplikacja wykrywa to od razu przy starcie (nieszkodliwym, „pustym" wywołaniem `SetWindowPos`, które
niczego nie przesuwa) i zamiast milczeć: wypisuje ostrzeżenie w logu, pokazuje powiadomienie przy
ikonie, a na stronie **Ekrany** wyświetla żółty pasek z przyciskiem **Uruchom jako administrator**.
To samo polecenie jest w menu ikony.

Elewacja nigdy nie jest wymuszana przy starcie — manifest deklaruje `asInvoker`, a UAC pojawia się
wyłącznie wtedy, gdy sam klikniesz to polecenie.

### Autostart

Wpis w `HKCU\...\Run` **nie potrafi podnieść uprawnień** — to reguła Windows, nie ograniczenie tego
kodu. Dlatego strona **Uruchamianie** ma trzy możliwości:

| Wybór | Co robi |
|---|---|
| **Nie uruchamiaj** | Usuwa oba wpisy |
| **Uruchamiaj bez uprawnień administratora** | Wpis w `HKCU\...\Run`. Panel nie będzie pilnowany, jeśli AIDA64 działa jako administrator |
| **Uruchamiaj z uprawnieniami administratora** | Zadanie `SensorPanelPin` w Harmonogramie, `RunLevel = HighestAvailable`, wyzwalacz przy logowaniu z opóźnieniem 15 s. Startuje z uprawnieniami **bez monitu UAC przy logowaniu** |

Sama AIDA64 uruchamia się identycznie — zadaniem `\AIDA64 AutoStart` — i z tego samego powodu.
Opóźnienie 15 s wystarcza, żeby panel istniał, zanim SensorPanelPin zacznie go szukać; gdyby
AIDA64 startowała wolniej, aplikacja poczeka i złapie okno, gdy się pojawi.

Zadanie jest tworzone i usuwane przez API COM Harmonogramu zadań, wewnątrz procesu — bez
`schtasks.exe` i bez pliku tymczasowego. Utworzenie lub usunięcie zadania z uprawnieniami wymaga,
żeby program sam działał z uprawnieniami, i jest **odrzucane, jeśli program leży w miejscu, które
mogą zmieniać zwykli użytkownicy** (patrz niżej). Wpis zapamiętuje ścieżkę `SensorPanelPin.exe`
(nigdy procesu-hosta, np. `dotnet.exe`); po przeniesieniu programu ustaw tryb ponownie albo
uruchom skrypt instalacyjny, który przestawia zadanie sam.

Przenoszenie okien *innych* aplikacji działa bez elewacji, o ile te aplikacje same nie są
uruchomione jako administrator.

---

## Bezpieczeństwo

Program normalnie działa z tokenem administratora, uruchamiany przy logowaniu bez pytania UAC.
Właściwą granicą bezpieczeństwa jest więc ta między nim a zwykłym kodem działającym na tym samym
koncie bez uprawnień: wszystko, co ten kod może zmienić, a co program z uprawnieniami potem czyta,
uruchamia lub zapisuje, musi być sprawdzone.

* **Plik uruchamiany przy logowaniu musi być chroniony.** Przed utworzeniem zadania program sprawdza
  listy uprawnień (ACL) pliku wykonywalnego, plików obok niego, jego katalogu i wszystkich katalogów
  nadrzędnych: zapisywać, usuwać lub zmieniać uprawnienia mogą tylko `SYSTEM`, administratorzy i
  `TrustedInstaller`, a właścicielem musi być któryś z nich. Wersja 1 zarejestrowała zadanie dla
  pliku na `D:\`, gdzie dziedziczone uprawnienia dają każdemu zalogowanemu prawo modyfikacji — każdy
  program na zwykłym koncie mógł podmienić plik i przy następnym logowaniu dostać uprawnienia
  administratora. Dlatego instalacja trafia do `Program Files`.
* **Żadnych bibliotek z `%TEMP%`.** Publikacja bez samorozpakowywania — patrz *Instalacja*.
* **Zadanie przez API, nie przez plik.** Wersja 1 zapisywała definicję zadania do stałego pliku w
  `%TEMP%` i przekazywała go `schtasks.exe` uruchamianemu z uprawnieniami i bez pełnej ścieżki.
  Pozwalało to podmienić plik między zapisem a odczytem oraz podłożyć własny `schtasks.exe`.
* **Zapis do profilu użytkownika przez zweryfikowany uchwyt.** Log i konfiguracja leżą w
  `%APPDATA%`, który zwykły kod może podmienić na dowiązanie (junction lub hardlink nie wymagają
  uprawnień). Katalog jest sprawdzany pod kątem dowiązań, a każdy otwarty plik — przez uchwyt — musi
  mieć dokładnie jedno dowiązanie i leżeć tam, gdzie powinien. Jeśli nie, log się wyłącza (i mówi to
  powiadomieniem), a konfiguracja nie jest zapisywana.
* **Treść kontrolowana przez innych.** Tytuły okien ustawia program, który jest właścicielem okna.
  Każde przełamanie linii w komunikacie logu jest wcinane, więc tytuł nie podrobi wpisu; tytuły są
  czytane maksymalnie do 512 znaków i skracane w logu do 60; dopasowanie wzorców ma limit czasu.

---

## Jak to działa

```
WinEventHook (OUTOFCONTEXT)          zdarzenia systemowe            poll bezpieczeństwa
  SHOW / LOCATIONCHANGE                WM_DISPLAYCHANGE                  co 5 s
  FOREGROUND / MOVESIZEEND             WM_DPICHANGED
  MINIMIZEEND                          WM_POWERBROADCAST
        |                              WM_WTSSESSION_CHANGE                  |
        v                                     |                              |
   terminy: zdarzenia 150 ms (max 1 s)  zmiana ekranów 2 s (max 10 s)        |
        |                                     |                              |
        +------------> LoopClock (jeden zegar, wpis do kolejki UI) <---------+
                                  |
                             PinService                 strażnik w puli wątków:
                                  |                     „pętla nie odpowiada od N s"
                 +----------------+----------------+
                 v                                 v
        watchdog SensorPanela            relokator obcych okien
        (pozycja / rozmiar /             (filtr → anty-ping-pong →
         topmost / Alt+Tab)               WindowMover)
```

Kilka decyzji, które mają znaczenie:

* **Identyfikacja monitorów przez ścieżkę urządzenia.** `QueryDisplayConfig` zwraca
  `monitorDevicePath` (np. `\\?\DISPLAY#EYA713A#5&3af5c87a&0&UID4352#{...}`), które nie zmienia się
  po restarcie ani po przepięciu kabla. Gdy docelowy monitor jest nieobecny, aplikacja **czeka** —
  nigdy nie parkuje panelu na zastępczym ekranie.
* **Jeden zegar na terminach.** Wersja 1 miała trzy timery WinForms, restartowane przy każdym
  zdarzeniu okna. `Timer.Stop()` w WinForms niszczy ukryte okno timera, a `Start()` tworzy nowe —
  zmierzone 208 µs na restart, około 150 razy więcej niż reszta pracy. Gorzej: `WM_TIMER` jest
  generowany tylko wtedy, gdy kolejka komunikatów jest pusta, więc zalew zdarzeń uciszał wszystkie
  trzy timery naraz, łącznie z pollem, który miał być zabezpieczeniem. Zgadza się to z incydentem, w
  którym zalogowano trzy zmiany ekranów, a opóźnione wymuszenie nigdy nie nastąpiło. Teraz jeden
  zegar w puli wątków wstawia komunikat do kolejki UI (komunikat wstawiony nie jest głodzony jak
  `WM_TIMER`), zdarzenia tylko przesuwają terminy, a każda seria ma górny limit odroczenia. Osobny
  strażnik w puli wątków zapisuje w logu, jeśli pętla przestanie odpowiadać.
* **Nieufność wobec współrzędnych w trakcie zmiany układu.** Układ, w którym prostokąty monitorów
  częściowo na siebie nachodzą (klony z identycznymi prostokątami są w porządku), jest uznawany za
  przejściowy i nic się wtedy nie dzieje; a każde wyliczone miejsce docelowe panelu musi faktycznie
  leżeć na monitorze docelowym. Ta sama blokada obowiązuje przy przenoszeniu obcych okien.
* **Puls w logu.** Co 5 minut wpis ze stanem, także gdy pilnowanie jest wstrzymane, z liczbą
  wpisów logu, których nie udało się zapisać. Zmiany układu ekranów są logowane z pełnymi
  współrzędnymi. Wpisy utracone, gdy plik był zajęty, są liczone i zgłaszane, gdy zapis znów działa.
* **Przeniesienie jest sprawdzane, nie zakładane.** Po każdej operacji aplikacja odczytuje prostokąt
  okna i sprawdza, czy naprawdę jest już na ekranie docelowym. Log pokazuje, gdzie okno wylądowało.
* **Okna zmaksymalizowane**: przywrócenie na docelowym monitorze, przesunięcie, ponowna
  maksymalizacja. Gdy maksymalizacja się nie uda, okno wraca do pierwotnego stanu.
* **Anty-ping-pong z przerwami, nie z dożywociem.** Okno może być przeniesione 5 razy w ciągu 10 s
  (`PingPongLimit` / `PingPongWindowSeconds`); następne przeniesienie w tym czasie daje mu przerwę
  (domyślnie 30 s — `max(15 s, 3 × okno czasowe)`), po której dostaje kolejną szansę. Dopiero trzecia
  seria, po dwóch przerwach, kończy się odpuszczeniem okna do końca sesji. Osobny bezpiecznik chroni
  przed walką z samą AIDA64: po 12 udanych korektach panelu w ciągu 10 s następuje 30 s przerwy.
* **Czego nigdy nie ruszamy:** okien z właścicielem, `WS_EX_TOOLWINDOW`, bez tytułu,
  zminimalizowanych, ukrytych przez DWM (`DWMWA_CLOAKED`), okien powłoki, okien UAC/`consent.exe`,
  okien procesów, których nie da się odpytać, samego SensorPanela, czegokolwiek z procesu AIDA64,
  okien pełnoekranowych oraz wszystkiego, gdy `SHQueryUserNotificationState` zgłasza tryb
  pełnoekranowy D3D lub prezentacji.

---

## Konfiguracja

`%APPDATA%\SensorPanelPin\config.json` — edytowalny ręcznie; zapisywany atomowo, przez losowy plik
tymczasowy i zweryfikowany uchwyt. Ręcznie wpisane `null` są czyszczone przy wczytaniu.

| Klucz | Znaczenie | Domyślnie |
|---|---|---|
| `PanelMonitorId` | Ścieżka urządzenia monitora SensorPanela | (puste) |
| `HostMonitorId` | Monitor docelowy dla obcych okien; puste = główny ekran Windows | (puste) |
| `PanelRule` | Reguła rozpoznania okna panelu: `ProcessName`, `ClassPattern`, `TitlePattern` (wzorce `*` i `?`) | `ProcessName` = `aida64`, `ClassPattern` = `*SensorPanel*`, `TitlePattern` puste |
| `PanelGeometry` | Offset względem lewego górnego rogu monitora + rozmiar | uczy się sama |
| `KeepPanelPosition` | Przywracaj zapisane miejsce; wyłączone = pilnuj tylko właściwego ekranu | `true` |
| `RestorePanelSize` | Przywracaj też rozmiar | `false` |
| `KeepPanelTopmost` | `WS_EX_TOPMOST` na panelu (AIDA64 potrafi to zdejmować) | `false` |
| `HidePanelFromAltTab` | Dodaj `WS_EX_TOOLWINDOW` | `false` |
| `RelocateForeignWindows` | Przenoś obce okna z ekranu panelu | `true` |
| `Exceptions` | Lista reguł okien, których nie ruszamy | `[]` |
| `SafetyPollSeconds` | Interwał pollingu asekuracyjnego | `5` |
| `EventDebounceMs` | Odczekanie po zdarzeniu okna | `150` |
| `DisplayChangeDelayMs` | Odczekanie po zmianie układu ekranów / wybudzeniu | `2000` |
| `PingPongLimit` / `PingPongWindowSeconds` | Przeniesienia dozwolone w oknie czasowym; następne dają oknu przerwę | `5` / `10` |
| `VerboseLogging` | Poziom `Debug` w logu | `false` |
| `StartPaused` | Start w trybie wstrzymanym | `false` |

**Reguła panelu w tej instalacji** (odczytana z AIDA64 Extreme v8.35.8400): klasa
`TForm_HWMonitoringSensorPanel`, tytuł `SensorPanel`. Domyślny wzorzec `*SensorPanel*` pasuje do
niej. Gdyby przestał, **SensorPanel → Zaawansowane → Pokaż okna AIDA64** wypisze wszystkie okna
procesu i oznaczy to, które pasuje.

Logi: `%APPDATA%\SensorPanelPin\logs\sensorpanelpin.log` (rotacja 1 MB × 5 plików). Log zapisuje
skrócone (60 znaków) tytuły okien, które program przenosi lub — przy szczegółowym logu — pomija.

---

## Testy

```bash
dotnet test tests/SensorPanelPin.Tests/SensorPanelPin.Tests.csproj
```

226 testów xUnit, bez potrzeby pulpitu: spójność układu monitorów (w tym incydent z monitorem
zgłoszonym w (0,0)), anty-ping-pong z przerwami (incydent ze Steamem), arytmetyka terminów zegara,
odmowa zapisu przez junction i hardlink, kontrola ACL (plik w `%TEMP%` odrzucony, `dotnet.exe` w
`Program Files` przyjęty), wzorce, konfiguracja, log, przejęcie ustawień spod starej nazwy,
przełączniki środowiska uruchomieniowego (WPF nie zniesie trybu `InvariantGlobalization`: każdy szablon kontrolki rzuca wtedy `XamlParseException`).

### Pomiary na żywym sprzęcie

Windows 11 Pro 26220, AW3423DWF 3440×1440 @ 0,0 (główny) + eM713A 1024×600 @ 3440,0 (ekran
panelu), AIDA64 Extreme v8.35.8400. Pomiary silnika wykonane na wersji 1; wersja 2 zmieniła
harmonogram i interfejs, a tych pomiarów jeszcze nie powtórzono.

| Test | Wynik |
|---|---|
| Wykrycie monitorów po ścieżce urządzenia | 2/2, ze stabilnymi identyfikatorami i DPI |
| Wykrycie okna SensorPanela | znalezione w 100 ms od startu |
| Wykrycie braku uprawnień do okna AIDA64 | zgłoszone przy starcie, bez wymuszania UAC |
| Powrót panelu na miejsce po ręcznym przesunięciu | średnio **186 ms** (197 / 180 / 182) |
| Przeniesienie Notatnika z ekranu panelu na główny | **234 ms**, pozycja względna zachowana |
| Przenoszenie okna zmaksymalizowanego (Steam) | **254 ms**, nadal zmaksymalizowane |
| Okno z listy wyjątków | zostało na ekranie panelu |
| Docelowy monitor niedostępny | stan „czekam", panel nietknięty, brak awarii |
| Fizyczne odpięcie i podpięcie monitora panelu | oba `WM_DISPLAYCHANGE` wykryte, monitor rozpoznany bez ponownego wyboru, panel przywrócony |
| Pełny restart Windows | zadanie wystartowało aplikację 37 s po boocie, z uprawnieniami; panel na eM713A |
| Zużycie CPU w spoczynku (90 s) | **0,12% jednego rdzenia** |
| Pamięć w spoczynku | 69 MB working set / 28 MB prywatnej |

W wersji 2 sprawdzone: build bez ostrzeżeń, 226 testów, interfejs na zrzutach w trybie jasnym i
ciemnym na żywych danych z monitorów, nowy zegar w działaniu (puls co 5 minut z wersji z poprawkami
bezpieczeństwa), zainstalowana kopia w `Program Files`: start bez błędów interfejsu, Notatnik
przesunięty z ekranu panelu na główny w **185 ms**.

---

## Znane ograniczenia

1. **Okna procesów z wyższymi uprawnieniami** (w tym sama AIDA64) są nieruszalne bez elewacji.
   To ograniczenie Windows (UIPI).
2. **Gry w trybie pełnoekranowym wyłącznym** są celowo pomijane.
3. **Okno mignie, zanim zostanie przeniesione** — reakcja trwa ~0,2 s.
4. **Aplikacje, które same wymuszają swoją pozycję**, po trzech seriach przeniesień zostają
   zostawione w spokoju — widać je na stronie **Inne okna**.
5. **Autostart bez uprawnień administratora nie pilnuje panelu**, jeśli AIDA64 działa jako
   administrator.
6. **„Zawsze na wierzchu" bywa zdejmowane przez AIDA64** — pewniej włączyć tę opcję w AIDA64.
7. **Wiele monitorów sklonowanych** traktowanych jest jako jeden ekran.

---

## Struktura kodu

```
src/SensorPanelPin/
  Program.cs              pojedyncza instancja, log, pętla WPF z motywem Fluent
  app.manifest            PerMonitorV2 DPI, asInvoker
  app.ico                 ikona (16-256 px), generowana z UI/IconArtwork.cs
  Interop/                P/Invoke: NativeMethods, DwmInterop, DisplayConfigInterop (CCD), FileInterop
  Monitors/               MonitorRegistry (stabilne ID, geometria, spójność układu), MonitorInfo
  Windows/                WindowQuery/WindowInfo, WindowFilter, WindowMover, PingPongGuard, Wildcard
  Core/                   PinService (silnik), LoopClock (zegar terminów), WinEventListener,
                          SystemEventWindow, Elevation, ServiceStatus
  Config/                 AppConfig, ConfigStore, AutoStart (API COM Harmonogramu), ExecutableTrust (ACL)
  Diagnostics/            Logger, SafeDirectory (zapis odporny na dowiązania)
  UI/                     SettingsWindow + Pages/ (strony), SettingsViewModel, MonitorMap (mapa),
                          TrayController (zasobnik), IdentifyWindow, DiagnosticsWindow,
                          Theme.xaml (przełącznik, karty, InfoBar, rozwijane karty), AppIcon, IconArtwork
tests/SensorPanelPin.Tests/   xUnit
scripts/Install.ps1            instalacja do Program Files i przestawienie zadania
```

Ikona jest zdefiniowana raz, w `UI/IconArtwork.cs`, jako rysunek proporcjonalny do rozmiaru;
`app.ico` jest z niego wygenerowany i wchodzi do projektu jako ikona pliku wykonywalnego i jako
zasób, z którego program bierze ikonę okien i zasobnika. Po zmianie rysunku trzeba wygenerować
`app.ico` ponownie.
