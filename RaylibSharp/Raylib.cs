using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Matrix = System.Numerics.Matrix4x4;

namespace RaylibSharp {
	// Color type, RGBA (32bit)
	[StructLayout(LayoutKind.Sequential)]
	public struct Color {
		public static readonly Color White = new Color(255, 255, 255);
		public static readonly Color Red = new Color(255, 0, 0);
		public static readonly Color Green = new Color(0, 255, 0);
		public static readonly Color Blue = new Color(0, 0, 255);

		public byte r;
		public byte g;
		public byte b;
		public byte a;

		public Color(byte r, byte g, byte b, byte a) {
			this.r = r;
			this.g = g;
			this.b = b;
			this.a = a;
		}

		public Color(byte r, byte g, byte b) : this(r, g, b, 255) {
		}

		public Color(Vector4 Clr) : this((byte)(Clr.X * 255), (byte)(Clr.Y * 255), (byte)(Clr.Z * 255), (byte)(Clr.W * 255)) {
		}

		public Color(float RGB) : this(new Vector4(RGB, RGB, RGB, 1)) {
		}

		public void ToFloat(out Vector4 Clr) {
			Clr = new Vector4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
		}

		public override string ToString() {
			return string.Format("({0}, {1}, {2}, {3})", r, g, b, a);
		}

		public static Color operator *(Color A, Color B) {
			A.ToFloat(out Vector4 AFloat);
			B.ToFloat(out Vector4 BFloat);
			return new Color(AFloat * BFloat);
		}

		public static Color operator +(Color A, Color B) {
			A.ToFloat(out Vector4 AFloat);
			B.ToFloat(out Vector4 BFloat);
			return new Color(AFloat + BFloat);
		}
	}

	// Rectangle type
	[StructLayout(LayoutKind.Sequential)]
	public struct Rectangle {
		public float x;
		public float y;
		public float width;
		public float height;

		public Rectangle(float x, float y, float w, float h) {
			this.x = x;
			this.y = y;
			this.width = w;
			this.height = h;
		}
	}

	// Image type, bpp always RGBA (32bit)
	// NOTE: Data stored in CPU memory (RAM)
	[StructLayout(LayoutKind.Sequential)]
	public struct Image {
		public IntPtr data;             // Image raw data
		public int width;              // Image base width
		public int height;             // Image base height
		public int mipmaps;            // Mipmap levels, 1 by default
		public int format;             // Data format (PixelFormat type)
	}

