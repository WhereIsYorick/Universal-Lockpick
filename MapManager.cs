using Godot;
using System;
using System.Collections.Generic;

public partial class MapManager : Node
{
	private const string CONFIG_INI_PATH = "res://chests.ini";
	private const string SETTINGS_INI_PATH = "user://settings.ini"; // Файл настроек программы

	private LineEdit _searchField;
	private LineEdit _chestDescriptionInput;
	private GridContainer _chestGrid;
	private GridContainer _chestGridFull;
	private GridContainer _chestGridAlmost;
	private Label _labelFull;
	private Label _labelAlmost;
	private Button _contributeButton;
	private Button _resetButton;
	private Button _resetLinksButton;
	private OptionButton _langSelector; // Выпадающий список языков
	private Button _helpButton;         // Кнопка "?"
	private AcceptDialog _helpWindow;   // Окно справки

	private const string CONTRIBUTE_FORM_URL = "https://docs.google.com/forms/d/e/1FAIpQLSfmbioj1F5C-LMalOMSWay6Ba8B5y2ori4qH20JDPiNXdx0UA/viewform";
	private const string FORM_ENTRY_CHEST_DESCRIPTION = "entry.1484895692";
	private const string FORM_ENTRY_CELL_COUNT = "entry.1053036920";
	private const string FORM_ENTRY_START_POS = "entry.807992425";
	private const string FORM_ENTRY_RULES = "entry.1620143823";

	// Ссылки на поля левого калькулятора для заполнения данными
	private SpinBox _cellCountInput;
	private LineEdit _rulesInput;
	private Label _resultLabel;
	private SpinBox[] _mainSpinBoxes;

	private struct ChestConfig
	{
		public string DisplayName;
		public int CellCount;
		public string Rules;
		public string StartPos;
		public List<string> SearchTags;
	}

	private void OnResetButtonPressed()
	{
		// Reset inputs to defaults
		_cellCountInput.Value = 3;
		_rulesInput.Text = string.Empty;
		for (int i = 0; i < _mainSpinBoxes.Length; i++)
		{
			_mainSpinBoxes[i].Value = 0;
		}
		// Refresh list
		FilterAndGenerateButtons(_searchField.Text);
	}

	private void OnResetLinksButtonPressed()
	{
		// Ask PuzzleSolver (root node) to reset all links to 'НЕТ'
		var rootNode = GetTree().Root.GetChild(0);
		rootNode.Call("ResetAllLinks");
		FilterAndGenerateButtons(_searchField.Text);
	}

	private List<ChestConfig> _database = new List<ChestConfig>();

	// Словарь текстов встроенной справки для окна "?" (зависит от языка)
	private readonly Dictionary<string, Tuple<string, string>> _helpLoc = new Dictionary<string, Tuple<string, string>>
	{
		{ "en", new Tuple<string, string>("How to use?", "1. Run the app as Administrator. Set 'Windowed' mode in game graphics settings.\n2. In the right panel, try finding the chest by tags (e.g., 'Diego', 'Old Camp', 'Castle'). Click any chest in the list to auto-fill the combination and rules.\n3. If not found, enter the rules manually: specify the slot count and set current pins starting from 0. Define the rule.\n\nSlots are named with uppercase English letters starting from A (bottom slot). Type 'A:'. Check which slots move with slot A. If slot B moves synchronously, write 'B+'. If asynchronously, write 'B-'. Separate multiple slots with commas (,). Separate different slot rules with semicolons (;). If a slot moves independently and affects nothing, it is NOT NEEDED to be included in the rules!\n\nRules template (Diego's Hut):\nSlot count 6\n1,0,5,4,3,6\nB:C+,E-;D:B+,E-;E:B-,C+,F-;F:E+\nDo NOT put a semicolon (;) at the very end of the rules line!\n\n4. Click 'Calculate combination' to see the solution.\n5. For auto-lockpicking, set the macro delay and click 'Start Auto-play'. Quickly switch to the game window and wait.") },

{ "ru", new Tuple<string, string>("Инструкция", "1. Запустите программу от имени Администратора. В настройках игры включите режим 'В окне'.\n2. В правой части окна попробуйте найти нужный сундук по тегам (например: 'Диего', 'Старый лагерь', 'Замок'). Кликните по сундуку из списка, чтобы автоматически заполнить комбинацию и правила.\n3. Если сундука нет в базе, настройте всё вручную: укажите количество ячеек, выставьте их начальные положения (отсчет начинается с 0) и запишите правило.\n\nЯчейки строго именуются заглавными английскими буквами начиная с A (нижняя ячейка). Напишите 'A:'. Посмотрите в игре, какие ячейки двигаются вслед за A. Если ячейка B движется синхронно, укажите 'B+', если асинхронно — 'B-'. Несколько ячеек перечисляйте через запятую (,). Правила для разных ячеек разделяйте точкой с запятой (;). Если ячейка двигается самостоятельно и ни на что не влияет, в правилах ее учитывать НЕ НУЖНО!\n\nШаблон правил (Хижина Диего):\nКоличество ячееек 6\n1,0,5,4,3,6\nB:C+,E-;D:B+,E-;E:B-,C+,F-;F:E+\nВ самом конце строки точку с запятой (;) ставить НЕ НАДО!\n\n4. Нажмите 'Рассчитать комбинацию', чтобы получить решение.\n5. Для автоматического взлома задайте задержку макросу и нажмите 'Начать авто-игру'. Переключитесь на окно игры и ожидайте завершения.") },

{ "pl", new Tuple<string, string>("Instrukcja", "1. Uruchom aplikację jako Administrator. W ustawieniach gry włącz tryb 'W oknie'.\n2. W prawym panelu spróbuj znaleźć skrzynię po tagach (np. 'Diego', 'Stary Obóz', 'Zamek'). Kliknij skrzynię na liście, aby automatycznie uzupełnić kombinację i reguły.\n3. Jeśli nie znaleziono, wprowadź reguły ręcznie: określ liczbę gniazd, ustaw pozycje początkowe (odliczanie od 0) i zapisz regułę.\n\nGniazda są oznaczane wielkimi angielskimi literami zaczynając od A (dolne gniazdo). Wpisz 'A:'. Sprawdź, które gniazda poruszają się za gniazdem A. Jeśli gniazdo B porusza się synchronicznie, wpisz 'B+', jeśli asynchronicznie — 'B-'. Wiele gniazd rozdzielaj przecinkami (,). Reguły dla różnych gniazd rozdzielaj średnikami (;). Jeśli gniazdo porusza się niezależnie i na nic nie wpływa, NIE TRZEBA go uwzględniać w regułach!\n\nSzablon reguł (Chata Diego):\nLiczba gniazd 6\n1,0,5,4,3,6\nB:C+,E-;D:B+,E-;E:B-,C+,F-;F:E+\nNie stawiaj średnika (;) na samym końcu linii reguł!\n\n4. Kliknij 'Oblicz kombinację', aby zobaczyć rozwiązanie.\n5. Aby uruchomić automatyczne otwieranie, ustaw opóźnienie makra i kliknij 'Uruchom Auto-play'. Przełącz się na okno gry i poczekaj.") },

{ "de", new Tuple<string, string>("Anleitung", "1. Anwendung als Administrator ausführen. In den Spiel-Einstellungen den Modus 'Fenstermodus' wählen.\n2. Im rechten Bereich können Sie versuchen, die Truhe über Tags zu finden (z. B. 'Diego', 'Altes Lager', 'Burg'). Klicken Sie auf eine Truhe in der Liste, um Kombination und Regeln automatisch auszufüllen.\n3. Falls nicht vorhanden, Regeln manuell eingeben: Anzahl der Slots angeben, Startpositionen festlegen (Zählung beginnt bei 0) und Regel aufschreiben.\n\nSlots werden strikt mit englischen Großbuchstaben ab A (unterer Slot) benannt. Schreiben Sie 'A:'. Prüfen Sie im Spiel, welche Slots sich mit Slot A bewegen. Wenn sich Slot B synchron bewegt, 'B+' eingeben, wenn asynchron — 'B-'. Mehrere Slots mit Komma (,) trennen. Regeln für verschiedene Slots mit Semikolon (;) trennen. Wenn sich ein Slot unabhängig bewegt und nichts beeinflusst, muss er in den Regeln NICHT berücksichtigt werden!\n\nRegelvorlage (Diegos Hütte):\nAnzahl der Slots 6\n1,0,5,4,3,6\nB:C+,E-;D:B+,E-;E:B-,C+,F-;F:E+\nSetzen Sie kein Semikolon (;) ganz am Ende der Regelzeile!\n\n4. Auf 'Kombination berechnen' klicken, um die Lösung zu erhalten.\n5. Für automatisches Schlossknacken die Makro-Verzögerung einstellen und auf 'Auto-play starten' klicken. Zum Spielfenster wechseln und warten.") },

{ "ua", new Tuple<string, string>("Інструкція", "1. Запустіть програму від імені Адміністратора. У налаштуваннях гри увімкніть режим 'У вікні'.\n2. У правій частині вікна спробуйте знайти потрібну скриню за тегами (наприклад: 'Дієго', 'Старий табір', 'Замок'). Клікніть по скрині зі списку, щоб автоматично заповнити комбінацію та правила.\n3. Якщо скрині немає в базі, налаштуйте все вручну: вкажіть кількість комірок, виставте їхні початкові положення (відлік починається з 0) та запишіть правило.\n\nКомірки суворо іменуються великими англійськими літерами починаючи з A (нижня комірка). Напишіть 'A:'. Подивіться в грі, які комірки рухаються слідом за A. Якщо комірка B рухається синхронно, вкажіть 'B+', якщо асинхронно — 'B-'. Кілька комірок перераховуйте через кому (,). Правила для різних комірок розділяйте крапкою з комою (;). Якщо комірка рухається самостійно і ні на що не впливає, в правилах її враховувати НЕ ТРЕБА!\n\nШаблон правил (Хатина Дієго):\nКількість комірок 6\n1,0,5,4,3,6\nB:C+,E-;D:B+,E-;E:B-,C+,F-;F:E+\nВ самому кінці рядка крапку з комою (;) ставити НЕ ТРЕБА!\n\n4. Натисніть 'Розрахувати комбінацію', щоб отримати рішення.\n5. Для автоматичного зламу задайте затримку макросу та натисніть 'Почати авто-гру'. Переключіться на вікно гри та очікуйте завершення.") }

};

