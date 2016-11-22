﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mesen.GUI.Debugger.Controls;
using Mesen.GUI.Config;

namespace Mesen.GUI.Debugger
{
	public partial class ctrlDebuggerCode : BaseScrollableTextboxUserControl
	{
		public delegate void AddressEventHandler(AddressEventArgs args);
		public event AddressEventHandler OnWatchAdded;
		public event AddressEventHandler OnSetNextStatement;
		private DebugViewInfo _config;

		public ctrlDebuggerCode()
		{
			InitializeComponent();
		}

		public void SetConfig(DebugViewInfo config)
		{
			_config = config;
			this.mnuShowLineNotes.Checked = config.ShowPrgAddresses;
			this.mnuShowCodeNotes.Checked = config.ShowByteCode;
			this.FontSize = config.FontSize;

			this.ctrlCodeViewer.ShowLineNumberNotes = this.mnuShowLineNotes.Checked;
			this.ctrlCodeViewer.ShowContentNotes = this.mnuShowCodeNotes.Checked;
		}

		private void UpdateConfig()
		{
			_config.ShowPrgAddresses = this.mnuShowLineNotes.Checked;
			_config.ShowByteCode = this.mnuShowCodeNotes.Checked;
			_config.FontSize = this.FontSize;
			ConfigManager.ApplyChanges();
		}

		public override float FontSize
		{
			get { return base.FontSize; }
			set { base.FontSize=value; UpdateConfig(); }
		}

		protected override ctrlScrollableTextbox ScrollableTextbox
		{
			get { return this.ctrlCodeViewer; }
		}

		private string _code;
		private bool _codeChanged;
		public string Code
		{
			get { return _code; }
			set
			{
				if(value != _code) {
					_codeChanged = true;
					_code = value;
					UpdateCode();
				}
			}
		}

		private UInt32? _currentActiveAddress = null;
		public void SelectActiveAddress(UInt32 address)
		{
			this.SetActiveAddress(address);
			this.ctrlCodeViewer.ScrollToLineNumber((int)address);
		}

		public void SetActiveAddress(UInt32 address)
		{
			//Set line background to yellow
			this.ctrlCodeViewer.ClearLineStyles();
			this.ctrlCodeViewer.SetLineColor((int)address, Color.Black, Color.Yellow, null, LineSymbol.Arrow);
			_currentActiveAddress = address;
		}
		
		public void ClearActiveAddress()
		{
			_currentActiveAddress = null;
		}

		public bool UpdateCode(bool forceUpdate = false)
		{
			if(_codeChanged || forceUpdate) {
				System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
				sw.Start();
				this.ctrlCodeViewer.ClearLineStyles();

				List<int> lineNumbers = new List<int>();
				List<string> lineNumberNotes = new List<string>();
				List<string> codeNotes = new List<string>();
				List<string> codeLines = new List<string>();
				
				string[] lines = _code.Split('\n');
				for(int i = 0, len = lines.Length - 1; i < len; i++) {
					string line = lines[i];
					string[] lineParts = line.Split('\x1');
					
					if(lineParts.Length >= 4) {
						lineNumbers.Add(ParseHexAddress(lineParts[0]));
						lineNumberNotes.Add(lineParts[1]);
						codeNotes.Add(lineParts[2]);
						codeLines.Add(lineParts[3]);
					}
				}

				ctrlCodeViewer.TextLines = codeLines.ToArray();
				ctrlCodeViewer.LineNumbers = lineNumbers.ToArray();
				ctrlCodeViewer.TextLineNotes = codeNotes.ToArray();
				ctrlCodeViewer.LineNumberNotes = lineNumberNotes.ToArray();
				sw.Stop();
				_codeChanged = false;
				return true;
			}
			return false;
		}

		private int ParseHexAddress(string hexAddress)
		{
			if(string.IsNullOrWhiteSpace(hexAddress)) {
				return -1;
			} else {
				return (int)UInt32.Parse(hexAddress, System.Globalization.NumberStyles.AllowHexSpecifier);
			}
		}

