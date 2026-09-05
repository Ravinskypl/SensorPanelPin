# SensorPanelGlue

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

**C# / .NET 8 + WinForms (tray) + P/Invoke do Win32.**

Zadanie sprowadza się do intensywnego korzystania z Win32: `SetWinEventHook`, `SetWindowPos`,
`SetWindowPlacement`, `EnumWindows`, `EnumDisplayMonitors` i CCD (`QueryDisplayConfig`). C# daje do
tego bezpośredni dostęp przez P/Invoke bez żadnej warstwy pośredniej, a jednocześnie — w odróżnieniu
od C++ — pozwala napisać okno konfiguracji, ikonę w zasobniku i obsługę JSON-a w kilkuset liniach
zamiast w kilku tysiącach. Pętla komunikatów WinForms jest tu wręcz potrzebna: hooki
`WINEVENT_OUTOFCONTEXT` dostarczają zdarzenia właśnie przez kolejkę komunikatów wątku, który je
zarejestrował, więc callback trafia od razu na wątek UI i nie wymaga marshalingu.

Python odpadł przez konieczność dowożenia runtime'u albo pakowania PyInstallerem (kilkadziesiąt MB
i wolniejszy start dla programu, który ma po prostu siedzieć w tle). C++ dałby minimalnie niższy
narzut, ale przy zmierzonym zużyciu 0,12% jednego rdzenia nie ma czego optymalizować.

Manifest deklaruje **Per-Monitor DPI Awareness V2**. Świadomie nie używamy
`Application.SetHighDpiMode` ani właściwości `ApplicationHighDpiMode`: system stosuje ustawienie z
manifestu jeszcze zanim uruchomi się kod zarządzany, a program, który mierzy i przestawia cudze
okna, musi być świadomy DPI od pierwszej instrukcji. Analizator WinForms `WFAC010` zakłada drogę
przez API, dlatego jest jawnie wyciszony w `.csproj` — kod nigdy nie woła `SetHighDpiMode`, więc nie
ma konfliktu ustawień.

---

## Uruchomienie

Wymagania: Windows 10/11 x64, .NET 8 Desktop Runtime (albo publish self-contained — patrz niżej).

```bash
dotnet build src/SensorPanelGlue/SensorPanelGlue.csproj -c Release
```

Plik wykonywalny: `src/SensorPanelGlue/bin/Release/net8.0-windows/win-x64/SensorPanelGlue.exe`

Jeden plik bez zależności od zainstalowanego runtime'u:

```bash
dotnet publish src/SensorPanelGlue/SensorPanelGlue.csproj -c Release -p:PublishSingleFile=true -p:SelfContained=true -o publish
```

### Pierwsze uruchomienie

1. Uruchom AIDA64 i włącz SensorPanel.
2. Uruchom `SensorPanelGlue.exe` — pojawi się ikona w zasobniku systemowym.
3. Kliknij ikonę dwukrotnie (albo prawym → **Konfiguracja**).
4. Zakładka **Ekrany**: kliknij **Identyfikuj ekrany**, żeby zobaczyć numery na fizycznych
   monitorach, zaznacz właściwy wiersz i naciśnij **Ekran SensorPanela**. Analogicznie wskaż
   **Ekran główny** — tam będą lądowały okna innych aplikacji.
5. Ustaw panel ręcznie tam, gdzie ma być, i naciśnij **Zapisz bieżącą pozycję panelu jako docelową**
   (zakładka *SensorPanel*). Jeśli tego nie zrobisz, aplikacja zapamięta bieżącą pozycję sama, przy
   pierwszym uruchomieniu, gdy panel będzie już na właściwym ekranie.
6. Zakładka **Zaawansowane** → **Uruchamiaj razem z Windows**, jeśli ma startować automatycznie.

---

## ⚠ Najważniejsze ograniczenie: AIDA64 działa jako administrator

AIDA64 prawie zawsze uruchamia się z podniesionymi uprawnieniami (potrzebuje ich do odczytu
czujników). Windows UIPI nie pozwala procesowi o niższym poziomie integralności wywołać
`SetWindowPos` na oknie procesu o wyższym — wywołanie kończy się błędem `ERROR_ACCESS_DENIED (5)`.