	public override void _Ready()
	{
		_searchField = GetNode<LineEdit>("SearchField");
		_chestDescriptionInput = GetNode<LineEdit>("ChestDescriptionInput");
		// New separate containers for full and partial matches (created in scene)
		_chestGridFull = GetNode<GridContainer>("ScrollContainer/BoxContainer/ChestGrid_full");
		_chestGridAlmost = GetNode<GridContainer>("ScrollContainer/BoxContainer/ChestGrid_almost");
		_labelFull = GetNode<Label>("ScrollContainer/BoxContainer/Label_full");
		_labelAlmost = GetNode<Label>("ScrollContainer/BoxContainer/Label_almost");
		_contributeButton = GetNode<Button>("ContributeButton");

		// ИСПРАВЛЕНО: Поднимаемся на уровень вверх (к корню Control) и заходим в левый VBoxContainer
		_langSelector = GetNode<OptionButton>("../VBoxContainer/HBoxContainerLang/OptionButton");
		_helpButton = GetNode<Button>("../VBoxContainer/HBoxContainerLang/HelpButton");
		// ИСПРАВЛЕНО: Находим окно помощи напрямую через корень сцены, игнорируя путаницу с VBoxContainer
		_helpWindow = GetTree().Root.GetChild(0).GetNode<AcceptDialog>("HelpWindow");


		// (Остальной код левой панели: _cellCountInput, _rulesInput, _mainSpinBoxes и т.д. ниже остается без изменений...)


		_cellCountInput = GetNode<SpinBox>("../VBoxContainer/HBoxContainer/CellCountInput");
		_rulesInput = GetNode<LineEdit>("../VBoxContainer/RulesInput");
		_resultLabel = GetNode<Label>("../VBoxContainer/ScrollContainer/Label");

		// Инициализируем массив строго по индексам [0..7]
		_mainSpinBoxes = new SpinBox[8];
		_mainSpinBoxes[0] = GetNode<SpinBox>("../VBoxContainer/GridContainer/A");
		_mainSpinBoxes[1] = GetNode<SpinBox>("../VBoxContainer/GridContainer/B");
		_mainSpinBoxes[2] = GetNode<SpinBox>("../VBoxContainer/GridContainer/C");
		_mainSpinBoxes[3] = GetNode<SpinBox>("../VBoxContainer/GridContainer/D");
		_mainSpinBoxes[4] = GetNode<SpinBox>("../VBoxContainer/GridContainer/E");
		_mainSpinBoxes[5] = GetNode<SpinBox>("../VBoxContainer/GridContainer/F");
		_mainSpinBoxes[6] = GetNode<SpinBox>("../VBoxContainer/GridContainer/G");
		_mainSpinBoxes[7] = GetNode<SpinBox>("../VBoxContainer/GridContainer/H");

		// Слушатели, чтобы при изменении селектора числа ячеек и стартовых позиций список обновлялся
		_cellCountInput.ValueChanged += OnCellCountValueChanged;
		for (int i = 0; i < _mainSpinBoxes.Length; i++)
		{
			_mainSpinBoxes[i].ValueChanged += OnStartPositionValueChanged;
		}

		// Reset button (clear inputs) — aligned to the far right in the row
		_resetButton = GetNode<Button>("../VBoxContainer/HBoxContainer/ResetButton");
		_resetButton.Pressed += OnResetButtonPressed;
		_resetLinksButton = GetNode<Button>("../VBoxContainer/HBoxContainer2/ResetLinksButton");
		_resetLinksButton.Pressed += OnResetLinksButtonPressed;

		// Заполнение выпадающего списка языков
		_langSelector.AddItem("English (EN)", 0);
		_langSelector.AddItem("Русский (RU)", 1);
		_langSelector.AddItem("Polski (PL)", 2);
		_langSelector.AddItem("Deutsch (DE)", 3);
		_langSelector.AddItem("Українська (UA)", 4);

		// Привязка событий
		_searchField.TextChanged += OnSearchTextChanged;
		_langSelector.ItemSelected += OnLanguageSelected;
		_helpButton.Pressed += OnHelpButtonPressed;

		_contributeButton.Pressed += OnContributeButtonPressed;

		// Настройка встроенных языковых пакетов Godot (Задаем переводы для ключей KEY_...)
		SetupTranslationServer();

		// Загрузка сохраненного языка из настроек и запуск базы
		LoadSavedLanguage();
		LoadIniDatabase();

		// Применяем переводы к статическим элементам сцены
		ApplyTranslationsToScene();

		// Apply translation to static buttons that use KEY_ placeholders in the scene
		_resetButton.Text = TranslationServer.Translate("KEY_RESET_CELLS");
		_resetLinksButton.Text = TranslationServer.Translate("KEY_RESET_LINKS");
	}