		public void HighlightBreakpoints()
		{
			ctrlCodeViewer.ClearLineStyles();
			if(_currentActiveAddress.HasValue) {
				SetActiveAddress(_currentActiveAddress.Value);
			}
			foreach(Breakpoint breakpoint in BreakpointManager.Breakpoints) {
				Color? fgColor = Color.White;
				Color? bgColor = null;
				Color? outlineColor = Color.FromArgb(140, 40, 40);
				LineSymbol symbol;
				if(breakpoint.Enabled) {
					bgColor = Color.FromArgb(140, 40, 40);
					symbol = LineSymbol.Circle;
				} else {
					fgColor = Color.Black;
					symbol = LineSymbol.CircleOutline;
				}
				if(breakpoint.Address == (UInt32)(_currentActiveAddress.HasValue ? (int)_currentActiveAddress.Value : -1)) {
					fgColor = Color.Black;
					bgColor = Color.Yellow;
					symbol |= LineSymbol.Arrow;
				}

				ctrlCodeViewer.SetLineColor((int)breakpoint.Address, fgColor, bgColor, outlineColor, symbol);
			}
		}

		#region Events
		private Point _previousLocation;
		private void ctrlCodeViewer_MouseMove(object sender, MouseEventArgs e)
		{
			if(e.Location.X < this.ctrlCodeViewer.CodeMargin / 5) {
				this.ContextMenuStrip = contextMenuMargin;
			} else {
				this.ContextMenuStrip = contextMenuCode;
			}

			if(_previousLocation != e.Location) {
				string word = GetWordUnderLocation(e.Location);
				if(word.StartsWith("$")) {
					UInt32 address = UInt32.Parse(word.Substring(1), System.Globalization.NumberStyles.AllowHexSpecifier);
					Byte memoryValue = InteropEmu.DebugGetMemoryValue(address);
					string valueText = "$" + memoryValue.ToString("X");
					toolTip.Show(valueText, ctrlCodeViewer, e.Location.X + 5, e.Location.Y - 20, 3000);
				} else {
					CodeLabel label = LabelManager.GetLabel(word);

					if(label == null) {
						toolTip.Hide(ctrlCodeViewer);
					} else {
						Byte memoryValue = InteropEmu.DebugGetMemoryValue(label.Address);
						toolTip.Show(
							"Label: " + label.Label + Environment.NewLine + 
							"Address: $" + InteropEmu.DebugGetRelativeAddress(label.Address).ToString("X4") + Environment.NewLine + 
							"Value: $" + memoryValue.ToString("X2") + Environment.NewLine +
							"Comment: " + (label.Comment.Contains(Environment.NewLine) ? (Environment.NewLine + label.Comment) : label.Comment)
						, ctrlCodeViewer, e.Location.X + 5, e.Location.Y - 60 - label.Comment.Split('\n').Length * 14, 3000);
					}
				}
				_previousLocation = e.Location;
			}
		}

		UInt32 _lastClickedAddress = UInt32.MaxValue;
		private void ctrlCodeViewer_MouseUp(object sender, MouseEventArgs e)
		{
			string word = GetWordUnderLocation(e.Location);
			if(word.StartsWith("$")) {
				_lastClickedAddress = UInt32.Parse(word.Substring(1), System.Globalization.NumberStyles.AllowHexSpecifier);
				mnuGoToLocation.Enabled = true;
				mnuGoToLocation.Text = "Go to Location (" + word + ")";

				mnuAddToWatch.Enabled = true;
				mnuAddToWatch.Text = "Add to Watch (" + word + ")";
			} else {
				mnuGoToLocation.Enabled = false;
				mnuGoToLocation.Text = "Go to Location";
				mnuAddToWatch.Enabled = false;
				mnuAddToWatch.Text = "Add to Watch";
			}
		}

		Breakpoint _lineBreakpoint = null;
		private void ctrlCodeViewer_MouseDown(object sender, MouseEventArgs e)
		{
			int address = ctrlCodeViewer.GetLineNumberAtPosition(e.Y);
			_lineBreakpoint = BreakpointManager.GetMatchingBreakpoint(address);

			if(e.Location.X < this.ctrlCodeViewer.CodeMargin / 5) {
				if(e.Button == System.Windows.Forms.MouseButtons.Left) {
					if(_lineBreakpoint == null) {
						Breakpoint bp = new Breakpoint();
						bp.Address = (UInt32)address;
						bp.BreakOnExec = true;
						BreakpointManager.AddBreakpoint(bp);
					} else {
						BreakpointManager.RemoveBreakpoint(_lineBreakpoint);
					}
				}
			}
		}
		
