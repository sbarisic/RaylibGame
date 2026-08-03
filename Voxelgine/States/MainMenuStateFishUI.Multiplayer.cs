using FishUI.Controls;
using System.Diagnostics;
using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;
using Voxelgine.WorldGeneration;

using Thread = System.Threading.Thread;

namespace Voxelgine.States;

public partial class MainMenuStateFishUI
{
	private NumericUpDown connectPortInput;
	private Textbox connectAddressInput;
	private Textbox connectNameInput;
	private Label connectStatusLabel;
	private NumericUpDown hostPortInput;
	private Textbox hostNameInput;
	private Textbox hostSeedInput;
	private Textbox hostPlanInput;
	private Button hostPlanBrowseButton;
	private CheckBox forceNewWorldCheckBox;
	private Label hostStatusLabel;
	private Button hostButton;

	private void CreateConnectWindow()
	{
		connectWindow = CreateModalWindow("Join Game", new Vector2(430, 330));
		connectAddressInput = new Textbox
		{
			ID = "connect_address",
			Text = "127.0.0.1",
			Size = new Vector2(245, 28),
		};
		AddDialogRow(connectWindow, 18, "Server Address", connectAddressInput);

		connectPortInput = CreatePortInput("connect_port");
		AddDialogRow(connectWindow, 64, "Port", connectPortInput);

		connectNameInput = new Textbox
		{
			ID = "connect_name",
			Text = "Player",
			Size = new Vector2(245, 28),
		};
		AddDialogRow(connectWindow, 110, "Player Name", connectNameInput);

		connectStatusLabel = new Label
		{
			ID = "connect_status",
			Position = new Vector2(22, 166),
			Size = new Vector2(376, 36),
			Alignment = Align.Center,
		};
		connectWindow.AddChild(connectStatusLabel);

		var connectButton = new Button
		{
			ID = "connect_submit",
			Text = "Connect",
			Position = new Vector2(80, 220),
			Size = new Vector2(125, 42),
		};
		connectButton.OnButtonPressed += (_, _, _) => ConnectToServer();
		connectWindow.AddChild(connectButton);

		var cancelButton = new Button
		{
			ID = "connect_cancel",
			Text = "Cancel",
			Position = new Vector2(225, 220),
			Size = new Vector2(125, 42),
		};
		cancelButton.OnButtonPressed += (_, _, _) => HideModal(connectWindow);
		connectWindow.AddChild(cancelButton);
	}

	private void CreateHostWindow()
	{
		hostWindow = CreateModalWindow("Host Game", new Vector2(430, 438));
		hostPortInput = CreatePortInput("host_port");
		AddDialogRow(hostWindow, 18, "Port", hostPortInput);

		hostNameInput = new Textbox
		{
			ID = "host_name",
			Text = "Player",
			Size = new Vector2(245, 28),
		};
		AddDialogRow(hostWindow, 64, "Player Name", hostNameInput);

		hostSeedInput = new Textbox
		{
			ID = "host_seed",
			Text = "666",
			TooltipText = "Leave blank to generate a random seed",
			Size = new Vector2(245, 28),
		};
		AddDialogRow(hostWindow, 110, "World Seed", hostSeedInput);

		hostPlanInput = new Textbox
		{
			ID = "host_world_plan",
			Text = string.Empty,
			TooltipText = "Optional exported world-plan directory; disables seed generation",
			Size = new Vector2(176, 28),
		};
		hostPlanInput.OnTextChanged += (_, text) => hostSeedInput.Disabled = !string.IsNullOrWhiteSpace(text) || hostedServerStartupPending;
		AddDialogRow(hostWindow, 156, "World Plan", hostPlanInput);
		hostPlanBrowseButton = new()
		{
			ID = "host_world_plan_browse",
			Text = "Browse",
			Position = new Vector2(337, 156),
			Size = new Vector2(61, 28),
			TooltipText = "Choose a world-plan manifest",
		};
		hostPlanBrowseButton.OnButtonPressed += (_, _, _) => ShowHostPlanPicker();
		hostWindow.AddChild(hostPlanBrowseButton);

		forceNewWorldCheckBox = new CheckBox("Force New World")
		{
			ID = "host_force_new_world",
			Position = new Vector2(145, 208),
			Size = new Vector2(22, 22),
			TooltipText = "Regenerate the world even when an existing save is present",
		};
		hostWindow.AddChild(forceNewWorldCheckBox);

		hostStatusLabel = new Label
		{
			ID = "host_status",
			Position = new Vector2(22, 256),
			Size = new Vector2(376, 36),
			Alignment = Align.Center,
		};
		hostWindow.AddChild(hostStatusLabel);

		hostButton = new Button
		{
			ID = "host_submit",
			Text = "Host",
			Position = new Vector2(80, 316),
			Size = new Vector2(125, 42),
		};
		hostButton.OnButtonPressed += (_, _, _) => StartHostedGame();
		hostWindow.AddChild(hostButton);

		var cancelButton = new Button
		{
			ID = "host_cancel",
			Text = "Cancel",
			Position = new Vector2(225, 316),
			Size = new Vector2(125, 42),
		};
		cancelButton.OnButtonPressed += (_, _, _) => CancelHostedServerStartup();
		hostWindow.AddChild(cancelButton);
	}