	private void SetupTranslationServer()
	{
		string[] codes = { "en", "ru", "pl", "de", "ua" };

		var titles = new[] { "Universal Lockpick", "Универсальная отмычка", "Uniwersalny Wytrych", "Universaldietrich", "Універсальний відмикач" };
		var cells = new[] { "Number of cells in the lock:", "Количество ячеек в замке:", "Liczba tarcz w zamku:", "Anzahl der Scheiben im Schloss:", "Кількість комірок у замку:" };

		// Массивы переводов для направлений ходов
		var logRight = new[] { "RIGHT", "ВПРАВО", "W PRAWO", "RECHTS", "ВПРАВО" };
		var logLeft = new[] { "LEFT", "ВЛЕВО", "W LEWO", "LINKS", "ВЛІВО" };

		// Перевод финального статуса со скриншота
		var logSuccessFinished = new[] {
		"SUCCESS: Autoplay finished. Minimizing window...",
		"УСПЕХ: Авто-игра успешно завершена. Сворачивание окна...",
		"SUKCES: Autoodtwarzanie zakończone. Minimalizowanie okna...",
		"ERFOLG: Auto-Play erfolgreich beendet. Fenster wird minimiert...",
		"УСПІХ: Авто-гру успішно завершено. Згортання вікна..."
	};

		// Перевод сообщения об экстренной остановке кликера
		var errEmergencyStop = new[] {
		"AUTOPLAY EMERGENCY STOPPED BY USER!\nInputs cancelled immediately.",
		"АВТО-ИГРА ЭКСТРЕННО ОСТАНОВЛЕНА ПОЛЬЗОВАТЕЛЕМ!\nВвод команд немедленно отменён.",
		"AUTODODTWARZANIE ZATRZYMANE AWARYJNIE PRZEZ UŻYTKOWNIKA!\nWprowadzanie klawiszy zostało natychmiast anulowane.",
		"AUTO-PLAY VOM BENUTZER NOTGESTOPPT!\nTastatureingaben wurden sofort abgebrochen.",
		"АВТОГРА ЕКСТРЕНО ЗУПИНЕНА КОРИСТУВАЧЕМ!\nВведення команд негайно скасовано."
	};

		// Ошибка, если игрок нажал автоплей до расчета комбинации
		var errCalcFirst = new[] {
		"ERROR: Please click 'Calculate the combination' successfully before running Auto-Play!",
		"ОШИБКА: Пожалуйста, сначала успешно рассчитайте комбинацию перед запуском авто-игры!",
		"BŁĄD: Proszę najpierw pomyślnie obliczyć kombinację przed uruchomieniem auto-gry!",
		"FEHLER: Bitte berechnen Sie zuerst die Kombination erfolgreich, bevor Sie Auto-Play starten!",
		"ПОМИЛКА: Будь ласка, спочатку успішно розрахуйте комбінацію перед запуском авто-гри!"
	};

		// Статусы загрузки базы данных из правого окна
		var dbSuccess = new[] {
		"Configuration and initial cell values successfully loaded from database.",
		"Конфигурация и начальные значения ячеек успешно загружены из базы данных.",
		"Konfiguracja i wartości początkowe tarcz zostały pomyślnie załadowane z bazy danych.",
		"Konfiguration und Anfangswerte der Scheiben erfolgreich aus der Datenbank geladen.",
		"Конфігурацію та початкові значення комірок успішно завантажено з бази даних."
	};
		var dbIncomplete = new[] {
		"[INCOMPLETE DATA] Chest: '{0}'\nStatus: PARTIAL_LOAD (Rules OK, Start Positions MISSING)\nAction: Input 0-6 values from your screen manually, then click 'Calculate'.",
		"[НЕПОЛНЫЕ ДАННЫЕ] Сундук: '{0}'\nСтатус: ЧАСТИЧНАЯ ЗАГРУЗКА (Правила ОК, Начальные пазы ОТСУТСТВУЮТ)\nДействие: Введите цифры 0-6 со своего экрана вручную, затем нажмите 'Calculate'.",
		"[NIEPEŁNE DANE] Skrzynia: '{0}'\nStatus: CZĘŚCIOWE ŁADOWANIE (Reguły OK, Pozycje początkowe BRAK)\nAkcja: Wprowadź numery tarcz 0-6 z ekranu ręcznie, a następnie kliknij 'Calculate'.",
		"[UNVOLLSTÄNDIGE DATEN] Truhe: '{0}'\nStatus: TEILWEISE GELADEN (Regeln OK, Startpositionen FEHLEN)\nAktion: Geben Sie die Scheibennummern 0-6 manuell ein und klicken Sie auf 'Calculate'.",
		"[НЕПОВНІ ДАНІ] Скриня: '{0}'\nСтатус: ЧАСТКОВЕ ЗАВАНТАЖЕННЯ (Правила ОК, Початкові пази ВІДСУТНІ)\nДія: Введіть цифри 0-6 зі свого екрана вручну, потім натисніть 'Calculate'."
	};

		// Тексты обратного отсчета автокликера
		var logCountdown = new[] {
		"[AUTOPLAY] Starting inputs in {0} seconds... Click 'STOP' to cancel.",
		"[АВТОИГРА] Запуск ввода через {0} сек... Нажмите 'STOP' для отмены.",
		"[AUTODODTWARZANIE] Rozpoczęcie wprowadzania za {0} sek... Kliknij 'STOP', aby anulować.",
		"[AUTO-PLAY] Eingabe startet in {0} Sekunden... Klicken Sie auf 'STOP' zum Abbrechen.",
		"[АВТОГРА] Запуск введення через {0} сек... Натисніть 'STOP' для скасування."
	};
		var logStep = new[] {
		"[AUTOPLAY] Step {0}/{1}\nMoving to Cell {2}...\nClick STOP to abort.",
		"[АВТОИГРА] Шаг {0}/{1}\nПеремещение к ячейке {2}...\nНажмите STOP для отмены.",
		"[AUTODODTWARZANIE] Krok {0}/{1}\nPrzechodzenie do tarczy {2}...\nKliknij STOP, aby przerwać.",
		"[AUTO-PLAY] Schritt {0}/{1}\nBewege zu Scheibe {2}...\nKlicken Sie auf STOP zum Abbrechen.",
		"[АВТОГРА] Крок {0}/{1}\nПереміщення до комірки {2}...\nНатисніть STOP для скасування."
	};

		// Новые ключи
		var initial = new[] {
		"Initial positions of the grooves in the lock cells (0-6):",
		"Начальные положения пазов в ячейках (0-6):",
		"Początkowe pozycje rowków w tarczach (0-6):",
		"Anfangspositionen der Nuten in den Scheiben (0-6):",
		"Початкові положення пазів у комірках (0-6):"
	};
		var rulesHint = new[] {
		"Enter the rules. Example: A:B+; B:A+,D-; D:A+",
		"Введите правила. Пример: A:B+; B:A+,D-; D:A+",
		"Wprowadź reguły. Przykład: A:B+; B:A+,D-; D:A+",
		"Regeln eingeben. Beispiel: A:B+; B:A+,D-; D:A+",
		"Введіть правила. Приклад: A:B+; B:A+,D-; D:A+"
	};
		var calc = new[] { "Calculate the combination", "Рассчитать комбинацию", "Oblicz kombinację", "Kombination berechnen", "Розрахувати комбінацію" };
		var delay = new[] {
		"Start delay before starting the autoclicker:",
		"Начальная задержка перед запуском автокликера:",
		"Opóźnienie przed uruchomieniem autoclickera:",
		"Startverzögerung vor dem Autoclicker:",
		"Початкова затримка перед запуском автоклікера:"
	};

		var auto = new[] { "Run auto-play", "Запустить авто-гру", "Uruchom auto-grę", "Auto-Play starten", "Запустити авто-гру" };
		var stopAuto = new[] { "[ STOP AUTO-PLAY ]", "[ ОСТАНОВИТЬ АВТО-ИГРУ ]", "[ STOP AUTO-GRĘ ]", "[ STOP AUTO-PLAY ]", "[ ЗУПИНИТИ АВТО-ГРУ ]" };
		var search = new[] { "Chest searching", "Поиск сундука", "Wyszukiwanie skrzyń", "Truhensuche", "Пошук скрині" };
		var hint = new[] { "Type chest location...", "Введите местоположение...", "Wpisz lokalizację skrzyni...", "Truhenstandort eingeben...", "Введіть місце розташування..." };
		var chestDescription = new[] {
		"Chest description",
		"Описание сундука",
		"Opis skrzyni",
		"Truhenbeschreibung",
		"Опис скрині"
	};
		var chestDescriptionHint = new[] {
		"Type location and landmark...",
		"Введите место и ориентир...",
		"Wpisz miejsce i punkt orientacyjny...",
		"Ort und Orientierungspunkt eingeben...",
		"Введіть місце та орієнтир..."
	};
		var contr = new[] {
		"Add current configuration to database",
		"Добавить текущую конфигурацию в базу",
		"Dodaj bieżącą konfigurację do bazy",
		"Aktuelle Konfiguration zur Datenbank hinzufügen",
		"Додати поточну конфігурацію до бази"
	};

		var linksTitle = new[] {
		"Slider links",
		"СВЯЗИ ПОЛЗУНКОВ",
		"Powiązania suwaków",
		"Verbindungen der Schieber",
		"ЗВ'ЯЗКИ ПОЛЗУНКІВ"
		};

		var linkTogether = new[] { "TOGETHER", "ВМЕСТЕ", "RAZEM", "ZUSAMMEN", "РАЗОМ" };
		var linkOpposite = new[] { "OPPOSITE", "ПРОТИВ", "PRZECIW", "GEGEN", "ПРОТИВ" };
		var linkNone = new[] { "NONE", "НЕТ", "BRAK", "LEER", "НІ" };

		var linkTooltipDiag = new[] {
		"Diagonal locked: slider always moves itself.",
		"Диагональ заблокирована: ползунок всегда двигает сам себя.",
		"Przekątna zablokowana: suwak zawsze porusza sam siebie.",
		"Diagonale gesperrt: Schieber bewegt immer sich selbst.",
		"Діагональ заблоковано: повзунок завжди рухає себе."
		};

		var linkTooltipClick = new[] {
		"Click: NONE -> TOGETHER -> OPPOSITE",
		"Клик: НЕТ -> ВМЕСТЕ -> ПРОТИВ",
		"Klik: BRAK -> RAZEM -> PRZECIW",
		"Klick: LEER -> ZUSAMMEN -> GEGEN",
		"Клік: НІ -> РАЗОМ -> ПРОТИВ"
		};

		var resetCells = new[] { "Reset cells", "Сброс ячеек", "Reset komórek", "Zellen zurücksetzen", "Скинути комірки" };
		var resetLinks = new[] { "Reset links", "Сброс связей", "Reset powiązań", "Verbindungen zurücksetzen", "Скинути зв'язки" };

		var exactMatch = new[] {
		"Exact match",
		"Точное совпадение",
		"Dokładne dopasowanie",
		"Exakte Übereinstimmung",
		"Точне співпадіння"
	};
		var partialMatch = new[] {
		"Partial match",
		"Частичное совпадение",
		"Częściowe dopasowanie",
		"Teilweises Übereinstimmen",
		"Часткове співпадіння"
	};
		var emptyResults = new[] {
		"Empty",
		"Пусто",
		"Puste",
		"Leer",
		"Порожньо"
	};

		// Массивы переводов для системных логов и ошибок
		var errNoSolution = new[] {
		"ERROR: No safe combination exists for this layout!",
		"ОШИБКА: Математического решения для текущей раскладки не существует!",
		"BŁĄD: Nie istnieje bezpieczna kombinacja dla tego układu!",
		"FEHLER: Für dieses Layout existiert keine sichere Kombination!",
		"ПОМИЛКА: Математичного рішення для поточної розкладки не існує!"
	};

		var errRulesEmpty = new[] {
		"ERROR: Rules string is empty.",
		"ОШИБКА: Строка правил пуста.",
		"BŁĄD: Ciąg reguł jest pusty.",
		"FEHLER: Die Regelzeichenfolge ist leer.",
		"ПОМИЛКА: Рядок правил порожній."
	};

		var errNoGame = new[] {
		"ERROR: Process 'G1R-Win64-Shipping.exe' was not found!",
		"ОШИБКА: Игровой процесс 'G1R-Win64-Shipping.exe' не найден!",
		"BŁĄD: Proces gry 'G1R-Win64-Shipping.exe' nie został znaleziony!",
		"FEHLER: Spielprozess 'G1R-Win64-Shipping.exe' wurde nicht gefunden!",
		"ПОМИЛКА: Ігровий процес 'G1R-Win64-Shipping.exe' не знайдено!"
	};

		var logPlan = new[] {
		"LOCKPICK COMBINATION PLAN",
		"ПЛАН ВЗЛОМА ЗАМКА",
		"PLAN WYTRYCHU",
		"DIETRICH-KOMBINATIONSPLAN",
		"ПЛАН ВІДМИКАННЯ ЗАМКУ"
	};

		var logSuccess = new[] {
		"SUCCESS: Autoplay finished. Minimizing window...",
		"УСПЕХ: Авто-игра завершена. Сворачивание окна...",
		"SUKCES: Autoodtwarzanie zakończone. Minimalizowanie okna...",
		"ERFOLG: Auto-Play beendet. Fenster wird minimiert...",
		"УСПІХ: Авто-гру завершено. Згортання вікна..."
	};

		// Перевод сообщения о поиске игрового процесса в ОС
		var logSearchProcess = new[] {
		"[AUTOPLAY] Searching for game process: '{0}.exe'...\n",
		"[АВТОИГРА] Поиск игрового процесса: '{0}.exe'...\n",
		"[AUTODODTWARZANIE] Szukanie procesu gry: '{0}.exe'...\n",
		"[AUTO-PLAY] Spielprozess wird gesucht: '{0}.exe'...\n",
		"[АВТОГРА] Пошук ігрового процесу: '{0}.exe'...\n"
	};

		// Перевод сообщения об успешном фокусе окна игры
		var logWindowFocused = new[] {
		"Gothic Remake window focused! Countdown started...\n",
		"Окно Gothic Remake сфокусировано! Обратный отсчёт запущен...\n",
		"Okno Gothic Remake zostało sfocusowane! Odliczanie rozpoczęte...\n",
		"Gothic Remake-Fenster fokussiert! Countdown gestartet...\n",
		"Вікно Gothic Remake сфокусовано! Зворотний відлік запущено...\n"
	};

		// Перевод ошибки скрытого окна (слепой режим)
		var errBlindMode = new[] {
		"ERROR: Process '{0}.exe' found, but window is hidden!\n[SYSTEM] Continuing in blind mode. Click on the game window now...",
		"ОШИБКА: Процесс '{0}.exe' найден, но его окно скрыто!\n[СИСТЕМА] Продолжение в слепом режиме. Пожалуйста, кликните по окну игры сейчас...",
		"BŁĄD: Proces '{0}.exe' został znaleziony, ale jego okno jest ukryte!\n[SYSTEM] Kontynuacja w trybie ślepym. Kliknij teraz na okno gry...",
		"FEHLER: Prozess '{0}.exe' gefunden, aber das Fenster ist ausgeblendet!\n[SYSTEM] Weiter im Blindmodus. Klicken Sie jetzt auf das Spielfenster...",
		"ПОМИЛКА: Процес '{0}.exe' знайдено, але його вікно приховано!\n[СИСТЕМА] Продовження в сліпому режимі. Будь ласка, клікніть по вікну гри зараз..."
	};

		var logParsingError = new[] {
		"Parsing error: {0}",
		"Ошибка разбора: {0}",
		"Błąd parsowania: {0}",
		"Analysefehler: {0}",
		"Помилка розбору: {0}"
	};

		var errRulesMissingColon = new[] {
		"ERROR in command '{0}': Missing colon ':'",
		"ОШИБКА в команде '{0}': отсутствует двоеточие ':'",
		"BŁĄD w poleceniu '{0}': brakuje dwukropka ':'",
		"FEHLER im Befehl '{0}': Doppelpunkt ':' fehlt",
		"ПОМИЛКА в команді '{0}': відсутній двокрапка ':'"
	};

		var errUnknownLetter = new[] {
		"ERROR: Unknown letter '{0}'.",
		"ОШИБКА: Неизвестная буква '{0}'.",
		"BŁĄD: Nieznana litera '{0}'.",
		"FEHLER: Unbekannter Buchstabe '{0}'.",
		"ПОМИЛКА: Невідома буква '{0}'."
	};

		var errEmptyRule = new[] {
		"ERROR in block '{0}': Empty rule.",
		"ОШИБКА в блоке '{0}': пустое правило.",
		"BŁĄD w bloku '{0}': pusta reguła.",
		"FEHLER im Block '{0}': leere Regel.",
		"ПОМИЛКА в блоці '{0}': порожнє правило."
	};

		var errInvalidCell = new[] {
		"ERROR: Invalid cell '{0}' in rule '{1}'.",
		"ОШИБКА: Неверная ячейка '{0}' в правиле '{1}'.",
		"BŁĄD: Nieprawidłowa komórka '{0}' w regule '{1}'.",
		"FEHLER: Ungültige Zelle '{0}' in Regel '{1}'.",
		"ПОМИЛКА: Неправильна комірка '{0}' у правилі '{1}'."
	};

		var errInvalidSign = new[] {
		"ERROR: Invalid sign '{0}' in rule '{1}'.",
		"ОШИБКА: Неверный знак '{0}' в правиле '{1}'.",
		"BŁĄD: Nieprawidłowy znak '{0}' w regule '{1}'.",
		"FEHLER: Ungültiges Zeichen '{0}' in Regel '{1}'.",
		"ПОМИЛКА: Неправильний знак '{0}' у правилі '{1}'."
	};

		var matrixPinLabel = new[] {
		"P",
		"П",
		"P",
		"P",
		"П"
	};

		// Перевод заголовка успешно сгенерированного плана взлома
		var logPlanGenerated = new[] {
		"COMBINATION PLAN GENERATED! (Steps: {0})\n",
		"ПЛАН КОМБИНАЦИИ СГЕНЕРИРОВАН! (Шагов: {0})\n",
		"PLAN KOMBINACJI WYGENEROWANY! (Kroków: {0})\n",
		"KOMBINATIONSPLAN GENERIERT! (Schritte: {0})\n",
		"ПЛАН КОМБІНАЦІЇ ЗГЕНЕРОВАНО! (Кроків: {0})\n"
	};

		for (int i = 0; i < codes.Length; i++)
		{
			Translation tx = new Translation();
			tx.Locale = codes[i];
			tx.AddMessage("KEY_TITLE", titles[i]);
			tx.AddMessage("KEY_CELLS", cells[i]);
			tx.AddMessage("KEY_INITIAL", initial[i]);       // Добавлено
			tx.AddMessage("KEY_RULES_HINT", rulesHint[i]); // Добавлено
			tx.AddMessage("KEY_CALCULATE", calc[i]);
			tx.AddMessage("KEY_DELAY", delay[i]);           // Добавлено
			tx.AddMessage("KEY_AUTOPLAY", auto[i]);
			tx.AddMessage("KEY_SEARCH_TITLE", search[i]);
			tx.AddMessage("KEY_SEARCH_HINT", hint[i]);
			tx.AddMessage("KEY_CHEST_DESCRIPTION", chestDescription[i]);
			tx.AddMessage("KEY_CHEST_DESCRIPTION_HINT", chestDescriptionHint[i]);
			tx.AddMessage("KEY_CONTRIBUTE", contr[i]);
			tx.AddMessage("KEY_EXACT_MATCH", exactMatch[i]);
			tx.AddMessage("KEY_PARTIAL_MATCH", partialMatch[i]);
			// Link UI keys
			tx.AddMessage("KEY_LINKS_TITLE", linksTitle[i]);
			tx.AddMessage("KEY_LINK_TOGETHER", linkTogether[i]);
			tx.AddMessage("KEY_LINK_OPPOSITE", linkOpposite[i]);
			tx.AddMessage("KEY_LINK_NONE", linkNone[i]);
			tx.AddMessage("KEY_LINK_TOOLTIP_DIAG", linkTooltipDiag[i]);
			tx.AddMessage("KEY_LINK_TOOLTIP_CLICK", linkTooltipClick[i]);
			tx.AddMessage("KEY_RESET_CELLS", resetCells[i]);
			tx.AddMessage("KEY_RESET_LINKS", resetLinks[i]);
			tx.AddMessage("KEY_STOP_AUTOPLAY", stopAuto[i]);
			// Aliases used in UI scene for full/almost boxes
			tx.AddMessage("KEY_FULL_CORRECT", exactMatch[i]);
			tx.AddMessage("KEY_ALMOST_CORRECT", partialMatch[i]);
			tx.AddMessage("KEY_EMPTY_RESULTS", emptyResults[i]);

			// 🔥 ИСПРАВЛЕНО: Строку Translation tx = new Translation(); мы отсюда УДАЛИЛИ.
			// Просто дописываем новые сообщения в уже существующий объект tx:
			tx.AddMessage("ERR_NO_SOLUTION", errNoSolution[i]);
			tx.AddMessage("ERR_RULES_EMPTY", errRulesEmpty[i]);
			tx.AddMessage("ERR_NO_GAME", errNoGame[i]);
			tx.AddMessage("LOG_PLAN", logPlan[i]);
			tx.AddMessage("LOG_SUCCESS", logSuccess[i]);

			tx.AddMessage("LOG_RIGHT", logRight[i]);
			tx.AddMessage("LOG_LEFT", logLeft[i]);
			tx.AddMessage("DB_SUCCESS", dbSuccess[i]);
			tx.AddMessage("DB_INCOMPLETE", dbIncomplete[i]);
			tx.AddMessage("LOG_COUNTDOWN", logCountdown[i]);
			tx.AddMessage("LOG_STEP", logStep[i]);

			tx.AddMessage("LOG_SUCCESS_FINISHED", logSuccessFinished[i]);
			tx.AddMessage("ERR_CALC_FIRST", errCalcFirst[i]);
			tx.AddMessage("ERR_EMERGENCY_STOP", errEmergencyStop[i]);
			tx.AddMessage("LOG_SEARCH_PROCESS", logSearchProcess[i]);

			tx.AddMessage("LOG_WINDOW_FOCUSED", logWindowFocused[i]);
			tx.AddMessage("ERR_BLIND_MODE", errBlindMode[i]);
			tx.AddMessage("LOG_PLAN_GENERATED", logPlanGenerated[i]);
			tx.AddMessage("ERR_PARSING", logParsingError[i]);
			tx.AddMessage("ERR_RULES_MISSING_COLON", errRulesMissingColon[i]);
			tx.AddMessage("ERR_UNKNOWN_LETTER", errUnknownLetter[i]);
			tx.AddMessage("ERR_EMPTY_RULE", errEmptyRule[i]);
			tx.AddMessage("ERR_INVALID_CELL", errInvalidCell[i]);
			tx.AddMessage("ERR_INVALID_SIGN", errInvalidSign[i]);
			tx.AddMessage("KEY_MATRIX_PIN_LABEL", matrixPinLabel[i]);

			// Эта строчка у вас тоже уже была в самом конце оригинального цикла, 
			// поэтому вторую такую же из моего куска кода нужно удалить:
			TranslationServer.AddTranslation(tx);
		}
	}


