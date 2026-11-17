# Wyszyciuszek — pomysły na rozwój

Lista rzeczy, których jeszcze nie ma. Gotowe funkcje opisuje [CHANGELOG.md](CHANGELOG.md).
Orientacyjna ilość pracy: **[S]** mała, **[M]** średnia, **[L]** duża.

## ✅ Zrobione w 1.1

Wykrywanie tła (jako opcja), kierunek ściegu, linie detali, tryb projektora, zużycie nici i czas szycia.
Szczegóły w [CHANGELOG.md](CHANGELOG.md).

## ⭐ Na początek (największy zysk za najmniej pracy)

1. **Za małe na wypełnienie [S]** — obszary węższe niż ok. 3 mm na czerwono, z przyciskiem „zamień na linię detalu”.
2. **Linie do kalkowania [S]** — przerywana albo szara linia i wydruk bez numerów, pod pisak znikający i stabilizator.
3. **Kalibracja drukarki [S]** — wydruk testowy z linijkami i poprawka skali, jak w projektorze.
4. **Autozapis i przywracanie po awarii [S]** — żadna praca nie przepadnie.
5. **Mądrzejsza kolejność szycia [M]** — najpierw to, co „pod spodem”, przy jak najmniejszej liczbie zmian nici.

## 🧵 Upraszczanie

- **Wycinanie obiektu z tła [M]:** zaznaczasz z grubsza kota na zdjęciu, a reszta staje się tkaniną. Wystarczy
  algorytm typu GrabCut na obrazie roboczym — bez sieci neuronowych i bez internetu.
- **Pędzel „tu ważne” [M]:** malujesz na oryginale twarz czy oczy, a tam program zostawia więcej szczegółów
  (mniejsza najmniejsza plama i cieńsze paski). Reszta upraszcza się mocniej.
- **Tryb „szkic nicią” [M]:** zamiast plam same linie — kontury i kreski cieniowania z wykrywania krawędzi (XDoG).
  To popularny styl haftu z wolnej ręki: czarna nić na jasnej tkaninie, jak rysunek ołówkiem. Wynik od razu
  trafiłby do linii detali, więc można go poprawiać jak każdą linię.
- **Tryb „jedna linia” [L]:** cały obrazek jako jedna ciągła ścieżka (gęstsza tam, gdzie ciemniej) — szyje się
  bez obcinania nici. Rozmieszczenie punktów (stippling) i trasa jak w problemie komiwojażera.
- **Cieniowanie zamiast twardych granic [M]:** w gradientach (niebo, policzki) program proponuje 2–3 odcienie
  w pasach o zadanej szerokości i rysuje linie przejścia, zamiast poszarpanej granicy.
- **Kontury przyklejone do krawędzi oryginału [M]:** po wygładzeniu linia lekko „przyciąga się” do najbliższej
  wyraźnej krawędzi zdjęcia. Mniej falowania w gradientach, wierniejsze kształty.
- **Kilka kolorów tła [S]:** opcja „tło to tkanina” rozpoznaje dziś jeden dominujący kolor brzegów. Zdjęcie na
  dwukolorowym tle (ściana i podłoga) mogłoby oznaczyć oba naraz.
- **Miniatury przy suwakach [S]:** po najechaniu na suwak małe podglądy dla kilku wartości obok siebie.
  Nie trzeba przesuwać i czekać, żeby porównać.
- **Przyspieszenie dla dużych obrazów [M]:** kolory liczone na zmniejszonej kopii, a granice doprecyzowane w pełnej
  rozdzielczości. Pozwoli to używać dokładności „bardzo dokładnie” bez czekania.

## 🪡 Wskazówki do szycia

- **Kierunek wzdłuż faktury zdjęcia [M]:** dziś automatyczny kierunek idzie wzdłuż kształtu plamy. Dla sierści,
  włosów czy piór lepszy byłby kierunek „włosa” odczytany z oryginału (tensor struktury), także zmienny wewnątrz
  jednej plamy — kilka strzałek zamiast jednej.
- **Mądrzejsza kolejność szycia [M]:** najpierw to, co jest „pod spodem” (tło, duże plamy), potem to, co na wierzchu,
  a przy tym jak najmniej zmian nici. Podpowiedź „ta nić ma 7 obszarów, przeskakuj między nimi w tej kolejności”.
- **Nić na szpulce [S]:** w „Moich niciach” ilość nici na szpulce; legenda ostrzega „ta nić skończy się w połowie”
  i podaje liczbę szpulek. Gęstość wypełnienia do wyboru (gęsto / średnio / szkicowo) zamiast jednego założenia.
- **Za małe na wypełnienie [S]:** zaznaczenie na czerwono obszarów, które w rzeczywistym rozmiarze są węższe niż
  ok. 3 mm (trudne do wypełnienia ściegiem) — z propozycją „połącz z sąsiadem” albo „zamień na linię detalu”.
- **Podpowiedź flizeliny [S]:** przy dużych, gęsto wypełnionych obszarach informacja o stabilizatorze i tamborku.
- **Realistyczny podgląd haftu [L]:** obszary narysowane „nitkami” w kierunku ze strzałek — tekstura ściegu, połysk,
  cień. Widać, jak wzór będzie wyglądał wyszyty, zanim się zacznie.

## ✏️ Projektowanie

- **Edycja linii detali [M]:** przeciąganie punktów, dokładanie odcinków, grubość (linia pojedyncza, potrójna, satyna)
  i ścieg ozdobny (np. ząbki) widoczny na wzorze.
