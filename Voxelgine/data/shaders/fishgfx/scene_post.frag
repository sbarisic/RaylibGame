#version 400

in vec4 vColor;
in vec2 vUv;

layout (location = 0) out vec4 outColor;

uniform sampler2D uTexture;
uniform vec2 uResolution;
uniform float uTime;
uniform int uUseFxaa;

const float FxaaReduceMin = 1.0 / 128.0;
const float FxaaReduceMul = 1.0 / 8.0;
const float FxaaSpanMax = 8.0;

vec3 applyFxaa(vec2 uv)
{
	vec2 inverseResolution = 1.0 / max(uResolution, vec2(1.0));
	vec3 rgbNorthWest = texture(uTexture, uv + vec2(-1.0, -1.0) * inverseResolution).rgb;
	vec3 rgbNorthEast = texture(uTexture, uv + vec2(1.0, -1.0) * inverseResolution).rgb;
	vec3 rgbSouthWest = texture(uTexture, uv + vec2(-1.0, 1.0) * inverseResolution).rgb;
	vec3 rgbSouthEast = texture(uTexture, uv + vec2(1.0, 1.0) * inverseResolution).rgb;
	vec3 rgbMiddle = texture(uTexture, uv).rgb;
	vec3 luma = vec3(0.299, 0.587, 0.114);
	float lumaNorthWest = dot(rgbNorthWest, luma);
	float lumaNorthEast = dot(rgbNorthEast, luma);
	float lumaSouthWest = dot(rgbSouthWest, luma);
	float lumaSouthEast = dot(rgbSouthEast, luma);
	float lumaMiddle = dot(rgbMiddle, luma);
	float lumaMinimum = min(lumaMiddle, min(min(lumaNorthWest, lumaNorthEast), min(lumaSouthWest, lumaSouthEast)));
	float lumaMaximum = max(lumaMiddle, max(max(lumaNorthWest, lumaNorthEast), max(lumaSouthWest, lumaSouthEast)));
	vec2 direction = vec2(
		-((lumaNorthWest + lumaNorthEast) - (lumaSouthWest + lumaSouthEast)),
		(lumaNorthWest + lumaSouthWest) - (lumaNorthEast + lumaSouthEast)
	);
	float reduction = max(
		(lumaNorthWest + lumaNorthEast + lumaSouthWest + lumaSouthEast) * 0.25 * FxaaReduceMul,
		FxaaReduceMin
	);
	float reciprocalMinimum = 1.0 / (min(abs(direction.x), abs(direction.y)) + reduction);
	direction = clamp(direction * reciprocalMinimum, -FxaaSpanMax, FxaaSpanMax) * inverseResolution;
	vec3 rgbA = 0.5 * (
		texture(uTexture, uv + direction * (1.0 / 3.0 - 0.5)).rgb
		+ texture(uTexture, uv + direction * (2.0 / 3.0 - 0.5)).rgb
	);
	vec3 rgbB = rgbA * 0.5 + 0.25 * (
		texture(uTexture, uv + direction * -0.5).rgb
		+ texture(uTexture, uv + direction * 0.5).rgb
	);
	float lumaB = dot(rgbB, luma);
	return lumaB < lumaMinimum || lumaB > lumaMaximum ? rgbA : rgbB;
}

void main()
{
	vec4 source = texture(uTexture, vUv);
	vec3 color = uUseFxaa != 0 ? applyFxaa(vUv) : source.rgb;
	float vignette = smoothstep(0.92, 0.18, length(vUv - vec2(0.5)));
	float grain = fract(sin(dot(vUv * uResolution + uTime, vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
	color = color * mix(0.965, 1.0, vignette) + grain * 0.0075;
	outColor = vec4(clamp(color, 0.0, 1.0), source.a) * vColor;
}
