﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Myra.Graphics2D.Text;
using Myra.Graphics2D.TextureAtlases;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.ColorPicker;
using Myra.Graphics2D.UI.File;
using Myra.Graphics2D.UI.Properties;
using Myra.Graphics2D.UI.Styles;
using Myra.MiniJSON;
using MyraPad.UI;
using Myra.Utility;
using Myra;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework.Input;
using System.Xml;
using System.Text;
using System.Threading;

namespace MyraPad
{
	public class Studio : Game
	{
		private static Studio _instance;

		private readonly List<WidgetInfo> _projectInfo = new List<WidgetInfo>();

		private readonly GraphicsDeviceManager _graphicsDeviceManager;
		private readonly State _state;
		private Desktop _desktop;
		private StudioWidget _ui;
		private PropertyGrid _propertyGrid;
		private Grid _statisticsGrid;
		private TextBlock _gcMemoryLabel;
		private TextBlock _fpsLabel;
		private TextBlock _widgetsCountLabel;
		private TextBlock _drawCallsLabel;
		//		private readonly FramesPerSecondCounter _fpsCounter = new FramesPerSecondCounter();
		private string _filePath;
		private string _lastFolder;
		private bool _isDirty;
		private Project _project;
		private bool _needsCloseTag;
		private string _parentTag;
		private int? _currentTagStart, _currentTagEnd;
		private int _line, _col, _indentLevel;
		private bool _applyAutoIndent = false;
		private bool _applyAutoClose = false;
		private Project _newProject;
		private object _newObject;
		private DateTime? _refreshInitiated;
		private VerticalMenu _autoCompleteMenu = null;
		private readonly Options _options = null;

		private const string RowsProportionsName = "RowsProportions";
		private const string ColumnsProportionsName = "ColumnsProportions";
		private const string ProportionName = "Proportion";
		private const string MenuItemName = "MenuItem";
		private const string ListItemName = "ListItem";

		private static readonly string[] SimpleWidgets = new[]
		{
			"Button",
			"TextButton",
			"ImageButton",
			"RadioButton",
			"SpinButton",
			"CheckBox",
			"HorizontalProgressBar",
			"VerticalProgressBar",
			"HorizontalSeparator",
			"VerticalSeparator",
			"HorizontalSlider",
			"VerticalSlider",
			"Image",
			"TextBlock",
			"TextField",
		};

		private static readonly string[] Containers = new[]
		{
			"Window",
			"Grid",
			"Panel",
			"ScrollPane",
			"VerticalSplitPane",
			"HorizontalSplitPane"
		};

		private static readonly string[] SpecialContainers = new[]
{
			"HorizontalMenu",
			"VerticalMenu",
			"ComboBox",
			"ListBox",
			"TabControl",
		};

		public static Studio Instance
		{
			get
			{
				return _instance;
			}
		}

		public string FilePath
		{
			get { return _filePath; }

			set
			{
				if (value == _filePath)
				{
					return;
				}

				_filePath = value;

				if (!string.IsNullOrEmpty(_filePath))
				{
					// Store last folder
					try
					{
						_lastFolder = Path.GetDirectoryName(_filePath);
					}
					catch (Exception)
					{
					}
				}

				UpdateTitle();
				UpdateMenuFile();
			}
		}

		public bool IsDirty
		{
			get { return _isDirty; }

			set
			{
				if (value == _isDirty)
				{
					return;
				}

				_isDirty = value;
				UpdateTitle();
			}
		}

		public Project Project
		{
			get { return _project; }

			set
			{
				if (value == _project)
				{
					return;
				}

				_project = value;

				_ui._projectHolder.Widgets.Clear();

				if (_project != null && _project.Root != null)
				{
					_ui._projectHolder.Widgets.Add(_project.Root);
				}

				_ui._menuFileReloadStylesheet.Enabled = _project != null && !string.IsNullOrEmpty(_project.StylesheetPath);
			}
		}

		public bool ShowDebugInfo
		{
			get
			{
				return _statisticsGrid.Visible;
			}

			set
			{
				_statisticsGrid.Visible = value;
			}
		}

		private string CurrentTag
		{
			get
			{
				if (_currentTagStart == null || _currentTagEnd == null || _currentTagEnd.Value <= _currentTagStart.Value)
				{
					return null;
				}

				return _ui._textSource.Text.Substring(_currentTagStart.Value, _currentTagEnd.Value - _currentTagStart.Value + 1);
			}
		}