	private void ShowHostPlanPicker()
	{
		string initialDirectory = Directory.Exists(hostPlanInput.Text) ? hostPlanInput.Text : runtimePaths.Root;
		FilePickerDialog picker = new(FilePickerMode.Open, gui.UI.FileSystem, initialDirectory, WorldPlanBundle.ManifestFileName)
		{
			Title = "Select World Plan",
		};
		picker.OnFileConfirmed += (_, selectedPath) =>
		{
			if (!string.Equals(Path.GetFileName(selectedPath), WorldPlanBundle.ManifestFileName, StringComparison.OrdinalIgnoreCase))
			{
				hostStatusLabel.Text = "Select a world-plan manifest.json file";
				return;
			}
			hostPlanInput.Text = Path.GetDirectoryName(Path.GetFullPath(selectedPath)) ?? string.Empty;
		};
		picker.Show(gui.UI);
	}

	internal static NumericUpDown CreatePortInput(string id)
	{
		return new NumericUpDown(7777, 1, 65535, 1)
		{
			ID = id,
			Size = new Vector2(245, 28),
			DecimalPlaces = 0,
		};
	}

	private static void AddDialogRow(Window window, float y, string labelText, Control input)
	{
		window.AddChild(new Label
		{
			Text = labelText,
			Position = new Vector2(22, y),
			Size = new Vector2(120, 28),
		});
		input.Position = new Vector2(153, y);
		window.AddChild(input);
	}

	private void ShowConnectWindow()
	{
		connectStatusLabel.Text = string.Empty;
		ShowModal(connectWindow);
	}

	private void ShowHostWindow()
	{
		hostStatusLabel.Text = string.Empty;
		ShowModal(hostWindow);
	}

	private void ShowDeveloperWindow()
	{
		ShowModal(developerWindow);
	}

	private void ConnectToServer()
	{
		string address = connectAddressInput.Text?.Trim() ?? string.Empty;
		string playerName = NormalizePlayerName(connectNameInput.Text);
		if (string.IsNullOrWhiteSpace(address))
		{
			connectStatusLabel.Text = "Server address is required";
			return;
		}

		int port = (int)connectPortInput.Value;
		HideModal(connectWindow);
		Client.Connect(address, port, playerName);
	}

	private void StartHostedGame()
	{
		string worldPlan = hostPlanInput.Text?.Trim() ?? string.Empty;
		string seedText = hostSeedInput.Text?.Trim() ?? string.Empty;
		int seed;
		if (worldPlan.Length != 0)
		{
			if (!Directory.Exists(worldPlan))
			{
				hostStatusLabel.Text = "World plan directory was not found";
				return;
			}
			seed = 666;
		}
		else if (seedText.Length == 0)
		{
			seed = Random.Shared.Next();
		}
		else if (!int.TryParse(seedText, out seed))
		{
			hostStatusLabel.Text = "World seed must be a 32-bit integer";
			return;
		}

		int port = (int)hostPortInput.Value;
		string playerName = NormalizePlayerName(hostNameInput.Text);
		bool forceNewWorld = forceNewWorldCheckBox.IsChecked;
		StopHostedServer();
		if (Client.HostedServer != null)
		{
			hostStatusLabel.Text = "The previous hosted server is still stopping";
			return;
		}

		hostStatusLabel.Text = forceNewWorld
			? "Generating world..."
			: "Loading world...";

		try
		{
			ServerApplication server = Client.StartHostedServer(port, seed, forceNewWorld, worldPlan.Length == 0 ? null : worldPlan);
			pendingHostPort = port;
			pendingHostPlayerName = playerName;
			hostedServerStartupPending = true;
			hostedServerStartupTimestamp = Stopwatch.GetTimestamp();
			SetHostControlsStarting(true);
		}
		catch (Exception exception)
		{
			hostStatusLabel.Text = $"Could not start server: {exception.Message}";
			StopHostedServer();
		}
	}

	private void UpdateHostedServerStartup()
	{
		if (!hostedServerStartupPending || Client.HostedServer == null)
			return;

		var startupTask = Client.HostedServer.StartupTask;
		if (!startupTask.IsCompleted)
		{
			TimeSpan elapsed = Stopwatch.GetElapsedTime(hostedServerStartupTimestamp);
			hostStatusLabel.Text = $"Preparing world... {elapsed.TotalSeconds:F0}s";
			return;
		}

		hostedServerStartupPending = false;
		SetHostControlsStarting(false);
		if (startupTask.IsCompletedSuccessfully)
		{
			int port = pendingHostPort;
			string playerName = pendingHostPlayerName;
			logging.Log(
				GameLogLevel.Info,
				"HostedServer",
				$"Hosted server ready port={port} startupMs={Stopwatch.GetElapsedTime(hostedServerStartupTimestamp).TotalMilliseconds:F0}"
			);
			HideModal(hostWindow);

			Client.Connect("127.0.0.1", port, playerName);
			return;
		}

		Exception startupError = startupTask.Exception?.GetBaseException();
		hostStatusLabel.Text = startupTask.IsCanceled
			? "Server startup cancelled"
			: $"Could not start server: {startupError?.Message ?? "Unknown error"}";
		StopHostedServer();
	}

	private void CancelHostedServerStartup()
	{
		if (hostedServerStartupPending)
		{
			StopHostedServer();
			hostStatusLabel.Text = "Server startup cancelled";
		}

		HideModal(hostWindow);
	}

	private void SetHostControlsStarting(bool starting)
	{
		if (hostWindow == null)
			return;

		hostPortInput.Disabled = starting;
		hostNameInput.Disabled = starting;
		hostSeedInput.Disabled = starting;
		hostPlanInput.Disabled = starting;
		hostPlanBrowseButton.Disabled = starting;
		if (!starting) hostSeedInput.Disabled = !string.IsNullOrWhiteSpace(hostPlanInput.Text);
		forceNewWorldCheckBox.Disabled = starting;
		hostButton.Disabled = starting;
		hostWindow.CloseButtonEnabled = !starting;
	}

	private static string NormalizePlayerName(string value)
	{
		string playerName = value?.Trim();
		return string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName;
	}
}