- **Napisy [M]:** imię albo data do wyszycia — dowolna czcionka zamieniona na linie detali albo obszary.
- **Edycja konturu [L]:** przeciąganie punktów linii, wygładzanie fragmentu, prostowanie odcinka. Mapa kolorów
  aktualizuje się według nowej linii.
- **Zaznaczanie wielu obszarów [M]:** lasso albo Shift+klik, a potem jedna operacja dla wszystkich (kolor, połączenie,
  usunięcie, kierunek ściegu).
- **Wiaderko na oryginale [S]:** klik w zdjęcie z tolerancją koloru — szybkie wydzielenie fragmentu, którego
  upraszczanie nie złapało.
- **Lupa i minimapa [S]:** przy malowaniu drobnych detali powiększenie pod kursorem i mały podgląd całości w rogu.
- **Historia zmian jako lista [S]:** „Wiaderko: dach → Żółty”, „Nowa linia detalu” — skok do dowolnego kroku.

## 🖨️ Przeniesienie na tkaninę

- **Linie do kalkowania [S]:** kreska przerywana albo kropki zamiast ciągłej linii, cienka szarość zamiast czerni,
  wydruk bez numerów — wersje pod pisak znikający, kopiowanie przez szybę i druk na stabilizatorze rozpuszczalnym.
- **Kalibracja drukarki [S]:** wydruk testowy z linijkami i poprawka skali X/Y oraz przesunięcia (jak w Kasetowniku
  i w projektorze), bo część drukarek drukuje 1–2% mniej mimo „100%”.
- **Projektor: odhaczanie odrysowanych fragmentów [S]:** klik w obszar przygasza jego kontur, żeby było widać,
  co już jest na tkaninie; do tego siatka pomocnicza co 1 cm.
- **Szablony do aplikacji [M]:** kształty wybranych kolorów jako osobne elementy do wycięcia z tkaniny, z zapasem
  na podwinięcie i znacznikiem, która strona jest prawa. Ułożone oszczędnie na kartce.
- **Eksport dla maszyn hafciarskich [L]:** pliki PES/DST z automatycznie wygenerowanym ściegiem (wypełnienie w kierunku
  ze strzałek, satyna, ścieg prosty dla linii detali) — dla osób, które mają też maszynę hafciarską.
  SVG z warstwami kolorów i linii jest dobrym startem.

## 🎨 Nici i kolory

- **Katalogi producentów [M]:** gotowe palety z numerami (np. Ariadna, Madeira, Gütermann), wybór „mam nici tej firmy”
  i dopasowanie z numerem katalogowym w legendzie. Dane z otwartych tabel kolorów; import z CSV.
- **Import i eksport „Moich nici” [S]:** plik CSV (nazwa, kolor, numer, ilość na szpulce) — przeniesienie pudełka
  na inny komputer albo wymiana z koleżanką.
- **Kolor tkaniny [S]:** wybór koloru materiału (len, czarny, jeans). Podgląd i podbarwienie wzoru pokazują,
  jak nici wyglądają na tym tle, a kolor „tkanina” dobiera się sam.
- **Kolory nici ze zdjęcia szpulek [M]:** zdjęcie pudełka z nićmi, klik w każdą szpulkę i dodanie do „Moich nici”.
- **Zamiana palety [S]:** cały wzór w innym nastroju (pastele, sepia, monochrom w 3 odcieniach jednego koloru)
  jednym kliknięciem — przydatne przy ozdobach na prezent.

## 🗂️ Wygoda

- **Autozapis i przywracanie po awarii [S]** oraz lista ostatnich projektów z miniaturami na ekranie powitalnym **[S]**.
- **Miniatury .wyszycie w Eksploratorze [L]** — rozszerzenie powłoki pokazujące podgląd w kolorach.
- **Biblioteka wzorów [M]:** ekran z miniaturami wszystkich projektów, tagami i wyszukiwaniem.
- **Wiele obrazków naraz [S]:** przeciągnięcie kilku plików otwiera je jako kolejne projekty albo robi z nich PDF-y
  z tymi samymi ustawieniami (tryb wiersza poleceń już to umie).
- **Wersja angielska [M]** — teksty w zasobach i wybór języka.
- **Jasny motyw [S]** — do pracy przy dziennym świetle i do porównania z wydrukiem na białej kartce.

## 🔧 Technika i wydania

- **Podpisany exe [M]:** niepodpisany plik pobrany z internetu wywołuje ostrzeżenie SmartScreen. Certyfikat
  (np. przez Azure Trusted Signing) i podpis w GitHub Actions usuną ten problem.
- **ARM64 w GitHub Actions [S]:** druga paczka dla laptopów z Snapdragonem (publish.ps1 już to obsługuje).
- **winget [S]:** manifest w repozytorium winget-pkgs po każdym wydaniu — instalacja jednym poleceniem.
- **Szybsze kontury po pędzlu [M]:** po poprawce przeliczany tylko zmieniony fragment, a nie cały wzór.
  Przy dużych obrazkach edycja będzie płynniejsza.
- **Testy wydruku na obrazach [S]:** kartki renderowane do PNG i porównywane z wzorcami — wykrywa przypadkowe
  zmiany w wyglądzie PDF/druku.
- **Testy interfejsu [M]:** automatyczne klikanie w oknie (UI Automation) w GitHub Actions — otwarcie obrazka,
  wiaderko, linia detalu, kierunek ściegu, cofnięcie, eksport, projektor.