		public Studio()
		{
			_instance = this;

			// Restore state
			_state = State.Load();

			_graphicsDeviceManager = new GraphicsDeviceManager(this);

			if (_state != null)
			{
				_graphicsDeviceManager.PreferredBackBufferWidth = _state.Size.X;
				_graphicsDeviceManager.PreferredBackBufferHeight = _state.Size.Y;

				if (_state.UserColors != null)
				{
					for(var i = 0; i < Math.Min(ColorPickerDialog.UserColors.Length, _state.UserColors.Length); ++i)
					{
						ColorPickerDialog.UserColors[i] = new Color(_state.UserColors[i]);
					}
				}

				_lastFolder = _state.LastFolder;
				_options = _state.Options;
			}
			else
			{
				_graphicsDeviceManager.PreferredBackBufferWidth = 1280;
				_graphicsDeviceManager.PreferredBackBufferHeight = 800;
				_options = new Options();
			}
		}

		protected override void Initialize()
		{
			base.Initialize();

			IsMouseVisible = true;
			Window.AllowUserResizing = true;
		}

		protected override void LoadContent()
		{
			base.LoadContent();

			MyraEnvironment.Game = this;

			BuildUI();

			if (_state != null && !string.IsNullOrEmpty(_state.EditedFile))
			{
				Load(_state.EditedFile);
			}
		}

		public void ClosingFunction(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (_isDirty)
			{
				OnExiting();
				e.Cancel = true;
			}
		}

		public void OnExiting()
		{
			var mb = Dialog.CreateMessageBox("Quit", "There are unsaved changes. Do you want to exit without saving?");

			mb.Closed += (o, args) =>
			{
				if (mb.Result)
				{
					Exit();
				}
			};

			mb.ShowModal(_desktop);
		}

		private void BuildUI()
		{
			_desktop = new Desktop();

			_desktop.ContextMenuClosed += _desktop_ContextMenuClosed;
			_desktop.KeyDownHandler = key =>
			{
				if (_autoCompleteMenu != null &&
					(key == Keys.Up || key == Keys.Down || key == Keys.Enter))
				{
					_autoCompleteMenu.OnKeyDown(key);
				}
				else
				{
					_desktop.OnKeyDown(key);
				}
			};

			_ui = new StudioWidget();

			_ui._menuFileNew.Selected += NewItemOnClicked;
			_ui._menuFileOpen.Selected += OpenItemOnClicked;
			_ui._menuFileReload.Selected += OnMenuFileReloadSelected;
			_ui._menuFileSave.Selected += SaveItemOnClicked;
			_ui._menuFileSaveAs.Selected += SaveAsItemOnClicked;
			_ui._menuFileExportToCS.Selected += ExportCsItemOnSelected;
			_ui._menuFileReloadStylesheet.Selected += OnMenuFileReloadStylesheet;
			_ui._menuFileReloadStylesheet.Enabled = false;
			_ui._menuFileDebugOptions.Selected += DebugOptionsItemOnSelected;
			_ui._menuFileQuit.Selected += QuitItemOnDown;

			_ui._menuEditFormatSource.Selected += _menuEditFormatSource_Selected;

			_ui._menuHelpAbout.Selected += AboutItemOnClicked;

			_ui._textSource.CursorPositionChanged += _textSource_CursorPositionChanged;
			_ui._textSource.TextChanged += _textSource_TextChanged;
			_ui._textSource.KeyDown += _textSource_KeyDown;
			_ui._textSource.Char += _textSource_Char;

			_ui._textStatus.Text = string.Empty;
			_ui._textLocation.Text = "Line: 0, Column: 0, Indent: 0";

			_propertyGrid = new PropertyGrid
			{
				IgnoreCollections = true
			};
			_propertyGrid.PropertyChanged += PropertyGridOnPropertyChanged;

			_ui._propertyGridPane.Content = _propertyGrid;

			_ui._topSplitPane.SetSplitterPosition(0, _state != null ? _state.TopSplitterPosition : 0.75f);
			_ui._leftSplitPane.SetSplitterPosition(0, _state != null ? _state.LeftSplitterPosition : 0.5f);

			_desktop.Widgets.Add(_ui);

			_statisticsGrid = new Grid
			{
				Visible = false
			};

			_statisticsGrid.RowsProportions.Add(new Grid.Proportion());
			_statisticsGrid.RowsProportions.Add(new Grid.Proportion());
			_statisticsGrid.RowsProportions.Add(new Grid.Proportion());

			_gcMemoryLabel = new TextBlock
			{
				Text = "GC Memory: ",
				Font = DefaultAssets.FontSmall
			};
			_statisticsGrid.Widgets.Add(_gcMemoryLabel);

			_fpsLabel = new TextBlock
			{
				Text = "FPS: ",
				Font = DefaultAssets.FontSmall,
				GridRow = 1
			};

			_statisticsGrid.Widgets.Add(_fpsLabel);

			_widgetsCountLabel = new TextBlock
			{
				Text = "Total Widgets: ",
				Font = DefaultAssets.FontSmall,
				GridRow = 2
			};

			_statisticsGrid.Widgets.Add(_widgetsCountLabel);

			_drawCallsLabel = new TextBlock
			{
				Text = "Draw Calls: ",
				Font = DefaultAssets.FontSmall,
				GridRow = 3
			};

			_statisticsGrid.Widgets.Add(_drawCallsLabel);

			_statisticsGrid.HorizontalAlignment = HorizontalAlignment.Left;
			_statisticsGrid.VerticalAlignment = VerticalAlignment.Bottom;
			_statisticsGrid.Left = 10;
			_statisticsGrid.Top = -10;
			_desktop.Widgets.Add(_statisticsGrid);

			UpdateMenuFile();
		}