**W praktyce oznacza to, że SensorPanelGlue uruchomiony normalnie nie przesunie panelu AIDA64.**

Aplikacja wykrywa to od razu przy starcie (nieszkodliwym, „pustym" wywołaniem `SetWindowPos`, które
niczego nie przesuwa) i zamiast milczeć:

* wypisuje ostrzeżenie w logu,
* pokazuje dymek przy ikonie w zasobniku,
* zmienia status na **„Brak uprawnień"**,
* wyświetla żółty pasek w oknie konfiguracji,
* udostępnia w menu **Uruchom ponownie jako administrator**.

Elewacja nigdy nie jest wymuszana przy starcie — manifest deklaruje `asInvoker`, a UAC pojawia się
wyłącznie wtedy, gdy sam klikniesz ten punkt menu.

### Autostart

Wpis w `HKCU\...\Run` **nie potrafi podnieść uprawnień** — to reguła Windows, nie ograniczenie tego
kodu. Autostart przez rejestr uruchomi aplikację bez uprawnień, czyli po restarcie systemu panel nie
będzie pilnowany, mimo że ikona w zasobniku będzie na miejscu.

Dlatego są trzy tryby, do wyboru w **Zaawansowane → Uruchamianie razem z Windows** albo w menu ikony:

| Tryb | Co robi |
|---|---|
| **Wyłączony** | Usuwa oba wpisy |
| **Rejestr (HKCU\\...\\Run)** | Klasyczny autostart, bez uprawnień |
| **Harmonogram zadań** | Zadanie `SensorPanelGlue` z `RunLevel = HighestAvailable`, wyzwalacz przy logowaniu z opóźnieniem 15 s (żeby AIDA64 zdążyła wstać). Startuje z uprawnieniami **bez monitu UAC przy każdym logowaniu** — UAC pojawia się tylko raz, przy tworzeniu zadania |

Sama AIDA64 uruchamia się identycznie — zadaniem `\AIDA64 AutoStart` — i z tego samego powodu:
program, który potrzebuje uprawnień, nie da się sensownie wystartować z klucza `Run`. Opóźnienie
15 s wystarcza, żeby panel istniał, zanim SensorPanelGlue zacznie go szukać; gdyby AIDA64 startowała
wolniej, aplikacja i tak poczeka i złapie okno, gdy się pojawi.

Tryby wykluczają się wzajemnie — wybranie jednego usuwa drugi. Aplikacja nie tworzy niczego z
własnej inicjatywy; zmiana następuje wyłącznie po wybraniu trybu w interfejsie.

Wpis zapamiętuje **bieżącą ścieżkę pliku wykonywalnego**. Przebudowanie w to samo miejsce niczego nie
psuje, ale po przeniesieniu programu (np. `dotnet publish` do innego katalogu) ustaw tryb ponownie.
Rejestrowana jest ścieżka do `SensorPanelGlue.exe`, nie do procesu-hosta — przy uruchomieniu przez
`dotnet SensorPanelGlue.dll` aplikacja odmówi zapisu zamiast zarejestrować `dotnet.exe`.

Przenoszenie okien *innych* aplikacji działa bez elewacji, o ile te aplikacje same nie są
uruchomione jako administrator.

---

## Jak to działa

```
WinEventHook (OUTOFCONTEXT)          zdarzenia systemowe            poll bezpieczeństwa
  SHOW / LOCATIONCHANGE                WM_DISPLAYCHANGE                  co 5 s
  FOREGROUND / MOVESIZEEND             WM_DPICHANGED
  MINIMIZEEND                          WM_POWERBROADCAST
        |                              WM_WTSSESSION_CHANGE                  |
        v                                     |                              |
   kolejka + debounce 150 ms          opóźnienie 2 s                         |
        |                                     |                              |
        +------------------> GlueService <----+------------------------------+
                                  |
                 +----------------+----------------+
                 v                                 v
        watchdog SensorPanela            relokator obcych okien
        (pozycja / rozmiar /             (filtr → anty-ping-pong →
         topmost / Alt+Tab)               WindowMover)
```

