namespace Voxelgine.Engine.Input;

public enum PhysicalKey
{
	Unknown = -1,
	Space = 32,
	Apostrophe = 39,
	Comma = 44,
	Minus = 45,
	Period = 46,
	Slash = 47,
	Alpha0 = 48,
	Alpha1 = 49,
	Alpha2 = 50,
	Alpha3 = 51,
	Alpha4 = 52,
	Alpha5 = 53,
	Alpha6 = 54,
	Alpha7 = 55,
	Alpha8 = 56,
	Alpha9 = 57,
	Semicolon = 59,
	Equal = 61,
	A = 65,
	B = 66,
	C = 67,
	D = 68,
	E = 69,
	F = 70,
	G = 71,
	H = 72,
	I = 73,
	J = 74,
	K = 75,
	L = 76,
	M = 77,
	N = 78,
	O = 79,
	P = 80,
	Q = 81,
	R = 82,
	S = 83,
	T = 84,
	U = 85,
	V = 86,
	W = 87,
	X = 88,
	Y = 89,
	Z = 90,
	LeftBracket = 91,
	Backslash = 92,
	RightBracket = 93,
	GraveAccent = 96,
	Escape = 256,
	Enter = 257,
	Tab = 258,
	Backspace = 259,
	Insert = 260,
	Delete = 261,
	Right = 262,
	Left = 263,
	Down = 264,
	Up = 265,
	PageUp = 266,
	PageDown = 267,
	Home = 268,
	End = 269,
	CapsLock = 280,
	ScrollLock = 281,
	NumLock = 282,
	PrintScreen = 283,
	Pause = 284,
	F1 = 290,
	F2 = 291,
	F3 = 292,
	F4 = 293,
	F5 = 294,
	F6 = 295,
	F7 = 296,
	F8 = 297,
	F9 = 298,
	F10 = 299,
	F11 = 300,
	F12 = 301,
	Numpad0 = 320,
	Numpad1 = 321,
	Numpad2 = 322,
	Numpad3 = 323,
	Numpad4 = 324,
	Numpad5 = 325,
	Numpad6 = 326,
	Numpad7 = 327,
	Numpad8 = 328,
	Numpad9 = 329,
	NumpadDecimal = 330,
	NumpadDivide = 331,
	NumpadMultiply = 332,
	NumpadSubtract = 333,
	NumpadAdd = 334,
	NumpadEnter = 335,
	NumpadEqual = 336,
	LeftShift = 340,
	LeftControl = 341,
	LeftAlt = 342,
	LeftSuper = 343,
	RightShift = 344,
	RightControl = 345,
	RightAlt = 346,
	RightSuper = 347,
	Menu = 348,
}

public enum PhysicalMouseButton
{
	Left = 0,
	Right = 1,
	Middle = 2,
	Button4 = 3,
	Button5 = 4,
	Button6 = 5,
	Button7 = 6,
	Button8 = 7,
}

public static class PhysicalInputNames
{
	private static readonly IReadOnlyDictionary<string, PhysicalKey> LegacyKeyNames =
		new Dictionary<string, PhysicalKey>(StringComparer.OrdinalIgnoreCase)
		{
			["Null"] = PhysicalKey.Unknown,
			["Zero"] = PhysicalKey.Alpha0,
			["One"] = PhysicalKey.Alpha1,
			["Two"] = PhysicalKey.Alpha2,
			["Three"] = PhysicalKey.Alpha3,
			["Four"] = PhysicalKey.Alpha4,
			["Five"] = PhysicalKey.Alpha5,
			["Six"] = PhysicalKey.Alpha6,
			["Seven"] = PhysicalKey.Alpha7,
			["Eight"] = PhysicalKey.Alpha8,
			["Nine"] = PhysicalKey.Alpha9,
			["Grave"] = PhysicalKey.GraveAccent,
			["Kp0"] = PhysicalKey.Numpad0,
			["Kp1"] = PhysicalKey.Numpad1,
			["Kp2"] = PhysicalKey.Numpad2,
			["Kp3"] = PhysicalKey.Numpad3,
			["Kp4"] = PhysicalKey.Numpad4,
			["Kp5"] = PhysicalKey.Numpad5,
			["Kp6"] = PhysicalKey.Numpad6,
			["Kp7"] = PhysicalKey.Numpad7,
			["Kp8"] = PhysicalKey.Numpad8,
			["Kp9"] = PhysicalKey.Numpad9,
			["KpDecimal"] = PhysicalKey.NumpadDecimal,
			["KpDivide"] = PhysicalKey.NumpadDivide,
			["KpMultiply"] = PhysicalKey.NumpadMultiply,
			["KpSubtract"] = PhysicalKey.NumpadSubtract,
			["KpAdd"] = PhysicalKey.NumpadAdd,
			["KpEnter"] = PhysicalKey.NumpadEnter,
			["KpEqual"] = PhysicalKey.NumpadEqual,
		};
	private static readonly IReadOnlyDictionary<string, PhysicalMouseButton> LegacyMouseButtonNames =
		new Dictionary<string, PhysicalMouseButton>(StringComparer.OrdinalIgnoreCase)
		{
			["Side"] = PhysicalMouseButton.Button4,
			["Extra"] = PhysicalMouseButton.Button5,
			["Forward"] = PhysicalMouseButton.Button6,
			["Back"] = PhysicalMouseButton.Button7,
		};

	public static bool TryParseKey(string value, out PhysicalKey key)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			key = PhysicalKey.Unknown;
			return false;
		}

		if (LegacyKeyNames.TryGetValue(value, out key))
			return true;

		return Enum.TryParse(value, true, out key);
	}

	public static bool TryParseMouseButton(string value, out PhysicalMouseButton button)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			button = PhysicalMouseButton.Left;
			return false;
		}

		if (LegacyMouseButtonNames.TryGetValue(value, out button))
			return true;

		return Enum.TryParse(value, true, out button);
	}
}
