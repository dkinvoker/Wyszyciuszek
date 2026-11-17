# Wyszyciuszek — pomysły na rozwój

Lista rzeczy, których jeszcze nie ma. Gotowe funkcje opisuje [CHANGELOG.md](CHANGELOG.md).
Orientacyjna ilość pracy: **[S]** mała, **[M]** średnia, **[L]** duża.

## ✅ Zrobione

- **1.1:** wykrywanie tła (jako opcja), kierunek ściegu, linie detali, tryb projektora, zużycie nici i czas szycia.
- **1.2:** aktualizacje jak w Kasetowniku, autozapis z przywracaniem po awarii, ostrzeżenie o obszarach za wąskich
  na wypełnienie (i zamiana pasków na linie), kolejność szycia od spodu do wierzchu, linie do kalkowania, kalibracja
  drukarki.
- **1.3:** „Moje nici” w CSV, metry na szpulce z ostrzeżeniem o braku nici, gęstość wypełnienia, kolor tkaniny
  z ostrzeżeniem o słabym kontraście, lupa i minimapa, odhaczanie i siatka 1 cm w projektorze.

Szczegóły w [CHANGELOG.md](CHANGELOG.md).

## ⭐ Na początek (największy zysk za najmniej pracy)

1. **Napisy [M]** — imię albo data na prezent: tekst zamieniony na linie detali albo obszary.
2. **Ekran powitalny z miniaturami [S]** — ostatnie projekty jako obrazki.
3. **Historia zmian jako lista [S]** — „Wiaderko: dach → Żółty”, skok do dowolnego kroku.
4. **Wiaderko na oryginale [S]** — szybkie wydzielenie fragmentu, którego upraszczanie nie złapało.
5. **Zamiana palety [S]** — cały wzór w pastelach, sepii albo w trzech odcieniach jednego koloru.

## 🧵 Upraszczanie

- **Wycinanie obiektu z tła [M]:** zaznaczasz z grubsza kota na zdjęciu, a reszta staje się tkaniną. Wystarczy
  algorytm typu GrabCut na obrazie roboczym — bez sieci neuronowych i bez internetu.
- **Pędzel „tu ważne” [M]:** malujesz na oryginale twarz czy oczy, a tam program zostawia więcej szczegółów
  (mniejsza najmniejsza plama i cieńsze paski). Reszta upraszcza się mocniej.
- **Tryb „szkic nicią” [M]:** zamiast plam same linie — kontury i kreski cieniowania z wykrywania krawędzi (XDoG).
  To popularny styl haftu z wolnej ręki: czarna nić na jasnej tkaninie, jak rysunek ołówkiem. Wynik od razu
  trafiłby do linii detali (środkowe linie umiemy już liczyć — jak przy „paskach na linie”).
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
- **Trasa po obszarach jednej nici [M]:** kolejność kolorów jest już „od spodu do wierzchu”; brakuje podpowiedzi,
  w jakiej kolejności przeskakiwać między obszarami tej samej nici (najkrótsza trasa, małe numerki 1a, 1b…).
- **Lista zakupów nici [S]:** nici, których we wzorze brakuje w pudełku albo zabraknie na szpulce — jedna kartka
  do sklepu, z numerami producenta.
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
  usunięcie, kierunek ściegu, zamiana na linie).
- **Wiaderko na oryginale [S]:** klik w zdjęcie z tolerancją koloru — szybkie wydzielenie fragmentu, którego
  upraszczanie nie złapało.
- **Historia zmian jako lista [S]:** „Wiaderko: dach → Żółty”, „Nowa linia detalu” — skok do dowolnego kroku.

## 🖨️ Przeniesienie na tkaninę

- **Kalibracja dla kilku drukarek [S]:** dziś jedna korekta skali; osobna dla każdej drukarki (po nazwie z okna drukowania).
- **Szablony do aplikacji [M]:** kształty wybranych kolorów jako osobne elementy do wycięcia z tkaniny, z zapasem
  na podwinięcie i znacznikiem, która strona jest prawa. Ułożone oszczędnie na kartce.
- **Eksport dla maszyn hafciarskich [L]:** pliki PES/DST z automatycznie wygenerowanym ściegiem (wypełnienie w kierunku
  ze strzałek, satyna, ścieg prosty dla linii detali) — dla osób, które mają też maszynę hafciarską.
  SVG z warstwami kolorów i linii jest dobrym startem.

## 🎨 Nici i kolory

- **Katalogi producentów [M]:** gotowe palety z numerami (np. Ariadna, Madeira, Gütermann), wybór „mam nici tej firmy”
  i dopasowanie z numerem katalogowym w legendzie. Dane z otwartych tabel kolorów; import z CSV.
- **Faktura tkaniny w podglądzie [S]:** splot lnu, jeansu czy filcu zamiast jednolitego koloru materiału.
- **Kolory nici ze zdjęcia szpulek [M]:** zdjęcie pudełka z nićmi, klik w każdą szpulkę i dodanie do „Moich nici”.
- **Zamiana palety [S]:** cały wzór w innym nastroju (pastele, sepia, monochrom w 3 odcieniach jednego koloru)
  jednym kliknięciem — przydatne przy ozdobach na prezent.

## 🗂️ Wygoda

- **Ekran powitalny z miniaturami [S]:** ostatnie projekty jako obrazki, nie tylko nazwy.
- **Miniatury .wyszycie w Eksploratorze [L]** — rozszerzenie powłoki pokazujące podgląd w kolorach.
- **Biblioteka wzorów [M]:** ekran z miniaturami wszystkich projektów, tagami i wyszukiwaniem.
- **Wiele obrazków naraz [S]:** przeciągnięcie kilku plików otwiera je jako kolejne projekty albo robi z nich PDF-y
  z tymi samymi ustawieniami (tryb wiersza poleceń już to umie).
- **Wersja angielska [M]** — teksty w zasobach i wybór języka.
- **Jasny motyw [S]** — do pracy przy dziennym świetle i do porównania z wydrukiem na białej kartce.

## 🔧 Technika i wydania

- **Podpisany exe [M]:** niepodpisany plik pobrany z internetu wywołuje ostrzeżenie SmartScreen. Certyfikat
  (np. przez Azure Trusted Signing) i podpis w GitHub Actions usuną ten problem.
- **ARM64 w GitHub Actions [S]:** druga paczka dla laptopów z Snapdragonem (publish.ps1 już to obsługuje); aktualizacja
  wybierałaby plik dla swojej architektury.
- **winget [S]:** manifest w repozytorium winget-pkgs po każdym wydaniu — instalacja jednym poleceniem.
- **Szybsze kontury po pędzlu [M]:** po poprawce przeliczany tylko zmieniony fragment, a nie cały wzór.
  Przy dużych obrazkach edycja będzie płynniejsza.
- **Testy wydruku na obrazach [S]:** kartki renderowane do PNG i porównywane z wzorcami — wykrywa przypadkowe
  zmiany w wyglądzie PDF/druku.
- **Testy interfejsu [M]:** automatyczne klikanie w oknie (UI Automation) w GitHub Actions — otwarcie obrazka,
  wiaderko, linia detalu, kierunek ściegu, aktualizacja z testowego serwera, autozapis, cofnięcie, eksport, projektor.