		private void _desktop_ContextMenuClosed(object sender, GenericEventArgs<Widget> e)
		{
			if (e.Data != _autoCompleteMenu)
			{
				return;
			}

			_autoCompleteMenu = null;
		}

		private void _menuEditFormatSource_Selected(object sender, EventArgs e)
		{
			try
			{
				var doc = new XmlDocument();
				doc.LoadXml(_ui._textSource.Text);

				StringBuilder sb = new StringBuilder();
				XmlWriterSettings settings = new XmlWriterSettings
				{
					Indent = _options.AutoIndent,
					IndentChars = new string(' ', _options.IndentSpacesSize),
					NewLineChars = "\n",
					NewLineHandling = NewLineHandling.Replace
				};
				using (XmlWriter writer = XmlWriter.Create(sb, settings))
				{
					doc.Save(writer);
				}

				_ui._textSource.Text = sb.ToString();
			}
			catch (Exception ex)
			{
				var messageBox = Dialog.CreateMessageBox("Error", ex.Message);
				messageBox.ShowModal(_desktop);
			}
		}

		private void _textSource_Char(object sender, GenericEventArgs<char> e)
		{
			_applyAutoClose = e.Data == '>';
		}

		private void _textSource_KeyDown(object sender, GenericEventArgs<Keys> e)
		{
			_applyAutoIndent = e.Data == Keys.Enter;
		}

		private void ApplyAutoIndent()
		{
			if (!_options.AutoIndent || _options.IndentSpacesSize <= 0 || !_applyAutoIndent)
			{
				return;
			}

			_applyAutoIndent = false;

			var text = _ui._textSource.Text;
			var pos = _ui._textSource.CursorPosition;

			if (string.IsNullOrEmpty(text) || pos == 0 || pos >= text.Length)
			{
				return;
			}

			var il = _indentLevel;
			if (pos < text.Length - 2 && text[pos] == '<' && text[pos + 1] == '/')
			{
				--il;
			}

			if (il <= 0)
			{
				return;
			}

			// Insert indent
			var indent = new string(' ', il * _options.IndentSpacesSize);
			_ui._textSource.Text = text.Substring(0, pos) + indent + text.Substring(pos);

			// Move cursor
			_ui._textSource.CursorPosition += indent.Length;
		}

		private void ApplyAutoClose()
		{
			if (!_options.AutoClose || !_applyAutoClose)
			{
				return;
			}

			_applyAutoClose = false;

			var text = _ui._textSource.Text;
			var pos = _ui._textSource.CursorPosition;

			var currentTag = CurrentTag;
			if (string.IsNullOrEmpty(currentTag) || !_needsCloseTag)
			{
				return;
			}

			var close = "</" + ExtractTag(currentTag) + ">";
			_ui._textSource.Text = text.Substring(0, pos) + close + text.Substring(pos);
		}

		private void _textSource_TextChanged(object sender, ValueChangedEventArgs<string> e)
		{
			try
			{
				IsDirty = true;

				var newLength = string.IsNullOrEmpty(e.NewValue) ? 0 : e.NewValue.Length;
				var oldLength = string.IsNullOrEmpty(e.OldValue) ? 0 : e.OldValue.Length;
				if (Math.Abs(newLength - oldLength) > 1 || _applyAutoClose)
				{
					// Refresh now
					QueueRefreshProject();
				}
				else
				{
					// Refresh after delay
					_refreshInitiated = DateTime.Now;
				}
			}
			catch (Exception)
			{
			}
		}