	private void LoadSavedLanguage()
	{
		var config = new ConfigFile();
		// Загружаем глобальный файл настроек программы
		Error err = config.Load(SETTINGS_INI_PATH);
		string lang = "en"; // Язык по умолчанию, если файла настроек еще нет

		if (err == Error.Ok)
		{
			// Читаем сохраненный язык из секции [General]
			lang = (string)config.GetValue("General", "language", "en");
		}

		// Принудительно выставляем правильный индекс в выпадающем списке интерфейса
		switch (lang.ToLower())
		{
			case "en": _langSelector.Selected = 0; break;
			case "ru": _langSelector.Selected = 1; break;
			case "pl": _langSelector.Selected = 2; break;
			case "de": _langSelector.Selected = 3; break;
			case "ua": _langSelector.Selected = 4; break;
			default: _langSelector.Selected = 0; break;
		}

		// Переключаем системную локализацию Godot на сохраненный язык
		TranslationServer.SetLocale(lang);
		RefreshPuzzleSolverTranslations();
	}

	private void OnLanguageSelected(long index)
	{
		string lang = "en";
		switch (index)
		{
			case 0: lang = "en"; break;
			case 1: lang = "ru"; break;
			case 2: lang = "pl"; break;
			case 3: lang = "de"; break;
			case 4: lang = "ua"; break;
		}

		// 1. Мгновенно меняем язык интерфейса во всей программе
		TranslationServer.SetLocale(lang);

		// Обновляем тексты всех статических элементов UI сразу
		ApplyTranslationsToScene();
		RefreshPuzzleSolverTranslations();

		// 2. ЗАПИСЫВАЕМ ВЫБОР В SETTINGS.INI (Автосохранение)
		var config = new ConfigFile();
		config.Load(SETTINGS_INI_PATH); // Подгружаем текущие настройки (например, тайминги автокликера)
		config.SetValue("General", "language", lang); // Добавляем или перезаписываем язык
		config.Save(SETTINGS_INI_PATH); // Сохраняем файл на диск
	}