Kilka decyzji, które mają znaczenie:

* **Identyfikacja monitorów przez ścieżkę urządzenia.** `QueryDisplayConfig` zwraca
  `monitorDevicePath` (np. `\\?\DISPLAY#EYA713A#5&3af5c87a&0&UID4352#{...}`), które nie zmienia się
  po restarcie ani po przepięciu kabla. Indeksy i współrzędne zmieniają się i nie nadają się na
  identyfikator. Gdy docelowy monitor jest nieobecny, aplikacja **czeka** — nigdy nie parkuje panelu
  na zastępczym ekranie.
* **Zdarzenia, nie polling.** `EVENT_OBJECT_LOCATIONCHANGE` jest bardzo gadatliwe (generuje je też
  ruch kursora), więc callback od razu odrzuca wszystko, co nie jest `OBJID_WINDOW` / `CHILDID_SELF`,
  a reszta trafia do kolejki z debounce'em. Poll co 5 s jest tylko siatką asekuracyjną.
* **Brak kradzieży fokusu.** Wszystkie przesunięcia używają `SWP_NOACTIVATE`. Okna zmaksymalizowane
  przenoszone są przez `SetWindowPlacement` — translacja `rcNormalPosition` o deltę i ponowna
  maksymalizacja na nowym monitorze, bez aktywacji okna.
* **Anty-ping-pong.** Jeśli to samo okno wraca na zakazany ekran mimo 3 przeniesień w ciągu 10 s,
  aplikacja odpuszcza je do końca sesji, loguje to raz i pokazuje w oknie konfiguracji
  (zakładka *Inne okna*). Analogiczny bezpiecznik chroni przed walką z samą AIDA64: po 12 korektach
  panelu w ciągu 10 s następuje 30-sekundowa przerwa.
* **Czego nigdy nie ruszamy:** okien z właścicielem, `WS_EX_TOOLWINDOW`, bez tytułu,
  zminimalizowanych, ukrytych przez DWM (`DWMWA_CLOAKED`), okien powłoki (`Progman`, `WorkerW`,
  `Shell_TrayWnd`, menu Start, wyszukiwarka), okien UAC/`consent.exe`, okien procesów, których nie da
  się odpytać (zwykle podniesione uprawnienia), samego SensorPanela, czegokolwiek z procesu AIDA64,
  okien pełnoekranowych oraz wszystkiego, gdy `SHQueryUserNotificationState` zgłasza tryb
  pełnoekranowy D3D lub prezentacji (gra przechwytująca ekran zostaje w spokoju).

---

## Konfiguracja

`%APPDATA%\SensorPanelGlue\config.json` — plik można edytować ręcznie, aplikacja zapisuje go
atomowo (zapis do `.tmp` + podmiana).

| Klucz | Znaczenie | Domyślnie |
|---|---|---|
| `PanelMonitorId` | Ścieżka urządzenia monitora SensorPanela | (puste) |
| `HostMonitorId` | Monitor docelowy dla obcych okien; puste = systemowy główny | (puste) |
| `PanelRule` | Reguła rozpoznania okna panelu: `ProcessName`, `ClassPattern`, `TitlePattern` (wzorce `*` i `?`) | `aida64` / `*SensorPanel*` |
| `PanelGeometry` | Offset względem lewego górnego rogu monitora + rozmiar | uczy się sama |
| `KeepPanelPosition` | Przywracaj zapisaną pozycję; wyłączone = pilnuj tylko właściwego ekranu | `true` |
| `RestorePanelSize` | Przywracaj też rozmiar | `false` |
| `KeepPanelTopmost` | `WS_EX_TOPMOST` na panelu | `false` |
| `HidePanelFromAltTab` | Dodaj `WS_EX_TOOLWINDOW` | `false` |
| `RelocateForeignWindows` | Przenoś obce okna z ekranu panelu | `true` |
| `Exceptions` | Lista reguł okien, których nie ruszamy | `[]` |
| `SafetyPollSeconds` | Interwał pollingu asekuracyjnego | `5` |
| `EventDebounceMs` | Debounce zdarzeń okien | `150` |
| `DisplayChangeDelayMs` | Odczekanie po zmianie układu ekranów / wybudzeniu | `2000` |
| `PingPongLimit` / `PingPongWindowSeconds` | Próg rezygnacji z okna | `3` / `10` |
| `VerboseLogging` | Poziom `Debug` w logu | `false` |
| `StartPaused` | Start w trybie wstrzymanym | `false` |

