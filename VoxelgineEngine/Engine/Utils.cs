using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine
{
	public delegate bool RaycastCallbackFunc(int X, int Y, int Z, Vector3 FaceNormal);
	public delegate bool Raycast2CallbackFunc(Vector3 Pos, Vector3 FaceNormal);

	public static partial class Utils
	{
		public static Random Rnd = new Random();
		static Dictionary<int, Vector3[]> SphereDirections = new Dictionary<int, Vector3[]>();

		public static readonly Vector3[] MainDirs = new[] { new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1) };
		public static readonly Vector3[] VoxelSphere8 = GenerateVoxelSphere(8, true);

		public static float ParseFloat(this string Str, float Default)
		{
			if (string.IsNullOrWhiteSpace(Str))
				return Default;

			return Str.ParseFloat();
		}

		public static float ParseFloat(this string Str)
		{
			return float.Parse(Str, CultureInfo.InvariantCulture);
		}

		public static int ParseInt(this string Str, int Default)
		{
			if (string.IsNullOrWhiteSpace(Str))
				return Default;

			return Str.ParseInt();
		}

		public static int ParseInt(this string Str)
		{
			return int.Parse(Str, CultureInfo.InvariantCulture);
		}

		public static int Mod(int A, int B)
		{
			if (B <= 0)
				throw new ArgumentOutOfRangeException(nameof(B), "The modulus must be positive.");
			int remainder = A % B;
			return remainder < 0 ? remainder + B : remainder;
		}

		public static int FloorDiv(int value, int divisor)
		{
			if (divisor <= 0)
				throw new ArgumentOutOfRangeException(nameof(divisor), "The divisor must be positive.");
			int quotient = Math.DivRem(value, divisor, out int remainder);
			return remainder < 0 ? quotient - 1 : quotient;
		}

		public static void Swap<T>(ref T A, ref T B)
		{
			T Tmp = A;
			A = B;
			B = Tmp;
		}

		public static float Clamp(float Num, float Min, float Max)
		{
			if (Num < Min)
				return Min;

			if (Num > Max)
				return Max;

			return Num;
		}

		public static int Clamp(int Num, int Min, int Max)
		{
			if (Num < Min)
				return Min;

			if (Num > Max)
				return Max;

			return Num;
		}

		public static int Random(int InclusiveMin, int ExclusiveMax)
		{
			return Rnd.Next(InclusiveMin, ExclusiveMax);
		}

		/// <summary>
		/// Returns a random unit vector (normalized, uniformly distributed on sphere surface).
		/// </summary>
		public static Vector3 GetRandomUnitVector()
		{
			// Use spherical coordinates for uniform distribution
			float theta = System.Random.Shared.NextSingle() * 2f * MathF.PI;
			float phi = MathF.Acos(2f * System.Random.Shared.NextSingle() - 1f);

			float sinPhi = MathF.Sin(phi);
			return new Vector3(
				sinPhi * MathF.Cos(theta),
				sinPhi * MathF.Sin(theta),
				MathF.Cos(phi)
			);
		}

		public static void Deconstruct(Quaternion Q, out float Pitch, out float Yaw, out float Roll)
		{
			GetEulerAngles(Q, out Pitch, out Yaw, out Roll);
		}

		public static void GetEulerAngles(Quaternion Quat, out float Pitch, out float Yaw, out float Roll)
		{
			double SqW = Quat.W * Quat.W;
			double SqX = Quat.X * Quat.X;
			double SqY = Quat.Y * Quat.Y;
			double SqZ = Quat.Z * Quat.Z;
			double Test = Quat.X * Quat.Y + Quat.Z * Quat.W;

			if (Test > 0.49999)  // Singularity at north pole
				Yaw = (float)(2 * Math.Atan2(Quat.X, Quat.W));
			else if (Test < -0.49999)  // Singularity at south pole
				Yaw = (float)(-2 * Math.Atan2(Quat.X, Quat.W));
			else
				Yaw = (float)Math.Atan2(2 * Quat.Y * Quat.W - 2 * Quat.X * Quat.Z, SqX - SqY - SqZ + SqW);

			Yaw *= (float)(180.0 / Math.PI);
			if (Yaw < 0)
				Yaw += 360;

			Pitch = (float)-Math.Atan2(2.0 * Quat.X * Quat.W + 2.0 * Quat.Y * Quat.Z, 1.0 - 2.0 * (SqZ + SqW));
			Pitch *= (float)(180.0 / Math.PI);

			if (Yaw > 270 || Yaw < 90)
				if (Pitch < 0)
					Pitch += 180;
				else
					Pitch -= 180;

			// TODO: Stop, drop and ROLL baby
			Roll = 0;
		}

		public static byte DirToByte(Vector3 Normal)
		{
			int X = (int)Normal.X;
			int Y = (int)Normal.Y;
			int Z = (int)Normal.Z;

			if (X == -1)
				return 0;

			if (X == 1)
				return 1;

			if (Y == -1)
				return 2;

			if (Y == 1)
				return 3;

			if (Z == -1)
				return 4;

			if (Z == 1)
				return 5;

			throw new Exception("Invalid direction");
		}

		public static double NormalizeLoop(double Val, double Start, double End)
		{
			double Width = End - Start;
			double OffsetVal = Val - Start;
			return (OffsetVal - (Math.Floor(OffsetVal / Width) * Width)) + Start;
		}

		public static Vector3 EulerBetweenVectors(Vector3 A, Vector3 B)
		{
			Vector3 NormalA = Vector3.Normalize(B - A);
			Vector3 NormalB = Vector3.Normalize(new Vector3(A.X, 0, A.Z));

			Vector3 Axis = Vector3.Normalize(Vector3.Cross(NormalA, NormalB));

			double Angle = Math.Acos(Vector3.Dot(NormalA, NormalB));



			//Console.WriteLine(Angle);

			return ToEuler(Axis.X, Axis.Y, Axis.Z, Angle) * (180.0f / (float)Math.PI);
		}

		public static Vector3 ToEuler(double X, double Y, double Z, double Angle)
		{
			double s = Math.Sin(Angle);
			double c = Math.Cos(Angle);
			double t = 1 - c;

			double Yaw;
			double Pitch;
			double Roll;

			if ((X * Y * t + Z * s) > 0.998)
			{ // north pole singularity detected
				Yaw = 2 * Math.Atan2(X * Math.Sin(Angle / 2), Math.Cos(Angle / 2));
				Pitch = Math.PI / 2;
				Roll = 0;
				return new Vector3((float)Yaw, (float)Pitch, (float)Roll);
			}
			if ((X * Y * t + Z * s) < -0.998)
			{ // south pole singularity detected
				Yaw = -2 * Math.Atan2(X * Math.Sin(Angle / 2), Math.Cos(Angle / 2));
				Pitch = -Math.PI / 2;
				Roll = 0;
				return new Vector3((float)Yaw, (float)Pitch, (float)Roll);
			}
			Yaw = Math.Atan2(Y * s - X * Z * t, 1 - (Y * Y + Z * Z) * t);
			Pitch = Math.Asin(X * Y * t + Z * s);
			Roll = Math.Atan2(X * s - Y * Z * t, 1 - (X * X + Z * Z) * t);

			return new Vector3((float)Yaw, (float)Pitch, (float)Roll);
		}

		static bool IsOnVoxelSphereEdge(float Radius, int X, int Y, int Z)
		{
			float Dist = X * X + Y * Y + Z * Z;
			return Dist <= (Radius * Radius) && Dist >= ((Radius - 1) * (Radius - 1));
		}

		static bool IsInsideVoxelSphere(float Radius, int X, int Y, int Z)
		{
			float Dist = X * X + Y * Y + Z * Z;
			return Dist <= (Radius * Radius);
		}

		public static Vector3[] GenerateVoxelSphere(int Radius, bool Hollow)
		{
			List<Vector3> Voxels = new List<Vector3>();
			int Diameter = Radius * 2;

			for (int x = 0; x < Diameter; x++)
				for (int y = 0; y < Diameter; y++)
					for (int z = 0; z < Diameter; z++)
					{
						if (Hollow)
						{
							if (IsOnVoxelSphereEdge(Radius, x - Radius, y - Radius, z - Radius))
								Voxels.Add(new Vector3(x - Radius, y - Radius, z - Radius));
						}
						else
						{
							if (IsInsideVoxelSphere(Radius, x - Radius, y - Radius, z - Radius))
								Voxels.Add(new Vector3(x - Radius, y - Radius, z - Radius));
						}
					}

			return Voxels.ToArray();
		}

		static Vector3[] CalculateSphereDirections(int Slices)
		{
			if (SphereDirections.ContainsKey(Slices))
				return SphereDirections[Slices];

			List<Vector3> Directions = new List<Vector3>();

			double da = Math.PI / (Slices - 1);
			double a = -0.5 * Math.PI;
			//int Hits = 0;

			for (int ia = 0; ia < Slices; ia++, a += da)
			{
				double r = Math.Cos(a);
				int nb = (int)Math.Ceiling(2.0 * Math.PI * r / da);
				double db = 2.0 * Math.PI / (nb);

				if ((ia == 0) || (ia == Slices - 1))
				{
					nb = 1;
					db = 0.0;
				}

				double b = 0;
				for (int ib = 0; ib < nb; ib++, b += db)
				{
					float x = (float)(r * Math.Cos(b));
					float y = (float)(r * Math.Sin(b));
					float z = (float)Math.Sin(a);

					Vector3 Normal = Vector3.Normalize(new Vector3(x, y, z));

					Directions.Add(Normal);
				}
			}

			Vector3[] DirectionsArray = Directions.ToArray();
			SphereDirections.Add(Slices, DirectionsArray);
			return DirectionsArray;
		}

		public static string ToString(float F)
		{
			string Str = string.Format(CultureInfo.InvariantCulture, "{0}", F);

			if (Str.Contains("."))
				Str = Str + "f";

			return Str;
		}

		public static string ToString(Vector3 V)
		{
			return string.Format(CultureInfo.InvariantCulture, "new Vector3({0}, {1}, {2})", ToString(V.X), ToString(V.Y), ToString(V.Z));
		}

		public static string ToString(Vector2 V)
		{
			return string.Format(CultureInfo.InvariantCulture, "new Vector2({0}, {1})", ToString(V.X), ToString(V.Y));
		}

		public static T Random<T>(this IEnumerable<T> Collection)
		{
			ArgumentNullException.ThrowIfNull(Collection);
			T[] Elements = Collection.ToArray();
			if (Elements.Length == 0)
				throw new InvalidOperationException("Cannot select a random element from an empty sequence.");
			return Elements[Random(0, Elements.Length)];
		}

		public static float ToDeg(float Rad)
		{
			return (float)((Rad * 180) / Math.PI);
		}

		public static float ToRad(float Deg)
		{
			return (float)(Deg * (Math.PI / 180));
		}

		public static IEnumerable<Vector2> ToVec2(float[] F)
		{
			ArgumentNullException.ThrowIfNull(F);
			if ((F.Length & 1) != 0)
				throw new ArgumentException("A Vector2 sequence requires an even number of values.", nameof(F));
			for (int i = 0; i < F.Length; i += 2)
			{
				yield return new Vector2(F[i + 0], F[i + 1]);
			}
		}

		public static Vector3 ToVec3(float[] F)
		{
			ArgumentNullException.ThrowIfNull(F);
			if (F.Length != 3)
				throw new ArgumentException("A Vector3 requires exactly three values.", nameof(F));
			return new Vector3(F[0], F[1], F[2]);
		}

		public static Vector3 ProjectOnPlane(Vector3 Vec, Vector3 Normal, float Eps)
		{
			if (Vec.X == 0 && Vec.Y == 0 && Normal.X == 0 && Normal.Y == 0)
				return Vec;

			if (Vec.X == 0 && Vec.Z == 0 && Normal.X == 0 && Normal.Z == 0)
				return Vec;

			if (Vec.Z == 0 && Vec.Y == 0 && Normal.Z == 0 && Normal.Y == 0)
				return Vec;

			float SqrMag = Vector3.Dot(Normal, Normal);

			if (SqrMag < Eps)
			{
				return Vec;
			}
			else
			{
				float Dot = Vector3.Dot(Vec, Normal);
				return new Vector3(Vec.X - Normal.X * Dot / SqrMag, Vec.Y - Normal.Y * Dot / SqrMag, Vec.Z - Normal.Z * Dot / SqrMag);
			}
		}

		// If F = 0, then it's A, as F approaches 1, then it returns lerp towards B.
		// If < 0, return A, if > 1, return B.
		public static Vector3 Lerp(Vector3 A, Vector3 B, float F)
		{
			if (F <= 0)
				return A;
			if (F >= 1)
				return B;
			return new Vector3(
				A.X + (B.X - A.X) * F,
				A.Y + (B.Y - A.Y) * F,
				A.Z + (B.Z - A.Z) * F
			);
		}

		public static Quaternion Lerp(Quaternion A, Quaternion B, float F)
		{
			if (F <= 0)
				return A;
			if (F >= 1)
				return B;
			// Linear interpolation, not normalized
			Quaternion result = new Quaternion(
				A.X + (B.X - A.X) * F,
				A.Y + (B.Y - A.Y) * F,
				A.Z + (B.Z - A.Z) * F,
				A.W + (B.W - A.W) * F
			);
			// Normalize to avoid drift
			return Quaternion.Normalize(result);
		}

		public static void RestartGame()
		{
			string[] Args = Environment.GetCommandLineArgs().Skip(1).ToArray();
			string FileName = Process.GetCurrentProcess().MainModule.FileName;
			Process.Start(FileName, Args);
			Environment.Exit(0);
		}

		// Helper to normalize a Vector4 plane (only xyz part)
		public static Vector4 NormalizePlane(Vector4 plane)
		{
			Vector3 normal = new Vector3(plane.X, plane.Y, plane.Z);
			float length = normal.Length();
			if (!float.IsFinite(length) || length <= float.Epsilon)
				throw new ArgumentException("A plane must have a finite, non-zero normal.", nameof(plane));
			return plane / length;
		}

		public static string GetOSName()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return "Windows";
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return "Linux";
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return "MacOS";
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
				return "FreeBSD";

			return RuntimeInformation.OSDescription;
		}
	}
}
