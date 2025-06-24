#version 330

#define FXAA_REDUCE_MIN (1.0/128.0)
#define FXAA_REDUCE_MUL (1.0/8.0)
#define FXAA_SPAN_MAX 8.0

in vec2 fragTexCoord;
in vec4 fragColor;

out vec4 finalColor;

uniform sampler2D texture0;
uniform vec2 resolution;
uniform float time;

float pixelWidth = 2.0;
float pixelHeight = 2.0;

float random(vec2 st) {
    // 7907 	7919
    st = st + (vec2(time, time + 1) / 5000000.0);

    return fract(sin(dot(st.xy, vec2(12.9898, 78.233))) * (43758.5453123));
}

float sdSquare(vec2 point, float width) {
	vec2 d = abs(point) - width;
	return min(max(d.x,d.y),0.0) + length(max(d,0.0));
}

float vignette(vec2 uv, vec2 size, float roundness, float smoothness) {
	// Center UVs
	uv -= 0.5;

	// Shift UVs based on the larger of width or height
	float minWidth = min(size.x, size.y);
	uv.x = sign(uv.x) * clamp(abs(uv.x) - abs(minWidth - size.x), 0.0, 1.0);
	uv.y = sign(uv.y) * clamp(abs(uv.y) - abs(minWidth - size.y), 0.0, 1.0);

	// Signed distance calculation
	float boxSize = minWidth * (1.0 - roundness);
	float dist = sdSquare(uv, boxSize) - (minWidth * roundness);

	return 1.0 - smoothstep(0.0, smoothness, dist);
}


vec2 v2_clamp(vec2 v, float amt) {
    return floor(v * amt) / amt;
}

vec3 color_palette(vec3 clr) {
    clr = clr * 255;
    //clr = floor(clr / 8) * 8;
    return clr / 255;
}

void main()
{

    // Pixelate filter
    vec2 inverse_resolution = vec2(1.0 / resolution.x, 1.0 / resolution.y);
    float aspect = resolution.y / resolution.x;

    float dx = pixelWidth * inverse_resolution.x;
    float dy = pixelHeight * inverse_resolution.y;

    vec2 coord = vec2(dx * floor(fragTexCoord.x / dx), dy * floor(fragTexCoord.y / dy));
    
    //finalColor = texture2D(texture0, coord);    
    vec4 rt_color = texture2D(texture0, fragTexCoord);

    float RX = (random(v2_clamp( fragTexCoord * vec2(1, aspect), 200 )) - 0.5) / 23;
    float vign = vignette(fragTexCoord, vec2(0.4, 0.4), 1.5, 1);

    //float RX = vignette(fragTexCoord, vec2(1, 1), 1, 1);

    finalColor = vec4((color_palette(rt_color.xyz) + vec3(RX)) * vign, rt_color.a);

    //bullshit
}
