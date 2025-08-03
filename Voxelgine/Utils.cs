using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine {
	delegate bool RaycastCallbackFunc(int X, int Y, int Z, Vector3 FaceNormal);
	delegate bool Raycast2CallbackFunc(Vector3 Pos, Vector3 FaceNormal);

	static class Utils {
		public static Random Rnd = new Random();
		static Dictionary<int, Vector3[]> SphereDirections = new Dictionary<int, Vector3[]>();

		public static readonly Vector3[] MainDirs = new[] { new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1) };
		public static readonly Vector3[] VoxelSphere8 = GenerateVoxelSphere(8, true);

		public static float ParseFloat(this string Str, float Default) {
			if (string.IsNullOrWhiteSpace(Str))
				return Default;

			return Str.ParseFloat();
		}

		public static float ParseFloat(this string Str) {
			return float.Parse(Str, CultureInfo.InvariantCulture);
		}

		public static int ParseInt(this string Str, int Default) {
			if (string.IsNullOrWhiteSpace(Str))
				return Default;

			return Str.ParseInt();
		}

		public static int ParseInt(this string Str) {
			return int.Parse(Str, CultureInfo.InvariantCulture);
		}

		public static int Mod(int A, int B) {
			return A - B * (int)Math.Floor((float)A / (float)B);
		}

		public static void Swap<T>(ref T A, ref T B) {
			T Tmp = A;
			A = B;
			B = Tmp;
		}

		public static float Clamp(float Num, float Min, float Max) {
			if (Num < Min)
				return Min;

			if (Num > Max)
				return Max;

			return Num;
		}

		public static int Clamp(int Num, int Min, int Max) {
			if (Num < Min)
				return Min;

			if (Num > Max)
				return Max;

			return Num;
		}

		public static int Random(int InclusiveMin, int ExclusiveMax) {
			return Rnd.Next(InclusiveMin, ExclusiveMax);
		}

		public static void Deconstruct(Quaternion Q, out float Pitch, out float Yaw, out float Roll) {
			GetEulerAngles(Q, out Pitch, out Yaw, out Roll);
		}

		public static void GetEulerAngles(Quaternion Quat, out float Pitch, out float Yaw, out float Roll) {
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

		public static byte DirToByte(Vector3 Normal) {
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

		public static double NormalizeLoop(double Val, double Start, double End) {
			double Width = End - Start;
			double OffsetVal = Val - Start;
			return (OffsetVal - (Math.Floor(OffsetVal / Width) * Width)) + Start;
		}

		public static Vector3 EulerBetweenVectors(Vector3 A, Vector3 B) {
			Vector3 NormalA = Vector3.Normalize(B - A);
			Vector3 NormalB = Vector3.Normalize(new Vector3(A.X, 0, A.Z));

			Vector3 Axis = Vector3.Normalize(Vector3.Cross(NormalA, NormalB));

			double Angle = Math.Acos(Vector3.Dot(NormalA, NormalB));



			//Console.WriteLine(Angle);

			return ToEuler(Axis.X, Axis.Y, Axis.Z, Angle) * (180.0f / (float)Math.PI);
		}

		public static Vector3 ToEuler(double X, double Y, double Z, double Angle) {
			double s = Math.Sin(Angle);
			double c = Math.Cos(Angle);
			double t = 1 - c;

			double Yaw;
			double Pitch;
			double Roll;

			if ((X * Y * t + Z * s) > 0.998) { // north pole singularity detected
				Yaw = 2 * Math.Atan2(X * Math.Sin(Angle / 2), Math.Cos(Angle / 2));
				Pitch = Math.PI / 2;
				Roll = 0;
				return new Vector3((float)Yaw, (float)Pitch, (float)Roll);
			}
			if ((X * Y * t + Z * s) < -0.998) { // south pole singularity detected
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

		static bool IsOnVoxelSphereEdge(float Radius, int X, int Y, int Z) {
			float Dist = X * X + Y * Y + Z * Z;
			return Dist <= (Radius * Radius) && Dist >= ((Radius - 1) * (Radius - 1));
		}

		static bool IsInsideVoxelSphere(float Radius, int X, int Y, int Z) {
			float Dist = X * X + Y * Y + Z * Z;
			return Dist <= (Radius * Radius);
		}

		public static Vector3[] GenerateVoxelSphere(int Radius, bool Hollow) {
			List<Vector3> Voxels = new List<Vector3>();
			int Diameter = Radius * 2;

			for (int x = 0; x < Diameter; x++)
				for (int y = 0; y < Diameter; y++)
					for (int z = 0; z < Diameter; z++) {
						if (Hollow) {
							if (IsOnVoxelSphereEdge(Radius, x - Radius, y - Radius, z - Radius))
								Voxels.Add(new Vector3(x - Radius, y - Radius, z - Radius));
						} else {
							if (IsInsideVoxelSphere(Radius, x - Radius, y - Radius, z - Radius))
								Voxels.Add(new Vector3(x - Radius, y - Radius, z - Radius));
						}
					}

			return Voxels.ToArray();
		}

		static Vector3[] CalculateSphereDirections(int Slices) {
			if (SphereDirections.ContainsKey(Slices))
				return SphereDirections[Slices];

			List<Vector3> Directions = new List<Vector3>();

			double da = Math.PI / (Slices - 1);
			double a = -0.5 * Math.PI;
			int Hits = 0;

			for (int ia = 0; ia < Slices; ia++, a += da) {
				double r = Math.Cos(a);
				int nb = (int)Math.Ceiling(2.0 * Math.PI * r / da);
				double db = 2.0 * Math.PI / (nb);

				if ((ia == 0) || (ia == Slices - 1)) {
					nb = 1;
					db = 0.0;
				}

				double b = 0;
				for (int ib = 0; ib < nb; ib++, b += db) {
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

		public static bool Raycast(Vector3 Origin, Vector3 Direction, float Length, RaycastCallbackFunc Callback) {
			// Cube containing origin point.
			float X = (float)Math.Floor(Origin.X);
			float Y = (float)Math.Floor(Origin.Y);
			float Z = (float)Math.Floor(Origin.Z);

			// Break out direction vector.
			float Dx = Direction.X;
			float Dy = Direction.Y;
			float Dz = Direction.Z;

			// Direction to increment x,y,z when stepping.
			float StepX = Dx > 0 ? 1 : Dx < 0 ? -1 : 0;
			float StepY = Dy > 0 ? 1 : Dy < 0 ? -1 : 0;
			float StepZ = Dz > 0 ? 1 : Dz < 0 ? -1 : 0;

			/*const float StepScale = 0.2f;
			StepX *= StepScale;
			StepY *= StepScale;
			StepZ *= StepScale;*/

			// See description above. The initial values depend on the fractional
			// part of the origin. tMaxX, tMaxY, and tMaxZ store the t-value at which we cross a cube boundary along each axis.
			// The following logic chooses the closest cube boundary and updates the position and face normal accordingly.
			float tMaxX = IntBound(Origin.X, Dx);
			float tMaxY = IntBound(Origin.Y, Dy);
			float tMaxZ = IntBound(Origin.Z, Dz);

			// The change in t when taking a step (always positive).
			float tDeltaX = StepX / Dx;
			float tDeltaY = StepY / Dy;
			float tDeltaZ = StepZ / Dz;

			// Buffer for reporting faces to the callback.
			var face = new Vector3();

			// Avoids an infinite loop.
			if (Dx == 0 && Dy == 0 && Dz == 0)
				throw new Exception("Raycast in zero direction!");

			// Rescale from units of 1 cube-edge to units of 'direction' so we can
			// compare with 't'.
			Length /= (float)Math.Sqrt(Dx * Dx + Dy * Dy + Dz * Dz);

			while (true) {
				if (Callback((int)X, (int)Y, (int)Z, face))
					return true;

				// tMaxX stores the t-value at which we cross a cube boundary along the
				// X axis, and similarly for Y and Z. Therefore, choosing the least tMax
				// chooses the closest cube boundary. Only the first case of the four
				// has been commented in detail.
				if (tMaxX < tMaxY) {
					if (tMaxX < tMaxZ) {
						if (tMaxX > Length)
							break;
						// Update which cube we are now in.
						X += StepX;
						// Adjust tMaxX to the next X-oriented boundary crossing.
						tMaxX += tDeltaX;
						// Record the normal vector of the cube face we entered.
						face.X = -StepX;
						face.Y = 0;
						face.Z = 0;
					} else {
						if (tMaxZ > Length)
							break;
						Z += StepZ;
						tMaxZ += tDeltaZ;
						face.X = 0;
						face.Y = 0;
						face.Z = -StepZ;
					}
				} else {
					if (tMaxY < tMaxZ) {
						if (tMaxY > Length)
							break;
						Y += StepY;
						tMaxY += tDeltaY;
						face.X = 0;
						face.Y = -StepY;
						face.Z = 0;
					} else {
						// Identical to the second case, repeated for simplicity in
						// the conditionals.
						if (tMaxZ > Length)
							break;
						Z += StepZ;
						tMaxZ += tDeltaZ;
						face.X = 0;
						face.Y = 0;
						face.Z = -StepZ;
					}
				}
			}

			return false;
		}

		static bool IsRecording = false;
		static List<Tuple<Vector3, Vector3, Color>> RaycastDrawList = new List<Tuple<Vector3, Vector3, Color>>();

		public static void ClearRaycastRecord() {
			RaycastDrawList.Clear();
		}

		public static void BeginRaycastRecord() {
			ClearRaycastRecord();
			IsRecording = true;
		}

		public static void EndRaycastRecord() {
			IsRecording = false;
		}

		public static void DrawRaycastRecord() {
			foreach (var Tupl in RaycastDrawList) {
				if (Tupl.Item1 == Tupl.Item2) {
					Raylib.DrawSphereEx(Tupl.Item1, 0.05f, 10, 10, Tupl.Item3);
				} else {
					Raylib.DrawLine3D(Tupl.Item1, Tupl.Item2, Tupl.Item3);
				}
			}
		}

		public static bool HasRecord() {
			return RaycastDrawList.Count > 0;
		}

		public static void AddRaycastRecord(Vector3 Start, Vector3 End, Color Clr) {
			if (!IsRecording)
				return;

			RaycastDrawList.Add(new Tuple<Vector3, Vector3, Color>(Start, End, Clr));
		}

		static float IntBound(float S, float Ds) {
			if (Ds < 0) {
				Ds = -Ds;
				S = -S;
			}

			return (1 - (S % 1 + 1) % 1) / Ds;
		}

		public static string ToString(float F) {
			string Str = string.Format(CultureInfo.InvariantCulture, "{0}", F);

			if (Str.Contains("."))
				Str = Str + "f";

			return Str;
		}

		public static string ToString(Vector3 V) {
			return string.Format(CultureInfo.InvariantCulture, "new Vector3({0}, {1}, {2})", ToString(V.X), ToString(V.Y), ToString(V.Z));
		}

		public static string ToString(Vector2 V) {
			return string.Format(CultureInfo.InvariantCulture, "new Vector2({0}, {1})", ToString(V.X), ToString(V.Y));
		}

		public static string ToString(Color C) {
			return string.Format(CultureInfo.InvariantCulture, "new Color({0}, {1}, {2})", C.R, C.G, C.B, C.A);
		}

		public static string ToString(Vertex3 V) {
			return string.Format("new Vertex3({0}, {1}, {2})", ToString(V.Position), ToString(V.UV), ToString(V.Color));
		}

		public static T Random<T>(this IEnumerable<T> Collection) {
			T[] Elements = Collection.ToArray();
			return Elements[Random(0, Elements.Length)];
		}

		public static float ToDeg(float Rad) {
			return (float)((Rad * 180) / Math.PI);
		}

		public static float ToRad(float Deg) {
			return (float)(Deg * (Math.PI / 180));
		}

		public static Color Color(float B) {
			return new Color(B, B, B);
		}

		public static Color ColorMul(Color A, Color B) {
			Vector4 AA = new Vector4(A.R / 255.0f, A.G / 255.0f, A.B / 255.0f, A.A / 255.0f);
			Vector4 BB = new Vector4(B.R / 255.0f, B.G / 255.0f, B.B / 255.0f, B.A / 255.0f);
			Vector4 Res = AA * BB;
			return new Color(Res.X, Res.Y, Res.Z, Res.W);
		}

		public static Vector4 ColorVec(Color C) {
			return new Vector4(C.R / 255.0f, C.G / 255.0f, C.B / 255.0f, C.A / 255.0f);
		}

		public static IEnumerable<Vector2> ToVec2(float[] F) {
			for (int i = 0; i < F.Length; i += 2) {
				yield return new Vector2(F[i + 0], F[i + 1]);
			}
		}

		public static Vector3 ToVec3(float[] F) {
			return new Vector3(F[0], F[1], F[2]);
		}

		public static Vector3 ProjectOnPlane(Vector3 Vec, Vector3 Normal, float Eps) {
			if (Vec.X == 0 && Vec.Y == 0 && Normal.X == 0 && Normal.Y == 0)
				return Vec;

			if (Vec.X == 0 && Vec.Z == 0 && Normal.X == 0 && Normal.Z == 0)
				return Vec;

			if (Vec.Z == 0 && Vec.Y == 0 && Normal.Z == 0 && Normal.Y == 0)
				return Vec;

			float SqrMag = Vector3.Dot(Normal, Normal);

			if (SqrMag < Eps) {
				return Vec;
			} else {
				float Dot = Vector3.Dot(Vec, Normal);
				return new Vector3(Vec.X - Normal.X * Dot / SqrMag, Vec.Y - Normal.Y * Dot / SqrMag, Vec.Z - Normal.Z * Dot / SqrMag);
			}
		}

		// If F = 0, then it's A, as F approaches 1, then it returns lerp towards B.
		// If < 0, return A, if > 1, return B.
		public static Vector3 Lerp(Vector3 A, Vector3 B, float F) {
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

		public static Quaternion Lerp(Quaternion A, Quaternion B, float F) {
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

		public static void RestartGame() {
			string[] Args = Environment.GetCommandLineArgs().Skip(1).ToArray();
			string FileName = Process.GetCurrentProcess().MainModule.FileName;
			Process.Start(FileName, Args);
			Environment.Exit(0);
		}
	}
}