**Reguła panelu w tej instalacji** (odczytana z uruchomionej AIDA64 Extreme v8.35.8400):
klasa `TForm_HWMonitoringSensorPanel`, tytuł `SensorPanel`. Domyślny wzorzec `*SensorPanel*` pasuje
do niej i przetrwa ewentualną zmianę prefiksu w kolejnych wersjach AIDA64. Gdyby przestał pasować,
użyj **Tryb diagnostyczny** z menu ikony — wypisze wszystkie okna procesu z klasą, tytułem, stylami
i monitorem, i oznaczy to, które pasuje do reguły.

Logi: `%APPDATA%\SensorPanelGlue\logs\sensorpanelglue.log` (rotacja 1 MB × 5 plików).

---

## Wyniki testów

Mierzone na tym stanowisku: Windows 11 Pro 26220, AW3423DWF 3440×1440 @ 0,0 (główny) +
eM713A 1024×600 @ 3440,0 (ekran panelu), AIDA64 Extreme v8.35.8400.

| Test | Wynik |
|---|---|
| Build `Release` | 0 błędów, 0 ostrzeżeń |
| Wykrycie monitorów po ścieżce urządzenia | 2/2, ze stabilnymi identyfikatorami i DPI |
| Wykrycie okna SensorPanela | `TForm_HWMonitoringSensorPanel` / `SensorPanel`, znalezione w 100 ms od startu |
| Wykrycie braku uprawnień do okna AIDA64 | zgłoszone przy starcie, bez wyjątku i bez wymuszania UAC |
| Powrót „panelu" na miejsce po ręcznym przesunięciu | 197 / 180 / 182 ms (średnio **186 ms**) |
| Przeniesienie Notatnika z ekranu panelu na główny | **234 ms**, pozycja względna zachowana |
| Anty-ping-pong | rezygnacja dokładnie po 3 przeniesieniach w 10 s, wpis `WARN` w logu |
| Okno z listy wyjątków | zostało na ekranie panelu przez cały test |
| Docelowy monitor niedostępny | stan „czekam", panel nietknięty, jedno ostrzeżenie w logu, brak awarii |
| Fizyczne odpięcie i podpięcie monitora panelu | oba `WM_DISPLAYCHANGE` wykryte, w trakcie odpięcia panel nietknięty, monitor rozpoznany po ścieżce urządzenia bez ponownego wyboru |
| Powrót panelu po hot-plugu (instancja z uprawnieniami) | znaleziony na `{X=0,Y=0}` i przywrócony na eM713A w **3 ms** |
| Okno konfiguracji | wszystkie 4 zakładki konstruują się i renderują, układ sprawdzony na zrzutach |
| XML zadania Harmonogramu | `RunLevel=HighestAvailable`, `LogonType=InteractiveToken`, poprawny UserId i ścieżka; `schtasks` przyjął plik i odrzucił dopiero na braku uprawnień |
| Odczyt bieżącego trybu autostartu | 54 ms, bez blokowania interfejsu |
| Pełny restart Windows | zadanie wystartowało aplikację 37 s po boocie, z uprawnieniami; panel bez żadnej ingerencji znalazł się na eM713A |
| Zużycie CPU w spoczynku (90 s) | 109 ms CPU → **0,12% jednego rdzenia**, 0,008% całego CPU (16 rdzeni) |
| Pamięć w spoczynku | 69 MB working set / 28 MB prywatnej |