		private void QueueRefreshProject()
		{
			_refreshInitiated = null;
			ThreadPool.QueueUserWorkItem(RefreshProjectAsync);
		}

		private void RefreshProjectAsync(object state)
		{
			try
			{
				_ui._textStatus.Text = "Reloading...";
				_newProject = Project.LoadFromXml(_ui._textSource.Text);
				_ui._textStatus.Text = string.Empty;
			}
			catch (Exception ex)
			{
				_ui._textStatus.Text = ex.Message;
			}
		}

		private static readonly Regex TagResolver = new Regex("<([A-Za-z0-9]+)");

		private static string ExtractTag(string source)
		{
			if (string.IsNullOrEmpty(source))
			{
				return null;
			}

			return TagResolver.Match(source).Groups[1].Value;
		}

		private void UpdatePositions()
		{
			var lastStart = _currentTagStart;
			var lastEnd = _currentTagEnd;

			_line = _col = _indentLevel = 0;
			_parentTag = null;
			_currentTagStart = null;
			_currentTagEnd = null;
			_needsCloseTag = false;

			if (string.IsNullOrEmpty(_ui._textSource.Text))
			{
				return;
			}

			var cursorPos = _ui._textSource.CursorPosition;
			var text = _ui._textSource.Text;

			int? tagOpen = null;
			var isOpenTag = true;
			var length = text.Length;

			string currentTag = null;
			Stack<string> parentStack = new Stack<string>();
			for (var i = 0; i < length; ++i)
			{
				if (tagOpen == null)
				{
					if (i >= cursorPos)
					{
						break;
					}

					currentTag = null;
					_currentTagStart = null;
					_currentTagEnd = null;
				}

				if (i < cursorPos)
				{
					++_col;
				}

				var c = text[i];
				if (c == '\n')
				{
					++_line;
					_col = 0;
				}

				if (c == '<')
				{
					if (tagOpen != null && isOpenTag && i >= cursorPos + 1)
					{
						// tag is not closed
						_currentTagStart = tagOpen;
						_currentTagEnd = null;
						break;
					}

					if (i < length - 1 && text[i + 1] != '?')
					{
						tagOpen = i;
						isOpenTag = text[i + 1] != '/';
					}
				}

				if (tagOpen != null && i > tagOpen.Value && c == '>')
				{
					if (isOpenTag)
					{
						var needsCloseTag = text[i - 1] != '/';
						_needsCloseTag = needsCloseTag;

						currentTag = text.Substring(tagOpen.Value, i - tagOpen.Value + 1);
						_currentTagStart = tagOpen;
						_currentTagEnd = i;

						if (needsCloseTag && i <= cursorPos)
						{
							parentStack.Push(currentTag);
						}
					}
					else
					{
						if (parentStack.Count > 0)
						{
							parentStack.Pop();
						}
					}

					tagOpen = null;
				}
			}

			_indentLevel = parentStack.Count;
			if (parentStack.Count > 0)
			{
				_parentTag = parentStack.Pop();
			}

			_ui._textLocation.Text = string.Format("Line: {0}, Col: {1}, Indent: {2}", _line + 1, _col + 1, _indentLevel);

			if (!string.IsNullOrEmpty(_parentTag))
			{
				_parentTag = ExtractTag(_parentTag);

				_ui._textLocation.Text += ", Parent: " + _parentTag;
			}

			if ((lastStart != _currentTagStart || lastEnd != _currentTagEnd))
			{
				_propertyGrid.Object = null;
				if (!string.IsNullOrEmpty(currentTag))
				{
					var xml = currentTag;

					if (_needsCloseTag)
					{
						var tag = ExtractTag(currentTag);
						xml += "</" + tag + ">";
					}

					ThreadPool.QueueUserWorkItem(LoadObjectAsync, xml);
				}
			}

			HandleAutoComplete();
		}