		private void ctrlCodeViewer_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			int address = ctrlCodeViewer.GetLineNumberAtPosition(e.Y);

			if(address >= 0 && e.Location.X > this.ctrlCodeViewer.CodeMargin / 2 && e.Location.X < this.ctrlCodeViewer.CodeMargin) {
				UInt32 absoluteAddr = (UInt32)InteropEmu.DebugGetAbsoluteAddress((UInt32)address);
				CodeLabel existingLabel = LabelManager.GetLabel(absoluteAddr);
				CodeLabel newLabel = new CodeLabel() { Label = existingLabel?.Label, Comment = existingLabel?.Comment };

				frmEditLabel frm = new frmEditLabel(absoluteAddr, newLabel);
				if(frm.ShowDialog() == DialogResult.OK) {
					if(string.IsNullOrWhiteSpace(newLabel.Label) && string.IsNullOrWhiteSpace(newLabel.Comment)) {
						LabelManager.DeleteLabel(absoluteAddr);
					} else {
						LabelManager.SetLabel(absoluteAddr, newLabel.Label, newLabel.Comment);
					}
				}
			}
		}

		#region Context Menu

		private void contextMenuMargin_Opening(object sender, CancelEventArgs e)
		{
			if(_lineBreakpoint == null) {
				e.Cancel = true;
			} else {
				mnuDisableBreakpoint.Text = _lineBreakpoint.Enabled ? "Disable breakpoint" : "Enable breakpoint";
			}
		}

		private void mnuRemoveBreakpoint_Click(object sender, EventArgs e)
		{
			BreakpointManager.RemoveBreakpoint(_lineBreakpoint);
		}

		private void mnuEditBreakpoint_Click(object sender, EventArgs e)
		{
			BreakpointManager.EditBreakpoint(_lineBreakpoint);
		}

		private void mnuDisableBreakpoint_Click(object sender, EventArgs e)
		{
			_lineBreakpoint.SetEnabled(!_lineBreakpoint.Enabled);
		}

		private void contextMenuCode_Opening(object sender, CancelEventArgs e)
		{
			mnuShowNextStatement.Enabled = _currentActiveAddress.HasValue;
			mnuSetNextStatement.Enabled = _currentActiveAddress.HasValue;
		}

		private void mnuShowNextStatement_Click(object sender, EventArgs e)
		{
			this.ctrlCodeViewer.ScrollToLineNumber((int)_currentActiveAddress.Value);
		}

		private void mnuShowOnlyDisassembledCode_Click(object sender, EventArgs e)
		{
			UpdateCode(true);
			if(_currentActiveAddress.HasValue) {
				SelectActiveAddress(_currentActiveAddress.Value);
			}
			this.UpdateConfig();
		}
		
		private void mnuShowLineNotes_Click(object sender, EventArgs e)
		{
			this.ctrlCodeViewer.ShowLineNumberNotes = this.mnuShowLineNotes.Checked;
			this.UpdateConfig();
		}

		private void mnuShowCodeNotes_Click(object sender, EventArgs e)
		{
			this.ctrlCodeViewer.ShowContentNotes = this.mnuShowCodeNotes.Checked;
			this.UpdateConfig();
		}
		
		private void mnuGoToLocation_Click(object sender, EventArgs e)
		{
			this.ctrlCodeViewer.ScrollToLineNumber((int)_lastClickedAddress);
		}

		private void mnuAddToWatch_Click(object sender, EventArgs e)
		{
			if(this.OnWatchAdded != null) {
				this.OnWatchAdded(new AddressEventArgs() { Address = _lastClickedAddress});
			}
		}

		private void mnuSetNextStatement_Click(object sender, EventArgs e)
		{
			if(this.OnSetNextStatement != null) {
				this.OnSetNextStatement(new AddressEventArgs() { Address = (UInt32)this.ctrlCodeViewer.CurrentLine });
			}
		}

		private void ctrlCodeViewer_FontSizeChanged(object sender, EventArgs e)
		{
			UpdateConfig();
		}

		#endregion

		#endregion
	}

	public class AddressEventArgs : EventArgs
	{
		public UInt32 Address { get; set; }
	}
}