	// Texture2D type
	// NOTE: Data stored in GPU memory
	[StructLayout(LayoutKind.Sequential)]
	public struct Texture2D {
		public uint id;        // OpenGL texture id
		public int width;              // Texture base width
		public int height;             // Texture base height
		public int mipmaps;            // Mipmap levels, 1 by default
		public int format;             // Data format (PixelFormat type)
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct Texture {
		public Texture2D tex;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct TextureCubemap {
		public Texture2D tex;
	}

	// RenderTexture2D type, for texture rendering
	[StructLayout(LayoutKind.Sequential)]
	public struct RenderTexture2D {
		public uint id;        // OpenGL Framebuffer Object (FBO) id
		public Texture2D texture;      // Color buffer attachment texture
		public Texture2D depth;        // Depth buffer attachment texture
		public bool depthTexture;      // Track if depth attachment is a texture or renderbuffer
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct RenderTexture {
		public RenderTexture2D tex;
	}

	// N-Patch layout info
	[StructLayout(LayoutKind.Sequential)]
	public struct NPatchInfo {
		public Rectangle sourceRec;   // Region in the texture
		public int left;              // left border offset
		public int top;               // top border offset
		public int right;             // right border offset
		public int bottom;            // bottom border offset
		public int type;              // layout of the n-patch: 3x3, 1x3 or 3x1
	}

	// Font character info
	[StructLayout(LayoutKind.Sequential)]
	public struct CharInfo {
		public int value;              // Character value (Unicode)
		public int offsetX;            // Character offset X when drawing
		public int offsetY;            // Character offset Y when drawing
		public int advanceX;           // Character advance position X
		public Image image;            // Character image data
	}

	// Font type, includes texture and charSet array data
	[StructLayout(LayoutKind.Sequential)]
	unsafe public struct Font {
		public int baseSize;           // Base size (default chars height)
		public int charsCount;         // Number of characters
		public Texture2D texture;      // Characters texture atlas
		public Rectangle* recs;        // Characters rectangles in texture
		public CharInfo* chars;        // Characters info data
	}

	// Camera type, defines a camera position/orientation in 3d space
	[StructLayout(LayoutKind.Sequential)]
	public struct Camera3D {
		public Vector3 position;       // Camera position
		public Vector3 target;         // Camera target it looks-at
		public Vector3 up;             // Camera up vector (rotation over its axis)
		public float fovy;             // Camera field-of-view apperture in Y (degrees) in perspective, used as near plane width in orthographic
		public int type;               // Camera type, defines projection type: CAMERA_PERSPECTIVE or CAMERA_ORTHOGRAPHIC

		public Camera3D(Vector3 position, Vector3 target, Vector3 up, float fovy = 90, CameraType type = CameraType.CAMERA_PERSPECTIVE) {
			this.position = position;       // Camera position
			this.target = target;         // Camera target it looks-at
			this.up = up;             // Camera up vector (rotation over its axis)
			this.fovy = fovy;             // Camera field-of-view apperture in Y (degrees) in perspective, used as near plane width in orthographic
			this.type = (int)type;               // Camera type, defines projection type: CAMERA_PERSPECTIVE or CAMERA_ORTHOGRAPHIC
		}
	}

	// Camera2D type, defines a 2d camera
	[StructLayout(LayoutKind.Sequential)]
	public struct Camera2D {
		public Vector2 offset;         // Camera offset (displacement from target)
		public Vector2 target;         // Camera target (rotation and zoom origin)
		public float rotation;         // Camera rotation in degrees
		public float zoom;             // Camera zoom (scaling), should be 1.0f by default
	}

	// Vertex data definning a mesh
	// NOTE: Data stored in CPU memory (and GPU)
	[StructLayout(LayoutKind.Sequential)]
	unsafe public struct Mesh {
		public int vertexCount;        // Number of vertices stored in arrays
		public int triangleCount;      // Number of triangles stored (indexed or not)

		// Default vertex data
		public float* vertices;        // Vertex position (XYZ - 3 components per vertex) (shader-location = 0)
		public float* texcoords;       // Vertex texture coordinates (UV - 2 components per vertex) (shader-location = 1)
		public float* texcoords2;      // Vertex second texture coordinates (useful for lightmaps) (shader-location = 5)
		public float* normals;         // Vertex normals (XYZ - 3 components per vertex) (shader-location = 2)
		public float* tangents;        // Vertex tangents (XYZW - 4 components per vertex) (shader-location = 4)
		public byte* colors;  // Vertex colors (RGBA - 4 components per vertex) (shader-location = 3)
		public ushort* indices;// Vertex indices (in case vertex data comes indexed)

		// Animation vertex data
		public float* animVertices;    // Animated vertex positions (after bones transformations)
		public float* animNormals;     // Animated normals (after bones transformations)
		public int* boneIds;           // Vertex bone ids, up to 4 bones influence by vertex (skinning)
		public float* boneWeights;     // Vertex bone weight, up to 4 bones influence by vertex (skinning)

		// OpenGL identifiers
		public uint vaoId;     // OpenGL Vertex Array Object id
		public uint* vboId;    // OpenGL Vertex Buffer Objects id (default vertex data)
	}

	// Shader type (generic)
	[StructLayout(LayoutKind.Sequential)]
	unsafe public struct Shader {
		public uint id;        // Shader program id
		public int* locs;              // Shader locations array (MAX_SHADER_LOCATIONS)
	}

	// Material texture map
	[StructLayout(LayoutKind.Sequential)]
	public struct MaterialMap {
		public Texture2D texture;      // Material map texture
		public Color color;            // Material map color
		public float value;            // Material map value
	}

	// Material type (generic)
	[StructLayout(LayoutKind.Sequential)]
	unsafe public struct Material {
		public Shader shader;          // Material shader
		public MaterialMap* maps;      // Material maps array (MAX_MATERIAL_MAPS)
		public float* params_;          // Material generic parameters (if required)
	}

	// Transformation properties
	[StructLayout(LayoutKind.Sequential)]
	public struct Transform {
		public Vector3 translation;    // Translation
		public Quaternion rotation;    // Rotation
		public Vector3 scale;          // Scale
	}

	// Bone information
	[StructLayout(LayoutKind.Sequential)]
	unsafe public struct BoneInfo {
		public fixed byte name[32];          // Bone name
		public int parent;             // Bone parent
	}

	// Model type
	[StructLayout(LayoutKind.Sequential)]
	unsafe public struct Model {
		public Matrix transform;       // Local transform matrix

		public int meshCount;          // Number of meshes
		public Mesh* meshes;           // Meshes array

		public int materialCount;      // Number of materials
		public Material* materials;    // Materials array
		public int* meshMaterial;      // Mesh material number

		// Animation data
		public int boneCount;          // Number of bones
		public BoneInfo* bones;        // Bones information (skeleton)
		public Transform* bindPose;    // Bones base transformation (pose)
	}

	// Model animation
	[StructLayout(LayoutKind.Sequential)]
	unsafe public struct ModelAnimation {
		public int boneCount;          // Number of bones
		public BoneInfo* bones;        // Bones information (skeleton)

		public int frameCount;         // Number of animation frames
		public Transform** framePoses; // Poses array by frame
	}

	// Ray type (useful for raycast)
	[StructLayout(LayoutKind.Sequential)]
	public struct Ray {
		public Vector3 position;       // Ray position (origin)
		public Vector3 direction;      // Ray direction
	}

	// Raycast hit information
	[StructLayout(LayoutKind.Sequential)]
	public struct RayHitInfo {
		public bool hit;               // Did the ray hit something?
		public float distance;         // Distance to nearest hit
		public Vector3 position;       // Position of nearest hit
		public Vector3 normal;         // Surface normal of hit
	}

	// Bounding box type
	[StructLayout(LayoutKind.Sequential)]
	public struct BoundingBox {
		public Vector3 min;            // Minimum vertex box-corner
		public Vector3 max;            // Maximum vertex box-corner
	}

	// Wave type, defines audio wave data
	[StructLayout(LayoutKind.Sequential)]
	public struct Wave {
		public uint sampleCount;       // Total number of samples
		public uint sampleRate;        // Frequency (samples per second)
		public uint sampleSize;        // Bit depth (bits per sample): 8, 16, 32 (24 not supported)
		public uint channels;          // Number of channels (1-mono, 2-stereo)
		public IntPtr data;                     // Buffer data pointer
	}

	//typedef public struct rAudioBuffer rAudioBuffer;

	// Audio stream type
	// NOTE: Useful to create custom audio streams not bound to a specific file
	[StructLayout(LayoutKind.Sequential)]
	unsafe public struct AudioStream {
		public uint sampleRate;        // Frequency (samples per second)
		public uint sampleSize;        // Bit depth (bits per sample): 8, 16, 32 (24 not supported)
		public uint channels;          // Number of channels (1-mono, 2-stereo)

		// rAudioBuffer*
		public IntPtr buffer;           // Pointer to internal data used by the audio system
	}

	// Sound source type
	[StructLayout(LayoutKind.Sequential)]
	public struct Sound {
		public uint sampleCount;       // Total number of samples
		public AudioStream stream;             // Audio stream
	}

	// Music stream type (audio file streaming from memory)
	// NOTE: Anything longer than ~10 seconds should be streamed
	[StructLayout(LayoutKind.Sequential)]
	public struct Music {
		public int ctxType;                    // Type of music context (audio filetype)
		public IntPtr ctxData;                  // Audio context data, depends on type

		public uint sampleCount;       // Total number of samples
		public uint sampleLeft;        // Number of samples left to end
		public uint loopCount;         // Loops count (times music will play), 0 means infinite loop

		public AudioStream stream;             // Audio stream
	}

	// Head-Mounted-Display device parameters
	[StructLayout(LayoutKind.Sequential)]
	unsafe public struct VrDeviceInfo {
		public int hResolution;                // HMD horizontal resolution in pixels
		public int vResolution;                // HMD vertical resolution in pixels
		public float hScreenSize;              // HMD horizontal size in meters
		public float vScreenSize;              // HMD vertical size in meters
		public float vScreenCenter;            // HMD screen center in meters
		public float eyeToScreenDistance;      // HMD distance between eye and display in meters
		public float lensSeparationDistance;   // HMD lens separation distance in meters
		public float interpupillaryDistance;   // HMD IPD (distance between pupils) in meters
		public fixed float lensDistortionValues[4];  // HMD lens distortion constant parameters
		public fixed float chromaAbCorrection[4];    // HMD chromatic aberration correction parameters
	}

	//----------------------------------------------------------------------------------
	// public enumerators Definition
	//----------------------------------------------------------------------------------
	// System config flags
	// NOTE: Used for bit masks
	public enum ConfigFlag {
		FLAG_SHOW_LOGO = 1,    // Set to show raylib logo at startup
		FLAG_FULLSCREEN_MODE = 2,    // Set to run program in fullscreen
		FLAG_WINDOW_RESIZABLE = 4,    // Set to allow resizable window
		FLAG_WINDOW_UNDECORATED = 8,    // Set to disable window decoration (frame and buttons)
		FLAG_WINDOW_TRANSPARENT = 16,   // Set to allow transparent window
		FLAG_WINDOW_HIDDEN = 128,  // Set to create the window initially hidden
		FLAG_WINDOW_ALWAYS_RUN = 256,  // Set to allow windows running while minimized
		FLAG_MSAA_4X_HINT = 32,   // Set to try enabling MSAA 4X
		FLAG_VSYNC_HINT = 64    // Set to try enabling V-Sync on GPU
	}

	// Trace log type
	public enum TraceLogType {
		LOG_ALL = 0,        // Display all logs
		LOG_TRACE,
		LOG_DEBUG,
		LOG_INFO,
		LOG_WARNING,
		LOG_ERROR,
		LOG_FATAL,
		LOG_NONE            // Disable logging
	}

	// Keyboard keys
	public enum KeyboardKey {
		// Alphanumeric keys
		KEY_APOSTROPHE = 39,
		KEY_COMMA = 44,
		KEY_MINUS = 45,
		KEY_PERIOD = 46,
		KEY_SLASH = 47,
		KEY_ZERO = 48,
		KEY_ONE = 49,
		KEY_TWO = 50,
		KEY_THREE = 51,
		KEY_FOUR = 52,
		KEY_FIVE = 53,
		KEY_SIX = 54,
		KEY_SEVEN = 55,
		KEY_EIGHT = 56,
		KEY_NINE = 57,
		KEY_SEMICOLON = 59,
		KEY_EQUAL = 61,
		KEY_A = 65,
		KEY_B = 66,
		KEY_C = 67,
		KEY_D = 68,
		KEY_E = 69,
		KEY_F = 70,
		KEY_G = 71,
		KEY_H = 72,
		KEY_I = 73,
		KEY_J = 74,
		KEY_K = 75,
		KEY_L = 76,
		KEY_M = 77,
		KEY_N = 78,
		KEY_O = 79,
		KEY_P = 80,
		KEY_Q = 81,
		KEY_R = 82,
		KEY_S = 83,
		KEY_T = 84,
		KEY_U = 85,
		KEY_V = 86,
		KEY_W = 87,
		KEY_X = 88,
		KEY_Y = 89,
		KEY_Z = 90,

		// Function keys
		KEY_SPACE = 32,
		KEY_ESCAPE = 256,
		KEY_ENTER = 257,
		KEY_TAB = 258,
		KEY_BACKSPACE = 259,
		KEY_INSERT = 260,
		KEY_DELETE = 261,
		KEY_RIGHT = 262,
		KEY_LEFT = 263,
		KEY_DOWN = 264,
		KEY_UP = 265,
		KEY_PAGE_UP = 266,
		KEY_PAGE_DOWN = 267,
		KEY_HOME = 268,
		KEY_END = 269,
		KEY_CAPS_LOCK = 280,
		KEY_SCROLL_LOCK = 281,
		KEY_NUM_LOCK = 282,
		KEY_PRINT_SCREEN = 283,
		KEY_PAUSE = 284,
		KEY_F1 = 290,
		KEY_F2 = 291,
		KEY_F3 = 292,
		KEY_F4 = 293,
		KEY_F5 = 294,
		KEY_F6 = 295,
		KEY_F7 = 296,
		KEY_F8 = 297,
		KEY_F9 = 298,
		KEY_F10 = 299,
		KEY_F11 = 300,
		KEY_F12 = 301,
		KEY_LEFT_SHIFT = 340,
		KEY_LEFT_CONTROL = 341,
		KEY_LEFT_ALT = 342,
		KEY_LEFT_SUPER = 343,
		KEY_RIGHT_SHIFT = 344,
		KEY_RIGHT_CONTROL = 345,
		KEY_RIGHT_ALT = 346,
		KEY_RIGHT_SUPER = 347,
		KEY_KB_MENU = 348,
		KEY_LEFT_BRACKET = 91,
		KEY_BACKSLASH = 92,
		KEY_RIGHT_BRACKET = 93,
		KEY_GRAVE = 96,

		// Keypad keys
		KEY_KP_0 = 320,
		KEY_KP_1 = 321,
		KEY_KP_2 = 322,
		KEY_KP_3 = 323,
		KEY_KP_4 = 324,
		KEY_KP_5 = 325,
		KEY_KP_6 = 326,
		KEY_KP_7 = 327,
		KEY_KP_8 = 328,
		KEY_KP_9 = 329,
		KEY_KP_DECIMAL = 330,
		KEY_KP_DIVIDE = 331,
		KEY_KP_MULTIPLY = 332,
		KEY_KP_SUBTRACT = 333,
		KEY_KP_ADD = 334,
		KEY_KP_ENTER = 335,
		KEY_KP_EQUAL = 336
	}

	// Android buttons
	public enum AndroidButton {
		KEY_BACK = 4,
		KEY_MENU = 82,
		KEY_VOLUME_UP = 24,
		KEY_VOLUME_DOWN = 25
	}


	// Mouse buttons
	public enum MouseButton {
		MOUSE_LEFT_BUTTON = 0,
		MOUSE_RIGHT_BUTTON = 1,
		MOUSE_MIDDLE_BUTTON = 2
	}

	// Gamepad number
	public enum GamepadNumber {
		GAMEPAD_PLAYER1 = 0,
		GAMEPAD_PLAYER2 = 1,
		GAMEPAD_PLAYER3 = 2,
		GAMEPAD_PLAYER4 = 3
	}

	// Gamepad Buttons
	public enum GamepadButton {
		// This is here just for error checking
		GAMEPAD_BUTTON_UNKNOWN = 0,

		// This is normally [A,B,X,Y]/[Circle,Triangle,Square,Cross]
		// No support for 6 button controllers though..
		GAMEPAD_BUTTON_LEFT_FACE_UP,
		GAMEPAD_BUTTON_LEFT_FACE_RIGHT,
		GAMEPAD_BUTTON_LEFT_FACE_DOWN,
		GAMEPAD_BUTTON_LEFT_FACE_LEFT,

		// This is normally a DPAD
		GAMEPAD_BUTTON_RIGHT_FACE_UP,
		GAMEPAD_BUTTON_RIGHT_FACE_RIGHT,
		GAMEPAD_BUTTON_RIGHT_FACE_DOWN,
		GAMEPAD_BUTTON_RIGHT_FACE_LEFT,

		// Triggers
		GAMEPAD_BUTTON_LEFT_TRIGGER_1,
		GAMEPAD_BUTTON_LEFT_TRIGGER_2,
		GAMEPAD_BUTTON_RIGHT_TRIGGER_1,
		GAMEPAD_BUTTON_RIGHT_TRIGGER_2,

		// These are buttons in the center of the gamepad
		GAMEPAD_BUTTON_MIDDLE_LEFT,     //PS3 Select
		GAMEPAD_BUTTON_MIDDLE,          //PS Button/XBOX Button
		GAMEPAD_BUTTON_MIDDLE_RIGHT,    //PS3 Start

		// These are the joystick press in buttons
		GAMEPAD_BUTTON_LEFT_THUMB,
		GAMEPAD_BUTTON_RIGHT_THUMB
	}

	public enum GamepadAxis {
		// This is here just for error checking
		GAMEPAD_AXIS_UNKNOWN = 0,

		// Left stick
		GAMEPAD_AXIS_LEFT_X,
		GAMEPAD_AXIS_LEFT_Y,

		// Right stick
		GAMEPAD_AXIS_RIGHT_X,
		GAMEPAD_AXIS_RIGHT_Y,

		// Pressure levels for the back triggers
		GAMEPAD_AXIS_LEFT_TRIGGER,      // [1..-1] (pressure-level)
		GAMEPAD_AXIS_RIGHT_TRIGGER      // [1..-1] (pressure-level)
	}

	// Shader location point type
	public enum ShaderLocationIndex {
		LOC_VERTEX_POSITION = 0,
		LOC_VERTEX_TEXCOORD01,
		LOC_VERTEX_TEXCOORD02,
		LOC_VERTEX_NORMAL,
		LOC_VERTEX_TANGENT,
		LOC_VERTEX_COLOR,
		LOC_MATRIX_MVP,
		LOC_MATRIX_MODEL,
		LOC_MATRIX_VIEW,
		LOC_MATRIX_PROJECTION,
		LOC_VECTOR_VIEW,
		LOC_COLOR_DIFFUSE,
		LOC_COLOR_SPECULAR,
		LOC_COLOR_AMBIENT,
		LOC_MAP_ALBEDO,          // LOC_MAP_DIFFUSE
		LOC_MAP_METALNESS,       // LOC_MAP_SPECULAR
		LOC_MAP_NORMAL,
		LOC_MAP_ROUGHNESS,
		LOC_MAP_OCCLUSION,
		LOC_MAP_EMISSION,
		LOC_MAP_HEIGHT,
		LOC_MAP_CUBEMAP,
		LOC_MAP_IRRADIANCE,
		LOC_MAP_PREFILTER,
		LOC_MAP_BRDF
	}



	// Shader uniform data types
	public enum ShaderUniformDataType {
		UNIFORM_FLOAT = 0,
		UNIFORM_VEC2,
		UNIFORM_VEC3,
		UNIFORM_VEC4,
		UNIFORM_INT,
		UNIFORM_IVEC2,
		UNIFORM_IVEC3,
		UNIFORM_IVEC4,
		UNIFORM_SAMPLER2D
	}

	// Material map type
	public enum MaterialMapType : int {
		MAP_ALBEDO = 0,       // MAP_DIFFUSE
		MAP_METALNESS = 1,       // MAP_SPECULAR
		MAP_NORMAL = 2,
		MAP_ROUGHNESS = 3,
		MAP_OCCLUSION,
		MAP_EMISSION,
		MAP_HEIGHT,
		MAP_CUBEMAP,             // NOTE: Uses GL_TEXTURE_CUBE_MAP
		MAP_IRRADIANCE,          // NOTE: Uses GL_TEXTURE_CUBE_MAP
		MAP_PREFILTER,           // NOTE: Uses GL_TEXTURE_CUBE_MAP
		MAP_BRDF
	}



	// Pixel formats
	// NOTE: Support depends on OpenGL version and platform
	public enum PixelFormat {
		UNCOMPRESSED_GRAYSCALE = 1,     // 8 bit per pixel (no alpha)
		UNCOMPRESSED_GRAY_ALPHA,        // 8*2 bpp (2 channels)
		UNCOMPRESSED_R5G6B5,            // 16 bpp
		UNCOMPRESSED_R8G8B8,            // 24 bpp
		UNCOMPRESSED_R5G5B5A1,          // 16 bpp (1 bit alpha)
		UNCOMPRESSED_R4G4B4A4,          // 16 bpp (4 bit alpha)
		UNCOMPRESSED_R8G8B8A8,          // 32 bpp
		UNCOMPRESSED_R32,               // 32 bpp (1 channel - float)
		UNCOMPRESSED_R32G32B32,         // 32*3 bpp (3 channels - float)
		UNCOMPRESSED_R32G32B32A32,      // 32*4 bpp (4 channels - float)
		COMPRESSED_DXT1_RGB,            // 4 bpp (no alpha)
		COMPRESSED_DXT1_RGBA,           // 4 bpp (1 bit alpha)
		COMPRESSED_DXT3_RGBA,           // 8 bpp
		COMPRESSED_DXT5_RGBA,           // 8 bpp
		COMPRESSED_ETC1_RGB,            // 4 bpp
		COMPRESSED_ETC2_RGB,            // 4 bpp
		COMPRESSED_ETC2_EAC_RGBA,       // 8 bpp
		COMPRESSED_PVRT_RGB,            // 4 bpp
		COMPRESSED_PVRT_RGBA,           // 4 bpp
		COMPRESSED_ASTC_4x4_RGBA,       // 8 bpp
		COMPRESSED_ASTC_8x8_RGBA        // 2 bpp
	}

	// Texture parameters: filter mode
	// NOTE 1: Filtering considers mipmaps if available in the texture
	// NOTE 2: Filter is accordingly set for minification and magnification
	public enum TextureFilterMode {
		FILTER_POINT = 0,               // No filter, just pixel aproximation
		FILTER_BILINEAR,                // Linear filtering
		FILTER_TRILINEAR,               // Trilinear filtering (linear with mipmaps)
		FILTER_ANISOTROPIC_4X,          // Anisotropic filtering 4x
		FILTER_ANISOTROPIC_8X,          // Anisotropic filtering 8x
		FILTER_ANISOTROPIC_16X,         // Anisotropic filtering 16x
	}

	// Cubemap layout type
	public enum CubemapLayoutType {
		CUBEMAP_AUTO_DETECT = 0,        // Automatically detect layout type
		CUBEMAP_LINE_VERTICAL,          // Layout is defined by a vertical line with faces
		CUBEMAP_LINE_HORIZONTAL,        // Layout is defined by an horizontal line with faces
		CUBEMAP_CROSS_THREE_BY_FOUR,    // Layout is defined by a 3x4 cross with cubemap faces
		CUBEMAP_CROSS_FOUR_BY_THREE,    // Layout is defined by a 4x3 cross with cubemap faces
		CUBEMAP_PANORAMA                // Layout is defined by a panorama image (equirectangular map)
	}

	// Texture parameters: wrap mode
	public enum TextureWrapMode {
		WRAP_REPEAT = 0,        // Repeats texture in tiled mode
		WRAP_CLAMP,             // Clamps texture to edge pixel in tiled mode
		WRAP_MIRROR_REPEAT,     // Mirrors and repeats the texture in tiled mode
		WRAP_MIRROR_CLAMP       // Mirrors and clamps to border the texture in tiled mode
	}

	// Font type, defines generation method
	public enum FontType {
		FONT_DEFAULT = 0,       // Default font generation, anti-aliased
		FONT_BITMAP,            // Bitmap font generation, no anti-aliasing
		FONT_SDF                // SDF font generation, requires external shader
	}

	// Color blending modes (pre-defined)
	public enum BlendMode {
		BLEND_ALPHA = 0,        // Blend textures considering alpha (default)
		BLEND_ADDITIVE,         // Blend textures adding colors
		BLEND_MULTIPLIED        // Blend textures multiplying colors
	}

	// Gestures type
	// NOTE: It could be used as flags to enable only some gestures
	public enum GestureType {
		GESTURE_NONE = 0,
		GESTURE_TAP = 1,
		GESTURE_DOUBLETAP = 2,
		GESTURE_HOLD = 4,
		GESTURE_DRAG = 8,
		GESTURE_SWIPE_RIGHT = 16,
		GESTURE_SWIPE_LEFT = 32,
		GESTURE_SWIPE_UP = 64,
		GESTURE_SWIPE_DOWN = 128,
		GESTURE_PINCH_IN = 256,
		GESTURE_PINCH_OUT = 512
	}

	// Camera system modes
	public enum CameraMode : int {
		CAMERA_CUSTOM = 0,
		CAMERA_FREE,
		CAMERA_ORBITAL,
		CAMERA_FIRST_PERSON,
		CAMERA_THIRD_PERSON
	}

	// Camera projection modes
	public enum CameraType {
		CAMERA_PERSPECTIVE = 0,
		CAMERA_ORTHOGRAPHIC
	}

	// Type of n-patch
	public enum NPatchType {
		NPT_9PATCH = 0,         // Npatch defined by 3x3 tiles
		NPT_3PATCH_VERTICAL,    // Npatch defined by 1x3 tiles
		NPT_3PATCH_HORIZONTAL   // Npatch defined by 3x1 tiles
	}

	// TODO
	// typedef void (* TraceLogCallback) (int logType, string text, va_list args);

	public unsafe static partial class Raylib {
		const string LibName = "raylib";
		const CallingConvention CConv = CallingConvention.Cdecl;
		const CharSet CSet = CharSet.Ansi;


		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void InitWindow(int width, int height, string title);  // Initialize window and OpenGL context



		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool WindowShouldClose();                               // Check if KEY_ESCAPE pressed or Close icon pressed

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void CloseWindow();                                     // Close window and unload OpenGL context

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsWindowReady();                                   // Check if window has been initialized successfully

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsWindowMinimized();                               // Check if window has been minimized (or lost focus)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsWindowResized();                                 // Check if window has been resized

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsWindowHidden();                                  // Check if window is currently hidden

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ToggleFullscreen();                                // Toggle fullscreen mode (only PLATFORM_DESKTOP)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnhideWindow();                                    // Show the window

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void HideWindow();                                      // Hide the window

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetWindowIcon(Image image);                            // Set icon for window (only PLATFORM_DESKTOP)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetWindowTitle(string title);                     // Set title for window (only PLATFORM_DESKTOP)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetWindowPosition(int x, int y);                       // Set window position on screen (only PLATFORM_DESKTOP)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetWindowMonitor(int monitor);                         // Set monitor for the current window (fullscreen mode)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetWindowMinSize(int width, int height);               // Set window minimum dimensions (for FLAG_WINDOW_RESIZABLE)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetWindowSize(int width, int height);                  // Set window dimensions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern IntPtr GetWindowHandle();                                // Get native window handle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetScreenWidth();                                   // Get current screen width

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetScreenHeight();                                  // Get current screen height

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetMonitorCount();                                  // Get number of connected monitors

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetMonitorWidth(int monitor);                           // Get primary monitor width

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetMonitorHeight(int monitor);                          // Get primary monitor height

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetMonitorPhysicalWidth(int monitor);                   // Get primary monitor physical width in millimetres

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetMonitorPhysicalHeight(int monitor);                  // Get primary monitor physical height in millimetres

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GetMonitorName(int monitor);                    // Get the human-readable, UTF-8 encoded name of the primary monitor

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GetClipboardText();                         // Get clipboard text content

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetClipboardText(string text);                    // Set clipboard text content

		// Cursor-related functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ShowCursor();                                      // Shows cursor

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void HideCursor();                                      // Hides cursor

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsCursorHidden();                                  // Check if cursor is not visible

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void EnableCursor();                                    // Enables cursor (unlock cursor)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DisableCursor();                                   // Disables cursor (lock cursor)

		// Drawing-related functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ClearBackground(Color color);                          // Set background color (framebuffer clear color)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void BeginDrawing();                                    // Setup canvas (framebuffer) to start drawing

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void EndDrawing();                                      // End canvas drawing and swap buffers (double buffering)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void BeginMode2D(Camera2D camera);                          // Initialize 2D mode with custom camera (2D)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void EndMode2D();                                       // Ends 2D mode with custom camera

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void BeginMode3D(Camera3D camera);                          // Initializes 3D mode with custom camera (3D)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void EndMode3D();                                       // Ends 3D mode and returns to default 2D orthographic mode

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void BeginTextureMode(RenderTexture2D target);              // Initializes render texture for drawing

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void EndTextureMode();                                  // Ends drawing to render texture

		// Screen-space-related functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Ray GetMouseRay(Vector2 mousePosition, Camera3D camera);      // Returns a ray trace from mouse position

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector2 GetWorldToScreen(Vector3 position, Camera3D camera);  // Returns the screen space position for a 3d world space position

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Matrix GetCameraMatrix(Camera3D camera);                      // Returns camera transform matrix (view matrix)

		// Timing-related functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetTargetFPS(int fps);                                 // Set target FPS (maximum)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetFPS();                                           // Returns current FPS

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GetFrameTime();                                   // Returns time in seconds for last frame drawn

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern double GetTime();                                       // Returns elapsed time in seconds since InitWindow()

		// Color-related functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int ColorToInt(Color color);                                // Returns hexadecimal value for a Color

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector4 ColorNormalize(Color color);                        // Returns color normalized as float [0..1]

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector3 ColorToHSV(Color color);                            // Returns HSV values for a Color

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Color ColorFromHSV(Vector3 hsv);                            // Returns a Color from HSV values

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Color GetColor(int hexValue);                               // Returns a Color public struct from hexadecimal value

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Color Fade(Color color, float alpha);                       // Color fade-in or fade-out, alpha goes from 0.0f to 1.0f

		// Misc. functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetConfigFlags(uint flags);                    // Setup window configuration flags (view FLAGS)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetTraceLogLevel(int logType);                         // Set the current threshold (minimum) log level

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetTraceLogExit(int logType);                          // Set the exit threshold (minimum) log level

		//[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		//public static extern void SetTraceLogCallback(TraceLogCallback callback);        // Set a trace log callback to enable custom logging

		//[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		//public static extern void TraceLog(int logType, string text, ...);          // Show trace log messages (LOG_DEBUG, LOG_INFO, LOG_WARNING, LOG_ERROR)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void TakeScreenshot(string fileName);                  // Takes a screenshot of current screen (saved a .png)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetRandomValue(int min, int max);                       // Returns a random value between min and max (both included)

		// Files management functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool FileExists(string fileName);                      // Check if file exists

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsFileExtension(string fileName, string ext);// Check file extension

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool DirectoryExists(string dirPath);                  // Check if a directory path exists

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GetExtension(string fileName);             // Get pointer to extension for a filename string

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GetFileName(string filePath);              // Get pointer to filename for a path string

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GetFileNameWithoutExt(string filePath);    // Get filename string without extension (memory should be freed)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GetDirectoryPath(string fileName);         // Get full path for a given fileName (uses static string)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GetPrevDirectoryPath(string path);         // Get previous directory path for a given path (uses static string)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GetWorkingDirectory();                      // Get current working directory (uses static string)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern char** GetDirectoryFiles(string dirPath, int* count);  // Get filenames in a directory path (memory should be freed)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ClearDirectoryFiles();                             // Clear directory files paths buffers (free memory)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool ChangeDirectory(string dir);                      // Change working directory, returns true if success

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsFileDropped();                                   // Check if a file has been dropped into window

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern char** GetDroppedFiles(int* count);                         // Get dropped files names (memory should be freed)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ClearDroppedFiles();                               // Clear dropped files paths buffer (free memory)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern long GetFileModTime(string fileName);                  // Get file modification time (last write time)

		// Persistent storage management

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void StorageSaveValue(int position, int value);             // Save integer value to storage file (to defined position)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int StorageLoadValue(int position);                         // Load integer value from storage file (from defined position)


		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void OpenURL(string url);                              // Open URL with default system browser (if available)

		//------------------------------------------------------------------------------------
		// Input Handling Functions (Module: core)
		//------------------------------------------------------------------------------------

		// Input-related functions: keyboard

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsKeyPressed(KeyboardKey key);                             // Detect if a key has been pressed once

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsKeyDown(KeyboardKey key);                                // Detect if a key is being pressed

		public static bool IsKeyDown(char C) {
			return IsKeyDown((KeyboardKey)C);
		}

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsKeyReleased(KeyboardKey key);                            // Detect if a key has been released once

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsKeyUp(KeyboardKey key);                                  // Detect if a key is NOT being pressed

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetKeyPressed();                                // Get latest key pressed

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetExitKey(int key);                               // Set a custom key to exit program (default is ESC)

		// Input-related functions: gamepads

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsGamepadAvailable(int gamepad);                   // Detect if a gamepad is available

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsGamepadName(int gamepad, string name);      // Check gamepad name (if available)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GetGamepadName(int gamepad);                // Return gamepad internal name id

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsGamepadButtonPressed(int gamepad, int button);   // Detect if a gamepad button has been pressed once

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsGamepadButtonDown(int gamepad, int button);      // Detect if a gamepad button is being pressed

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsGamepadButtonReleased(int gamepad, int button);  // Detect if a gamepad button has been released once

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsGamepadButtonUp(int gamepad, int button);        // Detect if a gamepad button is NOT being pressed

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetGamepadButtonPressed();                      // Get the last gamepad button pressed

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetGamepadAxisCount(int gamepad);                   // Return gamepad axis count for a gamepad

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GetGamepadAxisMovement(int gamepad, int axis);    // Return axis movement value for a gamepad axis

		// Input-related functions: mouse

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsMouseButtonPressed(MouseButton button);                  // Detect if a mouse button has been pressed once

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsMouseButtonDown(MouseButton button);                     // Detect if a mouse button is being pressed

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsMouseButtonReleased(MouseButton button);                 // Detect if a mouse button has been released once

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsMouseButtonUp(MouseButton button);                       // Detect if a mouse button is NOT being pressed

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetMouseX();                                    // Returns mouse position X

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetMouseY();                                    // Returns mouse position Y

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector2 GetMousePosition();                         // Returns mouse position XY

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMousePosition(int x, int y);                    // Set mouse position XY

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMouseOffset(int offsetX, int offsetY);          // Set mouse offset

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMouseScale(float scaleX, float scaleY);         // Set mouse scaling

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetMouseWheelMove();                            // Returns mouse wheel movement Y

		// Input-related functions: touch

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetTouchX();                                    // Returns touch position X for touch point 0 (relative to screen size)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetTouchY();                                    // Returns touch position Y for touch point 0 (relative to screen size)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector2 GetTouchPosition(int index);                    // Returns touch position XY for a touch point index (relative to screen size)

		//------------------------------------------------------------------------------------
		// Gestures and Touch Handling Functions (Module: gestures)
		//------------------------------------------------------------------------------------

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetGesturesEnabled(uint gestureFlags);     // Enable a set of gestures using flags

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsGestureDetected(int gesture);                    // Check if a gesture have been detected

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetGestureDetected();                           // Get latest detected gesture

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetTouchPointsCount();                          // Get touch points count

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GetGestureHoldDuration();                     // Get gesture hold time in milliseconds

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector2 GetGestureDragVector();                     // Get gesture drag vector

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GetGestureDragAngle();                        // Get gesture drag angle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector2 GetGesturePinchVector();                    // Get gesture pinch delta

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GetGesturePinchAngle();                       // Get gesture pinch angle

		//------------------------------------------------------------------------------------
		// Camera System Functions (Module: camera)
		//------------------------------------------------------------------------------------

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetCameraMode(Camera3D camera, CameraMode mode);                // Set camera mode (multiple camera modes available)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UpdateCamera(Camera3D* camera);                          // Update camera position for selected mode

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UpdateCamera(ref Camera3D camera);                          // Update camera position for selected mode

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetCameraPanControl(int panKey);                       // Set camera pan key to combine with mouse movement (free camera)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetCameraAltControl(int altKey);                       // Set camera alt key to combine with mouse movement (free camera)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetCameraSmoothZoomControl(int szKey);                 // Set camera smooth zoom key to combine with mouse (free camera)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetCameraMoveControls(int frontKey, int backKey, int rightKey, int leftKey, int upKey, int downKey); // Set camera move controls (1st person and 3rd person cameras)

		//------------------------------------------------------------------------------------
		// Basic Shapes Drawing Functions (Module: shapes)
		//------------------------------------------------------------------------------------

		// Basic shapes drawing functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawPixel(int posX, int posY, Color color);                                                   // Draw a pixel

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawPixelV(Vector2 position, Color color);                                                    // Draw a pixel (Vector version)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, Color color);                // Draw a line

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawLineV(Vector2 startPos, Vector2 endPos, Color color);                                     // Draw a line (Vector version)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawLineEx(Vector2 startPos, Vector2 endPos, float thick, Color color);                       // Draw a line defining thickness

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawLineBezier(Vector2 startPos, Vector2 endPos, float thick, Color color);                   // Draw a line using cubic-bezier curves in-out

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawLineStrip(Vector2* points, int numPoints, Color color);                                   // Draw lines sequence

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCircle(int centerX, int centerY, float radius, Color color);                              // Draw a color-filled circle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCircleSector(Vector2 center, float radius, int startAngle, int endAngle, int segments, Color color);     // Draw a piece of a circle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCircleSectorLines(Vector2 center, float radius, int startAngle, int endAngle, int segments, Color color);    // Draw circle sector outline

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCircleGradient(int centerX, int centerY, float radius, Color color1, Color color2);       // Draw a gradient-filled circle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCircleV(Vector2 center, float radius, Color color);                                       // Draw a color-filled circle (Vector version)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCircleLines(int centerX, int centerY, float radius, Color color);                         // Draw circle outline

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRing(Vector2 center, float innerRadius, float outerRadius, int startAngle, int endAngle, int segments, Color color); // Draw ring

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRingLines(Vector2 center, float innerRadius, float outerRadius, int startAngle, int endAngle, int segments, Color color);    // Draw ring outline

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangle(int posX, int posY, int width, int height, Color color);                        // Draw a color-filled rectangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangleV(Vector2 position, Vector2 size, Color color);                                  // Draw a color-filled rectangle (Vector version)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangleRec(Rectangle rec, Color color);                                                 // Draw a color-filled rectangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectanglePro(Rectangle rec, Vector2 origin, float rotation, Color color);                 // Draw a color-filled rectangle with pro parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangleGradientV(int posX, int posY, int width, int height, Color color1, Color color2);// Draw a vertical-gradient-filled rectangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangleGradientH(int posX, int posY, int width, int height, Color color1, Color color2);// Draw a horizontal-gradient-filled rectangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangleGradientEx(Rectangle rec, Color col1, Color col2, Color col3, Color col4);       // Draw a gradient-filled rectangle with custom vertex colors

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangleLines(int posX, int posY, int width, int height, Color color);                   // Draw rectangle outline

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangleLinesEx(Rectangle rec, int lineThick, Color color);                              // Draw rectangle outline with extended parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangleRounded(Rectangle rec, float roundness, int segments, Color color);              // Draw rectangle with rounded edges

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRectangleRoundedLines(Rectangle rec, float roundness, int segments, int lineThick, Color color); // Draw rectangle with rounded edges outline

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTriangle(Vector2 v1, Vector2 v2, Vector2 v3, Color color);                                // Draw a color-filled triangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTriangleLines(Vector2 v1, Vector2 v2, Vector2 v3, Color color);                           // Draw triangle outline

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTriangleFan(Vector2* points, int numPoints, Color color);                                 // Draw a triangle fan defined by points

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTriangleStrip(Vector2* points, int pointsCount, Color color);                             // Draw a triangle strip defined by points

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawPoly(Vector2 center, int sides, float radius, float rotation, Color color);               // Draw a regular polygon (Vector version)


		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetShapesTexture(Texture2D texture, Rectangle source);                                        // Define default texture used to draw shapes

		// Basic shapes collision detection functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionRecs(Rectangle rec1, Rectangle rec2);                                           // Check collision between two rectangles

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionCircles(Vector2 center1, float radius1, Vector2 center2, float radius2);        // Check collision between two circles

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionCircleRec(Vector2 center, float radius, Rectangle rec);                         // Check collision between circle and rectangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Rectangle GetCollisionRec(Rectangle rec1, Rectangle rec2);                                         // Get collision rectangle for two rectangles collision

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionPointRec(Vector2 point, Rectangle rec);                                         // Check if point is inside rectangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionPointCircle(Vector2 point, Vector2 center, float radius);                       // Check if point is inside circle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionPointTriangle(Vector2 point, Vector2 p1, Vector2 p2, Vector2 p3);               // Check if point is inside a triangle

		//------------------------------------------------------------------------------------
		// Texture Loading and Drawing Functions (Module: textures)
		//------------------------------------------------------------------------------------

		// Image/Texture2D data loading/unloading/saving functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image LoadImage(string fileName);                                                             // Load image from file into CPU memory (RAM)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image LoadImageEx(Color* pixels, int width, int height);                                           // Load image from Color array data (RGBA - 32bit)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image LoadImagePro(IntPtr data, int width, int height, int format);                                 // Load image from raw data with parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image LoadImageRaw(string fileName, int width, int height, int format, int headerSize);       // Load image from RAW file data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ExportImage(Image image, string fileName);                                               // Export image data to file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ExportImageAsCode(Image image, string fileName);                                         // Export image as code file defining an array of bytes

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Texture2D LoadTexture(string fileName);                                                       // Load texture from file into GPU memory (VRAM)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Texture2D LoadTextureFromImage(Image image);                                                       // Load texture from image data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern TextureCubemap LoadTextureCubemap(Image image, int layoutType);                                    // Load cubemap from image, multiple image cubemap layouts supported

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern RenderTexture2D LoadRenderTexture(int width, int height);                                          // Load texture for rendering (framebuffer)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadImage(Image image);                                                                     // Unload image from CPU memory (RAM)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadTexture(Texture2D texture);                                                             // Unload texture from GPU memory (VRAM)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadRenderTexture(RenderTexture2D target);                                                  // Unload render texture from GPU memory (VRAM)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Color* GetImageData(Image image);                                                                  // Get pixel data from image as a Color public struct array

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector4* GetImageDataNormalized(Image image);                                                      // Get pixel data from image as Vector4 array (float normalized)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Rectangle GetImageAlphaBorder(Image image, float threshold);                                       // Get image alpha border rectangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetPixelDataSize(int width, int height, int format);                                           // Get pixel data size in bytes (image or texture)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GetTextureData(Texture2D texture);                                                           // Get pixel data from GPU texture and return an Image

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GetScreenData();                                                                         // Get pixel data from screen buffer and return an Image (screenshot)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UpdateTexture(Texture2D texture, IntPtr pixels);                                         // Update GPU texture with new data

		// Image manipulation functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image ImageCopy(Image image);                                                                      // Create an image duplicate (useful for transformations)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image ImageFromImage(Image image, Rectangle rec);                                                  // Create an image from another image piece

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageToPOT(Image* image, Color fillColor);                                                    // Convert image to POT (power-of-two)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageFormat(Image* image, int newFormat);                                                     // Convert image data to desired format

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageAlphaMask(Image* image, Image alphaMask);                                                // Apply alpha mask to image

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageAlphaClear(Image* image, Color color, float threshold);                                  // Clear alpha channel to desired color

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageAlphaCrop(Image* image, float threshold);                                                // Crop image depending on alpha value

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageAlphaPremultiply(Image* image);                                                          // Premultiply alpha channel

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageCrop(Image* image, Rectangle crop);                                                      // Crop an image to a defined rectangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageResize(Image* image, int newWidth, int newHeight);                                       // Resize image (Bicubic scaling algorithm)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageResizeNN(Image* image, int newWidth, int newHeight);                                      // Resize image (Nearest-Neighbor scaling algorithm)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageResizeCanvas(Image* image, int newWidth, int newHeight, int offsetX, int offsetY, Color color);  // Resize canvas and fill with color

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageMipmaps(Image* image);                                                                   // Generate all mipmap levels for a provided image

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageDither(Image* image, int rBpp, int gBpp, int bBpp, int aBpp);                            // Dither image data to 16bpp or lower (Floyd-Steinberg dithering)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Color* ImageExtractPalette(Image image, int maxPaletteSize, int* extractCount);                    // Extract color palette from image to maximum size (memory should be freed)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image ImageText(string text, int fontSize, Color color);                                      // Create an image from text (default font)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image ImageTextEx(Font font, string text, float fontSize, float spacing, Color tint);         // Create an image from text (custom sprite font)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageDraw(Image* dst, Image src, Rectangle srcRec, Rectangle dstRec, Color tint);             // Draw a source image within a destination image (tint applied to source)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageDrawRectangle(Image* dst, Rectangle rec, Color color);                                   // Draw rectangle within an image

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageDrawRectangleLines(Image* dst, Rectangle rec, int thick, Color color);                   // Draw rectangle lines within an image

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageDrawText(Image* dst, Vector2 position, string text, int fontSize, Color color);     // Draw text (default font) within an image (destination)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageDrawTextEx(Image* dst, Vector2 position, Font font, string text, float fontSize, float spacing, Color color); // Draw text (custom sprite font) within an image (destination)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageFlipVertical(Image* image);                                                              // Flip image vertically

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageFlipHorizontal(Image* image);                                                            // Flip image horizontally

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageRotateCW(Image* image);                                                                  // Rotate image clockwise 90deg

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageRotateCCW(Image* image);                                                                 // Rotate image counter-clockwise 90deg

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageColorTint(Image* image, Color color);                                                    // Modify image color: tint

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageColorInvert(Image* image);                                                               // Modify image color: invert

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageColorGrayscale(Image* image);                                                            // Modify image color: grayscale

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageColorContrast(Image* image, float contrast);                                             // Modify image color: contrast (-100 to 100)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageColorBrightness(Image* image, int brightness);                                           // Modify image color: brightness (-255 to 255)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ImageColorReplace(Image* image, Color color, Color replace);                                  // Modify image color: replace color

		// Image generation functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GenImageColor(int width, int height, Color color);                                           // Generate image: plain color

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GenImageGradientV(int width, int height, Color top, Color bottom);                           // Generate image: vertical gradient

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GenImageGradientH(int width, int height, Color left, Color right);                           // Generate image: horizontal gradient

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GenImageGradientRadial(int width, int height, float density, Color inner, Color outer);      // Generate image: radial gradient

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GenImageChecked(int width, int height, int checksX, int checksY, Color col1, Color col2);    // Generate image: checked

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GenImageWhiteNoise(int width, int height, float factor);                                     // Generate image: white noise

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GenImagePerlinNoise(int width, int height, int offsetX, int offsetY, float scale);           // Generate image: perlin noise

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GenImageCellular(int width, int height, int tileSize);                                       // Generate image: cellular algorithm. Bigger tileSize means bigger cells

		// Texture2D configuration functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GenTextureMipmaps(Texture2D* texture);                                                        // Generate GPU mipmaps for a texture

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetTextureFilter(Texture2D texture, TextureFilterMode filterMode);                                          // Set texture scaling filter mode

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetTextureWrap(Texture2D texture, TextureWrapMode wrapMode);                                              // Set texture wrapping mode

		// Texture2D drawing functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTexture(Texture2D texture, int posX, int posY, Color tint);                               // Draw a Texture2D

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTextureV(Texture2D texture, Vector2 position, Color tint);                                // Draw a Texture2D with position defined as Vector2

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTextureEx(Texture2D texture, Vector2 position, float rotation, float scale, Color tint);  // Draw a Texture2D with extended parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTextureRec(Texture2D texture, Rectangle sourceRec, Vector2 position, Color tint);         // Draw a part of a texture defined by a rectangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTextureQuad(Texture2D texture, Vector2 tiling, Vector2 offset, Rectangle quad, Color tint);  // Draw texture quad with tiling and offset parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTexturePro(Texture2D texture, Rectangle sourceRec, Rectangle destRec, Vector2 origin, float rotation, Color tint);       // Draw a part of a texture defined by a rectangle with 'pro' parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTextureNPatch(Texture2D texture, NPatchInfo nPatchInfo, Rectangle destRec, Vector2 origin, float rotation, Color tint);  // Draws a texture (or part of it) that stretches or shrinks nicely

		//------------------------------------------------------------------------------------
		// Font Loading and Text Drawing Functions (Module: text)
		//------------------------------------------------------------------------------------

		// Font loading/unloading functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Font GetFontDefault();                                                            // Get the default Font

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Font LoadFont(string fileName);                                                  // Load font from file into GPU memory (VRAM)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Font LoadFontEx(string fileName, int fontSize, int* fontChars, int charsCount);  // Load font from file with extended parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Font LoadFontFromImage(Image image, Color key, int firstChar);                        // Load font from Image (XNA style)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern CharInfo* LoadFontData(string fileName, int fontSize, int* fontChars, int charsCount, int type); // Load font data for further use

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Image GenImageFontAtlas(CharInfo* chars, Rectangle** recs, int charsCount, int fontSize, int padding, int packMethod);  // Generate image font atlas using chars info

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadFont(Font font);                                                           // Unload Font from GPU memory (VRAM)

		// Text drawing functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawFPS(int posX, int posY);                                                     // Shows current FPS

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawText(string text, int posX, int posY, int fontSize, Color color);       // Draw text (using default font)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTextEx(Font font, string text, Vector2 position, float fontSize, float spacing, Color tint);                // Draw text using font and additional parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTextRec(Font font, string text, Rectangle rec, float fontSize, float spacing, bool wordWrap, Color tint);   // Draw text using font inside rectangle limits

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawTextRecEx(Font font, string text, Rectangle rec, float fontSize, float spacing, bool wordWrap, Color tint,
										int selectStart, int selectLength, Color selectText, Color selectBack);    // Draw text using font inside rectangle limits with support for text selection

		// Text misc. functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int MeasureText(string text, int fontSize);                                      // Measure string width for default font

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector2 MeasureTextEx(Font font, string text, float fontSize, float spacing);    // Measure string size for Font

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetGlyphIndex(Font font, int character);                                          // Get index position for a unicode character on font

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetNextCodepoint(string text, int* bytesProcessed);                          // Returns next codepoint in a UTF8 encoded string
																											  // NOTE: 0x3f('?') is returned on failure

		// Text strings management functions
		// NOTE: Some strings allocate memory internally for returned strings, just be careful!

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool TextIsEqual(string text1, string text2);                               // Check if two text string are equal

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern uint TextLength(string text);                                            // Get text length, checks for '\0' ending

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern uint TextCountCodepoints(string text);                                   // Get total number of characters (codepoints) in a UTF8 encoded string

		//[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		//public static extern string TextFormat(string text, ...);                                        // Text formatting with variables (sprintf style)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string TextSubtext(string text, int position, int length);                  // Get a piece of a text string

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern char* TextReplace(char* text, string replace, string by);                   // Replace text string (memory should be freed!)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern char* TextInsert(string text, string insert, int position);                 // Insert text in a position (memory should be freed!)

		//[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		//public static extern string TextJoin(string* textList, int count, string delimiter);        // Join text strings with delimiter

		//[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		//public static extern string* TextSplit(string text, char delimiter, int* count);                 // Split text into multiple strings

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void TextAppend(char* text, string append, int* position);                       // Append text at specific position and move cursor!

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int TextFindIndex(string text, string find);                                // Find first text occurrence within a string

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string TextToUpper(string text);                      // Get upper case version of provided string

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string TextToLower(string text);                      // Get lower case version of provided string

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string TextToPascal(string text);                     // Get Pascal case notation version of provided string

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int TextToInteger(string text);                            // Get integer value from text (negative values not supported)

		//------------------------------------------------------------------------------------
		// Basic 3d Shapes Drawing Functions (Module: models)
		//------------------------------------------------------------------------------------

		// Basic geometric 3D shapes drawing functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawLine3D(Vector3 startPos, Vector3 endPos, Color color);                                    // Draw a line in 3D world space

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCircle3D(Vector3 center, float radius, Vector3 rotationAxis, float rotationAngle, Color color); // Draw a circle in 3D world space

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCube(Vector3 position, float width, float height, float length, Color color);             // Draw cube

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCubeV(Vector3 position, Vector3 size, Color color);                                       // Draw cube (Vector version)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCubeWires(Vector3 position, float width, float height, float length, Color color);        // Draw cube wires

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCubeWiresV(Vector3 position, Vector3 size, Color color);                                  // Draw cube wires (Vector version)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCubeTexture(Texture2D texture, Vector3 position, float width, float height, float length, Color color); // Draw cube textured

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawSphere(Vector3 centerPos, float radius, Color color);                                     // Draw sphere

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawSphereEx(Vector3 centerPos, float radius, int rings, int slices, Color color);            // Draw sphere with extended parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawSphereWires(Vector3 centerPos, float radius, int rings, int slices, Color color);         // Draw sphere wires

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCylinder(Vector3 position, float radiusTop, float radiusBottom, float height, int slices, Color color); // Draw a cylinder/cone

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawCylinderWires(Vector3 position, float radiusTop, float radiusBottom, float height, int slices, Color color); // Draw a cylinder/cone wires

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawPlane(Vector3 centerPos, Vector2 size, Color color);                                      // Draw a plane XZ

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawRay(Ray ray, Color color);                                                                // Draw a ray line

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawGrid(int slices, float spacing);                                                          // Draw a grid (centered at (0, 0, 0))

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawGizmo(Vector3 position);                                                                  // Draw simple gizmo
																																//DrawTorus(), DrawTeapot() could be useful?

		//------------------------------------------------------------------------------------
		// Model 3d Loading and Drawing Functions (Module: models)
		//------------------------------------------------------------------------------------

		// Model loading/unloading functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Model LoadModel(string fileName);                                                            // Load model from files (meshes and materials)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Model LoadModelFromMesh(Mesh mesh);                                                               // Load model from generated mesh (default material)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadModel(Model model);                                                                    // Unload model from memory (RAM and/or VRAM)

		// Mesh loading/unloading functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh* LoadMeshes(string fileName, int* meshCount);                                           // Load meshes from model file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ExportMesh(Mesh mesh, string fileName);                                                 // Export mesh data to file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadMesh(Mesh mesh);                                                                       // Unload mesh from memory (RAM and/or VRAM)

		// Material loading/unloading functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Material* LoadMaterials(string fileName, int* materialCount);                                // Load materials from model file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Material LoadMaterialDefault();                                                               // Load default material (Supports: DIFFUSE, SPECULAR, NORMAL maps)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadMaterial(Material material);                                                           // Unload material from GPU memory (VRAM)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMaterialTexture(Material* material, MaterialMapType mapType, Texture2D texture);                      // Set texture for a material map type (MAP_DIFFUSE, MAP_SPECULAR...)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetModelMeshMaterial(Model* model, int meshId, int materialId);                              // Set material for a mesh

		// Model animations loading/unloading functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern ModelAnimation* LoadModelAnimations(string fileName, int* animsCount);                       // Load model animations from file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UpdateModelAnimation(Model model, ModelAnimation anim, int frame);                           // Update model animation pose

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadModelAnimation(ModelAnimation anim);                                                   // Unload animation data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsModelAnimationValid(Model model, ModelAnimation anim);                                     // Check model animation skeleton match

		// Mesh generation functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshPoly(int sides, float radius);                                                        // Generate polygonal mesh

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshPlane(float width, float length, int resX, int resZ);                                 // Generate plane mesh (with subdivisions)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshCube(float width, float height, float length);                                        // Generate cuboid mesh

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshSphere(float radius, int rings, int slices);                                          // Generate sphere mesh (standard sphere)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshHemiSphere(float radius, int rings, int slices);                                      // Generate half-sphere mesh (no bottom cap)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshCylinder(float radius, float height, int slices);                                     // Generate cylinder mesh

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshTorus(float radius, float size, int radSeg, int sides);                               // Generate torus mesh

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshKnot(float radius, float size, int radSeg, int sides);                                // Generate trefoil knot mesh

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshHeightmap(Image heightmap, Vector3 size);                                             // Generate heightmap mesh from image data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshCubicmap(Image cubicmap, Vector3 cubeSize);                                           // Generate cubes-based map mesh from image data

		// Mesh manipulation functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern BoundingBox MeshBoundingBox(Mesh mesh);                                                           // Compute mesh bounding box limits

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void MeshTangents(Mesh* mesh);                                                                    // Compute mesh tangents

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void MeshBinormals(Mesh* mesh);                                                                   // Compute mesh binormals

		// Model drawing functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawModel(Model model, Vector3 position, float scale, Color tint);                           // Draw a model (with texture if set)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawModelEx(Model model, Vector3 position, Vector3 rotationAxis, float rotationAngle, Vector3 scale, Color tint); // Draw a model with extended parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawModelWires(Model model, Vector3 position, float scale, Color tint);                      // Draw a model wires (with texture if set)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawModelWiresEx(Model model, Vector3 position, Vector3 rotationAxis, float rotationAngle, Vector3 scale, Color tint); // Draw a model wires (with texture if set) with extended parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawBoundingBox(BoundingBox box, Color color);                                               // Draw bounding box (wires)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawBillboard(Camera3D camera, Texture2D texture, Vector3 center, float size, Color tint);     // Draw a billboard texture

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void DrawBillboardRec(Camera3D camera, Texture2D texture, Rectangle sourceRec, Vector3 center, float size, Color tint); // Draw a billboard texture defined by sourceRec

		// Collision detection functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionSpheres(Vector3 centerA, float radiusA, Vector3 centerB, float radiusB);       // Detect collision between two spheres

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionBoxes(BoundingBox box1, BoundingBox box2);                                     // Detect collision between two bounding boxes

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionBoxSphere(BoundingBox box, Vector3 center, float radius);                      // Detect collision between box and sphere

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionRaySphere(Ray ray, Vector3 center, float radius);                              // Detect collision between ray and sphere

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionRaySphereEx(Ray ray, Vector3 center, float radius, Vector3* collisionPoint);   // Detect collision between ray and sphere, returns collision point

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool CheckCollisionRayBox(Ray ray, BoundingBox box);                                              // Detect collision between ray and box

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern RayHitInfo GetCollisionRayModel(Ray ray, Model model);                                            // Get collision info between ray and model

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern RayHitInfo GetCollisionRayTriangle(Ray ray, Vector3 p1, Vector3 p2, Vector3 p3);                  // Get collision info between ray and triangle

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern RayHitInfo GetCollisionRayGround(Ray ray, float groundHeight);                                    // Get collision info between ray and ground plane (Y-normal plane)

		//------------------------------------------------------------------------------------
		// Shaders System Functions (Module: rlgl)
		// NOTE: This functions are useless when using OpenGL 1.1
		//------------------------------------------------------------------------------------

		// Shader loading/unloading functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern char* LoadText(string fileName);                               // Load chars array from text file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Shader LoadShader(string vsFileName, string fsFileName);  // Load shader from files and bind default locations

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Shader LoadShaderCode(char* vsCode, char* fsCode);                  // Load shader from code strings and bind default locations

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadShader(Shader shader);                                   // Unload shader from GPU memory (VRAM)


		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Shader GetShaderDefault();                                      // Get default shader

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Texture2D GetTextureDefault();                                  // Get default texture

		// Shader configuration functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetShaderLocation(Shader shader, string uniformName);      // Get shader uniform location

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetShaderValue(Shader shader, int uniformLoc, IntPtr value, int uniformType);               // Set shader uniform value

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetShaderValueV(Shader shader, int uniformLoc, IntPtr value, int uniformType, int count);   // Set shader uniform value vector

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetShaderValueMatrix(Shader shader, int uniformLoc, Matrix mat);         // Set shader uniform value (matrix 4x4)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetShaderValueTexture(Shader shader, int uniformLoc, Texture2D texture); // Set shader uniform value for texture

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMatrixProjection(Matrix proj);                              // Set a custom projection matrix (replaces internal projection matrix)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMatrixModelview(Matrix view);                               // Set a custom modelview matrix (replaces internal modelview matrix)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Matrix GetMatrixModelview();                                    // Get internal modelview matrix

		// Texture maps generation (PBR)
		// NOTE: Required shaders should be provided

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Texture2D GenTextureCubemap(Shader shader, Texture2D skyHDR, int size);       // Generate cubemap texture from HDR texture

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Texture2D GenTextureIrradiance(Shader shader, Texture2D cubemap, int size);   // Generate irradiance texture using cubemap data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Texture2D GenTexturePrefilter(Shader shader, Texture2D cubemap, int size);    // Generate prefilter texture using cubemap data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Texture2D GenTextureBRDF(Shader shader, int size);                  // Generate BRDF texture

		// Shading begin/end functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void BeginShaderMode(Shader shader);                                // Begin custom shader drawing

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void EndShaderMode();                                           // End custom shader drawing (use default shader)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void BeginBlendMode(int mode);                                      // Begin blending mode (alpha, additive, multiplied)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void EndBlendMode();                                            // End blending mode (reset to default: alpha blending)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void BeginScissorMode(int x, int y, int width, int height);         // Begin scissor mode (define screen area for following drawing)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void EndScissorMode();                                          // End scissor mode

		// VR control functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void InitVrSimulator();                       // Init VR simulator for selected device parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void CloseVrSimulator();                      // Close VR simulator for current device

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UpdateVrTracking(Camera3D* camera);            // Update VR tracking (position and orientation) and camera

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetVrConfiguration(VrDeviceInfo info, Shader distortion);      // Set stereo rendering configuration parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsVrSimulatorReady();                    // Detect if VR simulator is ready

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ToggleVrMode();                          // Enable/Disable VR experience

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void BeginVrDrawing();                        // Begin VR simulator stereo rendering

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void EndVrDrawing();                          // End VR simulator stereo rendering

		//------------------------------------------------------------------------------------
		// Audio Loading and Playing Functions (Module: audio)
		//------------------------------------------------------------------------------------

		// Audio device management functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void InitAudioDevice();                                     // Initialize audio device and context

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void CloseAudioDevice();                                    // Close the audio device and context

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsAudioDeviceReady();                                  // Check if audio device has been initialized successfully

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMasterVolume(float volume);                             // Set master volume (listener)

		// Wave/Sound loading/unloading functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Wave LoadWave(string fileName);                            // Load wave data from file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Sound LoadSound(string fileName);                          // Load sound from file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Sound LoadSoundFromWave(Wave wave);                             // Load sound from wave data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UpdateSound(Sound sound, IntPtr data, int samplesCount);// Update sound buffer with new data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadWave(Wave wave);                                     // Unload wave data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadSound(Sound sound);                                  // Unload sound

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ExportWave(Wave wave, string fileName);               // Export wave data to file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ExportWaveAsCode(Wave wave, string fileName);         // Export wave sample data to code (.h)

		// Wave/Sound management functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void PlaySound(Sound sound);                                    // Play a sound

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void StopSound(Sound sound);                                    // Stop playing a sound

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void PauseSound(Sound sound);                                   // Pause a sound

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ResumeSound(Sound sound);                                  // Resume a paused sound

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void PlaySoundMulti(Sound sound);                               // Play a sound (using multichannel buffer pool)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void StopSoundMulti();                                      // Stop any sound playing (using multichannel buffer pool)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GetSoundsPlaying();                                     // Get number of sounds playing in the multichannel

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsSoundPlaying(Sound sound);                               // Check if a sound is currently playing

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetSoundVolume(Sound sound, float volume);                 // Set volume for a sound (1.0 is max level)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetSoundPitch(Sound sound, float pitch);                   // Set pitch for a sound (1.0 is base level)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void WaveFormat(Wave* wave, int sampleRate, int sampleSize, int channels);  // Convert wave data to desired format

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Wave WaveCopy(Wave wave);                                       // Copy a wave to a new wave

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void WaveCrop(Wave* wave, int initSample, int finalSample);     // Crop a wave to defined samples range

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float* GetWaveData(Wave wave);                                  // Get samples data from wave as a floats array

		// Music management functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Music LoadMusicStream(string fileName);                    // Load music stream from file

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UnloadMusicStream(Music music);                            // Unload music stream

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void PlayMusicStream(Music music);                              // Start music playing

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UpdateMusicStream(Music music);                            // Updates buffers for music streaming

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void StopMusicStream(Music music);                              // Stop music playing

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void PauseMusicStream(Music music);                             // Pause music playing

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ResumeMusicStream(Music music);                            // Resume playing paused music

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsMusicPlaying(Music music);                               // Check if music is playing

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMusicVolume(Music music, float volume);                 // Set volume for music (1.0 is max level)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMusicPitch(Music music, float pitch);                   // Set pitch for a music (1.0 is base level)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetMusicLoopCount(Music music, int count);                 // Set music loop count (loop repeats)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GetMusicTimeLength(Music music);                          // Get music time length (in seconds)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GetMusicTimePlayed(Music music);                          // Get current music time played (in seconds)

		// AudioStream management functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern AudioStream InitAudioStream(uint sampleRate, uint sampleSize, uint channels); // Init audio stream (to stream raw audio pcm data)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void UpdateAudioStream(AudioStream stream, IntPtr data, int samplesCount); // Update audio stream buffers with data

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void CloseAudioStream(AudioStream stream);                      // Close audio stream and free memory

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsAudioBufferProcessed(AudioStream stream);                // Check if any audio stream buffers requires refill

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void PlayAudioStream(AudioStream stream);                       // Play audio stream

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void PauseAudioStream(AudioStream stream);                      // Pause audio stream

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void ResumeAudioStream(AudioStream stream);                     // Resume audio stream

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool IsAudioStreamPlaying(AudioStream stream);                  // Check if audio stream is playing

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void StopAudioStream(AudioStream stream);                       // Stop audio stream

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetAudioStreamVolume(AudioStream stream, float volume);    // Set volume for audio stream (1.0 is max level)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void SetAudioStreamPitch(AudioStream stream, float pitch);      // Set pitch for audio stream (1.0 is base level)

		//------------------------------------------------------------------------------------
		// Network (Module: network)
		//------------------------------------------------------------------------------------

		// IN PROGRESS: Check rnet.h for reference
	}
}