		private void HandleAutoComplete()
		{
			_desktop.HideContextMenu();

			if (_currentTagStart == null || _currentTagEnd != null || string.IsNullOrEmpty(_parentTag))
			{
				return;
			}

			var cursorPos = _ui._textSource.CursorPosition;
			var text = _ui._textSource.Text;

			// Tag isn't closed
			var typed = text.Substring(_currentTagStart.Value, cursorPos - _currentTagStart.Value);
			if (typed.StartsWith("<"))
			{
				typed = typed.Substring(1);

				var all = BuildAutoCompleteVariants();

				// Filter typed
				if (!string.IsNullOrEmpty(typed))
				{
					all = (from a in all where a.StartsWith(typed, StringComparison.OrdinalIgnoreCase) select a).ToList();
				}

				if (all.Count > 0)
				{
					var lastStartPos = _currentTagStart.Value;
					var lastEndPos = cursorPos;
					// Build context menu
					_autoCompleteMenu = new VerticalMenu();
					foreach (var a in all)
					{
						var menuItem = new MenuItem
						{
							Text = a
						};

						menuItem.Selected += (s, args) =>
						{
							var result = "<" + menuItem.Text;
							var skip = result.Length;
							var needsClose = false;

							if (SimpleWidgets.Contains(menuItem.Text) ||
								menuItem.Text == ProportionName ||
								menuItem.Text == MenuItemName ||
								menuItem.Text == ListItemName)
							{
								result += "/>";
								skip += 2;
							}
							else
							{
								result += ">";
								++skip;

								if (_options.AutoIndent && _options.IndentSpacesSize > 0)
								{
									// Indent before cursor pos
									result += "\n";
									var indentSize = _options.IndentSpacesSize * (_indentLevel + 1);
									result += new string(' ', indentSize);
									skip += indentSize;

									// Indent before closing tag
									result += "\n";
									indentSize = _options.IndentSpacesSize * _indentLevel;
									result += new string(' ', indentSize);
								}
								result += "</" + menuItem.Text + ">";
								++skip;
								needsClose = true;
							}

							text = text.Substring(0, lastStartPos) + result + text.Substring(lastEndPos);
							_ui._textSource.Text = text;
							_ui._textSource.CursorPosition = lastStartPos + skip;
							if (needsClose)
							{
//								_ui._textSource.OnKeyDown(Keys.Enter);
							}
						};

						_autoCompleteMenu.Items.Add(menuItem);
					}

					var screen = _ui._textSource.CursorScreenPosition;
					screen.Y += _ui._textSource.Font.LineSpacing;

					_autoCompleteMenu.HoverIndex = 0;
					_desktop.ShowContextMenu(_autoCompleteMenu, screen);
					_refreshInitiated = null;
				}
			}
		}

		private List<string> BuildAutoCompleteVariants()
		{
			var result = new List<string>();

			if (string.IsNullOrEmpty(_parentTag))
			{
				return result;
			}

			if (_parentTag == "Project")
			{
				result.AddRange(Containers);
				result.Add("Dialog");
			}
			else if (Containers.Contains(_parentTag) || _parentTag == "Dialog")
			{
				result.AddRange(SimpleWidgets);
				result.AddRange(Containers);
				result.AddRange(SpecialContainers);
			}
			else if (_parentTag == RowsProportionsName || _parentTag == ColumnsProportionsName)
			{
				result.Add(ProportionName);
			}
			else if (_parentTag.EndsWith("Menu"))
			{
				result.Add("MenuItem");
			}
			else if (_parentTag == "ListBox" || _parentTag == "ComboBox")
			{
				result.Add("ListItem");
			}
			else if (_parentTag == "TabControl")
			{
				result.Add("TabItem");
			}

			if (_parentTag == "Grid")
			{
				result.Add(ColumnsProportionsName);
				result.Add(RowsProportionsName);
			}

			result = result.OrderBy(s => s).ToList();

			return result;
		}

		private void LoadObjectAsync(object state)
		{
			try
			{
				var xml = (string)state;
				_newObject = Project.LoadObjectFromXml(xml);
			}
			catch (Exception)
			{
			}
		}

		private void UpdateCursor()
		{
			try
			{
				UpdatePositions();
				ApplyAutoIndent();
				ApplyAutoClose();
			}
			catch (Exception)
			{
			}
		}

		private void _textSource_CursorPositionChanged(object sender, EventArgs e)
		{
			UpdateCursor();
		}

		private void OnMenuFileReloadSelected(object sender, EventArgs e)
		{
			Load(FilePath);
		}

		private static string BuildPath(string folder, string fileName)
		{
			if (Path.IsPathRooted(fileName))
			{
				return fileName;
			}

			return Path.Combine(folder, fileName);
		}

