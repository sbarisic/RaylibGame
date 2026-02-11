using FishUI.Controls;
using Microsoft.Extensions.Hosting;
using MoonSharp.Interpreter.CoreLib;
using Raylib_cs;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TextCopy;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.States;

namespace Voxelgine
{
	class FEngineRunner : IFishEngineRunner
	{
		public FishDI DI { get; set; }

		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }

		public MainMenuStateFishUI MainMenuState { get; set; }
		public NPCPreviewState NPCPreviewState { get; set; }
		public MPClientGameState MultiplayerGameState { get; set; }

		//public IFishLogging Logging { get; set; }

		public FEngineRunner()
		{
		}

		public void Init()
		{
		}
	}

	internal class Program
	{
		static void Main(string[] args)
		{
			FishDI FDI = new FishDI();
			FDI.AddSingleton<IFishEngineRunner, FEngineRunner>();
			FDI.AddSingleton<IFishConfig, GameConfig>();
			FDI.AddSingleton<IClipboard, Clipboard>();
			FDI.AddSingleton<ILerpManager, LerpManager>();
			FDI.AddSingleton<IGameWindow, GameWindow>();
			FDI.AddSingleton<IFishDebug, Engine.Debug>();
			FDI.AddSingleton<IFishLogging, FishLogging>();

			IHost Host = FDI.Build();
			FDI.CreateScope();
			IFishEngineRunner Eng = FDI.GetRequiredService<IFishEngineRunner>();
			Eng.DI = FDI;
			//Host.Run();

			//Cfg = new GameConfig();
			GameConfig Cfg = FDI.GetRequiredService<GameConfig>();
			Cfg.LoadFromJson();

			IFishLogging Logging = FDI.GetRequiredService<IFishLogging>();
			Logging.Init();

			Logging.WriteLine("Aurora Falls - Voxelgine Engine");
			Logging.WriteLine($"Running on {Utils.GetOSName()}");

			// Set logging on static classes
			ResMgr.Logging = Logging;
			CustomModel.Logging = Logging;

			//Window = new GameWindow(Cfg.WindowWidth, Cfg.WindowHeight, Cfg.Title);

			IGameWindow Window = FDI.GetRequiredService<IGameWindow>();
			ResMgr.InitResources();
			ResMgr.InitHotReload();

			List<Texture2D> TexList = new List<Texture2D>();
			for (int i = 1; i < 12; i++)
			{
				TexList.Add(ResMgr.GetTexture($"smoke/{i}.png"));
			}
			ResMgr.CreateCollection("smoke", TexList.ToArray());

			// Fire particle textures
			List<Texture2D> FireTexList = new List<Texture2D>();
			for (int i = 1; i <= 4; i++)
			{
				FireTexList.Add(ResMgr.GetTexture($"fire/{i}.png"));
			}
			ResMgr.CreateCollection("fire", FireTexList.ToArray());

			// Blood particle textures
			List<Texture2D> BloodTexList = new List<Texture2D>();
			for (int i = 1; i <= 4; i++)
			{
				BloodTexList.Add(ResMgr.GetTexture($"blood/{i}.png"));
			}
			ResMgr.CreateCollection("blood", BloodTexList.ToArray());

			// Spark particle textures
			List<Texture2D> SparkTexList = new List<Texture2D>();
			for (int i = 1; i <= 4; i++)
			{
				SparkTexList.Add(ResMgr.GetTexture($"spark/{i}.png"));
			}
			ResMgr.CreateCollection("spark", SparkTexList.ToArray());

			GraphicsUtils.Init(Eng.DI.GetRequiredService<IFishLogging>());
			//Scripting.Init();

			Eng.DebugMode = Debugger.IsAttached;
			Eng.MainMenuState = new MainMenuStateFishUI(Window, Eng);
			Eng.NPCPreviewState = new NPCPreviewState(Window, Eng);
			Eng.MultiplayerGameState = new MPClientGameState(Window, Eng);

			Window.SetState(Eng.MainMenuState);
			Eng.Init();


			Stopwatch SWatch = Stopwatch.StartNew();
			const float MaxFrameTime = 0.25f;

			float Time = 0;

			float DeltaTime = 0.015f;//0.038f;  //float DeltaTime = 0.015f; // 66.6 update ticks per second
									 //float DeltaTime = 0.04f; // 25 updates per second
									 //float DeltaTime = 0.2f; // 5 updates per second

			float Accumulator = 0;
			float CurrentTime = 0;

			GameFrameInfo LastFrame = new GameFrameInfo();
			Rlgl.EnableBackfaceCulling();

			ILerpManager LerpMgr = FDI.GetRequiredService<ILerpManager>();

			while (Window.IsOpen())
			{
				Eng.TotalTime = (float)Raylib.GetTime();
				ResMgr.HandleHotReload();

				float NewTime = (float)SWatch.Elapsed.TotalSeconds;
				float FrameTime = NewTime - CurrentTime;
				if (FrameTime > MaxFrameTime)
				{
					FrameTime = MaxFrameTime;
				}

				CurrentTime = NewTime;
				Accumulator += FrameTime;
				int Updates = 0;

				Window.Tick(NewTime);

				while (Accumulator >= DeltaTime)
				{
					// PreviousState = CurrentState;

					// Update
					LerpMgr.Update(DeltaTime);
					Window.UpdateLockstep(Time, DeltaTime);
					Updates++;

					Time += DeltaTime;
					Accumulator -= DeltaTime;
				}

				float TimeAlpha = Accumulator / DeltaTime;

				// Interpolation between physics frames for smooth rendering
				Eng.ChunkDrawCalls = 0;
				LastFrame = Window.Draw(TimeAlpha, LastFrame);
			}
		}
	}
}
