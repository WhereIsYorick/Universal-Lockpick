using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading; // Добавлено пространство имен для токенов отмены
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;

public partial class PuzzleSolver : Control
{
	// === СЕКРЕТНЫЕ НАСТРОЙКИ (ТЕПЕРЬ ПОДГРУЖАЮТСЯ ИЗ ФАЙЛА) ===
	private const string CONFIG_FILE_NAME = "settings.ini";
	private const string GAME_PROCESS_NAME = "G1R-Win64-Shipping";

	// Переменные таймингов (больше не константы!)
	private int _switchAnimMs = 25; // Дефолтное значение, если файла нет
	private int _turnAnimMs = 25;   // Дефолтное значение, если файла нет
									 // =========================================================

	[DllImport("user32.dll", SetLastError = true)]
	private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);
	[DllImport("user32.dll")]
	private static extern bool IsIconic(IntPtr hWnd);
	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	private const int KEYEVENTF_KEYUP = 0x0002;
	private const int SW_RESTORE = 9;
	private const byte VK_W = 0x57; private const byte VK_S = 0x53;
	private const byte VK_A = 0x41; private const byte VK_D = 0x44;

	private SpinBox[] _spinBoxes;
	private HBoxContainer[] _startPositionRows;
	private CheckBox[,] _startPositionButtons;
	private bool _isSyncingStartPositionUi = false;
	private SpinBox _cellCountInput;
	private LineEdit _rulesInput;
	private Button _calcButton;
	private Button _autoplayButton;
	private SpinBox _startDelayInput;
	private ScrollContainer _scrollContainer;
	private Label _resultLabel;
	private ScrollContainer _linkMatrixScroll;
	private GridContainer _linkMatrixGrid;
	private Label _linkMatrixTitle;
	private Button[,] _linkButtons;

	private readonly string[] _names = { "A", "B", "C", "D", "E", "F", "G", "H" };
	private int[,] _dependencyMatrix = new int[8, 8];
	private int[,] _linkMatrix = new int[8, 8];
	private List<Tuple<int, int>> _cachedSteps = null;
	private bool _isSyncingRulesText = false;
	private bool _isSyncingMatrix = false;

	// === ПЕРЕМЕННЫЕ ДЛЯ ЭКСТРЕННОЙ ОСТАНОВКИ ===
	private CancellationTokenSource _cts = null;
	private bool _isAutoplayRunning = false;
	// ===========================================

	public override void _Ready()
	{
		// Инициализируем массив на 8 элементов
		_spinBoxes = new SpinBox[8];

		// ИСПРАВЛЕНО: Каждому элементу массива задан свой точный индекс [0..7]
		_spinBoxes[0] = GetNode<SpinBox>("VBoxContainer/GridContainer/A");
		_spinBoxes[1] = GetNode<SpinBox>("VBoxContainer/GridContainer/B");
		_spinBoxes[2] = GetNode<SpinBox>("VBoxContainer/GridContainer/C");
		_spinBoxes[3] = GetNode<SpinBox>("VBoxContainer/GridContainer/D");
		_spinBoxes[4] = GetNode<SpinBox>("VBoxContainer/GridContainer/E");
		_spinBoxes[5] = GetNode<SpinBox>("VBoxContainer/GridContainer/F");
		_spinBoxes[6] = GetNode<SpinBox>("VBoxContainer/GridContainer/G");
		_spinBoxes[7] = GetNode<SpinBox>("VBoxContainer/GridContainer/H");
		BuildStartPositionSelector();

		// (Остальной код инициализации кнопок, лейблов и задержек ниже остается без изменений...)
		_cellCountInput = GetNode<SpinBox>("VBoxContainer/HBoxContainer/CellCountInput");
		_rulesInput = GetNode<LineEdit>("VBoxContainer/RulesInput");
		_calcButton = GetNode<Button>("VBoxContainer/Button");
		_autoplayButton = GetNode<Button>("VBoxContainer/Button2");
		_startDelayInput = GetNode<SpinBox>("VBoxContainer/Delay");
		_scrollContainer = GetNode<ScrollContainer>("VBoxContainer/ScrollContainer");
		_resultLabel = GetNode<Label>("VBoxContainer/ScrollContainer/Label");

		_calcButton.Pressed += OnCalculatePressed;
		_autoplayButton.Pressed += OnAutoplayPressed;
		_cellCountInput.ValueChanged += OnCellCountChanged;
		_rulesInput.TextChanged += OnRulesTextChanged;

		_cellCountInput.Value = 6;
		UpdateVisibleSpinBoxes((int)_cellCountInput.Value);
		SetDefaultStartPositions();
		BuildLinkMatrixEditor();
		SetDefaultLinkMatrix((int)_cellCountInput.Value);
		SyncRulesTextFromLinkMatrix();

		// 📄 ЛОГИКА РАБОТЫ С INI-ФАЙЛОМ НАСТРОЕК
		var config = new ConfigFile();
		// Пытаемся открыть файл settings.ini в папке с приложением
		Error err = config.Load($"res://{CONFIG_FILE_NAME}");

		if (err == Error.Ok)
		{
			// Если файл успешно найден — считываем значения из секции [Timings]
			_switchAnimMs = (int)config.GetValue("Timings", "switch_cell_delay_ms", 200);
			_turnAnimMs = (int)config.GetValue("Timings", "turn_groove_delay_ms", 400);
		}
		else
		{
			// Если файла нет (первый запуск) — создаем его с дефолтными значениями
			config.SetValue("Timings", "switch_cell_delay_ms", _switchAnimMs);
			config.SetValue("Timings", "turn_groove_delay_ms", _turnAnimMs);
			config.Save($"res://{CONFIG_FILE_NAME}");
		}

		// Включаем умный перенос слов для ЛЮБЫХ сообщений (ошибок, планов, уведомлений базы)
		_resultLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

		// Разрешаем текстовому полю динамически увеличивать свою высоту при переносе строк
		_resultLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
	}

	private void BuildStartPositionSelector()
	{
		var grid = GetNode<GridContainer>("VBoxContainer/GridContainer");
		grid.Columns = 1;
		grid.AddThemeConstantOverride("h_separation", 0);
		grid.AddThemeConstantOverride("v_separation", 1);

		_startPositionRows = new HBoxContainer[8];
		_startPositionButtons = new CheckBox[8, 7];
		ButtonGroup[] groups = new ButtonGroup[8];

		for (int i = 0; i < _spinBoxes.Length; i++)
		{
			if (_spinBoxes[i] == null) continue;

			_spinBoxes[i].Visible = false;
			int capturedIndex = i;
			_spinBoxes[i].ValueChanged += _ => SyncStartPositionRowFromValue(capturedIndex);
			groups[i] = new ButtonGroup();
		}

		for (int slotIndex = _spinBoxes.Length - 1; slotIndex >= 0; slotIndex--)
		{
			HBoxContainer row = new HBoxContainer();
			row.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
			row.CustomMinimumSize = new Vector2(0, 28);
			row.AddThemeConstantOverride("separation", 1);

			Label label = new Label();
			label.Text = $"П{slotIndex + 1}";
			label.CustomMinimumSize = new Vector2(28, 0);
			label.HorizontalAlignment = HorizontalAlignment.Center;
			label.VerticalAlignment = VerticalAlignment.Center;
			row.AddChild(label);

			for (int value = 0; value <= 6; value++)
			{
				int capturedSlot = slotIndex;
				int capturedValue = value;
				CheckBox button = new CheckBox();
				button.Text = "";
				button.TooltipText = value.ToString();
				button.ButtonGroup = groups[slotIndex];
				button.CustomMinimumSize = new Vector2(28, 0);
				button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
				button.Pressed += () => SetStartPositionValue(capturedSlot, capturedValue);
				row.AddChild(button);
				_startPositionButtons[slotIndex, value] = button;
			}

			_startPositionRows[slotIndex] = row;
			grid.AddChild(row);
		}

		for (int i = 0; i < _spinBoxes.Length; i++)
			SyncStartPositionRowFromValue(i);
	}

	private void SetStartPositionValue(int slotIndex, int value)
	{
		if (_isSyncingStartPositionUi || slotIndex < 0 || slotIndex >= _spinBoxes.Length || _spinBoxes[slotIndex] == null)
			return;

		_spinBoxes[slotIndex].Value = value;
		SyncStartPositionRowFromValue(slotIndex);
	}

	private void SyncStartPositionRowFromValue(int slotIndex)
	{
		if (_startPositionButtons == null || slotIndex < 0 || slotIndex >= _spinBoxes.Length || _spinBoxes[slotIndex] == null)
			return;

		int value = Math.Clamp((int)_spinBoxes[slotIndex].Value, 0, 6);
		_isSyncingStartPositionUi = true;

		for (int i = 0; i <= 6; i++)
		{
			CheckBox button = _startPositionButtons[slotIndex, i];
			if (button != null) button.ButtonPressed = i == value;
		}

		_isSyncingStartPositionUi = false;
	}

	private void BuildLinkMatrixEditor()
	{
		var parent = GetNode<VBoxContainer>("VBoxContainer");
		int insertIndex = _rulesInput.GetIndex();

		Label title = new Label();
		title.Text = TranslationServer.Translate("KEY_LINKS_TITLE");
		title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		parent.AddChild(title);
		parent.MoveChild(title, insertIndex);
		_linkMatrixTitle = title;

		_linkMatrixScroll = new ScrollContainer();
		_linkMatrixScroll.CustomMinimumSize = new Vector2(0, 350);
		_linkMatrixScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		parent.AddChild(_linkMatrixScroll);
		parent.MoveChild(_linkMatrixScroll, insertIndex + 1);

		_linkMatrixGrid = new GridContainer();
		_linkMatrixGrid.Columns = 9;
		_linkMatrixGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_linkMatrixScroll.AddChild(_linkMatrixGrid);

		_linkButtons = new Button[8, 8];
		RebuildLinkMatrixGrid((int)_cellCountInput.Value);
	}

	private void RebuildLinkMatrixGrid(int activeCount)
	{
		if (_linkMatrixGrid == null) return;

		List<Node> oldChildren = new List<Node>();
		foreach (Node child in _linkMatrixGrid.GetChildren())
			oldChildren.Add(child);

		foreach (Node child in oldChildren)
		{
			_linkMatrixGrid.RemoveChild(child);
			child.QueueFree();
		}

		_linkMatrixGrid.Columns = activeCount + 1;
		AddMatrixHeader("");

		for (int col = 0; col < activeCount; col++)
			AddMatrixHeader($"П{col + 1}");

		for (int displayRow = activeCount - 1; displayRow >= 0; displayRow--)
		{
			int row = displayRow;
			AddMatrixHeader($"П{row + 1} ->");

			for (int col = 0; col < activeCount; col++)
			{
				int capturedRow = row;
				int capturedCol = col;
				Button btn = new Button();
				btn.CustomMinimumSize = new Vector2(78, 34);
				btn.ClipText = true;
				btn.Disabled = row == col;
				btn.Pressed += () => ToggleLinkCell(capturedRow, capturedCol);
				_linkButtons[row, col] = btn;
				_linkMatrixGrid.AddChild(btn);
			}
		}

		RefreshLinkButtons(activeCount);
	}

	private void AddMatrixHeader(string text)
	{
		Label label = new Label();
		label.Text = text;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.CustomMinimumSize = new Vector2(78, 24);
		_linkMatrixGrid.AddChild(label);
	}

	private void ToggleLinkCell(int row, int col)
	{
		if (row == col) return;

		// Cycle like the HTML version: none -> together -> opposite -> none.
		_linkMatrix[row, col] = _linkMatrix[row, col] == 0 ? 1 : (_linkMatrix[row, col] == 1 ? -1 : 0);
		RefreshLinkButtons((int)_cellCountInput.Value);
		SyncRulesTextFromLinkMatrix();
	}

	private void RefreshLinkButtons(int activeCount)
	{
		for (int row = 0; row < activeCount; row++)
		{
			for (int col = 0; col < activeCount; col++)
			{
				Button btn = _linkButtons[row, col];
				if (btn == null) continue;

				// Diagonal cells are always blocked and shown as "ВМЕСТЕ".
				int state = row == col ? 1 : _linkMatrix[row, col];
				if (state == 1)
					btn.Text = TranslationServer.Translate("KEY_LINK_TOGETHER");
				else if (state == -1)
					btn.Text = TranslationServer.Translate("KEY_LINK_OPPOSITE");
				else
					btn.Text = TranslationServer.Translate("KEY_LINK_NONE");

				btn.TooltipText = row == col
					? TranslationServer.Translate("KEY_LINK_TOOLTIP_DIAG")
					: TranslationServer.Translate("KEY_LINK_TOOLTIP_CLICK");

				btn.RemoveThemeColorOverride("font_color");
				btn.RemoveThemeColorOverride("font_disabled_color");
				if (state == 1)
				{
						btn.AddThemeColorOverride(row == col ? "font_disabled_color" : "font_color", new Color(0.45f, 1.0f, 0.55f));
				}
				else if (state == -1)
				{
					btn.AddThemeColorOverride("font_color", new Color(1.0f, 0.45f, 0.45f));
				}
			}
		}
	}

	public void RefreshLinkMatrixTranslations()
	{
		if (_linkMatrixTitle != null)
			_linkMatrixTitle.Text = TranslationServer.Translate("KEY_LINKS_TITLE");

		if (_cellCountInput != null)
			RefreshLinkButtons((int)_cellCountInput.Value);
	}

	private void SetDefaultLinkMatrix(int activeCount)
	{
		Array.Clear(_linkMatrix, 0, _linkMatrix.Length);
		for (int i = 0; i < activeCount; i++)
			_linkMatrix[i, i] = 1;

		if (activeCount == 6)
		{
			_linkMatrix[1, 3] = -1;
			_linkMatrix[2, 1] = -1;
			_linkMatrix[2, 3] = -1;
			_linkMatrix[2, 4] = -1;
			_linkMatrix[3, 4] = -1;
			_linkMatrix[4, 0] = 1;
			_linkMatrix[4, 1] = -1;
			_linkMatrix[4, 3] = -1;
			_linkMatrix[5, 3] = -1;
		}

		RefreshLinkButtons(activeCount);
	}

	public void ResetAllLinks()
	{
		// Clear all non-diagonal links to 0 ("НЕТ") and ensure diagonal stays as 1 (blocked)
		Array.Clear(_linkMatrix, 0, _linkMatrix.Length);
		int activeCount = (int)_cellCountInput.Value;
		for (int i = 0; i < activeCount; i++)
		{
			_linkMatrix[i, i] = 1;
		}
		RefreshLinkButtons(activeCount);
		SyncRulesTextFromLinkMatrix();
	}

	private void SetDefaultStartPositions()
	{
		int[] htmlPositionsConvertedToSolver = { 1, 2, 3, 5, 5, 6 };
		for (int i = 0; i < htmlPositionsConvertedToSolver.Length && i < _spinBoxes.Length; i++)
		{
			_spinBoxes[i].Value = htmlPositionsConvertedToSolver[i];
			SyncStartPositionRowFromValue(i);
		}
	}

	private void SyncRulesTextFromLinkMatrix()
	{
		if (_isSyncingMatrix) return;

		_isSyncingRulesText = true;
		int activeCount = (int)_cellCountInput.Value;
		StringBuilder sb = new StringBuilder();

		for (int row = 0; row < activeCount; row++)
		{
			List<string> targets = new List<string>();
			for (int col = 0; col < activeCount; col++)
			{
				if (row == col) continue;

				int state = _linkMatrix[row, col];
				if (state == 1) targets.Add($"{_names[col]}+");
				else if (state == -1) targets.Add($"{_names[col]}-");
			}

			if (targets.Count > 0)
			{
				if (sb.Length > 0) sb.Append("; ");
				sb.Append(_names[row]).Append(":").Append(string.Join(",", targets));
			}
		}

		_rulesInput.Text = sb.ToString();
		_isSyncingRulesText = false;
	}

	private void OnRulesTextChanged(string newText)
	{
		if (_isSyncingRulesText) return;
		SyncLinkMatrixFromRulesText(newText);
	}

	public void LoadRulesFromDatabase(string rulesText)
	{
		_rulesInput.Text = rulesText;
		SyncLinkMatrixFromRulesText(rulesText);
	}

	private void SyncLinkMatrixFromRulesText(string rulesText)
	{
		if (_linkMatrixGrid == null) return;

		_isSyncingMatrix = true;
		int activeCount = (int)_cellCountInput.Value;
		Array.Clear(_linkMatrix, 0, _linkMatrix.Length);
		for (int i = 0; i < activeCount; i++)
			_linkMatrix[i, i] = 1;

		try
		{
			string cleanText = rulesText.Replace(" ", "").ToUpper();
			string[] commands = cleanText.Split(';', StringSplitOptions.RemoveEmptyEntries);

			foreach (string cmd in commands)
			{
				string[] parts = cmd.Split(':');
				if (parts.Length != 2) continue;

				int sourceIdx = GetLetterNameIndex(parts[0]);
				if (sourceIdx < 0 || sourceIdx >= activeCount) continue;

				string[] targets = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
				foreach (string target in targets)
				{
					if (target.Length < 2) continue;

					string targetLetter = target.Substring(0, target.Length - 1);
					char sign = target[target.Length - 1];
					int targetIdx = GetLetterNameIndex(targetLetter);
					if (targetIdx < 0 || targetIdx >= activeCount || sourceIdx == targetIdx) continue;

					if (sign == '+') _linkMatrix[sourceIdx, targetIdx] = 1;
					else if (sign == '-') _linkMatrix[sourceIdx, targetIdx] = -1;
				}
			}
		}
		finally
		{
			RefreshLinkButtons(activeCount);
			_isSyncingMatrix = false;
		}
	}

	private void SimulateKeyPress(byte keyCode)
	{
		keybd_event(keyCode, 0, 0, UIntPtr.Zero);
		OS.DelayMsec(30);
		keybd_event(keyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
	}

	// Измененная логика кнопки Auto-Play (теперь она работает и как СТОП)
	private async void OnAutoplayPressed()
	{
		// 🛑 ЕСЛИ КЛИКНУЛИ ВО ВРЕМЯ РАБОТЫ — ОСТАНАВЛИВАЕМ
		if (_isAutoplayRunning)
		{
			_cts?.Cancel(); // Посылаем сигнал отмены роботу
			ResetAutoplayButtonState();
			_resultLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
			_resultLabel.Text = TranslationServer.Translate("ERR_EMERGENCY_STOP");
			return;
		}

		if (_cachedSteps == null || _cachedSteps.Count == 0)
		{
			_resultLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
			_resultLabel.Text = TranslationServer.Translate("ERR_CALC_FIRST");
			return;
		}

		// Включаем режим работы автоплея
		_isAutoplayRunning = true;
		_autoplayButton.Text = TranslationServer.Translate("KEY_STOP_AUTOPLAY");
		_autoplayButton.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f)); // Красный текст кнопки

		// Создаем новый токен отмены для этой сессии
		_cts = new CancellationTokenSource();

		try
		{
			int activeCount = (int)_cellCountInput.Value;
			int startDelayMs = (int)_startDelayInput.Value * 1000;

			_resultLabel.AddThemeColorOverride("font_color", new Color(1, 1, 0));
			string pattern = TranslationServer.Translate("LOG_SEARCH_PROCESS");
			_resultLabel.Text = string.Format(pattern, GAME_PROCESS_NAME);

			IntPtr gameHWnd = IntPtr.Zero;
			Process[] processes = Process.GetProcessesByName(GAME_PROCESS_NAME);
			if (processes.Length > 0)
			{
				Process gameProcess = processes[0]; // Фикс индекса массива
				gameHWnd = gameProcess.MainWindowHandle;

				if (gameHWnd == IntPtr.Zero)
				{
					foreach (ProcessThread thread in gameProcess.Threads)
					{
						NativeMethods.EnumThreadWindows(thread.Id, (hWnd, lParam) => {
							if (NativeMethods.IsWindowVisible(hWnd)) { gameHWnd = hWnd; return false; }
							return true;
						}, IntPtr.Zero);
						if (gameHWnd != IntPtr.Zero) break;
					}
				}
			}

			if (gameHWnd != IntPtr.Zero)
			{
				if (IsIconic(gameHWnd)) ShowWindow(gameHWnd, SW_RESTORE);
				SetForegroundWindow(gameHWnd);
				_resultLabel.Text += TranslationServer.Translate("LOG_WINDOW_FOCUSED");
			}
			else
			{
				_resultLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
				string pattern3 = TranslationServer.Translate("ERR_BLIND_MODE");
				_resultLabel.Text = string.Format(pattern3, GAME_PROCESS_NAME);
			}

			// Находим цикл отсчета секунд:
			for (int sec = startDelayMs / 1000; sec > 0; sec--)
			{
				_cts.Token.ThrowIfCancellationRequested();
				string pattern2 = TranslationServer.Translate("LOG_COUNTDOWN");
				_resultLabel.Text = string.Format(pattern2, sec); // Подставляем секунды
				await Task.Delay(1000, _cts.Token);
			}

			int currentSelectedCellIndex = 0;

			for (int i = 0; i < _cachedSteps.Count; i++)
			{
				// Проверяем отмену перед каждым физическим ходом
				_cts.Token.ThrowIfCancellationRequested();

				int targetCellIndex = _cachedSteps[i].Item1;
				int direction = _cachedSteps[i].Item2;

				// Находим строку внутри главного цикла ходов автокликера:
				_resultLabel.AddThemeColorOverride("font_color", new Color(0, 1, 1));

				string stepPattern = TranslationServer.Translate("LOG_STEP");
				// Подставляем: Номер шага, Всего шагов, Имя ячейки
				_resultLabel.Text = string.Format(stepPattern, i + 1, _cachedSteps.Count, _names[targetCellIndex]);

				// Навигация W / S
				while (currentSelectedCellIndex != targetCellIndex)
				{
					_cts.Token.ThrowIfCancellationRequested();
					if (currentSelectedCellIndex < targetCellIndex)
					{
						SimulateKeyPress(VK_W);
						currentSelectedCellIndex++;
					}
					else
					{
						SimulateKeyPress(VK_S);
						currentSelectedCellIndex--;
					}
					// Было: await Task.Delay(SWITCH_ANIM_MS, _cts.Token);
					await Task.Delay(_switchAnimMs, _cts.Token); // ИСПРАВЛЕНО
				}

				_cts.Token.ThrowIfCancellationRequested();

				// 🔄 ИСПРАВЛЕНО: Меняем полярность кнопок A и D!
				// Раньше direction == 1 (вправо) нажимал VK_D. Теперь нажимает VK_A.
				byte actionKey = direction == 1 ? VK_A : VK_D;
				SimulateKeyPress(actionKey);

				await Task.Delay(_turnAnimMs, _cts.Token);




			}

			_resultLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1, 0.4f));
			_resultLabel.Text = TranslationServer.Translate("LOG_SUCCESS_FINISHED");
			await Task.Delay(1500);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
		}
		catch (OperationCanceledException)
		{
			// Блок ловит отмену таски, предотвращая вылет приложения
		}
		finally
		{
			ResetAutoplayButtonState();
		}
	}

	private void ResetAutoplayButtonState()
	{
		_isAutoplayRunning = false;
		_autoplayButton.Text = TranslationServer.Translate("KEY_AUTOPLAY");
		_autoplayButton.RemoveThemeColorOverride("font_color"); // Возвращаем дефолтный цвет текста
		_cts?.Dispose();
		_cts = null;
	}

	// === КНОПКА CALCULATE И ОСТАЛЬНОЙ ХВОСТ КОДА ОСТАЮТСЯ БЕЗ ИЗМЕНЕНИЙ ===
	private async void OnCalculatePressed()
	{
		_cachedSteps = null;

		string parseError = ParseRulesAndReturnError(_rulesInput.Text);
		if (parseError != null)
		{
			_resultLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
			_resultLabel.Text = parseError;
			return;
		}

		int activeCount = (int)_cellCountInput.Value;
		int[] start = new int[activeCount];
		for (int i = 0; i < activeCount; i++)
		{
			start[i] = (int)_spinBoxes[i].Value;
		}

		List<Tuple<int, int>> steps = await FindSolution(start, activeCount);
		if (steps == null)
		{
			_resultLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
			// БЫЛО: _resultLabel.Text = "ERROR: No solution!";
			// СТАЛО:
			_resultLabel.Text = TranslationServer.Translate("ERR_NO_SOLUTION");
			return;
		}

		_cachedSteps = steps;
		_resultLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1, 0.4f));

		StringBuilder sb = new StringBuilder();
		string pattern4 = TranslationServer.Translate("LOG_PLAN_GENERATED");
		sb.AppendLine(string.Format(pattern4, steps.Count));

		// Находим цикл фор, который генерирует строки шагов плана:
		for (int i = 0; i < steps.Count; i++)
		{
			string dirKey = steps[i].Item2 == 1 ? "LOG_RIGHT" : "LOG_LEFT";
			string localizedDir = TranslationServer.Translate(dirKey);
			sb.AppendLine($"{i + 1}. {_names[steps[i].Item1]} -> {localizedDir}");
		}

		// Было: _resultLabel.Text = sb.ToString();
		// СТАЛО: Добавляем запас по высоте, чтобы Godot не обрезал нижние строки!
		_resultLabel.Text = sb.ToString() + "\n\n\n";
		_scrollContainer.ScrollVertical = 0;
	}


	// === ВСЕ ОСТАЛЬНЫЕ МАТЕМАТИЧЕСКИЕ МЕТОДЫ (БЕЗ ИЗМЕНЕНИЙ) ===
	private string ParseRulesAndReturnError(string rulesText)
	{
		// Полностью очищаем матрицу перед новым расчетом
		Array.Clear(_dependencyMatrix, 0, _dependencyMatrix.Length);
		if (string.IsNullOrWhiteSpace(rulesText)) return null;

		try
		{
			string cleanText = rulesText.Replace(" ", "").ToUpper();
			string[] commands = cleanText.Split(';', StringSplitOptions.RemoveEmptyEntries);

			foreach (string cmd in commands)
			{
				string[] parts = cmd.Split(':');
				if (parts.Length != 2) return $"ERROR in command '{cmd}': Missing colon ':'";

				// ИСПРАВЛЕНО: Берем parts[0] (первый элемент массива - имя ведущей кнопки)
				int sourceIdx = GetLetterNameIndex(parts[0]);
				if (sourceIdx == -1) return $"ERROR: Unknown letter '{parts[0]}'.";

				// ИСПРАВЛЕНО: Разбираем parts[1] (список ведомых ячеек через запятую)
				string[] targets = parts[1].Split(',');
				foreach (string t in targets)
				{
					if (t.Length < 2) return $"ERROR in block '{cmd}': Empty rule.";

					string targetLetter = t.Substring(0, t.Length - 1); // Буква (A, B, C...)
					char sign = t[t.Length - 1];                        // Знак (+ или -)

					int targetIdx = GetLetterNameIndex(targetLetter);
					if (targetIdx == -1) return $"ERROR: Invalid cell '{targetLetter}' in rule '{t}'.";

					if (sign == '+') _dependencyMatrix[sourceIdx, targetIdx] = 1;
					else if (sign == '-') _dependencyMatrix[sourceIdx, targetIdx] = -1;
					else return $"ERROR: Invalid sign '{sign}' in rule '{t}'.";
				}
			}
			return null; // Всё успешно распарсилось
		}
		catch (Exception ex) { return $"Parsing error: {ex.Message}"; }
	}

	private int[] ApplyClick(int[] state, int btnIndex, int direction, int activeCount)
	{
		int[] newState = (int[])state.Clone();

		// 1. Нажатая ячейка ВСЕГДА делает свой базовый шаг от клика
		newState[btnIndex] += direction;

		// 2. Применяем влияние на ВСЕ активные ячейки на основе матрицы правил
		for (int targetIdx = 0; targetIdx < activeCount; targetIdx++)
		{
			int influence = _dependencyMatrix[btnIndex, targetIdx];
			if (influence != 0)
			{
				// Сдвигаем ячейку с учетом направления клика и знака (+/-)
				newState[targetIdx] += direction * influence;
			}
		}

		// 3. Жесткая проверка упоров (0..6) для безопасности
		for (int i = 0; i < activeCount; i++)
		{
			if (newState[i] < 0 || newState[i] > 6) return null;
		}
		return newState;
	}

	private int GetLetterNameIndex(string letter)
	{
		if (letter == "A") return 0;
		if (letter == "B") return 1;
		if (letter == "C") return 2;
		if (letter == "D") return 3;
		if (letter == "E") return 4;
		if (letter == "F") return 5;
		if (letter == "G") return 6;
		if (letter == "H") return 7;
		return -1;
	}


	private async System.Threading.Tasks.Task<List<Tuple<int, int>>> FindSolution(int[] startState, int activeCount)
	{
		var queue = new Queue<Tuple<int[], List<Tuple<int, int>>>>();
		var visited = new HashSet<int>();
		int[] targetState = new int[activeCount];
		for (int i = 0; i < activeCount; i++) targetState[i] = 3;
		queue.Enqueue(new Tuple<int[], List<Tuple<int, int>>>(startState, new List<Tuple<int, int>>()));
		visited.Add(GetStateHash(startState));
		int safetyCounter = 0; int maxIterations = 500000;
		while (queue.Count > 0)
		{
			if (safetyCounter % 5000 == 0)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
			safetyCounter++; if (safetyCounter > maxIterations) break;
			var current = queue.Dequeue();
			int[] currentState = current.Item1;
			List<Tuple<int, int>> path = current.Item2;
			if (IsTargetReached(currentState, targetState, activeCount)) return path;
			for (int i = 0; i < activeCount; i++)
			{
				foreach (int dir in new int[] { 1, -1 })
				{
					int[] nextState = ApplyClick(currentState, i, dir, activeCount);
					if (nextState != null)
					{
						int stateHash = GetStateHash(nextState);
						if (!visited.Contains(stateHash))
						{
							visited.Add(stateHash);
							var newPath = new List<Tuple<int, int>>(path) { new Tuple<int, int>(i, dir) };
							queue.Enqueue(new Tuple<int[], List<Tuple<int, int>>>(nextState, newPath));
						}
					}
				}
			}
		}
		return null;
	}

	private int GetStateHash(int[] state)
	{
		int hash = 17;
		foreach (int val in state) { unchecked { hash = hash * 31 + val; } }
		return hash;
	}

	private void OnCellCountChanged(double value)
	{
		int count = (int)value;
		UpdateVisibleSpinBoxes(count);

		for (int i = 0; i < _linkMatrix.GetLength(0); i++)
			_linkMatrix[i, i] = i < count ? 1 : 0;

		RebuildLinkMatrixGrid(count);
		SyncRulesTextFromLinkMatrix();
	}

	private void UpdateVisibleSpinBoxes(int count)
	{
		for (int i = 0; i < _spinBoxes.Length; i++)
		{
			if (_spinBoxes[i] != null) _spinBoxes[i].Visible = false;
			if (_startPositionRows != null && _startPositionRows[i] != null) _startPositionRows[i].Visible = (i < count);
		}
	}

	private void UpdateStartPos(int count, string startPos)
	{
		// Защита: если строка пустая или null, сразу выходим, чтобы не плодить ошибки
		if (string.IsNullOrWhiteSpace(startPos)) return;

		string[] pos = startPos.Split(',');
		GD.Print(pos);

		// Добавляем условие i < pos.Length, чтобы цикл никогда не запрашивал несуществующие индексы
		for (int i = 0; i < _spinBoxes.Length && i < pos.Length; i++)
		{
			if (_spinBoxes[i] != null)
			{
				// Используем TryParse для максимальной надежности на случай, если пользователь введет букву вместо цифры
				if (double.TryParse(pos[i], CultureInfo.InvariantCulture, out double value))
				{
					_spinBoxes[i].Value = value;
					SyncStartPositionRowFromValue(i);
				}
			}
		}
	}

	private bool IsTargetReached(int[] state, int[] target, int count)
	{
		for (int i = 0; i < count; i++) if (state[i] != target[i]) return false;
		return true;
	}

	// Вспомогательный класс для глубокого сканирования скрытых окон Windows
	public static class NativeMethods
	{
		public delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);

		[DllImport("user32.dll")]
		public static extern bool EnumThreadWindows(int dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);

		[DllImport("user32.dll")]
		public static extern bool IsWindowVisible(IntPtr hWnd);
	}
}