		private Stylesheet StylesheetFromFile(string path)
		{
			var data = File.ReadAllText(path);
			var root = (Dictionary<string, object>)Json.Deserialize(data);

			var folder = Path.GetDirectoryName(path);

			// Load texture atlases
			var textureAtlases = new Dictionary<string, TextureRegionAtlas>();
			Dictionary<string, object> textureAtlasesNode;
			if (root.GetStyle("textureAtlases", out textureAtlasesNode))
			{
				foreach (var pair in textureAtlasesNode)
				{
					var atlasPath = BuildPath(folder, pair.Key.ToString());
					var imagePath = BuildPath(folder, pair.Value.ToString());
					using (var stream = File.OpenRead(imagePath))
					{
						var texture = Texture2D.FromStream(GraphicsDevice, stream);

						var atlasData = File.ReadAllText(atlasPath);
						textureAtlases[pair.Key] = TextureRegionAtlas.FromJson(atlasData, texture);
					}
				}
			}

			// Load fonts
			var fonts = new Dictionary<string, SpriteFont>();
			Dictionary<string, object> fontsNode;
			if (root.GetStyle("fonts", out fontsNode))
			{
				foreach (var pair in fontsNode)
				{
					var fontPath = BuildPath(folder, pair.Value.ToString());

					var fontData = File.ReadAllText(fontPath);
					fonts[pair.Key] = SpriteFontHelper.LoadFromFnt(fontData,
						s =>
						{
							if (s.Contains("#"))
							{
								var parts = s.Split('#');

								return textureAtlases[parts[0]][parts[1]];
							}

							var imagePath = BuildPath(folder, s);
							using (var stream = File.OpenRead(imagePath))
							{
								var texture = Texture2D.FromStream(GraphicsDevice, stream);

								return new TextureRegion(texture);
							}
						});
				}
			}

			return Stylesheet.CreateFromSource(data,
				s =>
				{
					TextureRegion result;
					foreach (var pair in textureAtlases)
					{
						if (pair.Value.Regions.TryGetValue(s, out result))
						{
							return result;
						}
					}

					throw new Exception(string.Format("Could not find texture region '{0}'", s));
				},
				s =>
				{
					SpriteFont result;

					if (fonts.TryGetValue(s, out result))
					{
						return result;
					}

					throw new Exception(string.Format("Could not find font '{0}'", s));
				}
			);
		}

		private static void IterateWidget(Widget w, Action<Widget> a)
		{
			a(w);

			var children = w.GetRealChildren();

			if (children != null)
			{
				foreach (var child in children)
				{
					IterateWidget(child, a);
				}
			}
		}

		private void SetStylesheet(Stylesheet stylesheet)
		{
			if (Project.Root != null)
			{
				IterateWidget(Project.Root, w => w.ApplyStylesheet(stylesheet));
			}

			Project.Stylesheet = stylesheet;

			if (stylesheet != null && stylesheet.DesktopStyle != null)
			{
				_ui._projectHolder.Background = stylesheet.DesktopStyle.Background;
			}
			else
			{
				_ui._projectHolder.Background = null;
			}
		}

		private void LoadStylesheet(string filePath)
		{
			if (string.IsNullOrEmpty(filePath))
			{
				return;
			}

			try
			{
				if (!Path.IsPathRooted(filePath))
				{
					filePath = Path.Combine(Path.GetDirectoryName(FilePath), filePath);
				}

				var stylesheet = StylesheetFromFile(filePath);
				SetStylesheet(stylesheet);
			}
			catch (Exception ex)
			{
				var dialog = Dialog.CreateMessageBox("Error", ex.ToString());
				dialog.ShowModal(_desktop);
			}
		}

		private void OnMenuFileLoadStylesheet(object sender, EventArgs e)
		{
			var dlg = new FileDialog(FileDialogMode.OpenFile)
			{
				Filter = "*.json"
			};

			try
			{
				if (!string.IsNullOrEmpty(Project.StylesheetPath))
				{
					var stylesheetPath = Project.StylesheetPath;
					if (!Path.IsPathRooted(stylesheetPath))
					{
						// Prepend folder path
						stylesheetPath = Path.Combine(Path.GetDirectoryName(FilePath), stylesheetPath);
					}

					dlg.Folder = Path.GetDirectoryName(stylesheetPath);
				}
				else if (!string.IsNullOrEmpty(FilePath))
				{
					dlg.Folder = Path.GetDirectoryName(FilePath);
				}
			}
			catch (Exception)
			{
			}

			dlg.Closed += (s, a) =>
			{
				if (!dlg.Result)
				{
					return;
				}

				var filePath = dlg.FilePath;
				LoadStylesheet(filePath);

				// Try to make stylesheet path relative to project folder
				try
				{
					var fullPathUri = new Uri(filePath, UriKind.Absolute);

					var folderPath = Path.GetDirectoryName(FilePath);
					if (!folderPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
					{
						folderPath += Path.DirectorySeparatorChar;
					}
					var folderPathUri = new Uri(folderPath, UriKind.Absolute);

					filePath = folderPathUri.MakeRelativeUri(fullPathUri).ToString();
				}
				catch (Exception)
				{
				}

				Project.StylesheetPath = filePath;

				IsDirty = true;
			};

			dlg.ShowModal(_desktop);
		}

		private void OnMenuFileReloadStylesheet(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(Project.StylesheetPath))
			{
				return;
			}

			LoadStylesheet(Project.StylesheetPath);
		}