	private void ApplyTranslationsToScene()
	{
		// Left panel
		var titleLabel = GetNode<Label>("../VBoxContainer/Label");
		titleLabel.Text = TranslationServer.Translate("KEY_TITLE");

		var cellsLabel = GetNode<Label>("../VBoxContainer/HBoxContainer/Label");
		cellsLabel.Text = TranslationServer.Translate("KEY_CELLS");

		var initialLabel = GetNode<Label>("../VBoxContainer/Label3");
		initialLabel.Text = TranslationServer.Translate("KEY_INITIAL");

		var rulesHintLabel = GetNode<Label>("../VBoxContainer/HBoxContainer2/Label2");
		rulesHintLabel.Text = TranslationServer.Translate("KEY_RULES_HINT");

		var calcButton = GetNode<Button>("../VBoxContainer/Button");
		calcButton.Text = TranslationServer.Translate("KEY_CALCULATE");

		var delayLabel = GetNode<Label>("../VBoxContainer/Label4");
		delayLabel.Text = TranslationServer.Translate("KEY_DELAY");

		var autoButton = GetNode<Button>("../VBoxContainer/Button2");
		autoButton.Text = TranslationServer.Translate("KEY_AUTOPLAY");

		// Right panel
		_labelFull.Text = TranslationServer.Translate("KEY_FULL_CORRECT");
		_labelAlmost.Text = TranslationServer.Translate("KEY_ALMOST_CORRECT");

		_chestDescriptionInput.PlaceholderText = TranslationServer.Translate("KEY_CHEST_DESCRIPTION_HINT");
		_chestDescriptionInput.Text = _chestDescriptionInput.Text; // keep user text
		_contributeButton.Text = TranslationServer.Translate("KEY_CONTRIBUTE");

		_searchField.PlaceholderText = TranslationServer.Translate("KEY_SEARCH_HINT");

		// Reset buttons (ensure they reflect translations)
		_resetButton.Text = TranslationServer.Translate("KEY_RESET_CELLS");
		_resetLinksButton.Text = TranslationServer.Translate("KEY_RESET_LINKS");
	}

