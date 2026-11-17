# Zmiany

## 1.1

- **Tło to tkanina** — nowa opcja w panelu „Upraszczanie”: kolor, który zajmuje większość brzegów obrazka, od razu
  dostaje oznaczenie „tkanina” (bez numeru, nie szyjemy go). Domyślnie wyłączona; po włączeniu zostaje zapamiętana
  dla kolejnych obrazków, a na gotowym wzorze działa od razu i da się ją cofnąć.
- **Tkanina nie zabiera numeru** — nici do szycia są zawsze numerowane od 1, niezależnie od tego, gdzie na liście jest tkanina.
- **Kierunek ściegu** — czerwone strzałki w obszarach (na wzorze, na wydruku, w PDF i jako ukryta warstwa SVG).
  Kierunek dobiera się sam (wzdłuż plamy w miejscu strzałki), a narzędziem „Kierunek ściegu” (D) ustawia się go
  ręcznie przeciągnięciem; kliknięcie przywraca automatyczny. Przełącznik strzałek w widoku kolorów.
- **Linie detali** — narzędzie „Linia” (L): oczy, wąsy, łodyżki jako samo przeszycie wybraną nicią, bez nowego obszaru.
  Shift rysuje prostą. Na wzorze kreska-kropka z numerem nici, w podglądzie kolorem nici, w SVG osobna warstwa.
  Zaznaczenie strzałką (V), zmiana nici kliknięciem na liście, usuwanie klawiszem Delete.
- **Tryb projektora** (F5) — wzór na pełnym ekranie w skali 1:1, od razu na drugim ekranie albo projektorze.
  Kalibracja linijką (raz na ekran), jasne linie na czarnym albo ciemne na białym (ekran jak podświetlana szyba),
  obrót, lustro, przesuwanie, wyróżnienie jednej nici.
- **Zużycie nici i czas szycia** — orientacyjnie dla każdej nici i dla całego wzoru: w legendzie, w panelu „Wydruk”
  i w podpowiedziach na liście nici.
- Legenda: dłuższe opisy zawijają się zamiast nachodzić na miniaturę, długie nazwy nici są skracane.
- Wiersz poleceń: `--background` (tło jako tkanina) i `--no-arrows` (bez strzałek).
- Projekt z liniami albo kierunkami zapisuje się w formacie 2; bez nich — nadal w formacie 1 (otworzy go wersja 1.0).

## 1.0

Pierwsza wersja.

- Upraszczanie obrazka do 2–24 nici: wygładzanie z zachowaniem krawędzi, kolory w L\*a\*b\*, usuwanie drobnych
  plamek i cienkich pasków, gładkie wspólne kontury.
- „Moje nici” — wzór tylko z nici, które masz w pudełku; zamiana kolorów gotowego wzoru na najbliższe nici.
- Edycja: wiaderko, pędzel, pipeta, zmiana koloru i nazwy nici, łączenie kolorów, kolejność szycia, tkanina.
- Widoki: oryginał, porównanie z suwakiem, kolory, wzór do druku, kontury na oryginale.
- Wydruk 1:1 z kwadratem kontrolnym, kafelkowanie dużych wzorów, lustrzane odbicie, numery, podbarwienie,
  znaczniki środka, tamborki, legenda z kolejnością szycia.
- Eksport: PDF (wektorowy), PNG, SVG z warstwami kolorów, projekt `.wyszycie`.
- Jeden plik exe z samodzielnym rozpakowaniem, skryptem odinstalowania i automatycznymi aktualizacjami.
