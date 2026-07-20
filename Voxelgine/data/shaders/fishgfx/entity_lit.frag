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
uniform float uShadowMapDepthRanges[4];
uniform float uShadowWorldTexelSizes[4];
uniform sampler2DShadow uShadowMaps[4];
uniform sampler2DShadow uDynamicShadowMaps[4];

float SampleCascade(int cascade, float nDotL)
{
	float slope = 1.0 - clamp(nDotL, 0.0, 1.0);
	float worldTexelSize = uShadowWorldTexelSizes[cascade];
	float normalOffset = min(worldTexelSize * 1.25 * slope, 0.2);
	vec3 receiverPosition = vWorldPosition + normalize(vWorldNormal) * normalOffset;
	vec4 projected = uShadowMatrices[cascade] * vec4(receiverPosition, 1.0);
	vec3 coordinate = projected.xyz / projected.w;
	vec2 uv = coordinate.xy * 0.5 + 0.5;
	float receiverDepth = coordinate.z * 0.5 + 0.5;

	if (projected.w <= 0.0 || uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0
		|| receiverDepth < 0.0 || receiverDepth > 1.0)
	{
		return 1.0;
	}

	float worldBias = 0.01 + worldTexelSize * 0.1 * slope;
	float bias = clamp(
		worldBias * 0.5 / max(uShadowMapDepthRanges[cascade], 1.0),
		0.000002,
		0.0001
	);
	vec2 texel = 1.0 / vec2(textureSize(uShadowMaps[cascade], 0));
	vec2 minimumUv = texel * 0.5;
	vec2 maximumUv = vec2(1.0) - minimumUv;
	float comparisonDepth = receiverDepth - bias;
	float staticVisibility = 0.0;
	if (uShadowFilterRadius <= 1)
	{
		const vec2 offsets[4] = vec2[4](
			vec2(-0.5, -0.5), vec2(0.5, -0.5),
			vec2(-0.5, 0.5), vec2(0.5, 0.5));
		for (int sampleIndex = 0; sampleIndex < 4; sampleIndex++)
		{
			vec2 sampleUv = clamp(uv + offsets[sampleIndex] * texel, minimumUv, maximumUv);
			staticVisibility += texture(uShadowMaps[cascade], vec3(sampleUv, comparisonDepth));
		}
		staticVisibility *= 0.25;
	}
	else
	{
		for (int y = -1; y <= 1; y++)
		{
			for (int x = -1; x <= 1; x++)
			{
				vec2 sampleUv = clamp(uv + vec2(x, y) * texel * 1.5, minimumUv, maximumUv);
				staticVisibility += texture(uShadowMaps[cascade], vec3(sampleUv, comparisonDepth));
			}
		}
		staticVisibility /= 9.0;
	}
	float dynamicVisibility = texture(
		uDynamicShadowMaps[cascade],
		vec3(clamp(uv, minimumUv, maximumUv), comparisonDepth));
	return min(staticVisibility, dynamicVisibility);
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
