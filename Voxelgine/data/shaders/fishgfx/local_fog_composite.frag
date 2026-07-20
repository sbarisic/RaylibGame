#version 430

in vec2 vUv;
layout (location = 0) out vec4 outColor;

uniform sampler2D uTexture;
uniform sampler2D uSceneDepth;
uniform sampler2D uFogTexture;
uniform vec2 uFogResolution;

void main()
{
	vec4 scene = texture(uTexture, vUv);
	float centerDepth = texture(uSceneDepth, vUv).r;
	vec2 pixel = vUv * uFogResolution - vec2(0.5);
	vec2 basePixel = floor(pixel);
	vec2 fractionValue = fract(pixel);
	vec4 fog = vec4(0.0);
	float totalWeight = 0.0;
	for (int y = 0; y <= 1; y++)
	{
		for (int x = 0; x <= 1; x++)
		{
			vec2 offset = vec2(x, y);
			vec2 sampleUv = (basePixel + offset + vec2(0.5)) / uFogResolution;
			float sampleDepth = texture(uSceneDepth, sampleUv).r;
			float spatial = mix(1.0 - fractionValue.x, fractionValue.x, float(x))
				* mix(1.0 - fractionValue.y, fractionValue.y, float(y));
			float depthWeight = exp(-abs(sampleDepth - centerDepth) * 400.0);
			float weight = spatial * max(depthWeight, 0.0001);
			fog += texture(uFogTexture, sampleUv) * weight;
			totalWeight += weight;
		}
	}
	fog /= max(totalWeight, 0.0001);
	outColor = vec4(fog.rgb + scene.rgb * fog.a, scene.a);
}