	private void RefreshPuzzleSolverTranslations()
	{
		var rootNode = GetTree().Root.GetChild(0) as PuzzleSolver;
		rootNode?.RefreshLinkMatrixTranslations();
	}

	private void OnHelpButtonPressed()
	{
		string currentLocale = TranslationServer.GetLocale();
		if (!_helpLoc.ContainsKey(currentLocale)) currentLocale = "en";

		// Заполняем всплывающее окно текстом на текущем выбранном языке
		_helpWindow.Title = _helpLoc[currentLocale].Item1;
		_helpWindow.DialogText = _helpLoc[currentLocale].Item2;
		_helpWindow.PopupCentered(); // Показываем окно по центру экрана
	}

	private void OnContributeButtonPressed()
	{
		string chestDescription = _chestDescriptionInput.Text.Trim();
		int cellCount = (int)_cellCountInput.Value;
		string startPositions = BuildCurrentStartPositions(cellCount);
		string rules = _rulesInput.Text.Trim();

		string url = CONTRIBUTE_FORM_URL +
			"?usp=pp_url" +
			BuildFormParameter(FORM_ENTRY_CHEST_DESCRIPTION, chestDescription) +
			BuildFormParameter(FORM_ENTRY_CELL_COUNT, cellCount.ToString()) +
			BuildFormParameter(FORM_ENTRY_START_POS, startPositions) +
			BuildFormParameter(FORM_ENTRY_RULES, rules);

		OS.ShellOpen(url);
	}