		private void DebugOptionsItemOnSelected(object sender1, EventArgs eventArgs)
		{
			var dlg = new DebugOptionsDialog();

			dlg.AddOption("Show debug info",
						() => { ShowDebugInfo = true; },
						() => { ShowDebugInfo = false; });

			dlg.ShowModal(_desktop);
		}

		private void ExportCsItemOnSelected(object sender1, EventArgs eventArgs)
		{
			var dlg = new ExportOptionsDialog();
			dlg.ShowModal(_desktop);
		}

		private void PropertyGridOnPropertyChanged(object sender, GenericEventArgs<string> eventArgs)
		{
			IsDirty = true;

			var xml = _project.SaveObjectToXml(_propertyGrid.Object);

			if (_needsCloseTag)
			{
				xml = xml.Replace("/>", ">");
			}

			if (_currentTagStart != null && _currentTagEnd != null)
			{
				var t = _ui._textSource.Text;

				_ui._textSource.Text = t.Substring(0, _currentTagStart.Value) + xml + t.Substring(_currentTagEnd.Value + 1);
				_currentTagEnd = _currentTagStart.Value + xml.Length - 1;
			}
		}

		private void QuitItemOnDown(object sender, EventArgs eventArgs)
		{
			var mb = Dialog.CreateMessageBox("Quit", "Are you sure?");

			mb.Closed += (o, args) =>
			{
				if (mb.Result)
				{
					Exit();
				}
			};

			mb.ShowModal(_desktop);
		}

		private void AboutItemOnClicked(object sender, EventArgs eventArgs)
		{
			var messageBox = Dialog.CreateMessageBox("About", "MyraPad " + MyraEnvironment.Version);
			messageBox.ShowModal(_desktop);
		}

		private void SaveAsItemOnClicked(object sender, EventArgs eventArgs)
		{
			Save(true);
		}

		private void SaveItemOnClicked(object sender, EventArgs eventArgs)
		{
			Save(false);
		}

		private void NewItemOnClicked(object sender, EventArgs eventArgs)
		{
			var dlg = new NewProjectWizard();

			dlg.Closed += (s, a) =>
			{
				if (!dlg.Result)
				{
					return;
				}

				var rootType = "Grid";

				if (dlg._radioButtonPanel.IsPressed)
				{
					rootType = "Panel";
				}
				else
				if (dlg._radioButtonScrollPane.IsPressed)
				{
					rootType = "ScrollPane";
				}
				else
				if (dlg._radioButtonHorizontalSplitPane.IsPressed)
				{
					rootType = "HorizontalSplitPane";
				}
				else
				if (dlg._radioButtonVerticalSplitPane.IsPressed)
				{
					rootType = "VerticalSplitPane";
				}
				else
				if (dlg._radioButtonWindow.IsPressed)
				{
					rootType = "Window";
				}
				else
				if (dlg._radioButtonDialog.IsPressed)
				{
					rootType = "Dialog";
				}

				New(rootType);
			};

			dlg.ShowModal(_desktop);
		}

		private void OpenItemOnClicked(object sender, EventArgs eventArgs)
		{
			var dlg = new FileDialog(FileDialogMode.OpenFile)
			{
				Filter = "*.xml"
			};

			if (!string.IsNullOrEmpty(FilePath))
			{
				dlg.Folder = Path.GetDirectoryName(FilePath);
			}
			else if (!string.IsNullOrEmpty(_lastFolder))
			{
				dlg.Folder = _lastFolder;
			}

			dlg.Closed += (s, a) =>
			{
				if (!dlg.Result)
				{
					return;
				}

				var filePath = dlg.FilePath;
				if (string.IsNullOrEmpty(filePath))
				{
					return;
				}

				Load(filePath);
			};

			dlg.ShowModal(_desktop);
		}

