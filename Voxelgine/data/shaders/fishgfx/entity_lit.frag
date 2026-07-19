#version 430

in vec4 vColor;
in vec2 vUv;
in vec3 vWorldPosition;
in vec3 vWorldNormal;

layout (location = 0) out vec4 outColor;

uniform sampler2D uTexture;
uniform float uAlphaCutoff;
uniform vec3 uBlockLight;
uniform float uSkyLight;
uniform vec3 LightDirection;
uniform float AmbientLight;
uniform vec3 SunColor;
uniform float SunIntensity;
uniform mat4 uView;
uniform int uShadowEnabled;
uniform int uShadowCascadeCount;
uniform float uShadowStrength;
uniform float uShadowBlendFraction;
uniform int uShadowFilterRadius;
uniform mat4 uShadowMatrices[4];
uniform float uShadowSplits[4];
uniform float uShadowDepthRanges[4];
uniform sampler2D uShadowMaps[4];

float SampleCascade(int cascade, float nDotL)
{
	vec4 projected = uShadowMatrices[cascade] * vec4(vWorldPosition, 1.0);
	vec3 coordinate = projected.xyz / projected.w;
	vec2 uv = coordinate.xy * 0.5 + 0.5;
	float receiverDepth = coordinate.z * 0.5 + 0.5;

	if (projected.w <= 0.0 || uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0
		|| receiverDepth < 0.0 || receiverDepth > 1.0)
	{
		return 1.0;
	}

	float bias = clamp(
		(0.00035 + 0.0015 * (1.0 - nDotL)) * (uShadowDepthRanges[cascade] / 128.0),
		0.0002,
		0.0040
	);
	vec2 texel = 1.0 / vec2(textureSize(uShadowMaps[cascade], 0));
	float visibility = 0.0;
	float sampleCount = 0.0;

	for (int y = -uShadowFilterRadius; y <= uShadowFilterRadius; y++)
	{
		for (int x = -uShadowFilterRadius; x <= uShadowFilterRadius; x++)
		{
			vec2 sampleUv = uv + vec2(x, y) * texel;

			if (sampleUv.x < 0.0 || sampleUv.x > 1.0 || sampleUv.y < 0.0 || sampleUv.y > 1.0)
			{
				visibility += 1.0;
			}
			else
			{
				float depth = texture(uShadowMaps[cascade], sampleUv).r;
				visibility += receiverDepth - bias <= depth ? 1.0 : 0.0;
			}

			sampleCount += 1.0;
		}
	}

	return visibility / sampleCount;
}

float SunVisibility(float nDotL)
{
	if (uShadowEnabled == 0 || uShadowCascadeCount == 0)
	{
		return 1.0;
	}

	float viewDepth = -(uView * vec4(vWorldPosition, 1.0)).z;
	int cascade = uShadowCascadeCount - 1;

	for (int index = 0; index < uShadowCascadeCount; index++)
	{
		if (viewDepth <= uShadowSplits[index])
		{
			cascade = index;
			break;
		}
	}

	float visibility = SampleCascade(cascade, nDotL);

	if (cascade + 1 < uShadowCascadeCount)
	{
		float width = max(uShadowDepthRanges[cascade] * uShadowBlendFraction, 0.0001);
		float blend = clamp((viewDepth - (uShadowSplits[cascade] - width)) / width, 0.0, 1.0);
		visibility = mix(visibility, SampleCascade(cascade + 1, nDotL), blend);
	}

	return mix(1.0, visibility, uShadowStrength);
}

void main()
{
	vec4 sampled = texture(uTexture, vUv) * vColor;

	if (sampled.a < uAlphaCutoff)
	{
		discard;
	}

	vec3 normal = normalize(vWorldNormal);
	float diffuse = max(dot(normal, normalize(-LightDirection)), 0.0);
	float visibility = SunVisibility(diffuse);
	vec3 skyAmbient = uSkyLight * SunColor * SunIntensity * AmbientLight;
	vec3 skyDirect = uSkyLight * SunColor * SunIntensity
		* diffuse * (1.0 - AmbientLight) * visibility;
	vec3 lighting = max(uBlockLight, skyAmbient + skyDirect);
	outColor = vec4(sampled.rgb * lighting, sampled.a);
}