Pomiar czasu powrotu panelu wykonany na oknie zastępczym bez podniesionych uprawnień (Notatnik
podstawiony pod regułę panelu), ponieważ okna samej AIDA64 są dla nieelewowanego procesu
niedostępne — patrz sekcja o uprawnieniach.

Hot-plug został potwierdzony na żywym sprzęcie: po wypięciu kabla aplikacja wykryła
`WM_DISPLAYCHANGE`, przeszła w stan „czekam" i nie przeniosła panelu na zastępczy ekran; po
podpięciu rozpoznała ten sam monitor po ścieżce urządzenia bez ponownej konfiguracji. Windows w
międzyczasie przeniósł panel na ekran główny i tam go zostawił — instancja bez uprawnień nie mogła
go ściągnąć z powrotem, instancja z uprawnieniami zrobiła to w 3 ms od startu. To najlepsza możliwa
ilustracja tego, dlaczego elewacja ma tu znaczenie.

---

## Znane ograniczenia

1. **Okna procesów z wyższymi uprawnieniami** (w tym sama AIDA64) są nieruszalne bez elewacji —
   opisane wyżej. To ograniczenie Windows (UIPI), nie do obejścia bez podniesienia uprawnień.
2. **Gry w trybie pełnoekranowym wyłącznym** są celowo pomijane. Przesuwanie okna gry, która
   przechwyciła ekran, kończy się zwykle utratą fokusu albo przełączeniem w tryb okienkowy.
3. **Okno mignie, zanim zostanie przeniesione.** Reakcja trwa ~0,2 s — tyle, ile potrzeba na
   zdarzenie systemowe plus debounce. Da się to skrócić (`EventDebounceMs`), kosztem większej liczby
   zbędnych przebiegów przy przeciąganiu okien.
4. **Aplikacje, które same wymuszają swoją pozycję**, po trzech próbach zostają zostawione w spokoju
   — zobaczysz je na liście w zakładce *Inne okna*. To celowe: alternatywą jest nieskończona pętla.
5. **Autostart przez rejestr nie podnosi uprawnień** — jeśli AIDA64 działa jako administrator,
   użyj trybu „Harmonogram zadań", inaczej po restarcie systemu panel nie będzie pilnowany.
6. **Wiele monitorów sklonowanych** dzieli jedno źródło GDI; aplikacja bierze pierwszą aktywną
   ścieżkę i traktuje klon jako jeden ekran.

---

## Struktura kodu

```
src/SensorPanelGlue/
  Program.cs                  single instance, logger, obsługa wyjątków, pętla aplikacji
  app.manifest                PerMonitorV2 DPI, asInvoker
  app.ico                     ikona programu (16-256 px), generowana z UI/IconArtwork.cs
  Interop/                    P/Invoke: user32, shcore, dwmapi, kernel32, CCD (QueryDisplayConfig)
  Monitors/                   MonitorRegistry — stabilne ID monitorów, mapowanie prostokątów
  Windows/                    odczyt okien, filtr, przesuwanie, anty-ping-pong, wildcardy
  Core/                       GlueService (orkiestracja), hooki, okno zdarzeń systemowych, elewacja
  Config/                     model konfiguracji, zapis/odczyt JSON, autostart
  Diagnostics/                log rotacyjny z throttlingiem
  UI/                         ikona zasobnika, okno konfiguracji, overlay „Identyfikuj"
```

Ikona jest zdefiniowana raz, w `UI/IconArtwork.cs`, jako rysunek proporcjonalny do rozmiaru.
`app.ico` (16, 20, 24, 32, 40, 48, 64, 128, 256 px) jest z niego wygenerowany i wchodzi do projektu
dwa razy: jako `ApplicationIcon` pliku wykonywalnego i jako zasób osadzony, z którego program
pobiera ikonę okien (32 px) oraz zasobnika w rozmiarze żądanym przez powłokę. Wariant wstrzymany —
ten sam znak w szarości — rysowany jest w locie, bo występuje wyłącznie w zasobniku. Po zmianie
rysunku trzeba wygenerować `app.ico` ponownie.