	private string BuildCurrentStartPositions(int cellCount)
	{
		List<string> values = new List<string>();
		int activeCount = Math.Clamp(cellCount, 0, _mainSpinBoxes.Length);

		for (int i = 0; i < activeCount; i++)
		{
			values.Add(((int)_mainSpinBoxes[i].Value).ToString());
		}

		return string.Join(",", values);
	}

	private string BuildFormParameter(string key, string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return "";

		return $"&{key}={Uri.EscapeDataString(value)}";
	}


	//КОНЕЦ ЛОКАЛИЗАЦИИ


	private void LoadIniDatabase()
	{
		_database.Clear();
		string globalPath = ProjectSettings.GlobalizePath(CONFIG_INI_PATH);

		if (!System.IO.File.Exists(globalPath))
		{
			GD.PrintErr("[DATABASE ERROR] chests.ini not found!");
			return;
		}

		string[] lines = System.IO.File.ReadAllLines(globalPath);

		ChestConfig currentChest = new ChestConfig();
		bool hasChest = false;

		foreach (string rawLine in lines)
		{
			// Вычищаем мусорные кавычки Windows по краям строки
			string line = rawLine.Trim();
			if (line.StartsWith("\"") && line.EndsWith("\"") && line.Length >= 2)
			{
				line = line.Substring(1, line.Length - 2).Trim();
			}

			// Убираем сдвоенные кавычки и подчищаем края от случайных остатков
			line = line.Replace("\"\"", "\"").Trim();
			line = line.Trim('"');

			if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

			// Распознаем секцию, даже если Windows вшила кавычки внутрь скобок
			if (line.StartsWith("[") && line.EndsWith("]"))
			{
				if (hasChest) _database.Add(currentChest);

				currentChest = new ChestConfig();
				currentChest.SearchTags = new List<string>();
				hasChest = true;
				continue;
			}

			if (!hasChest) continue;

			string[] kv = line.Split('=', 2);
			if (kv.Length != 2) continue;

			string key = kv[0].Trim().ToLower().Trim('"');
			string val = kv[1].Trim().Trim('"'); // ЖЕСТКАЯ ОЧИСТКА: Срезаем кавычки со значений

			switch (key)
			{
				case "name":
					currentChest.DisplayName = val;
					break;
				case "cells":
					int.TryParse(val, out currentChest.CellCount);
					break;
				case "rules":
					currentChest.Rules = val;
					break;
				case "start_pos":
					currentChest.StartPos = val;
					//string[] pos = val.Split(',');
					//foreach(string p in  pos)
					//{
					//	// Чистим каждый тег от случайных внутренних кавычек
					//	string cleanPos = p.Trim().ToLower().Trim('"');
					//	if (!string.IsNullOrWhiteSpace(cleanPos))
					//		currentChest.StartPos = cleanPos;
					//}
					break;
				case "tags":
					string[] tags = val.Split(',');
					foreach (string t in tags)
					{
						// Чистим каждый тег от случайных внутренних кавычек
						string cleanTag = t.Trim().ToLower().Trim('"');
						if (!string.IsNullOrWhiteSpace(cleanTag))
							currentChest.SearchTags.Add(cleanTag);
					}
					break;
			}
		}

		if (hasChest) _database.Add(currentChest);

		// Добавляем очищенные имена в теги для сквозного поиска
		for (int i = 0; i < _database.Count; i++)
		{
			var chest = _database[i];
			if (!string.IsNullOrEmpty(chest.DisplayName))
			{
				string cleanNameTag = chest.DisplayName.ToLower().Trim('"');
				if (!chest.SearchTags.Contains(cleanNameTag))
				{
					chest.SearchTags.Add(cleanNameTag);
				}
			}
		}

		// Автосортировка базы по выставленным начальным положениям
		_database.Sort(CompareChestConfigsByStartPos);

		GD.Print($"[DATABASE SUCCESS] Smart-loaded {_database.Count} chests from chests.ini!");
		FilterAndGenerateButtons("");
	}

	private static int CompareChestConfigsByStartPos(ChestConfig a, ChestConfig b)
	{
		int[] aPositions = ParseStartPositions(a.StartPos);
		int[] bPositions = ParseStartPositions(b.StartPos);

		int countComparison = a.CellCount.CompareTo(b.CellCount);
		if (countComparison != 0)
		{
			return countComparison;
		}

		int length = Math.Min(aPositions.Length, bPositions.Length);
		for (int i = 0; i < length; i++)
		{
			if (aPositions[i] != bPositions[i])
			{
				return aPositions[i].CompareTo(bPositions[i]);
			}
		}

		if (aPositions.Length != bPositions.Length)
		{
			return aPositions.Length.CompareTo(bPositions.Length);
		}

		return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
	}

	private static int CompareChestMatchByStartPos(ChestConfig a, ChestConfig b, int[] currentStartPositions)
	{
		int scoreA = GetStartPositionMatchScore(currentStartPositions, ParseStartPositions(a.StartPos));
		int scoreB = GetStartPositionMatchScore(currentStartPositions, ParseStartPositions(b.StartPos));

		int scoreDiff = scoreB.CompareTo(scoreA);
		if (scoreDiff != 0)
		{
			return scoreDiff;
		}

		return CompareChestConfigsByStartPos(a, b);
	}

	private static int GetStartPositionMatchScore(int[] current, int[] target)
	{
		if (current == null || target == null || current.Length == 0 || target.Length == 0)
		{
			return 0;
		}

		int score = 0;
		int maxIndex = Math.Min(current.Length, target.Length);

		for (int i = 0; i < maxIndex; i++)
		{
			if (current[i] == target[i])
			{
				score++;
			}
		}

		return score;
	}