		protected override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (_refreshInitiated != null && (DateTime.Now - _refreshInitiated.Value).TotalSeconds >= 0.75f)
			{
				QueueRefreshProject();
			}

			if (_newObject != null)
			{
				_propertyGrid.Object = _newObject;
				_newObject = null;
			}

			if (_newProject != null)
			{
				Project = _newProject;
				_newProject = null;
			}
		}

		protected override void Draw(GameTime gameTime)
		{
			base.Draw(gameTime);

			_gcMemoryLabel.Text = string.Format("GC Memory: {0} kb", GC.GetTotalMemory(false) / 1024);
			//			_fpsLabel.Text = string.Format("FPS: {0}", _fpsCounter.FramesPerSecond);
			_widgetsCountLabel.Text = string.Format("Visible Widgets: {0}", _desktop.CalculateTotalWidgets(true));

			GraphicsDevice.Clear(Color.Black);

			_desktop.Bounds = new Rectangle(0, 0,
				GraphicsDevice.PresentationParameters.BackBufferWidth,
				GraphicsDevice.PresentationParameters.BackBufferHeight);
			_desktop.Render();

#if !FNA
			_drawCallsLabel.Text = string.Format("Draw Calls: {0}", GraphicsDevice.Metrics.DrawCount);
#else
			_drawCallsLabel.Text = "Draw Calls: ?";
#endif

			//			_fpsCounter.Draw(gameTime);
		}

		protected override void EndRun()
		{
			base.EndRun();

			var state = new State
			{
				Size = new Point(GraphicsDevice.PresentationParameters.BackBufferWidth,
					GraphicsDevice.PresentationParameters.BackBufferHeight),
				TopSplitterPosition = _ui._topSplitPane.GetSplitterPosition(0),
				LeftSplitterPosition = _ui._leftSplitPane.GetSplitterPosition(0),
				EditedFile = FilePath,
				LastFolder = _lastFolder,
				UserColors = (from c in ColorPickerDialog.UserColors select c.PackedValue).ToArray()
			};

			state.Save();
		}

		private void New(string rootType)
		{
			var source = Resources.NewProjectTemplate.Replace("$containerType", rootType);

			_ui._textSource.Text = source;

			var newLineCount = 0;
			var pos = 0;
			while (pos < _ui._textSource.Text.Length && newLineCount < 3)
			{
				++pos;

				if (_ui._textSource.Text[pos] == '\n')
				{
					++newLineCount;
				}
			}

			_ui._textSource.CursorPosition = pos;
			_desktop.FocusedWidget = _ui._textSource;


			FilePath = string.Empty;
			IsDirty = false;
			_ui._projectHolder.Background = null;
		}

		private void ProcessSave(string filePath)
		{
			if (string.IsNullOrEmpty(filePath))
			{
				return;
			}

			File.WriteAllText(filePath, _ui._textSource.Text);

			FilePath = filePath;
			IsDirty = false;
		}

		private void Save(bool setFileName)
		{
			if (string.IsNullOrEmpty(FilePath) || setFileName)
			{
				var dlg = new FileDialog(FileDialogMode.SaveFile)
				{
					Filter = "*.xml"
				};

				if (!string.IsNullOrEmpty(FilePath))
				{
					dlg.FilePath = FilePath;
				}
				else if (!string.IsNullOrEmpty(_lastFolder))
				{
					dlg.Folder = _lastFolder;
				}

				dlg.ShowModal(_desktop);

				dlg.Closed += (s, a) =>
				{
					if (dlg.Result)
					{
						ProcessSave(dlg.FilePath);
					}
				};
			}
			else
			{
				ProcessSave(FilePath);
			}
		}

		private void Load(string filePath)
		{
			try
			{
				var data = File.ReadAllText(filePath);

				FilePath = filePath;

				_ui._textSource.Text = data;
				_ui._textSource.CursorPosition = 0;
				UpdateCursor();
				_desktop.FocusedWidget = _ui._textSource;

				IsDirty = false;
			}
			catch (Exception ex)
			{
				var dialog = Dialog.CreateMessageBox("Error", ex.ToString());
				dialog.ShowModal(_desktop);
			}
		}

		private void UpdateTitle()
		{
			var title = string.IsNullOrEmpty(_filePath) ? "MyraPad" : _filePath;

			if (_isDirty)
			{
				title += " *";
			}

			Window.Title = title;
		}

		private void UpdateMenuFile()
		{
			_ui._menuFileReload.Enabled = !string.IsNullOrEmpty(FilePath);
		}
	}
}