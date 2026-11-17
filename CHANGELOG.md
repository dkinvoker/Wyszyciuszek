# Zmiany

## 1.3

- **„Moje nici” w pliku CSV:** wczytywanie i zapis pudełka z nićmi (Nazwa; Kolor; Metry na szpulce). Plik otwiera
  się w Excelu z polskimi znakami. Przy wczytywaniu działa też przecinek albo tabulator, a te same nici tylko
  aktualizują metry.
- **Nić na szpulce:** przy każdej nici w „Moich niciach” można wpisać, ile metrów zostało. Legenda, panel „Wydruk”
  i lista nici ostrzegają, gdy na wzór zabraknie nici, i podają ile.
- **Gęstość wypełnienia** (gęste / średnie / szkicowe) do szacunku zużycia nici i czasu szycia.
- **Kolor tkaniny:** gotowe materiały (biel, ecru, len, jeans, czerń…) albo własny kolor. Podgląd, PNG, SVG
  i miniatura w legendzie pokazują nici na tym tle. Nici, które się z nim zlewają, dostają ostrzeżenie.
  Kolor wzoru prawie taki jak materiał sam zostaje tkaniną. Zapisywany w projekcie.
- **Lupa** (Z albo przycisk przy powiększeniu): koło obok kursora w 3× większym powiększeniu, z pędzlem i rysowaną linią.
- **Minimapa:** po przybliżeniu w lewym dolnym rogu widać całość z ramką widocznego fragmentu. Klik albo przeciąganie
  przesuwa widok.
- **Projektor:** kliknięcie obszaru odhacza go jako odrysowany (zabarwienie i przygaszony kontur, Delete czyści),
  a **siatka co 1 cm** (G) z grubszą linią co 5 cm pomaga mierzyć i prostować.
- Poprawka: druk, PDF, PNG i SVG z okna nie zawierały linii detali ani ręcznie ustawionych kierunków ściegu
  (z wiersza poleceń było dobrze). Zapis projektu i autozapis korzystają z niezależnej kopii, więc edycja
  w trakcie zapisu niczego nie pomiesza.
- Wiersz poleceń: `--fill dense|medium|sketch`, `--fabric "Len naturalny"|#RRGGBB`.

## 1.2

- **Aktualizacje jak w Kasetowniku.** Kilka sekund po starcie, najwyżej raz na ~20 godzin, program pyta GitHuba
  o nowe wydanie. Pokazuje pasek „Nowa wersja” z przyciskami *Zaktualizuj*, *Co nowego?* i *Pomiń tę wersję*.
  Pobrany plik jest sprawdzany sumą SHA-256 i podmieniany, a *Uruchom ponownie* startuje nową wersję.
  Zamknięcie paska nie pomija już wersji na zawsze. Pod numerem wersji w pasku tytułu jest menu: *Sprawdzaj przy
  starcie*, *Sprawdź teraz* i link do wydań. Kopia uruchomiona bez jednoplikowego exe też sprawdza wersję i odsyła
  na stronę wydania.
- **Autozapis i przywracanie po awarii.** Co minutę kopia niezapisanego projektu. Po nieoczekiwanym zamknięciu
  (awaria, brak prądu) przy następnym starcie pojawia się *Przywróć* albo *Usuń tę kopię*.
- **Za wąskie na wypełnienie.** Panel „Wydruk” ostrzega o obszarach węższych niż 3 mm w rzeczywistym rozmiarze.
  *Pokaż* zaznacza je na czerwono, a *Paski na linie* zamienia wąskie paski i kreski na linie detali w ich kolorze:
  środkowa linia, gałęzie osobno, plama wtapia się w sąsiadów. **Shift+L** robi to samo z zaznaczonym obszarem.
- **Kolejność szycia od spodu do wierzchu** (domyślna dla nowych wzorów i w menu kolejności): najpierw tło
  i szerokie plamy, potem to, co na nich leży, na końcu cienkie kreski i drobiazgi.
- **Linie do kalkowania:** kreska konturów ciągła, przerywana albo z kropek, szare linie i numery zamiast czarnych.
- **Kalibracja drukarki** (jak w Kasetowniku): strona testowa z miarkami 100 mm i pomiar linijką. Korekta skali
  działa przy każdym wydruku, a na życzenie także w PDF.
- Podpowiedź przy zaznaczonym obszarze podaje jego szerokość w mm.
- Wiersz poleceń: `--outline solid|dashed|dotted`, `--gray`, `--export-calibration-pdf`.

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