	private int[] GetCurrentStartPositions(int cellCount)
	{
		int activeCount = Math.Clamp(cellCount, 0, _mainSpinBoxes.Length);
		int[] result = new int[activeCount];

		for (int i = 0; i < activeCount; i++)
		{
			result[i] = (int)_mainSpinBoxes[i].Value;
		}

		return result;
	}

	private static bool IsExactStartPositionMatch(int[] current, int[] target)
	{
		if (current == null || target == null)
		{
			return false;
		}

		if (current.Length != target.Length)
		{
			return false;
		}

		for (int i = 0; i < current.Length; i++)
		{
			if (current[i] != target[i])
			{
				return false;
			}
		}

		return current.Length > 0;
	}

	private static int[] ParseStartPositions(string startPos)
	{
		if (string.IsNullOrWhiteSpace(startPos))
		{
			return Array.Empty<int>();
		}

		string[] parts = startPos.Split(',');
		var result = new List<int>(parts.Length);
		foreach (string part in parts)
		{
			if (int.TryParse(part.Trim(), out int value))
			{
				result.Add(value);
			}
			else
			{
				result.Add(int.MaxValue);
			}
		}

		return result.ToArray();
	}

	private void OnSearchTextChanged(string newText)
	{
		FilterAndGenerateButtons(newText);
	}

	private void OnCellCountValueChanged(double value)
	{
		FilterAndGenerateButtons(_searchField.Text);
	}

	private void OnStartPositionValueChanged(double value)
	{
		FilterAndGenerateButtons(_searchField.Text);
	}

	private void FilterAndGenerateButtons(string query)
	{


		string cleanQuery = query.Trim().ToLower();

		int requiredCellCount = (int)_cellCountInput.Value;
		int[] currentStartPositions = GetCurrentStartPositions(requiredCellCount);
		var matchingChests = new List<ChestConfig>();

		foreach (ChestConfig chest in _database)
		{
			bool isMatch = string.IsNullOrEmpty(cleanQuery);

			if (!isMatch)
			{
				foreach (string tag in chest.SearchTags)
				{
					if (tag.Contains(cleanQuery))
					{
						isMatch = true;
						break;
					}
				}
			}

			if (requiredCellCount > 0 && chest.CellCount != requiredCellCount)
			{
				isMatch = false;
			}

			if (isMatch)
			{
				matchingChests.Add(chest);
			}
		}

		var exactMatches = new List<ChestConfig>();
		var partialMatches = new List<ChestConfig>();

		foreach (ChestConfig chest in matchingChests)
		{
			int[] chestStartPositions = ParseStartPositions(chest.StartPos);
			if (IsExactStartPositionMatch(currentStartPositions, chestStartPositions))
			{
				exactMatches.Add(chest);
			}
			else
			{
				partialMatches.Add(chest);
			}
		}

		GD.Print($"[FILTER] query='{cleanQuery}' requiredCells={requiredCellCount} matching={matchingChests.Count} exact={exactMatches.Count} partial={partialMatches.Count}");

		// Clear both grid containers
		foreach (Node child in _chestGridFull.GetChildren())
		{
			child.QueueFree();
		}
		foreach (Node child in _chestGridAlmost.GetChildren())
		{
			child.QueueFree();
		}

		exactMatches.Sort(CompareChestConfigsByStartPos);
		partialMatches.Sort((a, b) => CompareChestMatchByStartPos(a, b, currentStartPositions));

		// Update header labels: full/empty depending on existence
		if (exactMatches.Count > 0)
			_labelFull.Text = TranslationServer.Translate("KEY_FULL_CORRECT");
		else
			_labelFull.Text = TranslationServer.Translate("KEY_EMPTY_RESULTS");

		_labelAlmost.Text = TranslationServer.Translate("KEY_ALMOST_CORRECT");

		// Populate full grid with exact matches
		if (exactMatches.Count > 0)
		{
			foreach (ChestConfig chest in exactMatches)
			{
				Button btn = new Button();
				btn.Text = chest.DisplayName;
				btn.CustomMinimumSize = new Vector2(190, 50);
				btn.ClipText = true;
				btn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				btn.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.7f));

				btn.Pressed += () => OnChestButtonClicked(chest);
				_chestGridFull.AddChild(btn);
			}
		}
		else
		{
			// Optionally show an empty label inside the full grid
			Label emptyLabel = new Label();
			emptyLabel.Text = TranslationServer.Translate("KEY_EMPTY_RESULTS");
			emptyLabel.CustomMinimumSize = new Vector2(190, 30);
			emptyLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
			_chestGridFull.AddChild(emptyLabel);
		}

		// Populate almost grid with partial matches
		if (partialMatches.Count > 0)
		{
			foreach (ChestConfig chest in partialMatches)
			{
				Button btn = new Button();
				btn.Text = chest.DisplayName;
				btn.CustomMinimumSize = new Vector2(190, 50);
				btn.ClipText = true;
				btn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				btn.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.7f));

				btn.Pressed += () => OnChestButtonClicked(chest);
				_chestGridAlmost.AddChild(btn);
			}
		}
		else
		{
			Label emptyLabel2 = new Label();
			emptyLabel2.Text = TranslationServer.Translate("KEY_EMPTY_RESULTS");
			emptyLabel2.CustomMinimumSize = new Vector2(190, 30);
			emptyLabel2.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
			_chestGridAlmost.AddChild(emptyLabel2);
		}
	}

	private void OnChestButtonClicked(ChestConfig chest)
	{
		_resultLabel.RemoveThemeColorOverride("font_color");
		_resultLabel.Text = "";

		if (string.IsNullOrWhiteSpace(chest.Rules) || chest.Rules.ToLower() == "none")
		{
			_resultLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
			_resultLabel.Text = $"[DATABASE ERROR] Chest: '{chest.DisplayName}'\n" +
							   "Status: CRITICAL_MISSING_RULES\n" +
							   "Action: Calculation blocked. Manual rules entry required.";
			_rulesInput.Text = "";
			return;
		}

		_cellCountInput.Value = chest.CellCount;

		var rootNode = GetTree().Root.GetChild(0);
		rootNode.Call("UpdateVisibleSpinBoxes", chest.CellCount);

		rootNode.Call("UpdateStartPos", chest.CellCount, chest.StartPos);
		//GD.Print(chest.StartPos);

		rootNode.Call("LoadRulesFromDatabase", chest.Rules);

		// Вместо жесткого текста FULL_DATA_LOADED и INCOMPLETE DATA:
		if (!string.IsNullOrEmpty(chest.StartPos) && chest.StartPos.ToLower() != "none")
		{
			// ... (код заполнения спинбоксов) ...
			_resultLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1, 0.4f));
			_resultLabel.Text = $"[DATABASE SUCCESS] Active: '{chest.DisplayName}'\n\n{TranslationServer.Translate("DB_SUCCESS")}";
		}
		else
		{
			_resultLabel.AddThemeColorOverride("font_color", new Color(1, 0.6f, 0.2f));
			// Используем string.Format, чтобы подставить имя конкретного сундука в перевод
			string pattern = TranslationServer.Translate("DB_INCOMPLETE");
			_resultLabel.Text = string.Format(pattern, chest.DisplayName);
		}
	}
}
