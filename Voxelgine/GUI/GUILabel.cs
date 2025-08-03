using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.GUI {
	class GUILabel : GUIElement {
		public bool IsInput = false;
		public bool IsReading = false;
		public Action<string> OnInputFunc;

		public bool ScrollText = false;
		public float Scroll = 0;
		public float MaxScroll = 0;

		public bool ClearOnEnter = true;

		public bool CenterText = false;
		Vector2 CenterOffset = Vector2.Zero;

		public StringBuilder TextBuilder = new StringBuilder();

		string Text;
		GUIManager Mgr;

		int SelectionStart;
		int SelectionLen;
		int Cursor;

		GUIUpdateResult UpdateResult;

		public GUILabel(GUIManager Mgr) {
			this.Mgr = Mgr;
		}

		public GUILabel(GUIManager Mgr, int InputCharCount) : this(Mgr) {
			IsInput = true;
			Size = Raylib.MeasureTextEx(Mgr.TxtFont, "#", Mgr.FntSize, 1);
			Size.X = Size.X * (InputCharCount + 1);
		}

		public string GetText() {
			return Text;
		}

		public void Clear() {
			TextBuilder.Clear();
			Text = "";
			Cursor = 0;
			SelectionStart = 0;
			SelectionLen = 0;
		}

		public void Input(string Str) {
			if (string.IsNullOrEmpty(Str))
				return;

			UpdateResult = GUIUpdateResult.ConsumedInput;

			if (IsInput) {
				if (Str == "\b") {
					if (Cursor > 0 && Cursor <= TextBuilder.Length) {
						TextBuilder.Remove(Cursor - 1, 1);
						Cursor--;
					}
				} else if (Str == "\f") {
					if (Cursor > 0 && Cursor <= TextBuilder.Length - 1) {
						TextBuilder.Remove(Cursor, 1);
					}
				} else if (Str == "\n") {
					Text = TextBuilder.ToString();

					if (OnInputFunc != null)
						OnInputFunc(Text);

					if (ClearOnEnter) {
						TextBuilder.Clear();
						Cursor = 0;
						SelectionLen = 0;
						SelectionStart = 0;
					}
				} else {
					if (SelectionLen > 0) {
						TextBuilder.Remove(SelectionStart, SelectionLen);
						TextBuilder.Insert(SelectionStart, Str);
						Cursor = SelectionStart + Str.Length;
						SelectionLen = 0;
					} else {
						TextBuilder.Insert(Cursor, Str);
						Cursor += Str.Length;
					}
				}

				Text = TextBuilder.ToString();
			} else {
				Text = Text + Str;
			}
		}

		public void WriteLine(string Str) {
			Input(Str + "\n");
		}

		public void WriteLine(string Fmt, params object[] Args) {
			WriteLine(string.Format(Fmt, Args));
		}

		bool InSelection = false;

		public void BeginSelection() {
			InSelection = true;

			if (Cursor == SelectionStart + SelectionLen) {
			} else if (Cursor == SelectionStart) {
			} else {
				SelectionStart = Cursor;
				SelectionLen = 0;
			}
		}

		public void EndSelection() {
			InSelection = false;
		}

		public bool IsInSelection() {
			return InSelection;
		}

		public void MoveCursor(int Move) {
			Cursor = Cursor + Move;

			if (Cursor > Text.Length) {
				Cursor = Text.Length;
				Move = 0;
			}

			if (Cursor < 0) {
				Cursor = 0;
				Move = 0;
			}

			if (InSelection) {

				if (Cursor - Move == SelectionStart + SelectionLen) {
					SelectionLen = Cursor - SelectionStart;
				} else if (Cursor - Move == SelectionStart) {
					SelectionStart = SelectionStart + Move;
					SelectionLen = SelectionLen - Move;
				}

				if (SelectionLen <= 0) {
					SelectionLen = 0;
					EndSelection();
				}
			}
		}

		bool CheckSelectionBounds() {
			if (SelectionLen > 0 && SelectionStart >= 0 && (SelectionStart + SelectionLen) <= Text.Length)
				return true;

			return false;
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			if (Text == null)
				Text = "";

			Mgr.DrawWindowBorder(Pos, Size);

			//Mgr.DrawRectLines(Pos, Size, Color.Pink);
			ScissorManager.BeginScissor(Pos.X, Pos.Y, Size.X, Size.Y);

			Color TextColor = new Color(225, 225, 225, 255);

			if (Hovered && !MouseDown)
				TextColor = new Color(200, 200, 200, 255);
			else if (Hovered && MouseDown)
				TextColor = Color.White;


			Vector2 Padding = new Vector2(5, 0);
			Vector2 Max = Vector2.Zero;
			Vector2 CharSize = Raylib.MeasureTextEx(Mgr.TxtFont, "#", Mgr.FntSize, 1);

			if (IsInput) {
				// Draw background
				Color BgColor = new Color(0, 0, 0, 100);
				if (Hovered)
					BgColor = new Color(0, 0, 0, 130);

				Raylib.DrawRectanglePro(new Rectangle(Pos, new Vector2(Size.X, CharSize.Y)), Vector2.Zero, 0, BgColor);

				// Draw text
				Mgr.DrawText(Text, Pos + Padding, TextColor);
				Vector2 TxtSz = Raylib.MeasureTextEx(Mgr.TxtFont, Text, Mgr.FntSize, 1);

				if (IsReading) {
					// Draw cursor
					if (Cursor != Text.Length)
						TxtSz = Raylib.MeasureTextEx(Mgr.TxtFont, Text.Substring(0, Cursor), Mgr.FntSize, 1);

					float CursorWidth = CharSize.X * 0.8f;
					float CursorSize = 2;
					float CursorOffset = -3;
					Rectangle CursorRect = new Rectangle(Pos + new Vector2(Padding.X + TxtSz.X, CharSize.Y - CursorSize + CursorOffset), new Vector2(CursorWidth, CursorSize));
					Raylib.DrawRectanglePro(CursorRect, Vector2.Zero, 0, Color.White);
				}

				// Draw selection
				if (CheckSelectionBounds()) {
					TxtSz = Raylib.MeasureTextEx(Mgr.TxtFont, Text.Substring(0, SelectionStart), Mgr.FntSize, 1);
					Vector2 TxtSz2 = Raylib.MeasureTextEx(Mgr.TxtFont, Text.Substring(SelectionStart, SelectionLen), Mgr.FntSize, 1);

					Vector2 SelectionPos = Pos + new Vector2(TxtSz.X + Padding.X, 0);
					Vector2 SelectionSize = new Vector2(TxtSz2.X, CharSize.Y);
					Raylib.DrawRectanglePro(new Rectangle(SelectionPos, SelectionSize), Vector2.Zero, 0, new Color(100, 100, 180, 80));
				}

			} else {
				string[] Lines = Text.Split('\n').ToArray();
				int Idx = -1;

				// Draw background
				Color BgColor = new Color(0, 0, 0, 70);
				if (Hovered)
					BgColor = new Color(0, 0, 0, 90);

				Raylib.DrawRectanglePro(new Rectangle(Pos, Size), Vector2.Zero, 0, BgColor);

				string LastChar = "";
				Vector2 LastCharPos = Vector2.Zero;
				Vector2 CharPos = Padding;
				Vector2 LastChrSz = Vector2.Zero;

				for (int l = 0; l < Lines.Length; l++) {
					string Line = Lines[l];

					for (int i = 0; i < Line.Length; i++) {
						string C = Line[i].ToString();
						Idx++;


						LastChrSz = Raylib.MeasureTextEx(Mgr.TxtFont, C, Mgr.FntSize, 1);
						Raylib.DrawTextEx(Mgr.TxtFont, C, Pos + CharPos + CenterOffset, Mgr.FntSize, 1, TextColor);

						if (Idx >= SelectionStart && Idx < SelectionStart + SelectionLen) {
							Raylib.DrawRectanglePro(new Rectangle(Pos + CharPos + CenterOffset, LastChrSz), Vector2.Zero, 0, new Color(100, 100, 180, 80));
						}

						Max.X = MathF.Max(CharPos.X + LastChrSz.X + Padding.X, Max.X);
						Max.Y = MathF.Max(CharPos.Y + LastChrSz.Y, Max.Y);

						CharPos = CharPos + new Vector2(LastChrSz.X, 0);
						LastCharPos = CharPos;
						LastChar = C;
					}

					CharPos.X = Padding.X;
					CharPos.Y = CharPos.Y + LastChrSz.Y;
				}
			}

			if (CenterText) {
				Vector2 TxtSize = Max;
				Vector2 Dif = Size - TxtSize;
				CenterOffset = Dif / 2;

				//Raylib.DrawRectangleLinesEx(new Rectangle(Pos + CenterOffset, TxtSize), 1, Color.Lime);
			} else if (ScrollText) {
				float ScrollMax = Max.Y - Size.Y;
				MaxScroll = ScrollMax;

				if (ScrollMax < 0)
					ScrollMax = 0;
				else {
					float Perc = 1.0f / (Max.Y / Size.Y);
					float Perc2 = Max.Y / Size.Y;
					float SBHeight = Perc * Size.Y;
					float SBWidth = 12;

					float MoveAmt = Size.Y - SBHeight;
					float MovePerc = 1 - ((ScrollMax - Scroll) / ScrollMax);
					MoveAmt = MoveAmt * MovePerc;

					Raylib.DrawRectangleRec(new Rectangle(Pos.X + Size.X - SBWidth, Pos.Y + Size.Y - SBHeight - MoveAmt, SBWidth, SBHeight), new Color(0, 0, 0, 80));
				}

				Scroll = Math.Clamp(Scroll, 0, ScrollMax);
				CenterOffset = new Vector2(0, -ScrollMax + Scroll);
			} else {
				CenterOffset = Vector2.Zero;
			}

			ScissorManager.EndScissor();
		}

		public override GUIUpdateResult Update() {
			int KeyPressed = 0;
			UpdateResult = GUIUpdateResult.OK;

			if (IsReading)
				UpdateResult = GUIUpdateResult.ConsumedInput;

			if (IsReading) {
				do {
					KeyPressed = Raylib.GetCharPressed();
					if (KeyPressed == 0)
						break;

					char K = (char)KeyPressed;
					//Console.WriteLine("{0} - '{1}'", KeyPressed, K);

					Input(K.ToString());
				} while (KeyPressed != 0);
			}

			if ((Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl)) && IsReading) {
				if (Raylib.IsKeyPressed(KeyboardKey.A)) {
					SelectionStart = 0;
					SelectionLen = Text.Length;
					Cursor = SelectionLen;
				} else if (Raylib.IsKeyPressed(KeyboardKey.C)) {
					if (CheckSelectionBounds()) {
						string CopyTxt = Text.Substring(SelectionStart, SelectionLen);
						//Console.WriteLine("Copy: '{0}'", CopyTxt);
						Program.Clipb.SetText(CopyTxt);
					}
				} else if (Raylib.IsKeyPressed(KeyboardKey.V)) {
					string Txt = Program.Clipb.GetText();

					if (Txt != null) {
						foreach (char C in Txt) {
							Input(C.ToString());
						}
					}
				} else {
					if (!IsInSelection())
						BeginSelection();
				}
			} else {
				if (IsInSelection())
					EndSelection();
			}

			if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && IsReading) {
				Input("\b");
			}

			if ((Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressedRepeat(KeyboardKey.Delete)) && IsReading) {
				Input("\f");
			}

			if ((Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) && IsReading) {
				Input("\n");
			}

			if ((Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressedRepeat(KeyboardKey.Left)) && IsReading) {
				MoveCursor(-1);
			}

			if ((Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressedRepeat(KeyboardKey.Right)) && IsReading) {
				MoveCursor(1);
			}

			if (Raylib.IsKeyPressed(KeyboardKey.Home) && IsReading) {
				MoveCursor(-9999);
			}

			if (Raylib.IsKeyPressed(KeyboardKey.End) && IsReading) {
				MoveCursor(9999);
			}

			if ((Raylib.IsKeyPressed(KeyboardKey.PageUp) || Raylib.IsKeyPressedRepeat(KeyboardKey.PageUp)) && !IsReading) {
				Scroll = Scroll + 64;
			}

			if ((Raylib.IsKeyPressed(KeyboardKey.PageDown) || Raylib.IsKeyPressedRepeat(KeyboardKey.PageDown)) && !IsReading) {
				Scroll = Scroll - 64;
			}

			if ((Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressedRepeat(KeyboardKey.Up)) && !IsReading) {
				Scroll = Scroll + 16;
			}

			if ((Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressedRepeat(KeyboardKey.Down)) && !IsReading) {
				Scroll = Scroll - 16;
			}

			if (base.Update() == GUIUpdateResult.ConsumedInput)
				UpdateResult = GUIUpdateResult.ConsumedInput;

			return UpdateResult;
		}
	}
}
