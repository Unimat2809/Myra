/* Generated by Myra UI Editor at 01.08.2018 0:12:25 */
using Myra.Editor.UI.File;
using Myra.Graphics2D.UI;
using Myra.Samples.FantasyMapGenerator.Generation;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Myra.Samples.FantasyMapGenerator.UI
{
	public partial class MainForm : HorizontalSplitPane
	{
		private float[,] _heightMap;
		private float _landMinimum;
		private int _generatingCounter = 0;
		private string _lastFolder = null;
		private DateTime _generatingStamp;

		private bool IsGenerating
		{
			get
			{
				return _textGenerating.Visible;
			}
		}

		public MainForm()
		{
			BuildUI();

			_buttonGenerate.Up += OnButtonGenerate;
			_sliderWaterLand.ValueChanged += (s, a) => UpdateTextWaterLand();
			_comboViewMode.SelectedIndexChanged += (s, a) => UpdateImage();
			_buttonSaveAsPng.Up += OnButtonSaveAsPng;

			_textGenerating.Visible = false;
			SetSplitterPosition(0, 0.3f);
			_sliderWaterLand.Value = 60;
			_spinVariability.Value = 4;

			UpdateTextWaterLand();
			UpdateEnabled();
		}

		private void UpdateEnabled()
		{
			_buttonSaveAsPng.Enabled = _heightMap != null;
		}

		private void OnButtonSaveAsPng(object sender, EventArgs args)
		{
			var dlg = new FileDialog(FileDialogMode.SaveFile)
			{
				Filter = "*.png"
			};

			if (!string.IsNullOrEmpty(_lastFolder))
			{
				dlg.Folder = _lastFolder;
			}

			dlg.ShowModal(Desktop);

			dlg.Closed += (s, a) =>
			{
				if (dlg.Result)
				{
					using (var stream = File.OpenWrite(dlg.FilePath))
					{
						var texture = _imageGenerated.TextureRegion.Texture;
						texture.SaveAsPng(stream, texture.Width, texture.Height);

						_lastFolder = dlg.Folder;

						var dlg2 = Dialog.CreateMessageBox("Save As Png", string.Format("Image saved to '{0}'", dlg.FilePath));
						dlg2.ShowModal(Desktop);
					}
				}
			};
		}

		private void OnButtonGenerate(object sender, EventArgs args)
		{
			Task.Factory.StartNew(() =>
			{
				_buttonGenerate.Enabled = false;
				_imageGenerated.Visible = false;
				_textGenerating.Visible = true;
				_generatingCounter = 0;
				_generatingStamp = DateTime.Now;

				try
				{
					int size = (int)(512 * Math.Pow(2, (int)_comboSize.SelectedIndex));

					int variability = (int)_spinVariability.Value;

					var generator = new Generator();

					_heightMap = generator.Generate(size,
							variability,
							_sliderWaterLand.Value / 100.0f,
							_checkSurrondedByWater.IsPressed,
							_checkSmooth.IsPressed,
							_checkRemoveSmalIslands.IsPressed,
							_checkRemoveSmallLakes.IsPressed);

					_landMinimum = generator.LandMinimum;

					UpdateImage();
				}
				catch (Exception ex)
				{
					var dlg = Dialog.CreateMessageBox("Error", ex.ToString());
					dlg.ShowModal(Desktop);
				}
				finally
				{
					_buttonGenerate.Enabled = true;
					_imageGenerated.Visible = true;
					_textGenerating.Visible = false;
				}
			});
		}

		private void UpdateImage()
		{
			if (_heightMap == null)
			{
				return;
			}

			var mapImageBuilder = new MapImageBuilder();

			mapImageBuilder.ViewMode = (ViewMode)_comboViewMode.SelectedIndex;
			mapImageBuilder.SetData(_heightMap, _landMinimum);

			_imageGenerated.TextureRegion = mapImageBuilder.BuildImage();

			UpdateEnabled();
		}

		private void UpdateTextWaterLand()
		{
			_textWaterLand.Text = string.Format("Water/Land({0}%):", (int)_sliderWaterLand.Value);
		}

		public override void InternalRender(RenderContext batch)
		{
			base.InternalRender(batch);

			if (!IsGenerating)
			{
				return;
			}

			var elapsed = DateTime.Now - _generatingStamp;
			if (elapsed.TotalMilliseconds < 500)
			{
				return;
			}

			++_generatingCounter;
			if (_generatingCounter > 3)
			{
				_generatingCounter = 0;
			}

			var sb = new StringBuilder();

			sb.Append("Generating");
			for (var i = 0; i < _generatingCounter; ++i)
			{
				sb.Append(".");
			}

			_textGenerating.Text = sb.ToString();
			_generatingStamp = DateTime.Now;
		}
	}
}